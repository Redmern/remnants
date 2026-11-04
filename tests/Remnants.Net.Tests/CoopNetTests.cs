using Remnants.Core.Scavenge;

namespace Remnants.Net.Tests;

/// <summary>
/// Co-op over the wire (loopback, real GameHost and data/): a co-op lobby, team fog (allies get each other's sight,
/// nobody else does), alliances formed and broken in the field with their messages going only to the players involved,
/// team pings, co-op commands, and whole scripted games with two allies and with two allies against a third player.
/// Every message is checked against the server's live state by <see cref="NetFixture"/>.
/// </summary>
public class CoopNetTests
{
    private static HostOptions Coop(int players = 2) => new() { Mode = MissionMode.Coop, MinPlayers = players, TurnSeconds = 30, ReconnectSeconds = 60 };

    private static List<Unit> UnitsOf(NetFixture f, TestPlayer p) =>
        f.Sim.State.Units.Where(u => u.PlayerId == p.Id && u.PlayerControlled).ToList();

    /// <summary>
    /// Plays every connected player's turns until the mission ends: extract on an exit, else shoot a hostile in reach,
    /// else walk toward the nearest exit, else wait. Uses only each client's own view, like a real client.
    /// </summary>
    private static void PlayOut(NetFixture f, params TestPlayer[] players)
    {
        for (var i = 0; i < 3000 && f.Host.Phase == HostPhase.Playing; i++)
        {
            var up = f.PlayerUp(players) ?? throw new InvalidOperationException("No connected player is up.");
            var view = up.Client.View!;
            var unit = view.State.FindUnit(f.Sim.State.CurrentUnitId!.Value)!;
            var map = view.State.Map;
            IScavengeCommand command;
            if (map.IsExit(unit.Pos)) command = new Extract(up.Id, unit.Id);
            else
            {
                var profile = view.Profile(unit);
                var shot = profile.Actions.Where(a => a.ApCost <= unit.Ap)
                    .SelectMany(a => view.State.Units.Where(t => t.IsActive && unit.HostileTo(t)
                                                                  && Combat.CheckReach(view.Data, map, unit.Pos, a, t.Pos) == ScavengeError.None)
                        .Select(t => (Strike: TileStrikes.StrikeAt(view, unit, a, t), Target: t)))
                    .Where(o => o.Strike is not null)
                    .OrderBy(o => o.Target.Id).FirstOrDefault();
                if (shot.Strike is { } strike) command = strike;
                else
                {
                    var exit = map.Exits.OrderBy(e => e.Chebyshev(unit.Pos)).ThenBy(map.Index).First();
                    var field = Movement.DistanceField(map, profile.Movement, exit);
                    var here = field.GetValueOrDefault(unit.Pos, int.MaxValue);
                    var step = view.Reachable(unit)
                        .Where(kv => kv.Value <= unit.Ap && field.ContainsKey(kv.Key) && field[kv.Key] < here)
                        .OrderBy(kv => field[kv.Key]).ThenBy(kv => map.Index(kv.Key))
                        .Select(kv => (Pos?)kv.Key).FirstOrDefault();
                    command = step is { } to ? new Move(up.Id, unit.Id, to) : new EndTurn(up.Id, unit.Id);
                }
            }
            var result = up.Do(command);
            if (!result.Ok) Assert.True(up.Do(new EndTurn(up.Id, unit.Id)).Ok);
        }
        Assert.Equal(HostPhase.Ended, f.Host.Phase);
    }

    [Fact]
    public void A_co_op_lobby_puts_everyone_on_one_team_and_they_share_sight()
    {
        var f = new NetFixture(Coop());
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        b.Client.SetTeam(5); // ignored in co-op
        Assert.Equal(MissionMode.Coop, a.Client.Lobby!.Mode);
        Assert.All(a.Client.Lobby.Players, p => Assert.Equal(1, p.Team));
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        Assert.Equal(HostPhase.Playing, f.Host.Phase);

        Assert.Equal(MissionMode.Coop, a.Client.Mission!.Mode);
        Assert.True(f.Sim.State.Allied(a.Id, b.Id));
        Assert.Equal(f.Sim.State.FindParty(a.Id)!.Side, a.View.Parties.Single(p => p.PlayerId == b.Id).Side);
        // Each client gets the ally's units whatever they see, and the same visible tiles.
        Assert.Equal(4, a.View.Units.Count(u => u.Kind == UnitKind.Survivor));
        Assert.Equal(a.Client.Visible, b.Client.Visible);
        var allyUnit = a.View.Units.First(u => u.PlayerId == b.Id);
        Assert.True(allyUnit.Origin!.Attributes.Values.All(v => v == 0), "an ally's colony record stays with them");
        f.AssertNoLeaks();
    }

    [Fact]
    public void Two_allies_play_a_whole_mission_and_each_gets_their_own_result()
    {
        var f = new NetFixture(Coop());
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);

        // Hand a bandage over and ping a tile on the way.
        var (giver, taker) = (UnitsOf(f, a)[0], UnitsOf(f, b)[0]);
        var up = f.PlayerUp(a, b)!;
        var mine = f.Sim.State.CurrentUnit!;
        var other = up == a ? taker : giver;
        other.Pos = f.FreeNeighbour(mine.Pos);
        Assert.True(up.Do(new PingTile(up.Id, mine.Id, other.Pos, PingKind.GoHere)).Ok);
        Assert.Contains(a.View.Pings, p => p.Pos == other.Pos);
        Assert.Contains(b.View.Pings, p => p.Pos == other.Pos);
        Assert.Contains(b.Client.TakeEvents().Concat(a.Client.TakeEvents()), e => e.Type == ScavengeEventType.Pinged);
        mine.Ap = 2;
        Assert.True(up.Do(new GiveItem(up.Id, mine.Id, other.Id, "bandage")).Ok, "give over the wire");
        Assert.Equal(2, f.Sim.State.FindUnit(other.Id)!.Pack.Count(i => i == "bandage"));
        // A blow at an ally's tile comes down on them: the aim only warns.
        var swing = f.Sim.Profile(mine).Actions.First(x => TileStrikes.Aim(f.Sim, mine, x, other.Pos) is not null).Id;
        Assert.True(up.Do(new AttackTile(up.Id, mine.Id, other.Pos, swing)).Ok, "a strike at an ally's tile over the wire");
        f.EndTurnsUntil(() => !TileStrikes.Readied(f.Sim.State, mine), a, b);
        Assert.Contains(f.Sim.State.Events, e => e.UnitId == mine.Id && e.TargetId == other.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);

        PlayOut(f, a, b);
        var ra = a.Last<MissionEnded>().Result;
        var rb = b.Last<MissionEnded>().Result;
        Assert.Equal(a.Id, ra.PlayerId);
        Assert.Equal(b.Id, rb.PlayerId);
        Assert.All(ra.Returned.Concat(ra.Deaths.Select(d => new ReturnedSurvivor(d, false, [], []))), r => Assert.StartsWith("A", r.Survivor.Name));
        Assert.All(rb.Returned, r => Assert.StartsWith("B", r.Survivor.Name));
        f.AssertNoLeaks();
    }

    [Fact]
    public void Two_allies_against_a_third_player_share_nothing_with_the_rival()
    {
        var f = new NetFixture(new HostOptions { MinPlayers = 3, TurnSeconds = 30, ReconnectSeconds = 60, ZoneId = "outskirts" });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        var c = f.Join("Carol");
        a.Client.SetTeam(1);
        b.Client.SetTeam(1);
        Assert.Equal([1, 1, null], c.Client.Lobby!.Players.Select(p => p.Team));
        foreach (var p in new[] { a, b, c }) p.Client.SetReady(true);
        Assert.Equal(HostPhase.Playing, f.Host.Phase);
        Assert.True(f.Sim.State.Allied(a.Id, b.Id));
        Assert.False(f.Sim.State.Allied(a.Id, c.Id));
        Assert.Equal([1, 1, null], a.Client.Mission!.Players.Select(p => p.Team));

        // A ping from the team never reaches Carol; the rival never sees allied units it cannot spot.
        var up = f.PlayerUp(a, b);
        if (up is not null) Assert.True(up.Do(new PingTile(up.Id, f.Sim.State.CurrentUnitId!.Value, new Pos(3, 3), PingKind.Danger)).Ok);
        Assert.Empty(c.View.Pings);
        Assert.DoesNotContain(c.Client.TakeEvents(), e => e.Type == ScavengeEventType.Pinged);

        PlayOut(f, a, b, c);
        Assert.All(new[] { a, b, c }, p => Assert.NotNull(p.Client.Result));
        f.AssertNoLeaks();
    }

    [Fact]
    public void An_alliance_formed_in_the_field_shares_sight_until_it_breaks()
    {
        var f = new NetFixture(new HostOptions { MinPlayers = 3, TurnSeconds = 30, ReconnectSeconds = 60 });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        var c = f.Join("Carol");
        foreach (var p in new[] { a, b, c }) p.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b, c) == a, a, b, c);
        var alice = f.Sim.State.CurrentUnit!;
        var bob = UnitsOf(f, b);
        bob[0].Pos = f.FreeNeighbour(alice.Pos);

        Assert.Equal(ScavengeError.NotAPlayer, a.Do(new ProposeAlliance(a.Id, alice.Id, 9)).Error);
        Assert.True(a.Do(new ProposeAlliance(a.Id, alice.Id, b.Id)).Ok);
        Assert.Equal(b.Id, a.Last<AllianceProposed>().ToPlayerId);
        Assert.Equal(a.Id, b.Last<AllianceProposed>().FromPlayerId);
        Assert.Empty(c.Link.Inbox.OfType<AllianceProposed>());
        Assert.Contains(b.View.Proposals, p => p.FromPlayerId == a.Id);
        Assert.Empty(c.View.Proposals);

        f.EndTurnsUntil(() => f.PlayerUp(a, b, c) == b, a, b, c);
        Assert.True(b.Do(new RespondAlliance(b.Id, f.Sim.State.CurrentUnitId!.Value, a.Id, Accept: true)).Ok);
        Assert.Equal([a.Id, b.Id], a.Last<AllianceAccepted>().Team.Order());
        Assert.NotNull(b.Last<AllianceAccepted>());
        Assert.Empty(c.Link.Inbox.OfType<AllianceAccepted>());

        // Shared sight: Alice now gets all of Bob's units, wherever they are.
        var current = f.Sim.State.CurrentUnit!;
        Assert.True(b.Do(new Lookout(b.Id, current.Id)).Ok);
        Assert.Equal(bob.Count, a.View.Units.Count(u => u.PlayerId == b.Id));
        Assert.Equal(a.Client.Visible, b.Client.Visible);

        // Bob announces the break; Alice and Bob are warned, Carol is not; it holds a full round.
        Assert.True(b.Do(new BreakAlliance(b.Id, current.Id)).Ok);
        var warning = a.Last<AllianceBreakWarning>();
        Assert.Equal(b.Id, warning.PlayerId);
        Assert.Empty(c.Link.Inbox.OfType<AllianceBreakWarning>());
        Assert.Equal(f.Sim.State.Round + 2, warning.EffectiveRound);
        f.EndTurnsUntil(() => f.Sim.State.Round == warning.EffectiveRound - 1, a, b, c);
        Assert.True(f.Sim.State.Allied(a.Id, b.Id));
        f.EndTurnsUntil(() => f.Sim.State.Round == warning.EffectiveRound, a, b, c);
        Assert.False(f.Sim.State.Allied(a.Id, b.Id));
        Assert.Equal([a.Id], b.Last<AllianceBroken>().FormerAllies);
        Assert.Equal(b.Id, a.Last<AllianceBroken>().PlayerId);
        Assert.Empty(c.Link.Inbox.OfType<AllianceBroken>());

        // From that moment Alice only sees Bob's units her own eyes pick out.
        var seen = Detection.SeenBy(f.Sim.Data, f.Sim.State, f.Sim.State.FindParty(a.Id)!.Side);
        Assert.All(a.View.Units.Where(u => u.PlayerId == b.Id), u => Assert.Contains(u.Id, seen));
        Assert.NotEqual(f.Sim.State.FindParty(a.Id)!.Side, a.View.Parties.Single(p => p.PlayerId == b.Id).Side);
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_declined_offer_is_told_to_both_and_nobody_else()
    {
        var f = new NetFixture(new HostOptions { MinPlayers = 3, TurnSeconds = 30, ReconnectSeconds = 60 });
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        var c = f.Join("Carol");
        foreach (var p in new[] { a, b, c }) p.Client.SetReady(true);
        f.EndTurnsUntil(() => f.PlayerUp(a, b, c) == a, a, b, c);
        UnitsOf(f, b)[0].Pos = f.FreeNeighbour(f.Sim.State.CurrentUnit!.Pos);
        Assert.True(a.Do(new ProposeAlliance(a.Id, f.Sim.State.CurrentUnitId!.Value, b.Id)).Ok);
        f.EndTurnsUntil(() => f.PlayerUp(a, b, c) == b, a, b, c);
        Assert.True(b.Do(new RespondAlliance(b.Id, f.Sim.State.CurrentUnitId!.Value, a.Id, Accept: false)).Ok);
        Assert.False(a.Last<AllianceDeclined>().Lapsed);
        Assert.Contains("declined", a.Client.Describe(a.Last<AllianceDeclined>()));
        Assert.Empty(c.Link.Inbox.OfType<AllianceDeclined>());
        f.AssertNoLeaks();
    }

    [Fact]
    public void A_downed_party_whose_ally_leaves_for_good_gets_its_result_and_the_mission_ends()
    {
        var f = new NetFixture(Coop());
        var a = f.Join("Alice");
        var b = f.Join("Bob");
        a.Client.SetReady(true);
        b.Client.SetReady(true);
        foreach (var u in UnitsOf(f, b))
        {
            u.Status = UnitStatus.Downed;
            u.Stabilized = true;
        }
        f.Sim.UpdateParties();
        Assert.False(f.Sim.State.FindParty(b.Id)!.Done, "waiting for Alice to carry them out");

        a.Client.Leave();
        Assert.True(f.Sim.State.FindParty(a.Id)!.Left);
        Assert.False(b.Last<MissionEnded>().Result.Returned.Any());
        Assert.Equal(HostPhase.Ended, f.Host.Phase);
        f.AssertNoLeaks();
    }
}
