using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// AC-24, AC-25: in a fight, using an item, hacking and firing a drone take the Action slot and go off in the resolve phase by
/// priority. With nobody hostile in sight they stay instant.
/// </summary>
public class PlannedActionTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
        Hacking = r.Hacking with { MinChance = 1, MaxChance = 1 },
    });

    private static (ScavengeSim Sim, Unit Medic, Unit Husk) Fight()
    {
        var medic = SurvivorUnit(1, new(2, 3), weapon: "pipe", pack: ["med_kit"]);
        var husk = EnemyUnit("husk", new(8, 3));
        return (Arena(14, 7, [medic, husk], data: Sure), medic, husk);
    }

    private static string[] Walled => ["......#.......", "......#.......", "......#.......", "......#.......", "......#.......", "......#.......", "......#......."];

    private static PlanStep Only(ScavengeSim sim, Unit unit) => Plans.Of(sim.State, unit)!.Steps.Single();

    [Fact]
    public void In_a_fight_an_item_takes_the_action_slot_and_waits_for_the_round()
    {
        var (sim, medic, _) = Fight();
        medic.Body.Injure(Survivors.BodyPart.LeftArm, 0.5);
        var before = (medic.Pack.Count, Survivors.Treatment.WorstInjury(medic.Body));
        Assert.True(PlannedActions.InFight(sim, medic));
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "med_kit")).Ok);
        Assert.Equal(PlanStepKind.UseItem, Only(sim, medic).Kind);
        Assert.Equal((before.Count, before.Item2), (medic.Pack.Count, Survivors.Treatment.WorstInjury(medic.Body)));
        Assert.False(Plans.HasRoom(sim, medic, PlanSlot.Action));
        Settle(sim);
        Assert.True(Survivors.Treatment.WorstInjury(medic.Body) < before.Item2 || medic.Pack.Count < before.Count);
    }

    [Fact]
    public void With_nobody_hostile_in_sight_an_item_is_instant()
    {
        var medic = SurvivorUnit(1, new(2, 3), weapon: "pipe", pack: ["med_kit"]);
        var husk = EnemyUnit("husk", new(8, 3));
        var sim = Arena(14, 7, [medic, husk], rows: Walled, data: Sure);
        medic.Body.Injure(Survivors.BodyPart.LeftArm, 0.5);
        var had = medic.Pack.Count;
        Assert.False(PlannedActions.InFight(sim, medic));
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "med_kit")).Ok);
        Assert.Null(Plans.Of(sim.State, medic)?.Steps.FirstOrDefault(s => s.Kind == PlanStepKind.UseItem));
        Assert.True(medic.Pack.Count < had || medic.Ap == 0);
    }

    [Fact]
    public void A_refused_item_is_refused_when_planned()
    {
        var (sim, medic, _) = Fight();
        Assert.Equal(ScavengeError.ItemNotCarried, sim.Execute(new UseItem(1, medic.Id, "flare")).Error);
        Assert.Null(Plans.Of(sim.State, medic));
    }

    [Fact]
    public void Clearing_a_planned_item_gives_the_points_back_and_uses_nothing()
    {
        var (sim, medic, _) = Fight();
        medic.Body.Injure(Survivors.BodyPart.LeftArm, 0.5);
        var (ap, fatigue, pack) = (medic.Ap, medic.Fatigue, medic.Pack.Count);
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "med_kit")).Ok);
        Assert.True(medic.Ap < ap);
        Assert.True(sim.Execute(new UndoPlan(1, medic.Id)).Ok);
        Assert.Equal((ap, fatigue, pack), (medic.Ap, medic.Fatigue, medic.Pack.Count));
    }

    [Fact]
    public void A_planned_item_goes_off_by_priority_and_a_faster_blow_can_stop_it()
    {
        var medic = SurvivorUnit(1, new(4, 3), weapon: "pipe", pack: ["med_kit"]);
        var killer = SurvivorUnit(2, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [medic, killer], data: Sure);
        medic.Body.Injure(Survivors.BodyPart.LeftArm, 0.5);
        medic.Hp = 1;
        var had = medic.Pack.Count;
        killer.PriorityBonus = 50;
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "med_kit")).Ok);
        Assert.True(sim.Execute(StrikeOn(sim, killer, medic, "swing")).Ok);
        Settle(sim);
        Assert.False(medic.IsActive);
        Assert.Equal(had, medic.Pack.Count);
    }

    [Fact]
    public void A_planned_hack_makes_no_progress_until_the_round_resolves()
    {
        ZoneDevice terminal = null!;
        var hacker = SurvivorUnit(1, new(1, 1), pack: ["hacking_kit"]);
        var husk = EnemyUnit("husk", new(6, 1));
        var sim = Arena(10, 5, [hacker, husk], data: Sure, setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Relay, new Pos(2, 1)));
        Assert.True(sim.Execute(new Hack(1, hacker.Id, terminal.Id)).Ok);
        Assert.Equal(PlanStepKind.Hack, Only(sim, hacker).Kind);
        Assert.Empty(terminal.Progress);
        Assert.DoesNotContain(sim.State.Events, e => e.Type is ScavengeEventType.HackProgress or ScavengeEventType.Hacked);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type is ScavengeEventType.HackProgress or ScavengeEventType.Hacked);
    }

    [Fact]
    public void A_hack_out_of_reach_is_refused_when_planned()
    {
        ZoneDevice terminal = null!;
        var hacker = SurvivorUnit(1, new(1, 1), pack: ["hacking_kit"]);
        var husk = EnemyUnit("husk", new(8, 1));
        var sim = Arena(14, 5, [hacker, husk], data: Sure, setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Relay, new Pos(12, 3)));
        Assert.False(sim.Execute(new Hack(1, hacker.Id, terminal.Id)).Ok);
        Assert.Null(Plans.Of(sim.State, hacker));
    }

    [Fact]
    public void A_hack_whose_device_went_out_of_reach_fizzles_with_a_line_in_the_log()
    {
        ZoneDevice terminal = null!;
        var hacker = SurvivorUnit(1, new(1, 1), pack: ["hacking_kit"]);
        var husk = EnemyUnit("husk", new(6, 1));
        var sim = Arena(10, 5, [hacker, husk], data: Sure, setup: (d, s) => terminal = Hacking.Place(d, s, DeviceKind.Relay, new Pos(2, 1)));
        Assert.True(sim.Execute(new Hack(1, hacker.Id, terminal.Id)).Ok);
        terminal.Used = true;
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == hacker.Id);
    }

    private static (ScavengeSim Sim, Unit Op, Unit Husk) DroneFight()
    {
        var op = SurvivorUnit(1, new(2, 2), pack: ["combat_drone"]);
        op.Hooks = Real.HooksFor(["drone_operator_1"]);
        op.Skills[Survivors.Skill.DronePiloting] = 80;
        var husk = EnemyUnit("husk", new(6, 2));
        var sim = Arena(14, 7, [op, husk], data: Sure);
        Assert.True(sim.Execute(new DeployDrone(1, op.Id, new(3, 2))).Ok);
        op.Ap = 1;
        return (sim, op, husk);
    }

    [Fact]
    public void A_drone_shot_waits_for_the_round_and_comes_down_on_the_tile()
    {
        var (sim, op, husk) = DroneFight();
        Assert.True(sim.Execute(new DroneFire(1, op.Id, husk.Id)).Ok);
        var step = Plans.Of(sim.State, op)!.Steps.Single(s => s.Kind == PlanStepKind.DroneFire);
        Assert.Equal(husk.Pos, step.Tile);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.DroneFired);
        Assert.Equal(husk.MaxHp, husk.Hp);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.DroneFired);
        Assert.True(husk.Hp < husk.MaxHp);
    }

    [Fact]
    public void A_target_that_walks_off_the_tile_dodges_a_drone_shot()
    {
        var (sim, op, husk) = DroneFight();
        Assert.True(sim.Execute(new DroneFire(1, op.Id, husk.Id)).Ok);
        husk.Pos = new Pos(9, 5);
        Settle(sim);
        Assert.Equal(husk.MaxHp, husk.Hp);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == op.Id);
    }

    [Fact]
    public void A_drone_shot_goes_through_block()
    {
        var (sim, op, husk) = DroneFight();
        Assert.True(sim.Execute(new DroneFire(1, op.Id, husk.Id)).Ok);
        Assert.True(Defences.Plan(sim, husk, new Defend(0, husk.Id, new(-1, 0))).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Blocked && e.UnitId == husk.Id);
    }

    [Fact]
    public void A_planned_item_survives_a_save()
    {
        var (sim, medic, _) = Fight();
        medic.Body.Injure(Survivors.BodyPart.LeftArm, 0.5);
        Assert.True(sim.Execute(new UseItem(1, medic.Id, "med_kit")).Ok);
        var loaded = ScavengeSave.Load(sim.Data, ScavengeSave.Save(sim.State));
        var step = Plans.Of(loaded.State, loaded.State.FindUnit(medic.Id)!)!.Steps.Single();
        Assert.Equal((PlanStepKind.UseItem, "med_kit"), (step.Kind, step.ItemId));
    }
}
