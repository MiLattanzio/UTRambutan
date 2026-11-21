using Rambutan.Integrations.UbuntuTouch;
using Tmds.DBus.Protocol;

namespace Rambutan.Integrations.Tests;

public sealed class MediaHubConnectionTests
{
    [Fact]
    public async Task DisposingExplicitlyDestroysTheCreatedSessionOnTheSameBusConnection()
    {
        using var bus = await PrivateBus.StartAsync();
        using var server = new DBusConnection(bus.Address);
        await server.ConnectAsync();
        var handler = new Service();
        server.AddMethodHandler(handler);
        await server.RequestNameAsync(MediaHubConnection.Service, RequestNameOptions.None);
        using var client = new MediaHubConnection(bus.Address);
        await client.ConnectAsync();
        await client.SeekAsync(TimeSpan.FromSeconds(17.25));
        Assert.Equal(17_250_000UL, handler.LastSeek);
        await client.DisposeAsync();
        client.Dispose();
        Assert.Equal("rambutan-session-test", handler.DestroyedUuid);
        Assert.Equal(1, handler.DestroyCount);
        Assert.Equal(handler.Creator, handler.Destroyer);
        Assert.Equal((short)2, handler.Role);
    }

    private sealed class Service : IPathMethodHandler
    {
        public string Path => MediaHubConnection.ServicePath;
        public bool HandlesChildPaths => true;
        public string? Creator { get; private set; }
        public string? Destroyer { get; private set; }
        public string? DestroyedUuid { get; private set; }
        public int DestroyCount { get; private set; }
        public short Role { get; private set; }
        public ulong LastSeek { get; private set; }
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var member = context.Request.MemberAsString;
            var r = context.Request.GetBodyReader();
            if (member == "CreateSession") Creator = context.Request.SenderAsString;
            else if (member == "DestroySession")
            {
                DestroyedUuid = r.ReadString();
                Destroyer = context.Request.SenderAsString;
                DestroyCount++;
            }
            else if (member == "Set")
            {
                Assert.Equal(MediaHubConnection.Player, r.ReadString());
                Assert.Equal("AudioStreamRole", r.ReadString());
                Role = r.ReadVariantValue().GetInt16();
            }
            else if (member == "Seek") LastSeek = r.ReadUInt64();
            else { context.ReplyUnknownMethodError(); return default; }

            var w = context.CreateReplyWriter(member == "CreateSession" ? "os" : null);
            try
            {
                if (member == "CreateSession")
                {
                    w.WriteObjectPath(new ObjectPath(Path + "/sessions/42"));
                    w.WriteString("rambutan-session-test");
                }
                context.Reply(w.CreateMessage());
            }
            finally { w.Dispose(); }
            return default;
        }
    }
}
