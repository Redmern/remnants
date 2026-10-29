using System.Net;
using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>The real TCP transport end to end: two clients on localhost join, start and play a turn.</summary>
public class TcpTests
{
    private static async Task Until(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition(), $"timed out waiting for {what}");
    }

    [Fact]
    public async Task Two_clients_play_over_localhost_tcp()
    {
        var host = new GameHost(NetFixture.Data, new HostOptions { TurnSeconds = 0 });
        using var server = new TcpServer(host, IPAddress.Loopback, 0);
        server.Start();

        var clients = new List<NetClient>();
        var gate = new object();
        foreach (var name in new[] { "Alice", "Bob" })
        {
            var link = await TcpClientTransport.ConnectAsync("127.0.0.1", server.Port);
            // Tests have no main thread: handle messages under one lock, as the game does on its main thread.
            var client = new NetClient(link, NetFixture.Data, work => { lock (gate) work(); });
            link.Start();
            client.Join(name, NetFixture.Party(name[..1]));
            client.SetReady(true);
            clients.Add(client);
        }

        await Until(() => clients.All(c => c.Phase == ClientPhase.Playing && c.View is not null), "the mission to start");
        await Until(() => clients.All(c => c.Turn is not null), "the first turn");
        var up = clients.Single(c => c.PlayerId == c.Turn!.PlayerId);
        var unit = up.Turn!.UnitId!.Value;

        var result = await up.Execute(new EndTurn(up.PlayerId, unit));
        Assert.True(result.Ok, result.Message);
        server.Run(() => Assert.NotEqual(unit, host.Sim!.State.CurrentUnitId));

        var rejected = await up.Execute(new Move(up.PlayerId, 9999, new Pos(0, 0)));
        Assert.Equal(ScavengeError.UnknownUnit, rejected.Error);
    }
}
