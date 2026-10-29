using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Net.Tests;

/// <summary>A clock the tests move by hand, so turn timers and reconnect windows run without waiting.</summary>
public sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
}

/// <summary>One player at the table: a loopback link and the client session on it.</summary>
public sealed class TestPlayer
{
    public required LoopbackClient Link { get; init; }
    public required NetClient Client { get; init; }
    public required string Name { get; init; }
    public int Id => Client.PlayerId;
    public ScavengeState View => Client.View!.State;
    public T Last<T>() where T : Message => Link.Inbox.OfType<T>().Last();

    /// <summary>Sends an intent; over loopback the verdict is already there when Execute returns.</summary>
    public ScavengeResult Do(IScavengeCommand command)
    {
        var task = Client.Execute(command);
        Assert.True(task.IsCompleted, "loopback answers synchronously");
        return task.Result;
    }
}

/// <summary>
/// A <see cref="GameHost"/> on a loopback hub with a manual clock. Every message a client receives is checked
/// against the server's live state at the moment it is sent, so fog leaks fail the test that caused them.
/// </summary>
public sealed class NetFixture
{
    private static readonly Lazy<ScavengeData> RealData =
        new(() => ScavengeDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));

    public static ScavengeData Data => RealData.Value;

    public ManualClock Clock { get; } = new();
    public GameHost Host { get; }
    public LoopbackHub Hub { get; }
    public List<string> FogViolations { get; } = new();

    public NetFixture(HostOptions? options = null)
    {
        Host = new GameHost(Data, options ?? new HostOptions { TurnSeconds = 30, ReconnectSeconds = 60 }, Clock);
        Hub = new LoopbackHub(Host);
    }

    public ScavengeSim Sim => Host.Sim!;

    public void AssertNoLeaks() => Assert.True(FogViolations.Count == 0, string.Join("\n", FogViolations.Distinct().Take(10)));

    public static List<PartyMember> Party(string prefix, int count = 2, int firstId = 1)
    {
        var party = new List<PartyMember>();
        for (var i = 0; i < count; i++)
        {
            var s = new Survivor { Id = firstId + i, Name = $"{prefix}{i + 1}", Sex = i % 2 == 0 ? Sex.Female : Sex.Male };
            foreach (var a in Enum.GetValues<AttributeKind>()) s.Set(a, 55);
            party.Add(new PartyMember(s, new() { [EquipSlot.Weapon] = "pistol" }, ["bandage"]));
        }
        return party;
    }

    public TestPlayer Connect(string name)
    {
        var link = Hub.Connect();
        TestPlayer? player = null;
        // Checked before the client handles it (the client's read model writes into the view it is handed).
        link.Received += m => CheckFog(player!, m);
        player = new TestPlayer { Link = link, Client = new NetClient(link, Data), Name = name };
        return player;
    }

    public TestPlayer Join(string name, List<PartyMember>? party = null)
    {
        var p = Connect(name);
        p.Client.Join(name, party ?? Party(name[..1]));
        return p;
    }

    /// <summary>Two players seated and ready: the mission is running.</summary>
    public (TestPlayer A, TestPlayer B) StartTwo()
    {
        var a = Join("Alice");
        var b = Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        Assert.Equal(HostPhase.Playing, Host.Phase);
        return (a, b);
    }

    public TestPlayer? PlayerUp(params TestPlayer[] players) =>
        Sim.State.CurrentUnit is { PlayerControlled: true } u ? players.FirstOrDefault(p => p.Id == u.PlayerId) : null;

    /// <summary>Ends turns through the clients until <paramref name="until"/> holds (or a guard runs out).</summary>
    public void EndTurnsUntil(Func<bool> until, params TestPlayer[] players)
    {
        for (var i = 0; i < 400 && !until() && !Sim.State.Finished; i++)
        {
            var up = PlayerUp(players) ?? throw new InvalidOperationException("No connected player is up.");
            var result = up.Do(new EndTurn(up.Id, Sim.State.CurrentUnitId!.Value));
            Assert.True(result.Ok, result.Message);
        }
        Assert.True(until(), "condition never held");
    }

    /// <summary>A free floor tile next to <paramref name="near"/> (for staging fights).</summary>
    public Pos FreeNeighbour(Pos near)
    {
        var map = Sim.State.Map;
        foreach (var d in Pos.Neighbours8)
        {
            var p = near + d;
            if (map.InBounds(p) && map[p] == Terrain.Floor && Sim.State.UnitAt(p) is null && !map.IsExit(p)) return p;
        }
        throw new InvalidOperationException($"No free tile next to {near}.");
    }

    private void CheckFog(TestPlayer player, Message message)
    {
        if (Host.Sim is not { } sim || player.Client.PlayerId == 0) return;
        var side = Unit.PlayerSide(player.Client.PlayerId);
        var visible = Visibility.SideVisible(sim.Data, sim.State, side);
        var map = sim.State.Map;
        switch (message)
        {
            case Snapshot s:
                foreach (var u in s.View.Units.Where(u => u.Side != side && u.PlayerId != player.Client.PlayerId))
                    if (!u.OnMap || !visible.Contains(map.Index(u.Pos)))
                        FogViolations.Add($"{player.Name} was sent {u.Name} at {u.Pos}, which their side cannot see.");
                foreach (var u in s.View.Units.Where(u => u.Side != side && u.Ai.TargetId is not null))
                    FogViolations.Add($"{player.Name} was sent {u.Name}'s AI memory.");
                if (s.View.RngState != 0) FogViolations.Add($"{player.Name} was sent the RNG state.");
                if (s.View.Explored.Keys.Any(k => k != side)) FogViolations.Add($"{player.Name} was sent another side's explored tiles.");
                foreach (var c in s.View.Caches)
                    if (!sim.State.Explored.GetValueOrDefault(side, []).Contains(map.Index(c.Pos)))
                        FogViolations.Add($"{player.Name} was sent cache {c.Id} on a tile they never saw.");
                break;
            case TurnChanged t when t.UnitId is { } id:
                var unit = sim.State.FindUnit(id)!;
                if (unit.Side != side && !visible.Contains(map.Index(unit.Pos)))
                    FogViolations.Add($"{player.Name} was told unseen {unit.Name} is up.");
                break;
        }
    }
}
