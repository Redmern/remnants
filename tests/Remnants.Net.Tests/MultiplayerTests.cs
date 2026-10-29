using Remnants.Core.Scavenge;
using Xunit.Abstractions;

namespace Remnants.Net.Tests;

/// <summary>Scripted two-player games over the loopback transport against the real GameHost and data/.</summary>
public class MultiplayerTests(ITestOutputHelper output)
{
    [Fact]
    public void Lobby_seats_players_and_starts_once_everyone_is_ready()
    {
        var f = new NetFixture();
        var a = f.Join("Alice");
        Assert.Equal(1, a.Id);
        Assert.Equal(ClientPhase.Lobby, a.Client.Phase);
        a.Client.SetReady(true);
        Assert.Equal(HostPhase.Lobby, f.Host.Phase); // one player is below the minimum

        var b = f.Join("Bob");
        Assert.Equal(2, b.Id);
        Assert.Equal(["Alice", "Bob"], a.Client.Lobby!.Players.Select(p => p.Name));
        b.Client.SetReady(true);

        Assert.Equal(HostPhase.Playing, f.Host.Phase);
        foreach (var p in new[] { a, b })
        {
            Assert.Equal(ClientPhase.Playing, p.Client.Phase);
            Assert.NotNull(p.Client.View);
            Assert.Equal(f.Sim.State.Map.Tiles, p.View.Map.Tiles);
            Assert.Equal(2, p.View.Units.Count(u => u.PlayerId == p.Id));
        }
        Assert.Equal(2, f.Sim.State.Parties.Count);
        f.AssertNoLeaks();
    }

    [Fact]
    public void Joins_are_refused_for_a_wrong_version_a_bad_party_or_a_running_mission()
    {
        var f = new NetFixture();
        var old = f.Connect("Old");
        old.Link.Send(new Join(Protocol.Version + 1, "Old", NetFixture.Party("O")));
        Assert.Contains("version", old.Last<JoinRefused>().Reason);
        Assert.False(old.Link.IsOpen);

        var cheat = f.Connect("Cheat");
        var party = NetFixture.Party("C");
        party[0].Equipment![Remnants.Core.Items.EquipSlot.Weapon] = "laser_cannon";
        cheat.Client.Join("Cheat", party);
        Assert.Contains("laser_cannon", cheat.Last<JoinRefused>().Reason);

        var family = f.Connect("Family");
        var withChild = NetFixture.Party("F");
        withChild[1].Survivor.LifeStage = Remnants.Core.Survivors.LifeStage.Child;
        family.Client.Join("Family", withChild);
        Assert.Contains("child", family.Last<JoinRefused>().Reason);

        var big = f.Connect("Big");
        big.Client.Join("Big", NetFixture.Party("G", 9));
        Assert.Contains("limited", big.Last<JoinRefused>().Reason);

        f.StartTwo();
        var late = f.Join("Late");
        Assert.Contains("under way", late.Last<JoinRefused>().Reason);
    }

    [Fact]
    public void Only_the_player_whose_unit_is_up_may_act_and_ids_cannot_be_spoofed()
    {
        var f = new NetFixture();
        var (a, b) = f.StartTwo();
        var up = f.PlayerUp(a, b)!;
        var other = up == a ? b : a;
        var unitId = f.Sim.State.CurrentUnitId!.Value;

        var spoof = other.Do(new EndTurn(up.Id, unitId));
        Assert.Equal(ScavengeError.NotYourUnit, spoof.Error);

        var otherUnit = f.Sim.State.Units.First(u => u.PlayerId == other.Id);
        var early = other.Do(new EndTurn(other.Id, otherUnit.Id));
        Assert.Equal(ScavengeError.NotYourTurn, early.Error);

        var snapshots = other.Link.Inbox.OfType<Snapshot>().Count();
        var ok = up.Do(new EndTurn(up.Id, unitId));
        Assert.True(ok.Ok, ok.Message);
        Assert.True(other.Link.Inbox.OfType<Snapshot>().Count() > snapshots, "the other player hears about the turn");
        Assert.NotEqual(unitId, f.Sim.State.CurrentUnitId);
        f.AssertNoLeaks();
    }

    [Fact]
    public void Turns_interleave_by_initiative_across_both_parties_and_the_ai()
    {
        var f = new NetFixture();
        var (a, b) = f.StartTwo();
        var actors = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var up = f.PlayerUp(a, b)!;
            actors.Add(up.Id);
            var unit = f.Sim.State.CurrentUnitId!.Value;
            Assert.Equal(unit, up.Last<TurnChanged>().UnitId);
            Assert.Equal(up.Id, (up == a ? b : a).Last<TurnChanged>().PlayerId);
            Assert.True(up.Do(new EndTurn(up.Id, unit)).Ok);
        }
        Assert.Contains(a.Id, actors);
        Assert.Contains(b.Id, actors);
        Assert.True(f.Sim.State.Round >= 3);
        // Both clients agree with the server on the round.
        Assert.Equal(f.Sim.State.Round, a.View.Round);
        Assert.Equal(f.Sim.State.Round, b.View.Round);
        f.AssertNoLeaks();
    }

    [Fact]
    public void Players_can_attack_each_others_units_and_both_hear_about_it()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var (a, b) = f.StartTwo();
        f.EndTurnsUntil(() => f.PlayerUp(a, b) == a, a, b);
        var shooter = f.Sim.State.CurrentUnit!;
        var victim = f.Sim.State.Units.First(u => u.PlayerId == b.Id && u.IsActive);
        victim.Pos = f.FreeNeighbour(shooter.Pos); // stage the duel (server-side, as a scenario setup)
        shooter.Ap = f.Sim.Profile(shooter).MaxAp;

        var action = f.Sim.Profile(shooter).Actions.First(x => Combat.CheckReach(f.Sim.Data, f.Sim.State.Map, shooter.Pos, x, victim.Pos) == ScavengeError.None);
        var result = a.Do(new Attack(a.Id, shooter.Id, victim.Id, action.Id));
        Assert.True(result.Ok, result.Message);

        bool AboutTheShot(ScavengeEvent e) => e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed && e.UnitId == shooter.Id && e.TargetId == victim.Id;
        Assert.Contains(a.Last<Snapshot>().Events, AboutTheShot);
        Assert.Contains(b.Last<Snapshot>().Events, AboutTheShot);
        // The victim sees the shooter now (adjacent), so it is in Bob's view with its redacted record.
        var seen = b.View.Units.Single(u => u.Id == shooter.Id);
        Assert.Equal(0, seen.Origin!.Id);
        Assert.DoesNotContain(55.0, seen.Origin.Attributes.Values);
        f.AssertNoLeaks();
    }

    [Fact]
    public void Each_side_only_receives_what_it_sees()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var (a, b) = f.StartTwo();
        var sim = f.Sim;
        var hidden = sim.State.Units.Where(u => u.OnMap && u.Kind == UnitKind.Enemy
            && !Visibility.SideVisible(sim.Data, sim.State, Unit.PlayerSide(a.Id)).Contains(sim.State.Map.Index(u.Pos))).ToList();
        Assert.NotEmpty(hidden); // the zone has enemies out of sight at the start
        foreach (var u in hidden) Assert.DoesNotContain(a.View.Units, v => v.Id == u.Id);
        Assert.True(a.View.Units.Count < sim.State.Units.Count);

        // Play a stretch of turns; the fixture checks every message against the live server state.
        f.EndTurnsUntil(() => sim.State.Round >= 6, a, b);
        f.AssertNoLeaks();
        Assert.Equal([Unit.PlayerSide(a.Id)], a.View.Explored.Keys);

        // Whenever an unseen unit is up, the view shows a placeholder, not who or where.
        foreach (var s in a.Link.Inbox.OfType<Snapshot>())
            if (s.View.CurrentUnitId is { } id && id != SideFog.HiddenUnitId) Assert.Contains(s.View.Units, u => u.Id == id);
    }

    [Fact]
    public void Remembered_caches_keep_their_last_seen_contents()
    {
        var sim = NewScavenge.Create(NetFixture.Data, "suburb_ruins", 7, [new PartySetup(1, NetFixture.Party("A"))]);
        var fog = new SideFog(1, Unit.PlayerSide(1));
        var me = sim.State.Units.First(u => u.PlayerId == 1);
        var cache = new LootCache { Id = 99, Pos = me.Pos, Items = ["scrap", "scrap"] };
        sim.State.Caches.Add(cache);
        fog.Update(sim);
        Assert.Equal(2, fog.View(sim).Caches.Single(c => c.Id == 99).Items.Count);

        // Out of sight someone empties it: the side still remembers two items until it looks again.
        foreach (var u in sim.State.Units.Where(u => u.PlayerId == 1)) u.Pos = new Pos(0, 0);
        cache.Pos = new Pos(sim.State.Map.Width - 1, 0);
        cache.Items.Clear();
        fog.Update(sim);
        Assert.Equal(2, fog.View(sim).Caches.Single(c => c.Id == 99).Items.Count);
    }

    [Fact]
    public void A_dropped_player_waits_out_their_turns_and_can_reconnect_with_the_token()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0, ReconnectSeconds = 60 });
        var (a, b) = f.StartTwo();
        var token = b.Client.ReconnectToken!;
        var bobUnits = f.Sim.State.Units.Where(u => u.PlayerId == b.Id).Select(u => (u.Id, u.Pos)).ToList();
        b.Link.Close();
        Assert.Contains(a.Link.Inbox.OfType<PlayerLeft>(), p => p.PlayerId == b.Id && p.ReconnectSeconds > 0);

        // Alice can play several rounds; Bob's units are skipped (wait) and hold position.
        var round = f.Sim.State.Round;
        f.EndTurnsUntil(() => f.Sim.State.Round >= round + 3, a);
        foreach (var (id, pos) in bobUnits)
            if (f.Sim.State.FindUnit(id) is { IsActive: true } u) Assert.Equal(pos, u.Pos);

        var back = f.Connect("Bob again");
        back.Client.Join("Bob", [], token);
        Assert.Equal(b.Id, back.Id);
        Assert.Equal(ClientPhase.Playing, back.Client.Phase);
        Assert.NotNull(back.Client.View);
        Assert.Contains(back.View.Units, u => u.PlayerId == b.Id);

        // Bob is in control again: when his unit comes up it waits for him.
        f.EndTurnsUntil(() => f.PlayerUp(a, back) == back, a, back);
        var unit = f.Sim.State.CurrentUnitId;
        f.Host.Tick();
        Assert.Equal(unit, f.Sim.State.CurrentUnitId);
        f.AssertNoLeaks();
    }

    [Fact]
    public void With_nobody_connected_the_instance_holds_still_until_the_seats_are_given_up()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0, ReconnectSeconds = 60 });
        var (a, b) = f.StartTwo();
        a.Link.Close(); // Bob still plays, so Alice's units wait out their turns until Bob is up
        b.Link.Close();
        var round = f.Sim.State.Round;
        var turn = f.Sim.State.CurrentUnitId;
        f.Clock.Advance(30);
        f.Host.Tick();
        Assert.Equal(round, f.Sim.State.Round);
        Assert.Equal(turn, f.Sim.State.CurrentUnitId);
        Assert.Equal(HostPhase.Playing, f.Host.Phase);

        f.Clock.Advance(31);
        f.Host.Tick();
        Assert.Equal(HostPhase.Ended, f.Host.Phase);
        Assert.Equal(round, f.Sim.State.Round);
    }

    [Fact]
    public void The_same_client_session_reconnects_over_a_new_link_and_keeps_playing()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0, ReconnectSeconds = 60 });
        var (a, b) = f.StartTwo();
        b.Link.Close();
        Assert.Equal(ClientPhase.Disconnected, b.Client.Phase);
        Assert.True(b.Client.CanReconnect);
        var bob = b.Client;
        var stale = b.View;

        f.EndTurnsUntil(() => f.Sim.State.Round >= 3, a);
        var link = f.Hub.Connect();
        bob.Reconnect(link);
        Assert.Equal(ClientPhase.Playing, bob.Phase);
        Assert.NotSame(stale, bob.View!.State);
        Assert.Equal(f.Sim.State.Round, bob.View.State.Round);

        var again = new TestPlayer { Link = link, Client = bob, Name = "Bob" };
        f.EndTurnsUntil(() => f.PlayerUp(a, again) == again, a, again);
        Assert.True(again.Do(new EndTurn(bob.PlayerId, f.Sim.State.CurrentUnitId!.Value)).Ok);

        bob.Leave();
        Assert.False(bob.CanReconnect);
    }

    [Fact]
    public void A_seat_is_given_up_after_the_reconnect_window()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0, ReconnectSeconds = 60 });
        var (a, b) = f.StartTwo();
        var token = b.Client.ReconnectToken!;
        b.Link.Close();
        f.Clock.Advance(61);
        f.Host.Tick();
        Assert.Contains(a.Link.Inbox.OfType<PlayerLeft>(), p => p.PlayerId == b.Id && p.ReconnectSeconds == 0);

        var late = f.Connect("Bob");
        late.Client.Join("Bob", [], token);
        Assert.Contains("cannot be retaken", late.Last<JoinRefused>().Reason);
    }

    [Fact]
    public void An_afk_player_times_out_into_a_wait()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 30 });
        var (a, b) = f.StartTwo();
        var up = f.PlayerUp(a, b)!;
        var unit = f.Sim.State.CurrentUnitId;
        Assert.Equal(30, up.Last<TurnChanged>().SecondsLeft, 3);

        f.Clock.Advance(29);
        f.Host.Tick();
        Assert.Equal(unit, f.Sim.State.CurrentUnitId);

        f.Clock.Advance(2);
        f.Host.Tick();
        Assert.NotEqual(unit, f.Sim.State.CurrentUnitId);
        Assert.Contains(a.Link.Inbox.OfType<Notice>(), n => n.Text.Contains("timed out"));
        // The next player's clock starts fresh.
        Assert.Equal(30, a.Last<TurnChanged>().SecondsLeft, 3);
    }

    [Fact]
    public void Each_player_gets_their_own_extraction_result()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var (a, b) = f.StartTwo();
        var exits = f.Sim.State.Map.Exits;

        // Whoever is up walks onto an exit (staged) and extracts, until both parties are out.
        for (var i = 0; i < 200 && f.Host.Phase == HostPhase.Playing; i++)
        {
            var up = f.PlayerUp(a, b)!;
            var unit = f.Sim.State.CurrentUnit!;
            unit.Pos = exits.First(e => f.Sim.State.UnitAt(e) is null);
            Assert.True(up.Do(new Extract(up.Id, unit.Id)).Ok);
        }

        Assert.Equal(HostPhase.Ended, f.Host.Phase);
        foreach (var p in new[] { a, b })
        {
            var result = p.Client.Result!;
            Assert.Equal(p.Id, result.PlayerId);
            Assert.True(result.Complete);
            Assert.Equal(2, result.Returned.Count);
            Assert.All(result.Returned, r => Assert.StartsWith(p.Name[..1], r.Survivor.Name));
            Assert.Single(p.Link.Inbox.OfType<MissionEnded>());
        }
        f.AssertNoLeaks();
    }

    [Fact]
    public void The_mission_ends_when_the_remaining_players_are_done_even_if_someone_left()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var (a, b) = f.StartTwo();
        b.Client.Leave();
        var exits = f.Sim.State.Map.Exits;
        for (var i = 0; i < 200 && f.Host.Phase == HostPhase.Playing; i++)
        {
            var unit = f.Sim.State.CurrentUnit!;
            unit.Pos = exits.First(e => f.Sim.State.UnitAt(e) is null);
            Assert.True(a.Do(new Extract(a.Id, unit.Id)).Ok);
        }
        Assert.Equal(HostPhase.Ended, f.Host.Phase);
        Assert.True(a.Client.Result!.Complete);
    }

    [Fact]
    public void Snapshot_size_per_action_stays_small()
    {
        var f = new NetFixture(new HostOptions { TurnSeconds = 0 });
        var (a, b) = f.StartTwo();
        var start = a.Link.BytesReceived;
        var mission = ProtocolJson.Serialize(a.Last<MissionStarted>()).Length;
        f.EndTurnsUntil(() => f.Sim.State.Round >= 5, a, b);
        var snapshots = a.Link.Inbox.OfType<Snapshot>().Select(s => ProtocolJson.Serialize(s).Length).ToList();
        var full = Remnants.Core.Scavenge.ScavengeSave.Save(f.Sim.State).Length;
        output.WriteLine($"MissionStarted (map) {mission} B; snapshots: n={snapshots.Count} avg {snapshots.Average():0} B max {snapshots.Max()} B; " +
                         $"bytes after start {a.Link.BytesReceived - start}; full indented save {full} B");
        Assert.True(snapshots.Max() < 32 * 1024, $"snapshot of {snapshots.Max()} bytes");
    }
}
