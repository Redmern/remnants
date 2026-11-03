using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// Co-op between players: teams share a side (no friendly fire, one fog of war, backs and overwatch), alliances formed in
/// the field and broken with a warning round, items handed over, the downed carried out, team pings, per-player
/// extraction, co-op spawns and enemy scaling.
/// </summary>
public class CoopTests
{
    private static Party PartyOf(ScavengeSim sim, int playerId) => sim.State.FindParty(playerId)!;

    /// <summary>Ends turns until <paramref name="unit"/> is up (other players' units just wait).</summary>
    private static void TurnOf(ScavengeSim sim, Unit unit)
    {
        for (var guard = 0; guard < 50 && sim.State.CurrentUnitId != unit.Id; guard++)
        {
            var current = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(current.PlayerId!.Value, current.Id)).Ok);
        }
        Assert.Equal(unit.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void Allies_never_shoot_each_other_and_overwatch_ignores_them()
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(2, 1), weapon: "pipe");
        var sim = Arena(10, 6, [a, b]);
        Assert.True(a.HostileTo(b), "two players start hostile");

        var side = Alliances.Merge(sim.State, 1, 2);
        Assert.Equal(side, a.Side);
        Assert.Equal(side, b.Side);
        Assert.Equal(side, PartyOf(sim, 2).Side);
        Assert.True(sim.State.Allied(1, 2));
        Assert.False(a.HostileTo(b));

        // A blow at an ally's tile comes down on nobody.
        Assert.True(StrikeNow(sim, a, b, "swing").Ok);
        Assert.Equal(100, b.Hp);
        Assert.Contains(sim.State.Events, e => e.UnitId == a.Id && e.Type == ScavengeEventType.IntentFizzled);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);

        Assert.True(sim.Execute(new SetStance(1, a.Id, Stance.Overwatch)).Ok);
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.True(sim.Execute(new Move(2, b.Id, new(3, 2))).Ok);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Overwatch && e.TargetId == b.Id);
        Assert.Equal(100, b.Hp);
    }

    [Fact]
    public void Allies_see_what_any_of_them_sees_and_count_for_each_others_backs()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(18, 1));
        var husk = EnemyUnit("husk", new(16, 1));
        var sim = Arena(20, 4, [a, b, husk]);
        var p1 = PartyOf(sim, 1).Side;
        Assert.False(Detection.SideSees(sim.Data, sim.State, p1, husk));
        Assert.DoesNotContain(sim.State.Map.Index(husk.Pos), sim.VisibleTo(1));
        sim.State.Contacts.Add(new Contact(PartyOf(sim, 2).Side, new(15, 1), ContactKind.Noise, 1));

        var team = Alliances.Merge(sim.State, 1, 2);
        Assert.True(Detection.SideSees(sim.Data, sim.State, team, husk), "b's eyes are a's eyes now");
        Assert.Contains(sim.State.Map.Index(husk.Pos), sim.VisibleTo(1));
        Assert.Contains(sim.State.Contacts, c => c.Side == team && c.Pos == new Pos(15, 1));
        Assert.DoesNotContain(sim.State.Contacts, c => c.Side.StartsWith("player:"));

    }

    [Fact]
    public void An_ally_next_to_the_target_watches_their_back()
    {
        string[] corridor =
        [
            "###########",
            "#.........#",
            "#.........#",
            "#.#########",
            "#..PPPPP.##",
            "#.#########",
            "#.........#",
            "#.........#",
            "###########",
        ];
        var u = SurvivorUnit(1, new(1, 4), attributes: 20);
        var ally = SurvivorUnit(2, new(1, 5), attributes: 20);
        var linked = EnemyUnit("linked", new(8, 4));
        var sim = Arena(11, 9, [u, ally, linked], corridor);
        Assert.False(Combat.AmbushAgainst(sim.Data, sim.State, linked, u)!.Backs, "a rival player next to you is no help");
        Alliances.Merge(sim.State, 1, 2);
        Assert.True(Combat.AmbushAgainst(sim.Data, sim.State, linked, u)!.Backs);
    }

    [Fact]
    public void An_alliance_needs_contact_and_an_answer_on_the_other_players_turn()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(28, 1));
        var sim = Arena(30, 4, [a, b]);

        Assert.Equal(ScavengeError.NoContact, sim.Execute(new ProposeAlliance(1, a.Id, 2)).Error);
        b.Pos = new(4, 1);
        Assert.True(Alliances.InContact(sim, 1, 2));
        Assert.True(sim.Execute(new ProposeAlliance(1, a.Id, 2)).Ok);
        Assert.Equal(ScavengeError.ProposalPending, sim.Execute(new ProposeAlliance(1, a.Id, 2)).Error);
        Assert.Equal(ScavengeError.NotYourTurn, sim.Execute(new RespondAlliance(2, b.Id, 1, Accept: true)).Error);
        Assert.Equal(sim.Profile(a).MaxAp, a.Ap); // talking is free

        var offer = sim.State.Events.Last(e => e.Type == ScavengeEventType.AllianceProposed);
        Assert.Equal([Unit.PlayerSide(1), Unit.PlayerSide(2)], offer.Sides);

        TurnOf(sim, b);
        Assert.Equal(ScavengeError.NoProposal, sim.Execute(new RespondAlliance(2, b.Id, 3, Accept: true)).Error);
        Assert.True(sim.Execute(new RespondAlliance(2, b.Id, 1, Accept: true)).Ok);
        Assert.True(sim.State.Allied(1, 2));
        Assert.Empty(sim.State.Proposals);
        Assert.Equal(ScavengeError.AlreadyAllied, sim.Execute(new ProposeAlliance(2, b.Id, 1)).Error);
    }

    [Fact]
    public void A_declined_or_unanswered_offer_leaves_everyone_hostile()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(4, 1));
        var sim = Arena(10, 4, [a, b]);
        Assert.True(sim.Execute(new ProposeAlliance(1, a.Id, 2)).Ok);
        TurnOf(sim, b);
        Assert.True(sim.Execute(new RespondAlliance(2, b.Id, 1, Accept: false)).Ok);
        Assert.False(sim.State.Allied(1, 2));
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.AllianceDeclined);

        TurnOf(sim, a);
        Assert.True(sim.Execute(new ProposeAlliance(1, a.Id, 2)).Ok);
        for (var round = sim.State.Round; sim.State.Round < round + sim.Data.Rules.Coop.ProposalRounds;)
            Assert.True(sim.Execute(new EndTurn(sim.State.CurrentUnit!.PlayerId!.Value, sim.State.CurrentUnitId!.Value)).Ok);
        Assert.Empty(sim.State.Proposals);
    }

    [Fact]
    public void Alliance_and_party_log_lines_name_the_players_or_fall_back_to_player_n()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(4, 1));
        var sim = Arena(10, 4, [a, b], ["E........."]);
        PartyOf(sim, 1).Name = "Alice";
        string Line(ScavengeEventType type) => sim.State.Events.Last(e => e.Type == type).Message;

        Assert.Equal("Alliance offered to Player 2.", sim.Execute(new ProposeAlliance(1, a.Id, 2)).Message);
        Assert.Equal("Alice offers Player 2 an alliance.", Line(ScavengeEventType.AllianceProposed));
        PartyOf(sim, 2).Name = "Rook";
        TurnOf(sim, b);
        Assert.True(sim.Execute(new RespondAlliance(2, b.Id, 1, Accept: true)).Ok);
        Assert.Equal("Alice and Rook are allies now: shared sight, no friendly fire.", Line(ScavengeEventType.AllianceAccepted));
        Assert.True(sim.Execute(new BreakAlliance(2, b.Id)).Ok);
        Assert.StartsWith("Rook will leave the alliance", Line(ScavengeEventType.AllianceBreakWarning));

        TurnOf(sim, a);
        Assert.True(sim.Execute(new Move(1, a.Id, new(0, 0))).Ok);
        Assert.True(sim.Execute(new Extract(1, a.Id)).Ok);
        Assert.Equal("Alice's run is over.", Line(ScavengeEventType.PartyDone));

        var named = NewScavenge.Create(Real, "suburb_ruins", 5, [Party(1) with { Name = "Alice" }, Party(2)]);
        Assert.Equal("Alice", named.State.PlayerName(1));
        Assert.Equal("Player 2", named.State.PlayerName(2));
        var node = System.Text.Json.Nodes.JsonNode.Parse(ScavengeSave.Save(named.State))!;
        Assert.Equal("Alice", ScavengeSave.Load(Real, node.ToJsonString()).State.PlayerName(1));
        node["scavenge"]!["parties"]![0]!.AsObject().Remove("name"); // a save from before names
        Assert.Equal("Player 1", ScavengeSave.Load(Real, node.ToJsonString()).State.PlayerName(1));
    }

    [Fact]
    public void Breaking_an_alliance_takes_a_full_warning_round_and_ends_shared_sight_then()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(18, 1));
        var husk = EnemyUnit("husk", new(16, 2));
        husk.Ai.Mode = AiMode.Idle;
        var sim = Arena(20, 4, [a, b, husk]);
        var team = Alliances.Merge(sim.State, 1, 2);
        sim.State.Contacts.Add(new Contact(team, new(15, 1), ContactKind.Noise, 1));
        Assert.Equal(ScavengeError.NotAllied, Arena(5, 5, [SurvivorUnit(3, new(1, 1))]).Execute(new BreakAlliance(3, 1)).Error);

        Assert.True(sim.Execute(new BreakAlliance(1, a.Id)).Ok);
        Assert.Equal(ScavengeError.BreakPending, sim.Execute(new BreakAlliance(1, a.Id)).Error);
        var warning = sim.State.Events.Last(e => e.Type == ScavengeEventType.AllianceBreakWarning);
        Assert.Equal([team], warning.Sides);
        var effective = sim.State.Breaks.Single().EffectiveRound;
        Assert.Equal(sim.State.Round + 2, effective);

        while (sim.State.Round < effective - 1 || (sim.State.Round == effective - 1 && sim.State.CurrentUnitId is not null && sim.State.TurnIndex < sim.State.TurnOrder.Count - 1))
        {
            Assert.True(sim.State.Allied(1, 2), $"still allied in round {sim.State.Round}");
            var current = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(current.PlayerId!.Value, current.Id)).Ok);
        }
        while (sim.State.Round < effective)
        {
            var current = sim.State.CurrentUnit!;
            Assert.True(sim.Execute(new EndTurn(current.PlayerId!.Value, current.Id)).Ok);
        }
        Assert.False(sim.State.Allied(1, 2));
        Assert.Equal(Unit.PlayerSide(1), a.Side);
        Assert.Equal(Unit.PlayerSide(2), b.Side);
        Assert.True(a.HostileTo(b));
        Assert.False(Detection.SideSees(sim.Data, sim.State, a.Side, husk), "a no longer sees through b's eyes");
        Assert.DoesNotContain(sim.State.Contacts, c => c.Side == team);
        var broken = sim.State.Events.Last(e => e.Type == ScavengeEventType.AllianceBroken);
        Assert.Equal([Unit.PlayerSide(1), Unit.PlayerSide(2)], broken.Sides);
        Assert.True(sim.State.Explored.ContainsKey(a.Side) && sim.State.Explored.ContainsKey(b.Side));
    }

    [Fact]
    public void A_downed_ally_can_be_carried_out_and_goes_home_to_their_own_player()
    {
        string[] rows = ["..........", "..........", ".........E"];
        var carrier = SurvivorUnit(1, new(6, 2));
        var downed = SurvivorUnit(2, new(7, 2));
        var sim = Arena(10, 3, [carrier, downed], rows);
        Alliances.Merge(sim.State, 1, 2);
        downed.Status = UnitStatus.Downed;
        downed.Hp = 0;
        downed.BleedOut = 3;
        downed.Stabilized = true;
        sim.UpdateParties();
        Assert.False(PartyOf(sim, 2).Done, "a downed party waits while an ally is still up");

        var range = sim.Profile(carrier).Movement.Modes[0].Range;
        Assert.Equal(ScavengeError.CannotCarry, sim.Execute(new Carry(1, carrier.Id, carrier.Id)).Error);
        Assert.True(sim.Execute(new Carry(1, carrier.Id, downed.Id)).Ok);
        Assert.Equal(sim.Profile(carrier).MaxAp - sim.Data.Rules.Coop.CarryApCost, carrier.Ap);
        Assert.Equal(Math.Max(1, range - sim.Data.Rules.Coop.CarryRangePenalty), sim.Profile(carrier).Movement.Modes[0].Range);
        Assert.Equal(carrier.Pos, downed.Pos);
        Assert.Same(carrier, sim.State.UnitAt(carrier.Pos));

        Assert.True(sim.Execute(new Move(1, carrier.Id, new(7, 2))).Ok);
        Assert.Equal(new Pos(7, 2), downed.Pos);
        Assert.True(sim.Execute(new Move(1, carrier.Id, new(8, 2))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, carrier.Id)).Ok);
        TurnOf(sim, carrier);
        Assert.True(sim.Execute(new Move(1, carrier.Id, new(9, 2))).Ok);
        Assert.True(sim.Execute(new Extract(1, carrier.Id)).Ok);

        Assert.Equal(UnitStatus.Extracted, downed.Status);
        Assert.True(sim.State.Finished);
        var mine = sim.ResultFor(1);
        var theirs = sim.ResultFor(2);
        Assert.Single(mine.Returned);
        Assert.Single(theirs.Returned);
        Assert.Equal("P2", theirs.Returned[0].Survivor.Name);
        Assert.Empty(theirs.Deaths);
    }

    [Fact]
    public void Putting_down_and_letting_go()
    {
        var carrier = SurvivorUnit(1, new(3, 2));
        var downed = SurvivorUnit(2, new(4, 2));
        var sim = Arena(8, 5, [carrier, downed]);
        Alliances.Merge(sim.State, 1, 2);
        downed.Status = UnitStatus.Downed;
        downed.BleedOut = 3;
        Assert.True(sim.Execute(new Carry(1, carrier.Id, downed.Id)).Ok);
        Assert.Equal(ScavengeError.CannotCarry, sim.Execute(new Carry(1, carrier.Id, downed.Id)).Error);
        Assert.Equal(ScavengeError.InvalidTile, sim.Execute(new PutDown(1, carrier.Id, new(6, 2))).Error);
        Assert.True(sim.Execute(new PutDown(1, carrier.Id, new(2, 2))).Ok);
        Assert.Equal(new Pos(2, 2), downed.Pos);
        Assert.Null(carrier.CarryingId);
        Assert.Null(downed.CarriedById);

        // A carrier that goes down drops their load on a free tile next to them.
        Assert.True(sim.Execute(new Carry(1, carrier.Id, downed.Id)).Ok);
        carrier.Status = UnitStatus.Downed;
        sim.UpdateParties();
        Assert.Null(downed.CarriedById);
        Assert.Equal(1, downed.Pos.Chebyshev(carrier.Pos));
    }

    [Fact]
    public void A_downed_party_with_no_ally_left_is_left_behind()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(3, 1));
        var sim = Arena(8, 4, [a, b], ["E......."]);
        Alliances.Merge(sim.State, 1, 2);
        b.Status = UnitStatus.Downed;
        b.BleedOut = 3;
        sim.UpdateParties();
        Assert.False(PartyOf(sim, 2).Done);

        Assert.True(sim.Execute(new Move(1, a.Id, new(0, 0))).Ok);
        Assert.True(sim.Execute(new Extract(1, a.Id)).Ok);
        Assert.True(PartyOf(sim, 2).Done);
        Assert.Equal(UnitStatus.Dead, b.Status);
        Assert.Single(sim.ResultFor(2).Deaths);
    }

    [Fact]
    public void Items_handed_to_an_ally_go_home_with_the_ally()
    {
        var a = SurvivorUnit(1, new(1, 1), pack: ["scrap", "bandage"]);
        var b = SurvivorUnit(2, new(2, 1));
        var far = SurvivorUnit(2, new(6, 1));
        var sim = Arena(8, 3, [a, b, far], ["........", "........", "E......."]);

        Assert.Equal(ScavengeError.InvalidTarget, sim.Execute(new GiveItem(1, a.Id, b.Id, "scrap")).Error); // not allied yet
        Alliances.Merge(sim.State, 1, 2);
        Assert.Equal(ScavengeError.ItemNotCarried, sim.Execute(new GiveItem(1, a.Id, b.Id, "med_kit")).Error);
        Assert.Equal(ScavengeError.TooFar, sim.Execute(new GiveItem(1, a.Id, far.Id, "scrap")).Error);
        Assert.True(sim.Execute(new GiveItem(1, a.Id, b.Id, "scrap")).Ok);
        Assert.Equal(["bandage"], a.Pack);
        Assert.Contains("scrap", b.Pack);
        Assert.Equal(sim.Profile(a).MaxAp - sim.Data.Rules.Coop.GiveApCost, a.Ap);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.ItemGiven && e.TargetId == b.Id);

        b.Pos = new(0, 2);
        b.Status = UnitStatus.Extracted;
        far.Status = UnitStatus.Extracted;
        sim.UpdateParties();
        Assert.Equal(1, sim.ResultFor(2).Loot["scrap"]);
        Assert.False(sim.ResultFor(1).Loot.ContainsKey("scrap"));
    }

    [Fact]
    public void A_bandage_or_med_kit_works_on_an_adjacent_ally()
    {
        var medic = SurvivorUnit(1, new(1, 1), pack: ["bandage", "med_kit"]);
        var hurt = SurvivorUnit(2, new(2, 1));
        var sim = Arena(6, 3, [medic, hurt]);
        hurt.Hp = 50;
        Assert.Equal(ScavengeError.InvalidTarget, sim.Execute(new UseItem(1, medic.Id, "bandage", hurt.Id)).Error);
        Alliances.Merge(sim.State, 1, 2);
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "bandage", hurt.Id)).Ok);
        Assert.Equal(60, hurt.Hp);

        TurnOf(sim, medic);
        hurt.Status = UnitStatus.Downed;
        hurt.Hp = 0;
        hurt.BleedOut = 2;
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "med_kit", hurt.Id)).Ok);
        Assert.Equal(UnitStatus.Active, hurt.Status);
    }

    [Fact]
    public void Each_player_extracts_on_their_own_and_the_ally_plays_on()
    {
        var a = SurvivorUnit(1, new(1, 0));
        var b = SurvivorUnit(2, new(5, 2));
        var npc = NpcUnit(new(2, 0));
        var sim = Arena(8, 3, [a, b, npc], ["E.......", "........", "........"]);
        Alliances.Merge(sim.State, 1, 2);
        Assert.True(sim.Execute(new Recruit(1, a.Id, npc.Id)).Ok);
        Assert.Equal(a.Side, npc.Side);

        // a goes down far from the exit; b escorts a's recruit out, and it joins b.
        TurnOf(sim, b);
        a.Status = UnitStatus.Downed;
        a.Pos = new(7, 2);
        a.BleedOut = 5;
        sim.UpdateParties();
        b.Pos = new(1, 1);
        npc.Pos = new(1, 0);
        Assert.True(sim.Execute(new Move(2, b.Id, new(0, 0))).Ok);
        Assert.True(sim.Execute(new Extract(2, b.Id)).Ok);
        Assert.Equal(2, npc.PlayerId);
        Assert.Single(sim.ResultFor(2).Recruits);
        Assert.Empty(sim.ResultFor(1).Recruits);
        Assert.Single(sim.ResultFor(1).Deaths); // nobody left to carry a out
        Assert.True(sim.State.Finished);
    }

    [Fact]
    public void One_player_extracting_does_not_end_the_mission_for_the_ally()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var b = SurvivorUnit(2, new(5, 2));
        var sim = Arena(8, 3, [a, b], ["E.......", "........", "........"]);
        Alliances.Merge(sim.State, 1, 2);
        Assert.True(sim.Execute(new Extract(1, a.Id)).Ok);
        Assert.True(PartyOf(sim, 1).Done);
        Assert.True(sim.ResultFor(1).Complete);
        Assert.False(PartyOf(sim, 2).Done);
        Assert.False(sim.State.Finished);
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void Pings_are_for_the_team_only_and_limited_per_turn()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(2, 1));
        var rival = SurvivorUnit(3, new(4, 1));
        var sim = Arena(8, 4, [a, b, rival]);
        var team = Alliances.Merge(sim.State, 1, 2);
        var limit = sim.Data.Rules.Coop.PingsPerTurn;
        for (var i = 0; i < limit; i++) Assert.True(sim.Execute(new PingTile(1, a.Id, new(5 + i, 2), PingKind.Danger)).Ok);
        Assert.Equal(ScavengeError.PingLimit, sim.Execute(new PingTile(1, a.Id, new(1, 2), PingKind.Loot)).Error);
        Assert.Equal(ScavengeError.InvalidTile, Arena(4, 4, [SurvivorUnit(1, new(1, 1))]).Execute(new PingTile(1, 1, new(9, 9), PingKind.GoHere)).Error);
        Assert.All(sim.State.Pings, p => Assert.Equal(team, p.Side));
        Assert.Equal(sim.Profile(a).MaxAp, a.Ap);

        var pings = sim.State.Events.Where(e => e.Type == ScavengeEventType.Pinged).ToList();
        Assert.Equal(limit, FogLog.For(sim, team, pings).Count);
        Assert.Empty(FogLog.For(sim, rival.Side, pings));

        for (var round = sim.State.Round; sim.State.Round < round + sim.Data.Rules.Coop.PingRounds;)
            Assert.True(sim.Execute(new EndTurn(sim.State.CurrentUnit!.PlayerId!.Value, sim.State.CurrentUnitId!.Value)).Ok);
        Assert.Empty(sim.State.Pings);
    }

    [Fact]
    public void A_co_op_mission_starts_allied_side_by_side_with_more_enemies()
    {
        var data = Real;
        var solo = NewScavenge.Create(data, "suburb_ruins", 5, [Party(1)]);
        var coop = NewScavenge.Create(data, "suburb_ruins", 5, [Party(1) with { Team = 1 }, Party(2) with { Team = 1 }]);
        var pvp = NewScavenge.Create(data, "suburb_ruins", 5, [Party(1), Party(2)]);

        Assert.True(coop.State.Allied(1, 2));
        Assert.StartsWith("team:", coop.State.FindParty(1)!.Side);
        Assert.False(pvp.State.Allied(1, 2));
        var p1 = coop.State.Units.Where(u => u.PlayerId == 1).ToList();
        var p2 = coop.State.Units.Where(u => u.PlayerId == 2).ToList();
        Assert.True(p1.Min(u => p2.Min(v => u.Pos.Chebyshev(v.Pos))) <= 3, "allies spawn together");
        Assert.All(coop.State.Units.Where(u => u.PlayerId is not null), u => Assert.Equal(coop.State.FindParty(1)!.Side, u.Side));

        int Enemies(ScavengeSim s) => s.State.Units.Count(u => u.Kind == UnitKind.Enemy);
        Assert.Equal(1 + data.Rules.Coop.EnemiesPerExtraPlayer, NewScavenge.EnemyScale(data, 2));
        Assert.Equal(1, NewScavenge.EnemyScale(data, 1));
        var soloAvg = Enumerable.Range(1, 20).Average(seed => Enemies(NewScavenge.Create(data, "outskirts", (ulong)seed, [Party(1)])));
        var duoAvg = Enumerable.Range(1, 20).Average(seed => Enemies(NewScavenge.Create(data, "outskirts", (ulong)seed, [Party(1) with { Team = 1 }, Party(2) with { Team = 1 }])));
        Assert.True(duoAvg > soloAvg * (1 + data.Rules.Coop.EnemiesPerExtraPlayer * 0.6), $"two players face more enemies ({soloAvg} solo, {duoAvg} co-op)");
        Assert.True(Enemies(solo) > 0);
    }

    [Fact]
    public void Co_op_state_survives_a_save_and_older_saves_load_without_it()
    {
        var coop = NewScavenge.Create(Real, "outskirts", 3, [Party(1) with { Team = 1 }, Party(2) with { Team = 1 }]);
        var a = coop.State.CurrentUnit!;
        Assert.True(coop.Execute(new PingTile(a.PlayerId!.Value, a.Id, new(2, 2), PingKind.Loot)).Ok);
        Assert.True(coop.Execute(new BreakAlliance(a.PlayerId!.Value, a.Id)).Ok);
        var json = ScavengeSave.Save(coop.State);
        var loaded = ScavengeSave.Load(Real, json);
        Assert.Equal(ScavengeSave.Save(coop.State), ScavengeSave.Save(loaded.State));
        Assert.Single(loaded.State.Pings);
        Assert.Single(loaded.State.Breaks);
        Assert.True(loaded.State.Allied(1, 2));

        var node = System.Text.Json.Nodes.JsonNode.Parse(ScavengeSave.Save(NewScavenge.Create(Real, "outskirts", 3, [Party(1)]).State))!;
        node["version"] = 3;
        var run = node["scavenge"]!.AsObject();
        foreach (var key in new[] { "proposals", "breaks", "pings", "nextTeamId" }) run.Remove(key);
        Assert.Contains("Unsupported scavenge save version", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Real, node.ToJsonString())).Message); // old runs reset
    }

    [Fact]
    public void Joining_a_bigger_team_keeps_a_pending_break_and_drops_moot_offers()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(2, 1));
        var c = SurvivorUnit(3, new(3, 1));
        var sim = Arena(8, 4, [a, b, c]);
        Alliances.Merge(sim.State, 1, 2);
        Assert.True(sim.Execute(new BreakAlliance(1, a.Id)).Ok);
        sim.State.Proposals.Add(new AllianceProposal(3, 1, sim.State.Round));
        sim.State.Proposals.Add(new AllianceProposal(3, 2, sim.State.Round));
        var team = Alliances.Merge(sim.State, 2, 3);
        Assert.Equal(team, sim.State.Breaks.Single().Side);
        Assert.Empty(sim.State.Proposals);
        Assert.All(sim.State.Events.Where(e => e.Type.ToString().StartsWith("Alliance")), e => Assert.True(e.UnitId is null && e.Pos is null));
    }

    [Fact]
    public void An_all_downed_party_stops_waiting_for_an_ally_who_left()
    {
        var a = SurvivorUnit(1, new(1, 1));
        var b = SurvivorUnit(2, new(3, 1));
        var sim = Arena(8, 4, [a, b]);
        Alliances.Merge(sim.State, 1, 2);
        b.Status = UnitStatus.Downed;
        b.Stabilized = true;
        sim.UpdateParties();
        Assert.False(PartyOf(sim, 2).Done);
        sim.Abandon(1);
        Assert.True(PartyOf(sim, 1).Left);
        Assert.True(PartyOf(sim, 2).Done);
        Assert.Equal(UnitStatus.Dead, b.Status);
        Assert.False(PartyOf(sim, 1).Done, "the one who left holds position");
    }
}
