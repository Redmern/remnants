using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

public class ScavengeCommandTests
{
    [Fact]
    public void Move_spends_ap_and_validates_reach_and_ap()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var sim = Arena(8, 8, [u]);

        Assert.True(sim.Execute(new Move(1, u.Id, new(2, 2))).Ok);
        Assert.Equal((new Pos(2, 2), 2), (u.Pos, u.Ap));
        Assert.Equal(ScavengeError.Unreachable, sim.Execute(new Move(1, u.Id, new(7, 7))).Error);
        Assert.Equal(ScavengeError.Unreachable, sim.Execute(new Move(1, u.Id, new(2, 2))).Error);

        u.Ap = 0;
        Assert.Equal(ScavengeError.NotEnoughAp, sim.Execute(new Move(1, u.Id, new(3, 3))).Error);
        Assert.Equal(new Pos(2, 2), u.Pos);
    }

    [Fact]
    public void Commands_check_unit_owner_and_turn()
    {
        var a = SurvivorUnit(1, new(0, 0));
        var b = SurvivorUnit(1, new(1, 0));
        var c = SurvivorUnit(2, new(5, 5));
        var sim = Arena(8, 8, [a, b, c]);

        Assert.Equal(ScavengeError.UnknownUnit, sim.Execute(new EndTurn(1, 99)).Error);
        Assert.Equal(ScavengeError.NotYourUnit, sim.Execute(new EndTurn(2, a.Id)).Error);
        Assert.Equal(ScavengeError.NotYourTurn, sim.Execute(new Move(1, b.Id, new(1, 1))).Error);
        Assert.Equal(ScavengeError.NotYourTurn, sim.Execute(new EndTurn(2, c.Id)).Error);

        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.True(sim.Execute(new EndTurn(1, b.Id)).Ok);
        Assert.Equal(c.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void A_tile_attack_validates_action_reach_and_ap()
    {
        var a = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var ally = SurvivorUnit(1, new(1, 0));
        var husk = EnemyUnit("husk", new(4, 0));
        var sim = Arena(6, 2, [a, ally, husk]);

        Assert.Equal(ScavengeError.UnknownAction, sim.Execute(new AttackTile(1, a.Id, husk.Pos, "shoot")).Error);
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new AttackTile(1, a.Id, husk.Pos, "swing")).Error);

        husk.Pos = new(0, 1);
        a.Ap = 0;
        Assert.Equal(ScavengeError.NotEnoughAp, sim.Execute(new AttackTile(1, a.Id, husk.Pos, "swing")).Error);
        a.Ap = 3;
        var result = StrikeNow(sim, a, husk, "swing");
        Assert.True(result.Ok, result.Message);
        Assert.Equal(2, a.Ap);
        Assert.Contains(sim.State.Events, e => e.UnitId == a.Id && e.TargetId == husk.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void A_tile_attack_never_strikes_an_ally_on_the_tile()
    {
        var a = SurvivorUnit(1, new(0, 0), weapon: "pipe");
        var ally = SurvivorUnit(1, new(1, 0));
        var sim = Arena(40, 2, [a, ally, EnemyUnit("husk", new(39, 1))],
            data: With(r => r with { Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1 } }));

        Assert.True(StrikeNow(sim, a, ally, "swing").Ok);
        Assert.Equal(ally.MaxHp, ally.Hp);
        Assert.DoesNotContain(sim.State.Events, e => e.TargetId == ally.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
        Assert.Contains(sim.State.Events, e => e.UnitId == a.Id && e.Type == ScavengeEventType.IntentFizzled);
    }

    [Fact]
    public void Pick_up_takes_from_an_adjacent_cache_within_carry_limit()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var sim = Arena(4, 4, [u]);
        sim.State.Caches.Add(new LootCache { Id = 1, Pos = new(1, 1), Items = ["scrap", "sch_scanner"] });
        sim.State.Caches.Add(new LootCache { Id = 2, Pos = new(3, 3), Items = ["scrap"] });

        Assert.Equal(ScavengeError.UnknownCache, sim.Execute(new PickUp(1, u.Id, 9, "scrap")).Error);
        Assert.Equal(ScavengeError.TooFar, sim.Execute(new PickUp(1, u.Id, 2, "scrap")).Error);
        Assert.Equal(ScavengeError.ItemNotInCache, sim.Execute(new PickUp(1, u.Id, 1, "pistol")).Error);

        Assert.True(sim.Execute(new PickUp(1, u.Id, 1, "sch_scanner")).Ok);
        Assert.Equal(["sch_scanner"], u.Pack);
        Assert.Equal(2, u.Ap);

        u.Pack.AddRange(Enumerable.Repeat("scrap", 24)); // capacity 25 kg, 24.1 carried
        Assert.Equal(ScavengeError.CarryLimitExceeded, sim.Execute(new PickUp(1, u.Id, 1, "scrap")).Error);
        u.Pack.RemoveAt(u.Pack.Count - 1);
        Assert.True(sim.Execute(new PickUp(1, u.Id, 1, "scrap")).Ok);
        Assert.DoesNotContain(sim.State.Caches, c => c.Id == 1); // emptied caches disappear
    }

    [Fact]
    public void Med_kit_stabilizes_and_revives_a_downed_ally()
    {
        var medic = SurvivorUnit(pos: new(0, 0), pack: ["med_kit"]);
        var hurt = SurvivorUnit(pos: new(1, 0));
        var sim = Arena(4, 4, [medic, hurt]);
        Combat.ApplyDamage(sim, hurt, BodyPart.Torso, 100);
        Assert.Equal(UnitStatus.Downed, hurt.Status);

        var result = sim.Execute(new UseItem(1, medic.Id, "med_kit", hurt.Id));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(UnitStatus.Active, hurt.Status);
        Assert.Equal(15, hurt.Hp);
        Assert.Empty(medic.Pack);
        Assert.Equal(1, medic.Ap);
    }

    [Fact]
    public void Use_item_validation()
    {
        var u = SurvivorUnit(pos: new(0, 0), pack: ["bandage", "scrap"]);
        var far = SurvivorUnit(pos: new(3, 3));
        var husk = EnemyUnit("husk", new(0, 1));
        var sim = Arena(4, 4, [u, far, husk]);

        Assert.Equal(ScavengeError.ItemNotCarried, sim.Execute(new UseItem(1, u.Id, "med_kit")).Error);
        Assert.Equal(ScavengeError.NotUsable, sim.Execute(new UseItem(1, u.Id, "scrap")).Error);
        Assert.Equal(ScavengeError.TooFar, sim.Execute(new UseItem(1, u.Id, "bandage", far.Id)).Error);
        Assert.Equal(ScavengeError.InvalidTarget, sim.Execute(new UseItem(1, u.Id, "bandage", husk.Id)).Error);

        u.Hp = 50;
        Assert.True(sim.Execute(new UseItem(1, u.Id, "bandage")).Ok);
        Assert.Equal(60, u.Hp);

        Combat.ApplyDamage(sim, far, BodyPart.Torso, 100);
        far.Pos = new(1, 1);
        u.Pack.Add("bandage");
        Assert.Equal(ScavengeError.NotUsable, sim.Execute(new UseItem(1, u.Id, "bandage", far.Id)).Error); // bandages cannot stop bleeding out
    }

    [Fact]
    public void Evac_beacon_pulls_a_downed_survivor_out_and_they_count_as_returned()
    {
        var a = SurvivorUnit(pos: new(0, 0), pack: ["evac_beacon"]);
        var b = SurvivorUnit(pos: new(1, 0), pack: ["scrap"]);
        var sim = Arena(4, 4, [a, b]);
        Combat.ApplyDamage(sim, b, BodyPart.LeftLeg, 30);
        Combat.ApplyDamage(sim, b, BodyPart.Torso, 70);

        Assert.True(sim.Execute(new UseItem(1, a.Id, "evac_beacon", b.Id)).Ok);
        Assert.Equal(UnitStatus.Evacuated, b.Status);
        Assert.Null(sim.State.UnitAt(new(1, 0)));

        var result = sim.ResultFor(1);
        var back = Assert.Single(result.Returned);
        Assert.True(back.Evacuated);
        Assert.Equal(1, back.Survivor.Health);
        Assert.Equal(PartCondition.Injured, back.Survivor.Body[BodyPart.LeftLeg].Condition);
        Assert.Equal(1, result.Loot["scrap"]);
        Assert.False(result.Complete);
    }

    [Fact]
    public void Extract_needs_an_exit_tile_and_ends_the_units_turn()
    {
        var a = SurvivorUnit(pos: new(0, 1));
        var b = SurvivorUnit(pos: new(3, 1));
        var sim = Arena(4, 2, [a, b], ["E...", "...."]);

        Assert.Equal(ScavengeError.NotOnExit, sim.Execute(new Extract(1, a.Id)).Error);
        Assert.True(sim.Execute(new Move(1, a.Id, new(0, 0))).Ok);
        Assert.True(sim.Execute(new Extract(1, a.Id)).Ok);
        Assert.Equal(UnitStatus.Extracted, a.Status);
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.False(sim.State.Finished);
    }

    [Fact]
    public void Run_ends_when_nobody_conscious_is_left_and_the_downed_are_left_behind()
    {
        var ally = SurvivorUnit(pos: new(2, 0));
        var downed = SurvivorUnit(pos: new(3, 1));
        var sim = Arena(4, 2, [ally, downed], ["E...", "...."]);
        Combat.ApplyDamage(sim, downed, BodyPart.Torso, 100);
        Assert.Equal(ScavengeError.Incapacitated, sim.Execute(new EndTurn(1, downed.Id)).Error);

        Assert.True(sim.Execute(new Move(1, ally.Id, new(0, 0))).Ok);
        Assert.True(sim.Execute(new Extract(1, ally.Id)).Ok);

        Assert.True(sim.State.Finished);
        Assert.Equal(UnitStatus.Dead, downed.Status);
        var result = sim.ResultFor(1);
        Assert.True(result.Complete);
        Assert.Single(result.Returned);
        Assert.Equal(0, Assert.Single(result.Deaths).Health);
        Assert.Equal(ScavengeError.Finished, sim.Execute(new EndTurn(1, ally.Id)).Error);
    }

    [Fact]
    public void Extraction_result_carries_injuries_gear_and_loot_home()
    {
        var a = SurvivorUnit(pos: new(0, 0), weapon: "pipe", pack: ["scrap", "scrap", "electronics"]);
        a.Body.Injure(BodyPart.Head, 0.2);
        a.Hp = 70;
        var sim = Arena(3, 1, [a], ["E.."]);
        Assert.True(sim.Execute(new Extract(1, a.Id)).Ok);

        var result = sim.ResultFor(1);
        Assert.True(result.Complete);
        var back = Assert.Single(result.Returned);
        Assert.False(back.Evacuated);
        Assert.Equal(70, back.Survivor.Health);
        Assert.Equal(0.2, back.Survivor.Body[BodyPart.Head].Severity, 9);
        Assert.Equal("pipe", back.Equipment[EquipSlot.Weapon]);
        Assert.Equal(new SortedDictionary<string, int> { ["electronics"] = 1, ["scrap"] = 2 }, result.Loot);
        Assert.Empty(result.Deaths);
        Assert.Equal(PartCondition.Healthy, a.Origin!.Body[BodyPart.Head].Condition); // the entry snapshot is untouched
    }

    [Fact]
    public void Recruited_npc_follows_and_extracts_with_its_leader()
    {
        var a = SurvivorUnit(pos: new(4, 2));
        var npc = NpcUnit(new(5, 2));
        var sim = Arena(8, 4, [a, npc], ["E.......", "........", "........", "........"]);

        Assert.Equal(ScavengeError.NotRecruitable, sim.Execute(new Recruit(1, a.Id, a.Id)).Error);
        Assert.True(sim.Execute(new Recruit(1, a.Id, npc.Id)).Ok);
        Assert.Equal((a.Side, 1, a.Id), (npc.Side, npc.PlayerId!.Value, npc.FollowingId!.Value));
        Assert.Equal(ScavengeError.NotYourUnit, sim.Execute(new EndTurn(1, npc.Id)).Error); // the AI walks recruits

        // Walk to the exit; the recruit keeps up on its own turns.
        while (a.Pos != new Pos(0, 0))
        {
            var step = sim.Reachable(a).Where(kv => kv.Value <= a.Ap).OrderBy(kv => kv.Key.Chebyshev(new(0, 0))).First().Key;
            Assert.True(sim.Execute(new Move(1, a.Id, step)).Ok);
            Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        }
        Assert.True(npc.Pos.Chebyshev(a.Pos) <= 2);
        Assert.True(sim.Execute(new Extract(1, a.Id)).Ok);

        var recruit = Assert.Single(sim.ResultFor(1).Recruits);
        Assert.Equal("Stranger", recruit.Survivor.Name);
        Assert.Equal(UnitStatus.Extracted, npc.Status);
    }
}

public class TurnTests
{
    [Fact]
    public void New_round_orders_by_initiative_and_refills_ap()
    {
        var slow = SurvivorUnit(1, new(0, 0), body: "riot_armor");
        var mid = SurvivorUnit(1, new(0, 7));
        var fast = SurvivorUnit(2, new(7, 0), body: "scout_jacket");
        fast.Attributes[AttributeKind.Agility] = 80;
        var sim = Arena(8, 8, [slow, mid, fast]);

        Assert.True(sim.Execute(new Move(1, slow.Id, new(1, 0))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, slow.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, mid.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, fast.Id)).Ok);

        Assert.Equal(2, sim.State.Round);
        Assert.Equal([fast.Id, mid.Id, slow.Id], sim.State.TurnOrder);
        Assert.Equal(fast.Id, sim.State.CurrentUnitId);
        Assert.Equal(3, fast.Ap);
    }

    [Fact]
    public void Two_players_share_the_map_and_only_command_their_own_units()
    {
        var sim = NewScavenge.Create(Real, "outskirts", 3, [Party(1), Party(2)]);
        Assert.Equal(2, sim.State.Parties.Count);
        Assert.Equal(4, sim.State.Units.Count(u => u.Kind == UnitKind.Survivor));
        Assert.True(sim.State.Units.Where(u => u.PlayerId == 1).All(u => u.HostileTo(sim.State.Units.First(v => v.PlayerId == 2))));

        var current = sim.State.CurrentUnit!;
        var otherPlayer = current.PlayerId == 1 ? 2 : 1;
        Assert.Equal(ScavengeError.NotYourUnit, sim.Execute(new EndTurn(otherPlayer, current.Id)).Error);
        Assert.True(sim.Execute(new EndTurn(current.PlayerId!.Value, current.Id)).Ok);

        PlayBot(sim);
        Assert.True(sim.State.Finished);
        Assert.True(sim.ResultFor(1).Complete);
        Assert.True(sim.ResultFor(2).Complete);
    }
}

public class AiTests
{
    [Fact]
    public void Husk_that_sees_a_survivor_closes_in_and_attacks()
    {
        var u = SurvivorUnit(pos: new(0, 0));
        var husk = EnemyUnit("husk", new(5, 0));
        var sim = Arena(8, 3, [u, husk]);

        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Equal(AiMode.Hunting, husk.Ai.Mode);
        Assert.True(husk.Pos.Chebyshev(u.Pos) < 5); // it closes in live, then readies a blow that comes down as the sub-round ends
        for (var i = 0; i < 4 && husk.Pos.Chebyshev(u.Pos) > 1; i++) Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.Equal(1, husk.Pos.Chebyshev(u.Pos));
        Assert.Contains(sim.State.Events, e => e.UnitId == husk.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void Husk_behind_a_wall_does_not_notice_until_it_hears_a_shot()
    {
        var shooter = SurvivorUnit(pos: new(0, 1), weapon: "pistol");
        var target = EnemyUnit("husk", new(4, 1));
        var listener = EnemyUnit("husk", new(6, 4));
        var sim = Arena(9, 6, [shooter, target, listener], [
            ".........",
            ".........",
            ".........",
            "#########",
            ".........",
            ".........",
        ]);
        Assert.Equal(AiMode.Idle, listener.Ai.Mode);
        Assert.True(StrikeNow(sim, shooter, target, "shoot").Ok);
        Assert.Equal(AiMode.Hunting, listener.Ai.Mode);
        Assert.Equal(shooter.Pos, listener.Ai.LastKnown);
    }

    [Fact]
    public void Linked_steps_into_cover_before_shooting()
    {
        var u = SurvivorUnit(pos: new(0, 2));
        var linked = EnemyUnit("linked", new(6, 2));
        var sim = Arena(9, 5, [u, linked], [
            ".........",
            ".........",
            ".........",
            "......c..",
            ".........",
        ]);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.True(Combat.CoverAgainst(Real, sim.State.Map, linked.Pos, u.Pos) > 0, $"Linked at {linked.Pos} has no cover.");
        Assert.Contains(sim.State.Events, e => e.UnitId == linked.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void Idle_husks_wander_deterministically()
    {
        string Run()
        {
            var u = SurvivorUnit(pos: new(0, 0));
            var husk = EnemyUnit("husk", new(15, 15));
            var sim = Arena(16, 16, [u, husk], seed: 7);
            for (var i = 0; i < 6; i++) sim.Execute(new EndTurn(1, u.Id));
            return string.Join(";", sim.State.Events.Where(e => e.UnitId == husk.Id).Select(e => e.Message));
        }
        var first = Run();
        Assert.Contains("moved", first);
        Assert.Equal(first, Run());
    }
}
