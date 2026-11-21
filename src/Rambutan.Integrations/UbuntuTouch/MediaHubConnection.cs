using Rambutan.Core.Diagnostics;
using Tmds.DBus.Protocol;

namespace Rambutan.Integrations.UbuntuTouch;

internal sealed record HubSnapshot(string Status, string TrackId, TimeSpan Position, TimeSpan? Duration);

internal interface IMediaHubConnection : IDisposable, IAsyncDisposable
{
    event Action? Ended;
    event Action<short>? Error;
    Task ConnectAsync();
    Task<bool> OpenAsync(string uri);
    Task CommandAsync(string command);
    Task SeekAsync(TimeSpan position);
    Task SetVolumeAsync(double volume);
    Task SetLoopAsync(string loop);
    Task<string[]> TracksAsync();
    Task AddAsync(string uri);
    Task RemoveAsync(string id);
    Task<HubSnapshot> SnapshotAsync();
}

/// <summary>The Media Hub wire protocol is not standard MPRIS: Position/Duration
/// are nanoseconds, Seek is an absolute uint64 in microseconds, and track ids
/// in TrackList methods are strings. No bus name or background policy is needed.</summary>
internal sealed class MediaHubConnection(string? address = null) : IMediaHubConnection
{
    internal const string Service = "com.lomiri.MediaHub.Service";
    internal const string ServicePath = "/com/lomiri/MediaHub/Service";
    internal const string Player = "org.mpris.MediaPlayer2.Player";
    internal const string TrackList = "org.mpris.MediaPlayer2.TrackList";
    private const string Properties = "org.freedesktop.DBus.Properties";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);
    private DBusConnection? _bus;
    private string? _path;
    private string? _uuid;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly SemaphoreSlim _disposeGate = new(1, 1);
    public event Action? Ended;
    public event Action<short>? Error;

    public async Task ConnectAsync()
    {
        if (_bus is not null) return;
        _bus = new DBusConnection(address ?? DBusAddress.Session
            ?? throw new IOException("Media Hub requires a session bus."));
        try
        {
            await _bus.ConnectAsync().AsTask().WaitAsync(Timeout).ConfigureAwait(false);
            (_path, _uuid) = await CallAsync(ServicePath, Service, "CreateSession", null, null,
                static (m, _) =>
                {
                    var r = m.GetBodyReader();
                    return (r.ReadObjectPath().ToString(), r.ReadString());
                }).ConfigureAwait(false);
            _subscriptions.Add(await _bus.AddMatchAsync(
                new MatchRule { Sender = Service, Path = _path, Interface = Player,
                    Member = "EndOfStream", Type = MessageType.Signal },
                static (_, _) => true,
                (Notification<bool> n) => { if (n.HasValue) Ended?.Invoke(); },
                emitOnCapturedContext: false).ConfigureAwait(false));
            _subscriptions.Add(await _bus.AddMatchAsync(
                new MatchRule { Sender = Service, Path = _path, Interface = Player,
                    Member = "Error", Type = MessageType.Signal },
                static (m, _) => m.GetBodyReader().ReadInt16(),
                (Notification<short> n) => { if (n.HasValue && n.Value != 0) Error?.Invoke(n.Value); },
                emitOnCapturedContext: false).ConfigureAwait(false));
            await SetAsync("AudioStreamRole", (short)2).ConfigureAwait(false);
            AppLog.Write("media-hub", $"Session created at {_path}; multimedia role, native audio backend.");
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<bool> OpenAsync(string uri) => CallAsync(_path!, Player, "OpenUri", "s",
        (ref MessageWriter w) => w.WriteString(uri), static (m, _) => m.GetBodyReader().ReadBool());
    public Task CommandAsync(string command) => EmptyAsync(_path!, Player, command);
    public Task SeekAsync(TimeSpan position) => EmptyAsync(_path!, Player, "Seek", "t",
        (ref MessageWriter w) => w.WriteUInt64((ulong)Math.Max(0, position.Ticks / 10)));
    public Task SetVolumeAsync(double volume) => SetAsync("Volume", volume);
    public Task SetLoopAsync(string loop) => SetAsync("LoopStatus", loop);
    public Task AddAsync(string uri) => EmptyAsync(_path + "/TrackList", TrackList, "AddTrack", "ssb",
        (ref MessageWriter w) =>
        {
            w.WriteString(uri);
            w.WriteString("/org/mpris/MediaPlayer2/TrackList/NoTrack");
            w.WriteBool(false);
        });
    public Task RemoveAsync(string id) => EmptyAsync(_path + "/TrackList", TrackList, "RemoveTrack", "s",
        (ref MessageWriter w) => w.WriteString(id));

    public Task<string[]> TracksAsync() => CallAsync(_path + "/TrackList", Properties, "Get", "ss",
        (ref MessageWriter w) => { w.WriteString(TrackList); w.WriteString("Tracks"); },
        static (m, _) => m.GetBodyReader().ReadVariantValue().GetArray<string>());

    public Task<HubSnapshot> SnapshotAsync() => CallAsync(_path!, Properties, "GetAll", "s",
        (ref MessageWriter w) => w.WriteString(Player), static (m, _) =>
        {
            var p = m.GetBodyReader().ReadDictionaryOfStringToVariantValue();
            var metadata = p["Metadata"].GetDictionary<string, VariantValue>();
            var id = metadata.TryGetValue("mpris:trackid", out var value)
                ? value.Type == VariantValueType.ObjectPath ? value.GetObjectPath().ToString() : value.GetString()
                : "";
            var duration = p["Duration"].GetInt64();
            return new HubSnapshot(p["PlaybackStatus"].GetString(), id,
                FromNanoseconds(p["Position"].GetInt64()), duration > 0 ? FromNanoseconds(duration) : null);
        });

    internal static TimeSpan FromNanoseconds(long value) => TimeSpan.FromTicks(Math.Max(0, value) / 100);

    private Task SetAsync(string name, VariantValue value) => EmptyAsync(_path!, Properties, "Set", "ssv",
        (ref MessageWriter w) => { w.WriteString(Player); w.WriteString(name); w.WriteVariant(value); });
    private delegate void Arguments(ref MessageWriter writer);
    private Task EmptyAsync(string path, string iface, string member, string? signature = null, Arguments? args = null)
        => CallAsync(path, iface, member, signature, args, static (_, _) => true);
    private Task<T> CallAsync<T>(string path, string iface, string member, string? signature,
        Arguments? args, MessageValueReader<T> reader)
    {
        var w = _bus!.GetMessageWriter();
        MessageBuffer message;
        try
        {
            w.WriteMethodCallHeader(Service, path, iface, member, signature, MessageFlags.None);
            args?.Invoke(ref w);
            message = w.CreateMessage();
        }
        finally { w.Dispose(); }
        return BoundedAsync(_bus.CallMethodAsync(message, reader, null), Timeout);
    }

    private static async Task<T> BoundedAsync<T>(Task<T> pending, TimeSpan timeout)
    {
        try { return await pending.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            // WaitAsync does not cancel the D-Bus operation. Observe any later
            // failure when disconnecting a timed-out connection.
            _ = pending.ContinueWith(static task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            throw;
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await _disposeGate.WaitAsync().ConfigureAwait(false);
        try { await CloseAsync().ConfigureAwait(false); }
        finally { _disposeGate.Release(); }
    }

    private async Task CloseAsync()
    {
        // Some shipped Media Hub versions do not notice a vanished bus client.
        // Destroy explicitly under the same AppArmor identity that created it.
        // Bound shutdown even when the service is unresponsive.
        if (_bus is not null && _uuid is { } uuid)
        {
            _uuid = null;
            try
            {
                await BoundedAsync(CallAsync(ServicePath, Service, "DestroySession", "s",
                    (ref MessageWriter w) => w.WriteString(uuid), static (_, _) => true),
                    TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                AppLog.Write("media-hub", "Session destroyed.");
            }
            catch (Exception ex) { AppLog.Write("media-hub", $"Session cleanup: {ex.GetType().Name}."); }
        }
        foreach (var subscription in _subscriptions) subscription.Dispose();
        _subscriptions.Clear();
        // Never detach the session on app exit.
        _bus?.Dispose();
        _bus = null;
        _path = _uuid = null;
    }
}
