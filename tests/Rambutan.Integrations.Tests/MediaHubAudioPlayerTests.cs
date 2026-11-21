using System.Collections.Concurrent;
using Rambutan.Core.Models;
using Rambutan.Core.Playback;
using Rambutan.Core.Queue;
using Rambutan.Core.Settings;
using Rambutan.Integrations.UbuntuTouch;

namespace Rambutan.Integrations.Tests;

public sealed class MediaHubAudioPlayerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rambutan-hub-tests-" + Guid.NewGuid());
    private readonly QueueModel _queue = new();
    private readonly Streams _streams = new();
    private readonly Hub _hub = new();
    private readonly MediaHubAudioPlayer _player;
    private readonly PlaybackCoordinator _coordinator;
    public MediaHubAudioPlayerTests()
    {
        _player = new(_queue, _streams, Path.Combine(_directory, "pins"), _hub);
        _coordinator = new(_streams, _player, _queue, new PlaybackSettings { Autoplay = false });
    }

    private static TrackInfo Track(string id) => new() { VideoId = id, Title = id, Artists = [], Duration = TimeSpan.FromSeconds(30) };
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }

    [Fact]
    public async Task TrackMetadataIsPreparedForEveryNativeQueueEntryIncludingDuplicates()
    {
        var queue = new QueueModel();
        var hub = new Hub();
        var prepared = new ConcurrentDictionary<string, TrackInfo>();
        using var player = new MediaHubAudioPlayer(queue, _streams, Path.Combine(_directory, "metadata"), hub,
            (track, source, output, ct) =>
            {
                var uri = new Uri(output + ".mka").AbsoluteUri;
                prepared[uri] = track;
                return Task.FromResult(uri);
            });
        using var coordinator = new PlaybackCoordinator(_streams, player, queue, new PlaybackSettings { Autoplay = false });
        var first = Track("a") with { Title = "Titolo = # uno", Artists = [new("Artista", null)], ThumbnailUrl = "https://art.test/one" };
        var second = Track("a") with { Title = "Seconda versione", Artists = [new("Altro artista", null)], ThumbnailUrl = "https://art.test/two" };
        await coordinator.PlayQueueAsync([first, second]);
        await Until(() => hub.Count == 2);
        Assert.Same(first, prepared[hub.Uris[0]]);
        Assert.Same(second, prepared[hub.Uris[1]]);
        Assert.NotEqual(hub.Uris[0], hub.Uris[1]);
        hub.MoveTo(1);
        await Until(() => ReferenceEquals(coordinator.NowPlaying, second));
        Assert.Equal(1, hub.Opens);
    }

    [Fact]
    public async Task ReplacingPlaybackCancelsSuccessorMetadataPreparation()
    {
        var queue = new QueueModel();
        var hub = new Hub();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var player = new MediaHubAudioPlayer(queue, _streams, Path.Combine(_directory, "cancel-metadata"), hub,
            async (track, source, output, ct) =>
            {
                if (track.VideoId == "b")
                {
                    started.SetResult();
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { cancelled.SetResult(); throw; }
                }
                return "https://prepared.test/" + track.VideoId;
            });
        using var coordinator = new PlaybackCoordinator(_streams, player, queue, new PlaybackSettings { Autoplay = false });
        await coordinator.PlayQueueAsync([Track("a"), Track("b")]);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await coordinator.PlayQueueAsync([Track("new")]);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(["https://prepared.test/new"], hub.Uris);
    }

    [Fact]
    public async Task EntireQueueIsTransferredAndMissedTracksAreReconciledFromSnapshot()
    {
        var tracks = new[] { Track("a"), Track("b"), Track("c") };
        await _coordinator.PlayQueueAsync(tracks);
        await Until(() => _hub.Count == 3);
        // The service runs alone: no intermediate callbacks for b, just the
        // snapshot the client reads when it gets CPU time again.
        _hub.MoveTo(2, "Paused", TimeSpan.FromSeconds(12));
        await Until(() => ReferenceEquals(_coordinator.NowPlaying, tracks[2]) && _coordinator.State == PlaybackState.Paused);
        Assert.Equal(2, _queue.CurrentIndex);
        Assert.Equal(1, _hub.Opens);
        _hub.Status = "Playing";
        await Until(() => _coordinator.State == PlaybackState.Playing);
    }

    [Fact]
    public async Task DuplicateVideosKeepTheirQueueRowIdentity()
    {
        var tracks = new[] { Track("a"), Track("a"), Track("a") };
        await _coordinator.PlayQueueAsync(tracks);
        await Until(() => _hub.Count == 3);
        _hub.MoveTo(2);
        await Until(() => _queue.CurrentIndex == 2);
        Assert.Same(tracks[2], _coordinator.NowPlaying);
    }

    [Fact]
    public async Task QueueEditReplacesNativeSuccessorsWithoutRestartingCurrentTrack()
    {
        await _coordinator.PlayQueueAsync([Track("a"), Track("b"), Track("c")]);
        await Until(() => _hub.Count == 3);
        _queue.Remove(1);
        await Until(() => _hub.Count == 2 && _hub.Uris.Last().EndsWith("/c"));
        Assert.Equal(1, _hub.Opens);
        _hub.MoveTo(1);
        await Until(() => _coordinator.NowPlaying?.VideoId == "c");
        Assert.Equal(1, _queue.CurrentIndex);
    }

    [Fact]
    public async Task NewPlayCancelsAnOldQueueResolution()
    {
        var blocked = new TaskCompletionSource<ResolvedMedia>(TaskCreationOptions.RunContinuationsAsynchronously);
        _streams.Blocked = blocked.Task;
        await _coordinator.PlayQueueAsync([Track("a"), Track("blocked")]);
        await Until(() => _streams.Requests.Contains("blocked"));
        await _coordinator.PlayQueueAsync([Track("new")]);
        blocked.SetResult(new("https://audio.test/obsolete", "opus"));
        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.Single(_hub.Uris);
        Assert.EndsWith("/new", _hub.Uris[0]);
    }

    [Fact]
    public async Task SeekAndTransportDoNotLoseTransferredQueue()
    {
        await _coordinator.PlayQueueAsync([Track("a"), Track("b")]);
        await Until(() => _hub.Count == 2);
        await _coordinator.SeekAsync(TimeSpan.FromSeconds(17));
        Assert.Equal(TimeSpan.FromSeconds(17), _hub.Position);
        _coordinator.Pause();
        await Until(() => _hub.Status == "Paused");
        _coordinator.Resume();
        await Until(() => _hub.Status == "Playing");
        Assert.Equal(2, _hub.Count);
    }

    [Theory]
    [InlineData(RepeatMode.One, "Track")]
    [InlineData(RepeatMode.All, "Playlist")]
    public async Task RepeatIsOwnedByService(RepeatMode mode, string expected)
    {
        _queue.Repeat = mode;
        await _coordinator.PlayQueueAsync([Track("a"), Track("b"), Track("c")], 1);
        await Until(() => _hub.Loop == expected);
        if (mode == RepeatMode.All)
            Assert.Equal(new[] { "https://audio.test/b", "https://audio.test/c", "https://audio.test/a" }, _hub.Uris);
    }

    [Fact]
    public async Task CachedFilesArePinnedUntilPlaybackIsReplaced()
    {
        Directory.CreateDirectory(_directory);
        var source = Path.Combine(_directory, "original.ogg");
        await File.WriteAllTextAsync(source, "audio", TestContext.Current.CancellationToken);
        _streams.Local = source;
        await _coordinator.PlayQueueAsync([Track("a"), Track("b")]);
        await Until(() => _hub.Count == 2);
        File.Delete(source);
        Assert.All(_hub.Uris, uri => Assert.True(File.Exists(new Uri(uri).LocalPath)));
    }

    [Fact]
    public async Task EndOfNativeQueueStopsCoordinator()
    {
        await _coordinator.PlayQueueAsync([Track("a")]);
        _hub.Finish();
        await Until(() => _coordinator.State == PlaybackState.Stopped);
    }

    [Fact]
    public async Task FailedOpenAwaitsAsynchronousSessionCleanup()
    {
        _queue.SetQueue([Track("a")]);
        _hub.RefuseOpen = true;
        await Assert.ThrowsAsync<StreamOpenException>(() => _player.PlayAsync(
            new("a#1", "https://audio.test/a", "a", null, TimeSpan.Zero)));
        Assert.Equal(1, _hub.AsyncDisposals);
        Assert.Equal(0, _hub.SyncDisposals);
    }

    [Fact]
    public async Task OpeningAndReplacingPlaybackPreservesTheSystemVolume()
    {
        // The coordinator restores the desktop preference before the first
        // play. Neither that value nor a new play may reset the phone's volume.
        _coordinator.Volume = 0.8;
        await _coordinator.PlayQueueAsync([Track("a")]);
        Assert.Empty(_hub.VolumeWrites);
        _coordinator.Volume = 0.25;
        await Until(() => _hub.VolumeWrites.Count == 1);
        Assert.Equal(0.25, _hub.VolumeWrites.Single());
        await _coordinator.PlayQueueAsync([Track("b")]);
        Assert.Single(_hub.VolumeWrites);
    }

    [Fact]
    public async Task ServiceRestartCreatesNewSessionAndRecoversPlayback()
    {
        await _coordinator.PlayQueueAsync([Track("a")]);
        _hub.FailNextSnapshot = true;
        await Until(() => _hub.Opens == 2 && _coordinator.State == PlaybackState.Playing);
        Assert.True(_hub.Connections >= 2);
        Assert.Empty(_hub.VolumeWrites);
    }

    [Fact]
    public async Task ExternalStopThenPlayResynchronizesWithoutOpeningTheTrackAgain()
    {
        await _coordinator.PlayQueueAsync([Track("a")]);
        _hub.Status = "Stopped";
        await Until(() => _coordinator.State == PlaybackState.Stopped);
        _hub.Status = "Playing";
        await Until(() => _coordinator.State == PlaybackState.Playing);
        Assert.Equal(1, _hub.Opens);
    }

    [Fact]
    public void PositionUsesNanosecondsRatherThanMprisMicroseconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(42), MediaHubConnection.FromNanoseconds(42_000_000_000));
        Assert.Equal(TimeSpan.Zero, MediaHubConnection.FromNanoseconds(-1));
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _player.Dispose();
        Assert.True(_hub.Disposed);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class Streams : IStreamSource
    {
        public ConcurrentBag<string> Requests { get; } = [];
        public Task<ResolvedMedia>? Blocked { get; set; }
        public string? Local { get; set; }
        public Task<ResolvedMedia> ResolveAsync(string id, bool bypassCache = false, CancellationToken ct = default)
        {
            Requests.Add(id);
            return id == "blocked" && Blocked is not null ? Blocked.WaitAsync(ct)
                : Task.FromResult(new ResolvedMedia(Local ?? "https://audio.test/" + id, "opus"));
        }
    }

    private sealed class Hub : IMediaHubConnection
    {
        private readonly object _sync = new();
        private readonly List<(string Id, string Uri)> _tracks = [];
        private int _serial;
        private int _current;
        public int Opens { get; private set; }
        public int Connections { get; private set; }
        public bool FailNextSnapshot { get; set; }
        public bool RefuseOpen { get; set; }
        public int AsyncDisposals { get; private set; }
        public int SyncDisposals { get; private set; }
        public ConcurrentQueue<double> VolumeWrites { get; } = new();
        public bool Disposed { get; private set; }
        public string Status { get; set; } = "Stopped";
        public string Loop { get; private set; } = "None";
        public TimeSpan Position { get; private set; }
        public string[] Uris { get { lock (_sync) return _tracks.Select(t => t.Uri).ToArray(); } }
        public int Count { get { lock (_sync) return _tracks.Count; } }
        public event Action? Ended;
        public event Action<short>? Error { add { } remove { } }
        public Task ConnectAsync() { Connections++; return Task.CompletedTask; }
        public Task<bool> OpenAsync(string uri)
        {
            lock (_sync) { _tracks.Clear(); _current = 0; _tracks.Add(($"/list/{++_serial}", uri)); Opens++; }
            return Task.FromResult(!RefuseOpen);
        }
        public Task CommandAsync(string command)
        { Status = command switch { "Play" => "Playing", "Pause" => "Paused", _ => "Stopped" }; return Task.CompletedTask; }
        public Task SeekAsync(TimeSpan position) { Position = position; return Task.CompletedTask; }
        public Task SetVolumeAsync(double volume) { VolumeWrites.Enqueue(volume); return Task.CompletedTask; }
        public Task SetLoopAsync(string loop) { Loop = loop; return Task.CompletedTask; }
        public Task<string[]> TracksAsync() { lock (_sync) return Task.FromResult(_tracks.Select(t => t.Id).ToArray()); }
        public Task AddAsync(string uri) { lock (_sync) _tracks.Add(($"/list/{++_serial}", uri)); return Task.CompletedTask; }
        public Task RemoveAsync(string id) { lock (_sync) _tracks.RemoveAll(t => t.Id == id); return Task.CompletedTask; }
        public Task<HubSnapshot> SnapshotAsync()
        {
            if (FailNextSnapshot) { FailNextSnapshot = false; throw new IOException("Service restarted"); }
            lock (_sync) return Task.FromResult(new HubSnapshot(Status,
                _tracks.Count == 0 ? "" : "/Track/" + _tracks[_current].Id.Split('/').Last(), Position, TimeSpan.FromSeconds(30)));
        }
        public void MoveTo(int index, string status = "Playing", TimeSpan position = default)
        { lock (_sync) { _current = index; Status = status; Position = position; } }
        public void Finish() { Status = "Stopped"; Ended?.Invoke(); }
        public void Dispose() { Disposed = true; SyncDisposals++; }
        public ValueTask DisposeAsync() { Disposed = true; AsyncDisposals++; return ValueTask.CompletedTask; }
    }
}
