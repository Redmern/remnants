using Remnants.Core.Survivors;
using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>A round is a planning phase (everyone at once, own plan editable) then a resolve phase: all moves by priority, then all actions by priority.</summary>
public class PlanResolveTests
{
    private static (ScavengeSim Sim, Unit A, Unit B) Duel(Pos? a = null, Pos? b = null, string weaponA = "pipe", string weaponB = "pipe")
    {
        var ua = SurvivorUnit(1, a ?? new Pos(1, 2), weapon: weaponA);
        var ub = SurvivorUnit(2, b ?? new Pos(8, 2), weapon: weaponB);
        return (Arena(12, 6, [ua, ub]), ua, ub);
    }

    [Fact]
    public void Any_unit_can_plan_in_any_order_and_nothing_moves_until_everyone_commits()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(new Move(2, b.Id, new Pos(6, 2))).Ok);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(3, 2))).Ok);
        Assert.Equal(new Pos(1, 2), a.Pos);
        Assert.Equal(new Pos(8, 2), b.Pos);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(new Pos(1, 2), a.Pos);
        Assert.Equal(1, sim.State.Round);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(new Pos(3, 2), a.Pos);
        Assert.Equal(new Pos(6, 2), b.Pos);
        Assert.Equal(2, sim.State.Round);
    }

    [Fact]
    public void A_plan_is_editable_until_planning_ends_and_refunds_what_it_paid()
    {
        var (sim, a, _) = Duel();
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(3, 2))).Ok);
        Assert.Equal(2, a.Mp);
        Assert.Equal(8, Economy.Stamina(sim, a));
        Assert.True(sim.Execute(new UndoPlan(1, a.Id)).Ok);
        Assert.Equal(4, a.Mp);
        Assert.Equal(10, Economy.Stamina(sim, a));
        Assert.Equal(new Pos(1, 2), Plans.Anchor(sim.State, a));
        Assert.Equal(ScavengeError.NoPlan, sim.Execute(new UndoPlan(1, a.Id)).Error);
    }

    [Fact]
    public void A_committed_unit_reopens_when_its_plan_is_edited()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(2, 2))).Ok);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void Moves_chain_from_where_the_last_planned_move_ends()
    {
        var (sim, a, _) = Duel();
        a.ExtraMoves = 1;
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(3, 2))).Ok);
        Assert.Equal(new Pos(3, 2), Plans.Anchor(sim.State, a));
        Assert.True(sim.Reachable(a).ContainsKey(new Pos(5, 2)));
        Assert.False(sim.Reachable(a).ContainsKey(new Pos(6, 2)));
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(5, 2))).Ok);
        Assert.Equal(0, a.Mp);
    }

    [Fact]
    public void Everything_moves_before_anything_strikes()
    {
        // B stands on the struck tile at planning time and walks off; A's blow finds nobody.
        var (sim, a, b) = Duel(new Pos(1, 2), new Pos(2, 2));
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new Pos(2, 2), "swing")).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new Pos(5, 2))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(100, b.Hp);
        Assert.DoesNotContain(sim.State.Events, e => e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void An_attack_is_aimed_from_where_the_planned_moves_end()
    {
        var (sim, a, b) = Duel(new Pos(1, 2), new Pos(4, 2));
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new AttackTile(1, a.Id, new Pos(4, 2), "swing")).Error);
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(3, 2))).Ok);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, new Pos(4, 2), "swing")).Ok);
        Assert.Equal(0, a.Ap);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.UnitId == a.Id && e.TargetId == b.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void A_faster_fresher_unit_resolves_first()
    {
        var (sim, a, b) = Duel(new Pos(1, 2), new Pos(2, 2));
        a.Attributes[AttributeKind.Agility] = 70;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        var strikes = sim.State.Events.Where(e => e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed).Select(e => e.UnitId).ToList();
        Assert.Equal([a.Id, b.Id], strikes);
    }

    [Fact]
    public void Low_stamina_drops_priority_steeply_below_the_cliff()
    {
        var (sim, a, b) = Duel();
        double At(int stamina)
        {
            a.Fatigue = 10 - stamina;
            return Priority.Movement(sim, a);
        }
        var above = At(6) - At(4);
        var below = At(2) - At(0);
        Assert.True(below > above, $"{below} should drop more than {above}");
    }

    [Fact]
    public void Action_weight_puts_a_defend_before_a_heavy_blow()
    {
        var (sim, a, _) = Duel(weaponA: "sword");
        var defend = new PlanStep { Kind = PlanStepKind.Defend };
        var heavy = new PlanStep { Kind = PlanStepKind.Strike, Strike = new TileStrike { ActionId = "blade", UnitId = a.Id } };
        Assert.True(Priority.Action(sim, a, defend) > Priority.Action(sim, a, heavy));
    }

    [Fact]
    public void A_units_own_actions_resolve_in_the_order_they_were_queued()
    {
        var (sim, a, b) = Duel(new Pos(1, 2), new Pos(2, 2));
        a.Fatigue = 6;
        (a.Ap, a.ExtraActions) = (2, 1);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new Recover(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        var first = sim.State.Events.FindIndex(e => e.UnitId == a.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
        var then = sim.State.Events.FindIndex(e => e.UnitId == a.Id && e.Type == ScavengeEventType.Recovered);
        Assert.True(first >= 0 && then > first);
    }

    [Fact]
    public void Tied_hostiles_both_resolve_at_reduced_effect_with_no_dice_on_order()
    {
        double Dealt(ulong seed, bool tied)
        {
            var a = SurvivorUnit(1, new Pos(1, 2), weapon: "pipe");
            var b = SurvivorUnit(2, new Pos(2, 2), weapon: "pipe");
            if (!tied) a.Attributes[AttributeKind.Agility] = 70;
            a.Skills[Skill.Melee] = 100;
            var sim = Arena(12, 6, [a, b], seed: seed);
            Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
            if (tied) Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
            Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
            Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
            if (tied) Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Clashed);
            return sim.State.Events.Where(e => e.Type == ScavengeEventType.Attacked && e.UnitId == a.Id).Sum(e => e.Amount ?? 0);
        }
        var seeds = Enumerable.Range(1, 40).Select(i => (ulong)i).ToList();
        Assert.True(seeds.Sum(s => Dealt(s, true)) < seeds.Sum(s => Dealt(s, false)));
    }

    [Fact]
    public void A_tied_unit_struck_down_first_still_gets_its_blow_off()
    {
        var a = SurvivorUnit(1, new Pos(1, 2), weapon: "pipe");
        var b = SurvivorUnit(2, new Pos(2, 2), weapon: "pipe");
        a.Skills[Skill.Melee] = 100;
        b.Skills[Skill.Melee] = 100;
        b.Hp = 1;
        var sim = Arena(12, 6, [a, b], seed: 3);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Contains(sim.State.Events, e => e.UnitId == b.Id && e.TargetId == a.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void Allies_on_the_same_score_are_no_clash()
    {
        var a = SurvivorUnit(1, new Pos(1, 2), weapon: "pipe");
        var c = SurvivorUnit(1, new Pos(1, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new Pos(2, 2), weapon: "pipe");
        b.Attributes[AttributeKind.Agility] = 10;
        var sim = Arena(12, 6, [a, c, b]);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new AttackTile(1, c.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, c.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.DoesNotContain(sim.State.Events, e => e.Type == ScavengeEventType.Clashed);
    }

    [Fact]
    public void A_blocked_walk_stops_on_the_last_free_tile_and_the_first_by_priority_gets_the_tile()
    {
        var (sim, a, b) = Duel(new Pos(2, 2), new Pos(6, 2));
        a.Attributes[AttributeKind.Agility] = 59;
        Assert.True(sim.Execute(new Move(1, a.Id, new Pos(4, 2))).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new Pos(4, 2))).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(new Pos(4, 2), a.Pos);
        Assert.Equal(1, b.Pos.Chebyshev(a.Pos));
    }

    [Fact]
    public void A_dead_units_queued_blow_is_lost()
    {
        var (sim, a, b) = Duel(new Pos(1, 2), new Pos(2, 2));
        a.Attributes[AttributeKind.Agility] = 90;
        a.Skills[Skill.Melee] = 100;
        b.Hp = 0.5;
        b.CanBeDowned = false;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(UnitStatus.Dead, b.Status);
        Assert.DoesNotContain(sim.State.Events, e => e.UnitId == b.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void The_same_seed_and_plans_give_the_same_events()
    {
        List<string> Run()
        {
            var u = SurvivorUnit(1, new Pos(1, 2), weapon: "pipe");
            var v = SurvivorUnit(1, new Pos(1, 3), weapon: "pipe");
            var h1 = EnemyUnit("husk", new Pos(4, 2));
            var h2 = EnemyUnit("husk", new Pos(4, 4));
            var sim = Arena(12, 6, [u, v, h1, h2], seed: 42);
            sim.Execute(new AttackTile(1, u.Id, new Pos(2, 2), "swing"));
            sim.Execute(new EndTurn(1, u.Id));
            sim.Execute(new EndTurn(1, v.Id));
            sim.Execute(new EndTurn(1, u.Id));
            sim.Execute(new EndTurn(1, v.Id));
            return sim.State.Events.Select(e => $"{e.Round}:{e.Type}:{e.Message}").ToList();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Enemy_moves_are_planned_when_the_round_opens_and_taken_in_the_movement_phase()
    {
        var u = SurvivorUnit(1, new Pos(1, 2), weapon: "pipe");
        var husk = EnemyUnit("husk", new Pos(5, 2));
        var sim = Arena(12, 6, [u, husk]);
        sim.Start();
        var plan = Plans.Of(sim.State, husk);
        Assert.NotNull(plan);
        Assert.Contains(plan.Steps, s => s.Kind == PlanStepKind.Move);
        Assert.Equal(new Pos(5, 2), husk.Pos);
        Assert.True(sim.Execute(new EndTurn(1, u.Id)).Ok);
        Assert.NotEqual(new Pos(5, 2), husk.Pos);
    }

    [Fact]
    public void An_ai_does_not_read_a_players_plan()
    {
        Pos Plans_(bool planned)
        {
            var u = SurvivorUnit(1, new Pos(1, 2), weapon: "pipe");
            var husk = EnemyUnit("husk", new Pos(5, 2));
            var sim = Arena(12, 6, [u, husk]);
            if (planned)
            {
                sim.Execute(new Move(1, u.Id, new Pos(3, 3)));
                sim.Execute(new AttackTile(1, u.Id, new Pos(4, 3), "swing"));
            }
            sim.Start();
            return Plans.Anchor(sim.State, husk);
        }
        Assert.Equal(Plans_(false), Plans_(true));
    }

    // What the retired reaction, overwatch and sub-round tests guarded, where the behaviour lives on.

    private static readonly ScavengeData Certain = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    [Fact]
    public void Stacked_damage_multipliers_are_capped_by_the_data()
    {
        double Cut(bool capped, Pos facing)
        {
            var data = With(r => r with { Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, MaxDamageMultiplier = capped ? 1 : r.Combat.MaxDamageMultiplier } });
            var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
            var b = SurvivorUnit(2, new(4, 3));
            b.Facing = facing;
            var sim = Arena(10, 7, [a, b], data: data);
            Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
            Settle(sim);
            return b.MaxHp - b.Hp;
        }
        var front = new Pos(-1, 0);
        var back = new Pos(1, 0);
        Assert.True(Cut(capped: false, back) > Cut(capped: false, front), "the test needs a rear hit worth more than a front one");
        Assert.Equal(Cut(capped: true, front), Cut(capped: true, back), 6);
    }

    [Fact]
    public void A_target_that_walks_off_the_aimed_tile_is_not_struck()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Arena(10, 7, [a, b], data: Certain);
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(6, 3))).Ok);
        Settle(sim);
        Assert.Equal(b.MaxHp, b.Hp);
        // Dodging earned nothing: no counter queued, no exposure to cash in.
        Assert.Empty(sim.State.Plans);
    }

    [Fact]
    public void A_plain_hit_leaves_the_victims_queued_blow_standing()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3), weapon: "pipe");
        var sim = Arena(10, 7, [a, b], data: Certain);
        a.PriorityBonus = 20;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Settle(sim);
        Assert.True(a.Hp < a.MaxHp);
    }

    [Fact]
    public void Queuing_an_action_tells_the_log_nothing()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Arena(10, 7, [a, b], data: Certain);
        var events = sim.State.Events.Count;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Assert.Equal(events, sim.State.Events.Count);
    }

    [Fact]
    public void A_walker_faces_the_way_it_went_and_counts_as_having_moved()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var sim = Arena(10, 7, [a, SurvivorUnit(2, new(9, 6))], data: Certain);
        Assert.True(sim.Execute(new Move(1, a.Id, new(3, 5))).Ok);
        Settle(sim);
        Assert.Equal(new Pos(0, 1), a.Facing);
        Assert.True(a.Moved);
    }

    [Fact]
    public void A_plan_survives_save_and_load_and_resolves_the_same()
    {
        (ScavengeSim Sim, Unit A) Planned()
        {
            var a = SurvivorUnit(1, new(1, 3), weapon: "pipe");
            var b = SurvivorUnit(2, new(5, 3));
            var sim = Arena(10, 7, [a, b], data: Certain);
            Assert.True(sim.Execute(new Dash(1, a.Id, new Pos(1, 0), 3)).Ok);
            (a.Ap, a.ExtraActions) = (2, 2);
            Assert.True(sim.Execute(new Push(1, a.Id, new Pos(1, 0))).Ok);
            Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
            return (sim, a);
        }
        var (live, _) = Planned();
        var (saved, aSaved) = Planned();
        var loaded = ScavengeSave.Load(Certain, ScavengeSave.Save(saved.State));
        Assert.Equal(new Pos(4, 3), Plans.Anchor(loaded.State, loaded.State.FindUnit(aSaved.Id)!));
        Settle(live);
        Settle(loaded);
        Assert.Equal(live.State.Events.Select(e => e.Message), loaded.State.Events.Select(e => e.Message));
    }
}
