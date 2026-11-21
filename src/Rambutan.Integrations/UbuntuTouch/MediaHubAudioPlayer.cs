using Rambutan.Core.Diagnostics;
using Rambutan.Core.Models;
using Rambutan.Core.Playback;
using Rambutan.Core.Queue;
using System.Runtime.InteropServices;

namespace Rambutan.Integrations.UbuntuTouch;

/// <summary>
/// Sends URIs and the resolved queue to Media Hub. No audio is decoded or served
/// by this process: the native playlist continues when Lomiri freezes the UI.
/// All bus operations are ordered; network resolution never holds that queue.
/// On resume, one authoritative snapshot reconciles even several missed tracks.
/// </summary>
public sealed class MediaHubAudioPlayer : IIndependentQueuePlayer, IDisposable
{
    private sealed record Entry(string Id, AudioTrackRequest Request, TrackInfo Track, int Index);
    private readonly IMediaHubConnection _hub;
    private readonly QueueModel _queue;
    private readonly IStreamSource _streams;
    private readonly string _spool;
    private readonly Func<TrackInfo, ResolvedMedia, string, CancellationToken, Task<string>>? _prepareTrack;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Entry> _entries = [];
    private readonly PosixSignalRegistration? _termination;
    private Task _tail = Task.CompletedTask;
    private CancellationTokenSource? _preparation;
    private Entry? _current;
    private long _generation;
    private long _chain;
    private double _volume = 1;
    private bool _publishing;
    private bool _disposed;
    private volatile bool _ended;
    private int _error;
    private string? _faultedKey;

    public MediaHubAudioPlayer(QueueModel queue, IStreamSource streams, string dataDirectory,
        Func<TrackInfo, ResolvedMedia, string, CancellationToken, Task<string>>? prepareTrack = null)
        : this(queue, streams, Path.Combine(dataDirectory, "media-hub"), new MediaHubConnection(), prepareTrack) { }

    internal MediaHubAudioPlayer(QueueModel queue, IStreamSource streams, string spool, IMediaHubConnection hub,
        Func<TrackInfo, ResolvedMedia, string, CancellationToken, Task<string>>? prepareTrack = null)
    {
        _queue = queue;
        _streams = streams;
        _spool = spool;
        _hub = hub;
        _prepareTrack = prepareTrack;
        _hub.Ended += OnEnded;
        _hub.Error += OnError;
        _queue.Changed += QueueChanged;
        // Lomiri closes apps with SIGTERM. Its default action would bypass the
        // Avalonia Exit event; release our remote session before allowing exit.
        if (OperatingSystem.IsLinux())
            _termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => ReleaseSession());
        _ = Task.Run(PollLoopAsync);
    }

    public event Action<AudioPlayerEvent>? EventRaised;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            Forget(Ordered(async () => { if (_current is not null) await _hub.SetVolumeAsync(_volume); }));
        }
    }

    public Task PlayAsync(AudioTrackRequest request, CancellationToken ct = default)
    {
        CancelPreparation();
        return Ordered(async () =>
        {
            ct.ThrowIfCancellationRequested();
            await _hub.ConnectAsync();
            _current = null;
            _entries.Clear();
            _ended = false;
            _error = 0;
            _faultedKey = null;
            ++_chain;
            // Closing the old pipeline before clearing pins prevents cached files
            // being evicted while a suspended client still needs them.
            try { await _hub.CommandAsync("Stop"); }
            catch
            {
                // A service restart invalidates the old object path even if the
                // session-bus connection itself stayed alive.
                await _hub.DisposeAsync();
                await _hub.ConnectAsync();
            }
            Directory.CreateDirectory(_spool);
            foreach (var file in Directory.EnumerateFiles(_spool)) File.Delete(file);
            try
            {
                var track = _queue.Current ?? throw new InvalidOperationException("The play queue is empty.");
                using var preparation = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
                var uri = await PrepareUriAsync(track, new ResolvedMedia(request.Url, request.AudioCodec ?? ""), preparation.Token);
                if (!await _hub.OpenAsync(uri))
                    throw new StreamOpenException("Media Hub refused the audio URI.");
                var ids = await _hub.TracksAsync();
                var entry = new Entry(ids.Single(), request, track, _queue.CurrentIndex);
                _entries.Add(entry);
                await _hub.SetLoopAsync("None");
                // PulseAudio restores the multimedia role's system volume when
                // the sink opens. Setting Media Hub Volume here overrides it
                // with the desktop preference (normally 100%), even while the
                // phone's volume indicator still shows the user's lower level.
                // Only an explicit volume adjustment may write that property.
                await _hub.CommandAsync("Play");
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
                while ((await _hub.SnapshotAsync()).Status != "Playing")
                {
                    ct.ThrowIfCancellationRequested();
                    if (_error != 0 || DateTime.UtcNow >= deadline)
                        throw new StreamOpenException($"Media Hub could not start audio (error {_error}).");
                    await Task.Delay(100, ct);
                }
                ct.ThrowIfCancellationRequested();
                if (request.StartAt > TimeSpan.Zero) await _hub.SeekAsync(request.StartAt);
                _current = entry;
                AppLog.Write("media-hub", $"Playing {request.Key}; audio owned by the system service.");
                QueueChanged();
            }
            catch
            {
                try { await _hub.CommandAsync("Stop"); }
                catch { /* Preserve the original open/cancellation error. */ }
                await _hub.DisposeAsync();
                _entries.Clear();
                throw;
            }
        });
    }

    // The coordinator's two-slot preload and crossfade belong to the desktop
    // engine. This backend sends the whole queue instead.
    public Task PreloadNextAsync(AudioTrackRequest request, CancellationToken ct = default) => Task.CompletedTask;
    public void ClearNext() => CancelPreparation();
    public void BeginCrossfade(TimeSpan duration) { }

    public async Task<SeekResult> SeekAsync(TimeSpan position, CancellationToken ct = default)
    {
        long? chain = null;
        await Ordered(async () =>
        {
            ct.ThrowIfCancellationRequested();
            if (_current is null) return;
            await _hub.SeekAsync(position);
            chain = ++_chain;
        });
        return new SeekResult(true, chain);
    }

    public void Pause() => Transport("Pause");
    public void Resume() => Transport("Play");
    private void Transport(string command) => Forget(Ordered(async () =>
    {
        if (_current is not null) await _hub.CommandAsync(command);
    }));
    public void Stop()
    {
        CancelPreparation();
        Forget(Ordered(async () =>
        {
            if (_current is null) return;
            _current = null;
            await _hub.CommandAsync("Stop");
        }));
    }

    private void OnEnded() => _ended = true;
    private void OnError(short code) => Interlocked.Exchange(ref _error, code);
    private void CancelPreparation()
    {
        lock (_sync)
        {
            ++_generation;
            _preparation?.Cancel();
        }
    }

    private void QueueChanged()
    {
        if (_publishing || _disposed) return;
        CancellationTokenSource preparation;
        long generation;
        lock (_sync)
        {
            _preparation?.Cancel();
            preparation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _preparation = preparation;
            generation = ++_generation;
        }
        _ = PrepareQueueAsync(generation, preparation);
    }

    private async Task PrepareQueueAsync(long generation, CancellationTokenSource preparation)
    {
        var ct = preparation.Token;
        try
        {
            // Coalesce a queue replacement, its current-index change and radio
            // extension. Do not delay the first song to resolve every successor.
            await Task.Delay(150, ct);
            QueueSnapshot? snapshot = null;
            int currentIndex = -1;
            await Ordered(async () =>
            {
                ct.ThrowIfCancellationRequested();
                if (_current is null) return;
                await PollAsync();
                snapshot = _queue.Snapshot();
                currentIndex = snapshot.CurrentIndex;
                if (currentIndex < 0 || !ReferenceEquals(snapshot.Tracks[currentIndex], _current.Track))
                { snapshot = null; return; }
                // Remove obsolete successors when the queue is edited/shuffled.
                // Keep the playing entry so preparation never restarts the song.
                foreach (var entry in _entries.Where(e => e.Id != _current.Id).ToArray())
                {
                    await _hub.RemoveAsync(entry.Id);
                    _entries.Remove(entry);
                }
                _current = _current with { Index = currentIndex };
                _entries[0] = _current;
                await _hub.SetLoopAsync(snapshot.Repeat == RepeatMode.One ? "Track" : "None");
            });
            if (snapshot is null) return;
            var indices = Enumerable.Range(currentIndex + 1, snapshot.Tracks.Count - currentIndex - 1);
            if (snapshot.Repeat == RepeatMode.All) indices = indices.Concat(Enumerable.Range(0, currentIndex));
            foreach (var index in indices)
            {
                ct.ThrowIfCancellationRequested();
                var track = snapshot.Tracks[index];
                var media = await _streams.ResolveAsync(track.VideoId, ct: ct);
                var uri = await PrepareUriAsync(track, media, ct);
                await Ordered(async () =>
                {
                    ct.ThrowIfCancellationRequested();
                    await _hub.AddAsync(uri);
                    var ids = await _hub.TracksAsync();
                    var id = ids.Single(id => _entries.All(e => e.Id != id));
                    var request = new AudioTrackRequest($"hub-{generation}-{index}", uri,
                        track.Title, track.Duration, TimeSpan.Zero);
                    _entries.Add(new(id, request, track, index));
                });
            }
            await Ordered(async () =>
            {
                ct.ThrowIfCancellationRequested();
                if (snapshot.Repeat == RepeatMode.All) await _hub.SetLoopAsync("Playlist");
                AppLog.Write("media-hub", $"Native queue ready: {_entries.Count} tracks, repeat={snapshot.Repeat}.");
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Already transferred tracks keep playing. At the end, the coordinator
            // retries the missing successor when the process is runnable again.
            AppLog.Write("media-hub", $"Queue preparation interrupted: {ex.GetType().Name}. Resolved tracks remain available.");
        }
        finally
        {
            lock (_sync) { if (_preparation == preparation) _preparation = null; }
            preparation.Dispose();
        }
    }

    private Task<string> PrepareUriAsync(TrackInfo track, ResolvedMedia media, CancellationToken ct)
    {
        var name = Guid.NewGuid().ToString("N");
        if (_prepareTrack is not null)
            return _prepareTrack(track, media, Path.Combine(_spool, name), ct);
        var url = media.Url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile) return Task.FromResult(uri.AbsoluteUri);
        var path = uri?.IsFile == true ? uri.LocalPath : Path.GetFullPath(url);
        var pinned = Path.Combine(_spool, name + Path.GetExtension(path));
        File.Copy(path, pinned, overwrite: true);
        return Task.FromResult(new Uri(pinned).AbsoluteUri);
    }

    private async Task PollLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(750));
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                await Ordered(PollAsync);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task PollAsync()
    {
        if (_current is null) return;
        try
        {
            var state = await _hub.SnapshotAsync();
            // Media Hub's Metadata uses /Track/N, while TrackList uses another
            // prefix. The numeric suffix is the same per-session identity.
            var entry = _entries.Find(e => Suffix(e.Id) == Suffix(state.TrackId));
            if (entry is not null && entry.Id != _current.Id)
            {
                var previous = _current.Request.Key;
                _current = entry;
                ++_chain;
                _faultedKey = null;
                _publishing = true;
                try { EventRaised?.Invoke(new QueuePositionChangedEvent(previous, entry.Request.Key, entry.Track, entry.Index)); }
                finally { _publishing = false; }
                AppLog.Write("media-hub", $"Native queue moved to row {entry.Index}.");
            }
            var key = _current.Request.Key;
            var error = Interlocked.Exchange(ref _error, 0);
            if (error != 0) { Fault($"Media Hub playback error {error}."); return; }
            if (_ended && state.Status == "Stopped")
            {
                _ended = false;
                EventRaised?.Invoke(new PlaybackEndedEvent(key));
                return;
            }
            var status = state.Status switch
            {
                "Playing" => PlaybackState.Playing,
                "Paused" => PlaybackState.Paused,
                "Stopped" => PlaybackState.Stopped,
                _ => (PlaybackState?)null,
            };
            if (status is { } playback) EventRaised?.Invoke(new RemotePlaybackStateEvent(key, playback));
            EventRaised?.Invoke(new ProgressEvent(key, state.Position, TimeSpan.Zero,
                state.Duration ?? _current.Request.Duration, false, _chain));
        }
        catch (Exception ex)
        {
            await _hub.DisposeAsync();
            Fault($"Media Hub connection failed ({ex.GetType().Name}).");
        }
    }

    private static string Suffix(string path) => path[(path.LastIndexOf('/') + 1)..];
    private void Fault(string reason)
    {
        if (_disposed || _current is not { } current || current.Request.Key == _faultedKey) return;
        _faultedKey = current.Request.Key;
        AppLog.Write("media-hub", reason);
        EventRaised?.Invoke(new PlayerFaultedEvent(current.Request.Key, reason));
    }

    private Task Ordered(Func<Task> action)
    {
        lock (_sync)
        {
            if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(MediaHubAudioPlayer)));
            _tail = _tail.ContinueWith(async _ => { if (!_disposed) await action(); },
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            return _tail;
        }
    }
    private async void Forget(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Fault($"Media Hub command failed ({ex.GetType().Name})."); }
    }

    public void Dispose()
    {
        ReleaseSession();
        _termination?.Dispose();
    }

    private void ReleaseSession()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _preparation?.Cancel();
        }
        _queue.Changed -= QueueChanged;
        _lifetime.Cancel();
        _hub.Ended -= OnEnded;
        _hub.Error -= OnError;
        _hub.Dispose();
    }
}
