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

    /// <summary>The weather is clear unless a test asks for some (a server otherwise rolls it from the seed).</summary>
    public NetFixture(HostOptions? options = null, ScavengeData? data = null)
    {
        options ??= new HostOptions { TurnSeconds = 30, ReconnectSeconds = 60 };
        Host = new GameHost(data ?? Data, options.Weather is null ? options with { Weather = "clear" } : options, Clock);
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

    public TestPlayer Join(string name, List<PartyMember>? party = null, FieldVehicle? vehicle = null)
    {
        var p = Connect(name);
        p.Client.Join(name, party ?? Party(name[..1]), vehicle: vehicle);
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

    /// <summary>Sends the tile strike at <paramref name="target"/>'s tile, then ends turns until it has come down.</summary>
    public ScavengeResult StrikeNow(TestPlayer by, Unit attacker, Unit target, string actionId, params TestPlayer[] players)
    {
        var action = Sim.Profile(attacker).Action(actionId);
        var strike = action is null ? null : TileStrikes.StrikeAt(Sim, attacker, action, target);
        var result = by.Do(strike ?? new AttackTile(by.Id, attacker.Id, target.Pos, actionId));
        if (result.Ok) EndTurnsUntil(() => !TileStrikes.Readied(Sim.State, attacker), players);
        return result;
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

    /// <summary>Per player: the units their last snapshot showed (events may name those).</summary>
    private readonly Dictionary<string, HashSet<int>> _lastShown = new();

    private void CheckFog(TestPlayer player, Message message)
    {
        if (Host.Sim is not { } sim || player.Client.PlayerId == 0) return;
        var me = player.Client.PlayerId;
        // The side the player is on right now: their team's while allied (team fog), their own otherwise.
        var side = sim.State.FindParty(me)?.Side ?? Unit.PlayerSide(me);
        var seen = Detection.SeenBy(sim.Data, sim.State, side);
        var map = sim.State.Map;
        bool Theirs(Unit u) => u.Side == side || u.PlayerId == player.Client.PlayerId;
        switch (message)
        {
            case Snapshot s:
                foreach (var u in s.View.Units.Where(u => !Theirs(u)))
                    if (!u.OnMap || !seen.Contains(u.Id))
                        FogViolations.Add($"{player.Name} was sent {u.Name} at {u.Pos}, whom their side cannot see.");
                var shown = s.View.Units.Select(u => u.Id).ToHashSet();
                var before = _lastShown.GetValueOrDefault(player.Name, []);
                foreach (var e in s.Events)
                {
                    if (e.SeenBy is not null) FogViolations.Add($"{player.Name} was sent who saw '{e.Message}'.");
                    if (e.TargetId is { } tid && sim.State.FindUnit(tid) is { } tu && !Theirs(tu) && !shown.Contains(tid) && !before.Contains(tid))
                        FogViolations.Add($"{player.Name} heard '{e.Message}' about {tu.Name}, whom they never saw.");
                    if (e.Anonymous is not null) FogViolations.Add($"{player.Name} was sent the actor-less text of '{e.Message}'.");
                    if (e.Side is { } only && only != side) FogViolations.Add($"{player.Name} was sent {only}'s private event '{e.Message}'.");
                    // FogLog names an actor the side saw when the event was logged (SeenBy), even if it is gone from sight by now.
                    bool SeenWhenLogged(int actor) => sim.State.Events.Any(o =>
                        o.UnitId == actor && o.Round == e.Round && o.Type == e.Type && o.Message == e.Message && o.SeenBy?.Contains(side) == true);
                    if (e.UnitId is { } actor && sim.State.FindUnit(actor) is { } a && !Theirs(a) && !shown.Contains(actor) && !before.Contains(actor)
                        && !SeenWhenLogged(actor))
                        FogViolations.Add($"{player.Name} heard '{e.Message}' naming {a.Name}, whom they never saw.");
                }
                _lastShown[player.Name] = shown;
                foreach (var c in s.View.Contacts.Where(c => c.Side != side))
                    FogViolations.Add($"{player.Name} was sent {c.Side}'s contact at {c.Pos}.");
                foreach (var x in s.View.Sensors.Where(x => x.Side != side))
                    FogViolations.Add($"{player.Name} was sent {x.Side}'s motion sensor at {x.Pos}.");
                foreach (var r in s.View.Reveals.Where(r => r.Side != side || !shown.Contains(r.UnitId)))
                    FogViolations.Add($"{player.Name} was sent a reveal of unit {r.UnitId} for {r.Side}.");
                foreach (var u in s.View.Units.Where(u => u.Side != side && u.Ai.TargetId is not null))
                    FogViolations.Add($"{player.Name} was sent {u.Name}'s AI memory.");
                foreach (var u in s.View.Units.Where(u => u.PlayerId != me && (u.Plan is { CardId: not null } or { TargetId: not null } || u.OutplayedId is not null)))
                    FogViolations.Add($"{player.Name} was sent {u.Name}'s plan card, plan target or dodge.");
                foreach (var u in s.View.Units.Where(u => u.PlayerId != me && (u.SlotCards.Count > 0 || u.SlotSpent.Count > 0 || u.LastCardId is not null || u.CounterId is not null)))
                    FogViolations.Add($"{player.Name} was sent {u.Name}'s slot cards, last card or counter.");
                foreach (var u in s.View.Units.Where(u => u.PlayerId != me && (u.CardHit != 0 || u.Guard != 0 || u.CardCover != 0 || u.CardMove != 0 || u.Plan is { MoveBonus: not 0 })))
                    FogViolations.Add($"{player.Name} was sent {u.Name}'s card buffs.");
                foreach (var d in s.View.Decks.Where(d => d.PlayerId != me && (!d.Hidden || d.Hand.Count + d.DrawPile.Count + d.Used.Count + d.Consumed.Count > 0)))
                    FogViolations.Add($"{player.Name} was sent player {d.PlayerId}'s cards.");
                foreach (var p in s.View.Parties.Where(p => p.PlayerId != me && p.CardPouch.Count > 0))
                    FogViolations.Add($"{player.Name} was sent player {p.PlayerId}'s card pouch.");
                foreach (var p in s.View.Parties.Where(p => p.PlayerId != me && (p.Bestiary.Count > 0 || p.SeenIds.Count > 0 || p.FoughtIds.Count > 0)))
                    FogViolations.Add($"{player.Name} was sent player {p.PlayerId}'s bestiary.");
                if (s.View.CardRngState != 0) FogViolations.Add($"{player.Name} was sent the card stream.");
                foreach (var d in s.View.Decks.Where(d => d.PlayerId == me && !d.DrawPile.SequenceEqual(d.DrawPile.OrderBy(x => x, StringComparer.Ordinal))))
                    FogViolations.Add($"{player.Name} was sent their draw pile in draw order.");
                foreach (var d in s.View.Decks.Where(d => d.PlayerId == me
                             && (d.UsedCount != d.Used.Count || d.DrawCount != d.DrawPile.Count || d.HandCount != d.Hand.Count)))
                    FogViolations.Add($"{player.Name}'s own deck counts do not match its cards.");
                foreach (var e in s.Events.Where(e => e.Type is ScavengeEventType.CardPlayed or ScavengeEventType.CardDiscarded or ScavengeEventType.CardFound
                             or ScavengeEventType.DeckShuffled && e.Side != side))
                    FogViolations.Add($"{player.Name} heard another side's card event '{e.Message}'.");
                var sideExplored = sim.State.Explored.GetValueOrDefault(side) ?? [];
                foreach (var c in s.View.Caches.Where(c => !sideExplored.Contains(map.Index(c.Pos))))
                    FogViolations.Add($"{player.Name} was sent the pile at {c.Pos}, which their side never saw.");
                foreach (var n in s.View.SalvageNodes.Where(n => !sideExplored.Contains(map.Index(n.Pos))))
                    FogViolations.Add($"{player.Name} was sent the salvage node at {n.Pos}, which their side never saw.");
                if (s.View.LootSeed != 0) FogViolations.Add($"{player.Name} was sent the loot seed.");
                foreach (var p in s.View.Pings.Where(p => p.Side != side))
                    FogViolations.Add($"{player.Name} was sent {p.Side}'s ping at {p.Pos}.");
                foreach (var o in s.View.Proposals.Where(o => o.FromPlayerId != me && o.ToPlayerId != me))
                    FogViolations.Add($"{player.Name} was sent the offer between players {o.FromPlayerId} and {o.ToPlayerId}.");
                foreach (var b in s.View.Breaks.Where(b => b.Side != side))
                    FogViolations.Add($"{player.Name} was sent {b.Side}'s break.");
                foreach (var u in s.View.Units.Where(u => u.PlayerId != me && u.Origin is { } o && o.Attributes.Values.Any(v => v > 0)))
                    FogViolations.Add($"{player.Name} was sent {u.Name}'s colony record.");
                foreach (var p in s.View.Parties.Where(p => p.Side != side && p.Side != Unit.PlayerSide(p.PlayerId)))
                    FogViolations.Add($"{player.Name} was told player {p.PlayerId} is on {p.Side}.");
                foreach (var u in s.View.Units.Where(u => u.PlayerId is { } o && u.Side != side && u.Side != Unit.PlayerSide(o) && u.Side != Unit.NeutralSide))
                    FogViolations.Add($"{player.Name} was told {u.Name} is on {u.Side}.");
                foreach (var e in s.Events.Where(e => e.Type.ToString().StartsWith("Alliance") && (e.UnitId is not null || e.Pos is not null)))
                    FogViolations.Add($"{player.Name} got an alliance event placing a unit: '{e.Message}'.");
                var explored = sim.State.Explored.GetValueOrDefault(side, []);
                foreach (var d in s.View.Devices)
                {
                    if (!explored.Contains(map.Index(d.Pos))) FogViolations.Add($"{player.Name} was sent device {d.Id} on a tile they never saw.");
                    if (d.Items.Count > 0) FogViolations.Add($"{player.Name} was sent what device {d.Id} holds.");
                    if (d.Progress.Any(p => p.Side != side) || d.Lockouts.Any(l => l.Side != side))
                        FogViolations.Add($"{player.Name} was sent another side's hack on device {d.Id}.");
                    if (d.Controller != side && d.Controller != Unit.EnemySide && (d.ControllerPlayerId is not null || !d.Controller.StartsWith("player:")))
                        FogViolations.Add($"{player.Name} was told device {d.Id} works for {d.Controller}.");
                    if (d.UnitId is { } du && !shown.Contains(du)) FogViolations.Add($"{player.Name} was sent device {d.Id}'s unseen unit.");
                }
                foreach (var m in s.View.Marks.Where(m => m.Side != side || !shown.Contains(m.UnitId)))
                    FogViolations.Add($"{player.Name} was sent {m.Side}'s mark on unit {m.UnitId}.");
                foreach (var u in s.View.Units)
                    if ((u.DroneId is { } dr && !shown.Contains(dr)) || (u.OperatorId is { } op && !shown.Contains(op)))
                        FogViolations.Add($"{player.Name} was sent {u.Name}'s link to a drone or operator they cannot see.");
                foreach (var u in s.View.Units.Where(u => u.PlayerId != me && (u.Hooks.Count > 0 || u.DroneBattery is not null)))
                    FogViolations.Add($"{player.Name} was sent {u.Name}'s perks or drone battery.");
                if (s.View.RngState != 0) FogViolations.Add($"{player.Name} was sent the RNG state.");
                if (s.View.Explored.Keys.Any(k => k != side)) FogViolations.Add($"{player.Name} was sent another side's explored tiles.");
                foreach (var c in s.View.Caches)
                    if (!sim.State.Explored.GetValueOrDefault(side, []).Contains(map.Index(c.Pos)))
                        FogViolations.Add($"{player.Name} was sent cache {c.Id} on a tile they never saw.");
                break;
            case AllianceProposed m when m.FromPlayerId != me && m.ToPlayerId != me:
            case AllianceDeclined d when d.FromPlayerId != me && d.ToPlayerId != me:
            case AllianceAccepted a when !a.Team.Contains(me):
            case AllianceBroken b when b.PlayerId != me && !b.FormerAllies.Contains(me):
                FogViolations.Add($"{player.Name} was told of someone else's alliance: {message}.");
                break;
            case AllianceBreakWarning w when sim.State.FindParty(w.PlayerId)?.Side != side:
                FogViolations.Add($"{player.Name} was warned of a break on another team: {message}.");
                break;
            case TurnChanged t when t.UnitId is { } id:
                var unit = sim.State.FindUnit(id)!;
                if (!Theirs(unit) && !seen.Contains(unit.Id))
                    FogViolations.Add($"{player.Name} was told unseen {unit.Name} is up.");
                break;
        }
    }
}
