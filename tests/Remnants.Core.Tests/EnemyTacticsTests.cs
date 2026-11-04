using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>
/// How enemies aim and what they do besides strike: every type by its data, from what it can see. A blow on an empty tile
/// leaves the enemy exposed, which is what baiting pays. Nothing here may read anyone's plan.
/// </summary>
public class EnemyTacticsTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    private static Unit Hunting(Unit enemy)
    {
        enemy.Ai.Mode = AiMode.Hunting;
        return enemy;
    }

    private static PlanStep? Action(ScavengeSim sim, Unit unit) =>
        Plans.Of(sim.State, unit)?.Steps.FirstOrDefault(s => Plans.SlotOf(s) == PlanSlot.Action);

    // Aim (AC-17, AC-18)

    [Fact]
    public void A_husk_in_reach_swings_at_the_target_tile()
    {
        var husk = Hunting(EnemyUnit("husk", new(4, 3)));
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [husk, target], data: Sure);
        sim.Start();
        Assert.Equal(new Pos(5, 3), Action(sim, husk)!.Strike!.Tile);
    }

    [Fact]
    public void A_husk_that_cannot_reach_swings_at_the_tile_next_to_it_the_target_would_step_on()
    {
        // Its walk ends two tiles off the target: it swings at the tile between them.
        var husk = Hunting(EnemyUnit("husk", new(1, 3)));
        var target = SurvivorUnit(1, new(7, 3), weapon: "pipe");
        var sim = Arena(14, 7, [husk, target], data: Sure);
        sim.Start();
        var anchor = Plans.Anchor(sim.State, husk);
        Assert.Equal(2, anchor.Chebyshev(target.Pos));
        Assert.Equal(anchor + Combat.Face(anchor, target.Pos), Action(sim, husk)!.Strike!.Tile);
    }

    [Fact]
    public void A_husk_with_the_target_far_off_does_not_swing_at_air()
    {
        var husk = Hunting(EnemyUnit("husk", new(1, 3)));
        var target = SurvivorUnit(1, new(9, 3), weapon: "pipe");
        var sim = Arena(14, 7, [husk, target], data: Sure);
        sim.Start();
        Assert.Null(Action(sim, husk));
    }

    [Fact]
    public void Stepping_next_to_a_husk_walks_into_its_swing()
    {
        var husk = Hunting(EnemyUnit("husk", new(1, 3)));
        var target = SurvivorUnit(1, new(7, 3), weapon: "pipe");
        var sim = Arena(14, 7, [husk, target], data: Sure);
        sim.Start();
        Assert.True(sim.Execute(new Move(1, target.Id, Action(sim, husk)!.Strike!.Tile)).Ok);
        Settle(sim);
        Assert.True(target.Hp < target.MaxHp);
    }

    [Fact]
    public void A_linked_leads_a_target_that_moved_last_round()
    {
        var linked = Hunting(EnemyUnit("linked", new(2, 3)));
        var target = SurvivorUnit(1, new(6, 3), weapon: "pipe");
        var sim = Arena(14, 7, [linked, target], data: Sure);
        target.LastMove = new Pos(2, 0);
        var action = sim.Profile(linked).Action("burst")!;
        Assert.Equal(new Pos(8, 3), EnemyAim.Tile(sim, linked, action, target));
    }

    [Fact]
    public void A_linked_aims_at_a_target_that_stood_still()
    {
        var linked = Hunting(EnemyUnit("linked", new(2, 3)));
        var target = SurvivorUnit(1, new(6, 3), weapon: "pipe");
        var sim = Arena(14, 7, [linked, target], data: Sure);
        var action = sim.Profile(linked).Action("burst")!;
        Assert.Equal(new Pos(6, 3), EnemyAim.Tile(sim, linked, action, target));
    }

    [Fact]
    public void A_lead_stops_short_of_a_wall()
    {
        var linked = Hunting(EnemyUnit("linked", new(2, 3)));
        var target = SurvivorUnit(1, new(6, 3), weapon: "pipe");
        string[] rows = ["..............", "..............", "..............", ".......#......", "..............", "..............", ".............."];
        var sim = Arena(14, 7, [linked, target], rows: rows, data: Sure);
        target.LastMove = new Pos(2, 0);
        var action = sim.Profile(linked).Action("burst")!;
        Assert.Equal(new Pos(6, 3), EnemyAim.Tile(sim, linked, action, target));
    }

    [Fact]
    public void A_round_of_walking_is_remembered_as_the_last_move()
    {
        var target = SurvivorUnit(1, new(6, 3), weapon: "pipe");
        var far = EnemyUnit("turret", new(0, 0));
        var sim = Arena(14, 7, [target, far], rows: ["#.............", "#.............", "#.............", "#.............", "#.............", "#.............", "#............."], data: Sure);
        sim.Start();
        Assert.True(sim.Execute(new EndTurn(1, target.Id)).Ok);
        Assert.Equal(default, target.LastMove);
        Assert.True(sim.Execute(new Move(1, target.Id, new(8, 3))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, target.Id)).Ok);
        Assert.Equal(new Pos(2, 0), target.LastMove);
        Assert.True(sim.Execute(new EndTurn(1, target.Id)).Ok);
        Assert.Equal(default, target.LastMove);
    }

    [Fact]
    public void Aim_and_techniques_are_the_same_on_the_same_seed()
    {
        static string Plan(ulong seed)
        {
            var units = new[] { Hunting(EnemyUnit("linked_warden", new(2, 3))), Hunting(EnemyUnit("cole", new(9, 3))), SurvivorUnit(1, new(6, 3), weapon: "pipe") };
            units[0].Hp = 10;
            var sim = Arena(14, 7, units, seed: seed, data: Sure);
            sim.Start();
            return string.Join("|", sim.State.Plans.SelectMany(p => p.Steps).Select(s => $"{s.Kind}:{s.To}:{s.Direction}:{s.Strike?.Tile}"));
        }
        Assert.Equal(Plan(7), Plan(7));
    }

    // No reading of plans (AC-17, AC-23)

    private static string EnemyPlan(ScavengeSim sim, Unit enemy) =>
        string.Join("|", Plans.Of(sim.State, enemy)?.Steps.Select(s => $"{s.Kind}:{s.To}:{s.Direction}:{s.Strike?.Tile}:{s.CardId}") ?? []);

    [Theory]
    [InlineData("husk")]
    [InlineData("linked")]
    [InlineData("linked_warden")]
    [InlineData("cole")]
    public void What_a_target_has_planned_changes_nothing_for_an_enemy(string defId)
    {
        string Think(bool poison)
        {
            var enemy = Hunting(EnemyUnit(defId, new(4, 3)));
            enemy.Hp = enemy.MaxHp * 0.3;
            var target = SurvivorUnit(1, new(6, 3), weapon: "pipe");
            var sim = Arena(14, 7, [enemy, target], data: Sure);
            if (poison)
            {
                Assert.True(sim.Execute(new Move(1, target.Id, new(9, 5))).Ok);
                Assert.True(sim.Execute(new Dash(1, target.Id, new(0, -1), 3)).Ok);
            }
            ScavengeAi.TakeTurn(sim, enemy);
            return EnemyPlan(sim, enemy);
        }
        Assert.Equal(Think(false), Think(true));
    }

    [Fact]
    public void An_enemy_does_not_read_a_hostile_enemys_plan()
    {
        string Think(bool poison)
        {
            // Cole has a guard card, and would play it on a hostile about to have him in reach.
            var cole = Hunting(EnemyUnit("cole", new(4, 3)));
            var rival = new[] { EnemyUnit("husk", new(10, 3)) }[0];
            rival.Side = "rival";
            var sim = Arena(14, 7, [cole, rival], data: Sure);
            if (poison) Plans.AddMove(sim, rival, new(5, 3), 0);
            ScavengeAi.TakeTurn(sim, cole);
            return EnemyPlan(sim, cole);
        }
        Assert.Equal(Think(false), Think(true));
    }

    [Fact]
    public void The_threat_overlay_shows_a_telegraphed_walk_but_not_a_hidden_dash()
    {
        var foe = SurvivorUnit(2, new(4, 3), weapon: "pipe");
        var sim = Arena(14, 7, [foe], data: Sure);
        Assert.True(sim.Execute(new Move(2, foe.Id, new(6, 3))).Ok);
        var walked = Combat.ThreatZone(sim, foe);
        Assert.Contains(new Pos(7, 3), walked);
        Assert.True(sim.Execute(new Dash(2, foe.Id, new(0, 1), 3)).Ok);
        Assert.Equal(walked, Combat.ThreatZone(sim, foe));
    }

    // Baiting and exposed (AC-19, AC-20)

    private static (ScavengeSim Sim, Unit Husk, Unit Target) Baited()
    {
        var husk = Hunting(EnemyUnit("husk", new(4, 3)));
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [husk, target], data: Sure);
        sim.Start();
        Assert.True(sim.Execute(new Move(1, target.Id, new(5, 1))).Ok);
        return (sim, husk, target);
    }

    [Fact]
    public void Walking_off_the_tile_a_husk_swings_at_dodges_it()
    {
        var (sim, _, target) = Baited();
        Settle(sim);
        Assert.Equal(target.MaxHp, target.Hp);
    }

    [Fact]
    public void A_blow_on_an_empty_tile_leaves_an_enemy_exposed_next_round()
    {
        var (sim, husk, target) = Baited();
        var fatigue = husk.Fatigue;
        Assert.True(sim.Execute(new EndTurn(1, target.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Exposed && e.UnitId == husk.Id);
        Assert.True(Exposed.Active(sim.State, husk));
        var rules = sim.Data.Rules.Combat.Exposed;
        Assert.Equal(-rules.PriorityPenalty, husk.PriorityBonus);
        Assert.True(rules.Stamina > 0);
        Assert.True(husk.Fatigue >= fatigue + rules.Stamina - Economy.Regen(sim, husk));
    }

    [Fact]
    public void A_blow_that_lands_exposes_nobody()
    {
        var husk = Hunting(EnemyUnit("husk", new(4, 3)));
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [husk, target], data: Sure);
        sim.Start();
        Assert.True(sim.Execute(new EndTurn(1, target.Id)).Ok);
        Assert.False(Exposed.Active(sim.State, husk));
    }

    [Fact]
    public void Exposure_ends_with_the_round_it_was_for()
    {
        var (sim, husk, target) = Baited();
        Assert.True(sim.Execute(new EndTurn(1, target.Id)).Ok);
        Assert.True(Exposed.Active(sim.State, husk));
        husk.ExposedRound = sim.State.Round;
        sim.State.Round++;
        Assert.False(Exposed.Active(sim.State, husk));
    }

    [Fact]
    public void A_players_whiff_exposes_nobody()
    {
        var unit = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var sim = Arena(14, 7, [unit], data: Sure);
        Assert.True(sim.Execute(new AttackTile(1, unit.Id, new(5, 3), "swing", Items.StrikeMode.Stab, new(1, 0))).Ok);
        Settle(sim);
        Assert.Equal(0, unit.ExposedRound);
    }

    // Tendencies (AC-21)

    [Fact]
    public void A_warden_guards_when_hurt_then_shoves()
    {
        var warden = Hunting(EnemyUnit("linked_warden", new(4, 3)));
        warden.Hp = warden.MaxHp * 0.3;
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [warden, target], data: Sure);
        ScavengeAi.TakeTurn(sim, warden);
        Assert.Equal(PlanStepKind.Defend, Action(sim, warden)!.Kind);
        sim.State.Plans.Clear();
        sim.State.Round++;
        (warden.Ap, warden.Mp) = (1, 6);
        ScavengeAi.TakeTurn(sim, warden);
        Assert.Equal(PlanStepKind.Push, Action(sim, warden)!.Kind);
        Assert.Equal(new Pos(1, 0), Action(sim, warden)!.Direction);
    }

    [Fact]
    public void A_healthy_warden_does_not_guard()
    {
        var warden = Hunting(EnemyUnit("linked_warden", new(4, 3)));
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [warden, target], data: Sure);
        ScavengeAi.TakeTurn(sim, warden);
        Assert.Equal(PlanStepKind.Strike, Action(sim, warden)!.Kind);
    }

    [Fact]
    public void A_linked_shoves_a_target_with_a_wall_at_its_back()
    {
        string[] rows = ["..............", "..............", "..............", "......#.......", "..............", "..............", ".............."];
        var linked = Hunting(EnemyUnit("linked", new(4, 3)));
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [linked, target], rows: rows, data: Sure);
        ScavengeAi.TakeTurn(sim, linked);
        Assert.Equal(PlanStepKind.Push, Action(sim, linked)!.Kind);
    }

    [Fact]
    public void A_linked_with_no_wall_behind_the_target_does_not_shove()
    {
        var linked = Hunting(EnemyUnit("linked", new(4, 3)));
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [linked, target], data: Sure);
        ScavengeAi.TakeTurn(sim, linked);
        Assert.NotEqual(PlanStepKind.Push, Action(sim, linked)?.Kind);
    }

    [Fact]
    public void Cole_guards_when_threatened_but_not_twice_running()
    {
        var cole = Hunting(EnemyUnit("cole", new(4, 3)));
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        target.Facing = new Pos(-1, 0);
        var sim = Arena(14, 7, [cole, target], data: Sure);
        ScavengeAi.TakeTurn(sim, cole);
        Assert.Equal(PlanStepKind.Defend, Action(sim, cole)!.Kind);
        sim.State.Plans.Clear();
        sim.State.Round++;
        (cole.Ap, cole.Mp) = (1, 6);
        ScavengeAi.TakeTurn(sim, cole);
        Assert.NotEqual(PlanStepKind.Defend, Action(sim, cole)?.Kind);
    }

    [Fact]
    public void Cole_dashes_to_a_targets_flank_when_it_faces_him_from_afar()
    {
        var cole = Hunting(EnemyUnit("cole", new(4, 3)));
        var target = SurvivorUnit(1, new(7, 3), weapon: "pipe");
        target.Facing = new Pos(-1, 0);
        var sim = Arena(14, 7, [cole, target], data: Sure);
        ScavengeAi.TakeTurn(sim, cole);
        var dash = Action(sim, cole)!;
        Assert.Equal(PlanStepKind.Dash, dash.Kind);
        Assert.NotEqual(FacingArc.Front, Combat.ArcOf(target, dash.Landing!.Value));
    }

    [Fact]
    public void A_husk_never_guards_shoves_or_dashes()
    {
        var husk = Hunting(EnemyUnit("husk", new(4, 3)));
        husk.Hp = 1;
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [husk, target], data: Sure);
        ScavengeAi.TakeTurn(sim, husk);
        Assert.Equal(PlanStepKind.Strike, Action(sim, husk)!.Kind);
    }

    [Fact]
    public void An_enemys_defend_goes_through_the_block_rules()
    {
        var warden = Hunting(EnemyUnit("linked_warden", new(4, 3)));
        warden.Hp = warden.MaxHp * 0.3;
        var target = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [warden, target], data: Sure);
        ScavengeAi.TakeTurn(sim, warden);
        Assert.True(sim.Execute(new AttackTile(1, target.Id, warden.Pos, "swing", Items.StrikeMode.Stab, new(-1, 0))).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Blocked && e.UnitId == warden.Id);
    }

    // Help (AC-22)

    [Theory]
    [InlineData("husk")]
    [InlineData("husk_crawler")]
    [InlineData("linked")]
    [InlineData("linked_warden")]
    [InlineData("cole")]
    public void Every_fighting_type_says_how_to_read_it(string defId) =>
        Assert.False(string.IsNullOrWhiteSpace(Real.Enemies[defId].Tactics?.Read));

    [Fact]
    public void The_bestiary_shows_habits_once_fought()
    {
        Assert.Null(Bestiary.Describe(Real, "husk", BestiaryTier.Seen).Habits);
        Assert.Equal(Real.Enemies["husk"].Tactics!.Read, Bestiary.Describe(Real, "husk", BestiaryTier.Fought).Habits);
    }
}

public class EnemyHoldTests
{
    private static (ScavengeSim Sim, Unit Husk, Unit A) Field()
    {
        var a = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var husk = EnemyUnit("husk", new(6, 3));
        return (Arena(14, 7, [a, husk]), husk, a);
    }

    [Fact]
    public void A_husk_holds_its_blow_when_the_target_walked_last_round()
    {
        var (sim, husk, a) = Field();
        a.LastMove = new Pos(1, 0);
        Assert.True(EnemyAim.Holds(sim, husk, a));
    }

    [Fact]
    public void A_husk_strikes_a_target_that_stood_still()
    {
        var (sim, husk, a) = Field();
        Assert.False(EnemyAim.Holds(sim, husk, a));
    }

    [Fact]
    public void A_linked_does_not_hold_because_it_leads_instead()
    {
        var a = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var linked = EnemyUnit("linked", new(6, 3));
        var sim = Arena(14, 7, [a, linked]);
        a.LastMove = new Pos(1, 0);
        Assert.False(EnemyAim.Holds(sim, linked, a));
    }

    [Fact]
    public void A_husk_blow_sweeps_three_tiles()
    {
        var (sim, husk, _) = Field();
        var claw = sim.Profile(husk).Action("claw")!;
        Assert.Equal(3, Combat.Covered(sim.State.Map, claw, Remnants.Core.Items.StrikeMode.Slash, husk.Pos, new Pos(-1, 0)).Count());
    }
}
