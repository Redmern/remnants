using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Telegraphed intents: declare a path and an attack on a tile, resolve at the start of the next own turn.</summary>
public class PlannedActionTests
{
    /// <summary>Every attack lands as a plain hit, so outcomes do not hang on the seed.</summary>
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    /// <summary>P1's pipe-wielder at (0,1), P2's survivor at (5,1) on an open 10×3 floor; P1 is up.</summary>
    private static (ScavengeSim Sim, Unit A, Unit B) Duel(ScavengeData? data = null, string[]? rows = null)
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 1), weapon: "pipe");
        var sim = Arena(10, 3, [a, b], rows: rows, data: data ?? Sure);
        return (sim, a, b);
    }

    private static DeclareIntent Charge(Unit a) => new(1, a.Id, [new(2, 1), new(4, 1)], new Pos(5, 1), "swing");

    [Fact]
    public void Declaring_stores_the_plan_ends_the_turn_and_costs_nothing_now()
    {
        var (sim, a, b) = Duel();
        var ap = a.Ap;
        var r = sim.Execute(Charge(a));
        Assert.True(r.Ok, r.Message);
        Assert.Equal(b.Id, sim.State.CurrentUnitId);
        Assert.Equal(ap, a.Ap);
        Assert.Equal(new Pos(0, 1), a.Pos);
        var plan = Assert.IsType<PlannedAction>(a.Plan);
        Assert.Equal([new Pos(2, 1), new Pos(4, 1)], plan.Path);
        Assert.Equal(new Pos(5, 1), plan.AttackTile);
        Assert.Equal(b.Id, plan.TargetId);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentDeclared && e.UnitId == a.Id);
    }

    [Fact]
    public void A_plan_resolves_at_the_start_of_the_next_own_turn_and_spends_that_turns_ap()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.Equal(a.Id, sim.State.CurrentUnitId);
        Assert.Null(a.Plan);
        Assert.Equal(new Pos(4, 1), a.Pos);
        var swing = sim.Profile(a).Action("swing")!;
        Assert.Equal(sim.Profile(a).MaxAp - 2 - swing.ApCost, a.Ap);
        Assert.True(b.Hp < b.MaxHp);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Attacked && e.UnitId == a.Id && e.TargetId == b.Id);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentResolved && e.UnitId == a.Id);
    }

    [Fact]
    public void A_target_that_moved_off_the_tile_is_missed_and_the_ap_is_gone()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(7, 1))).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.Equal(new Pos(4, 1), a.Pos);
        Assert.Equal(b.MaxHp, b.Hp);
        var swing = sim.Profile(a).Action("swing")!;
        Assert.Equal(sim.Profile(a).MaxAp - 2 - swing.ApCost, a.Ap);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == a.Id);
    }

    [Fact]
    public void Dodging_a_telegraphed_attack_gives_the_next_attack_on_the_attacker_the_ambush_bonus_once()
    {
        var (sim, a, b) = Duel(Real);
        Assert.True(sim.Execute(Charge(a)).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(6, 1))).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);

        Assert.Equal(a.Id, b.OutplayedId);
        var swing = sim.Profile(b).Action("swing")!;
        var withBonus = Combat.ChanceToHit(sim, b, swing, a, from: new(5, 1));
        b.OutplayedId = null;
        var without = Combat.ChanceToHit(sim, b, swing, a, from: new(5, 1));
        Assert.Equal(without + Real.Rules.Fog.Ambush.HitBonus, withBonus, 9);

        b.OutplayedId = a.Id;
        Assert.True(sim.Execute(new Move(2, b.Id, new(5, 1))).Ok);
        Assert.True(sim.Execute(new Attack(2, b.Id, a.Id, "swing")).Ok);
        Assert.Null(b.OutplayedId);
    }

    [Fact]
    public void A_plan_that_falls_short_dodges_nobody()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(6, 1))).Ok);
        a.Body.Injure(BodyPart.LeftLeg, 0.6);
        a.Body.Injure(BodyPart.RightLeg, 0.6);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.Equal(new Pos(0, 1), a.Pos);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == a.Id);
        Assert.Null(b.OutplayedId);
        Assert.Equal(sim.Profile(a).MaxAp, a.Ap); // the attack never came, so it cost nothing
    }

    [Fact]
    public void A_dodge_is_good_through_the_next_round_then_lapses()
    {
        var (sim, a, b) = Duel(Real);
        Assert.True(sim.Execute(Charge(a)).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(6, 1))).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);   // round 2: a swings at the empty tile
        var dodged = sim.State.Round;
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(a.Id, b.OutplayedId);                     // b's turn, the same round
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(dodged + 1, sim.State.Round);
        Assert.True(Combat.OutplayBonus(sim, b, a) > 0);       // still good the round after
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(dodged + 2, sim.State.Round);
        Assert.Null(b.OutplayedId);                            // lapsed when b's turn came round again
        Assert.Equal(0, Combat.OutplayBonus(sim, b, a));
    }

    [Fact]
    public void Overwatch_fires_on_a_plan_walking_into_view()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(7, 1), weapon: "pistol");
        b.Attributes[AttributeKind.Agility] = 35; // slower: a stays first, so b's overwatch holds until a's plan walks
        var sim = Arena(10, 3, [a, b], data: Sure);
        Assert.True(sim.Execute(new DeclareIntent(1, a.Id, [new(2, 1), new(4, 1)])).Ok);
        Assert.True(sim.Execute(new SetStance(2, b.Id, Stance.Overwatch)).Ok);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);

        var round = sim.State.Round;
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Overwatch && e.Round == round && e.UnitId == b.Id && e.TargetId == a.Id);
        Assert.True(a.Hp < a.MaxHp);
    }

    [Fact]
    public void A_pending_plan_still_resolves_after_its_player_has_gone()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        sim.Abandon(1);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Null(a.Plan);
        Assert.Equal(new Pos(4, 1), a.Pos);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentResolved && e.UnitId == a.Id);
    }

    [Fact]
    public void Anyone_hostile_who_steps_onto_the_tile_is_hit()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 1), weapon: "pipe");
        var c = SurvivorUnit(3, new(5, 0), weapon: "pipe");
        var sim = Arena(10, 3, [a, b, c], data: Sure);
        Assert.True(sim.Execute(Charge(a)).Ok);
        Assert.True(sim.Execute(new Move(2, b.Id, new(7, 1))).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.True(sim.Execute(new Move(3, c.Id, new(5, 1))).Ok);
        Assert.True(sim.Execute(new EndTurn(3, c.Id)).Ok);

        Assert.True(c.Hp < c.MaxHp);
        Assert.Equal(b.MaxHp, b.Hp);
    }

    [Fact]
    public void A_plan_must_be_reachable_in_reach_and_affordable_now()
    {
        var (sim, a, _) = Duel();
        Assert.Equal(ScavengeError.Unreachable, sim.Execute(new DeclareIntent(1, a.Id, [new(4, 1)])).Error);
        Assert.Equal(ScavengeError.OutOfRange, sim.Execute(new DeclareIntent(1, a.Id, [new(2, 1)], new Pos(5, 1), "swing")).Error);
        Assert.Equal(ScavengeError.UnknownAction, sim.Execute(new DeclareIntent(1, a.Id, [new(2, 1)], new Pos(3, 1), "shoot")).Error);
        Assert.Equal(ScavengeError.InvalidTarget, sim.Execute(new DeclareIntent(1, a.Id, [])).Error);
        // Checked against a full turn's AP (what it pays from), not what is left now: 3 moves and a swing is 4 of 3.
        Assert.Equal(ScavengeError.NotEnoughAp, sim.Execute(new DeclareIntent(1, a.Id, [new(2, 0), new(4, 0), new(6, 0)], new Pos(5, 1), "swing")).Error);
        Assert.Null(a.Plan);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);
    }

    [Fact]
    public void An_attack_needs_line_of_sight_from_the_destination()
    {
        var (sim, a, _) = Duel(rows: ["..........", ".....#....", ".........."]);
        a.Equipment[Remnants.Core.Items.EquipSlot.Weapon] = "pistol";
        Assert.Equal(ScavengeError.NoLineOfSight, sim.Execute(new DeclareIntent(1, a.Id, [new(2, 1)], new Pos(7, 1), "shoot")).Error);
        Assert.True(sim.Execute(new DeclareIntent(1, a.Id, [new(2, 0)], new Pos(7, 0), "shoot")).Ok);
    }

    [Fact]
    public void A_plan_is_locked_until_it_resolves()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        Assert.Equal(ScavengeError.NotYourTurn, sim.Execute(new DeclareIntent(1, a.Id, [new(1, 1)])).Error);
        Assert.Equal(ScavengeError.NotYourTurn, sim.Execute(new EndTurn(1, a.Id)).Error);
        Assert.NotNull(a.Plan);
        Assert.Equal([new Pos(2, 1), new Pos(4, 1)], a.Plan!.Path);
    }

    [Fact]
    public void Being_downed_cancels_the_plan()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        Combat.ApplyDamage(sim, a, BodyPart.Torso, 100);
        Assert.Equal(UnitStatus.Downed, a.Status);
        Assert.Null(a.Plan);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentCanceled && e.UnitId == a.Id);
    }

    [Fact]
    public void Being_pinned_or_stunned_cancels_the_plan()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        a.Suppressed = true;
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(a.Id, sim.State.CurrentUnitId);
        Assert.Null(a.Plan);
        Assert.Equal(new Pos(0, 1), a.Pos);
        Assert.Equal(b.MaxHp, b.Hp);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentCanceled && e.UnitId == a.Id);
    }

    [Fact]
    public void Legs_hurt_after_declaring_cut_the_path_short()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        a.Body.Injure(BodyPart.LeftLeg, 0.6);
        a.Body.Injure(BodyPart.RightLeg, 0.6);
        Assert.Equal(1, sim.Profile(a).Movement.Primary.Range);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);

        Assert.Equal(new Pos(0, 1), a.Pos); // (2,1) is two tiles away: the first leg of the path is out of reach
        Assert.Equal(b.MaxHp, b.Hp);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == a.Id);
    }

    [Fact]
    public void Plans_resolve_in_turn_order()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var b = SurvivorUnit(2, new(9, 1), weapon: "pipe");
        var sim = Arena(10, 3, [a, b], data: Sure);
        Assert.True(sim.Execute(new DeclareIntent(1, a.Id, [new(2, 1)])).Ok);
        Assert.True(sim.Execute(new DeclareIntent(2, b.Id, [new(7, 1)])).Ok);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);

        var resolved = sim.State.Events.Where(e => e.Type == ScavengeEventType.IntentResolved).Select(e => e.UnitId).ToList();
        Assert.Equal([a.Id, b.Id], resolved);
        Assert.True(sim.State.TurnOrder.IndexOf(a.Id) < sim.State.TurnOrder.IndexOf(b.Id));
        Assert.Equal((new Pos(2, 1), new Pos(7, 1)), (a.Pos, b.Pos));
    }

    [Fact]
    public void Declaring_makes_no_noise()
    {
        var a = SurvivorUnit(1, new(0, 1), weapon: "pipe");
        var husk = EnemyUnit("husk", new(9, 1));
        var sim = Arena(12, 3, [a, husk], data: Sure);
        husk.Ai.Mode = AiMode.Idle;
        Assert.True(sim.Execute(new DeclareIntent(1, a.Id, [new(2, 1)])).Ok);
        Assert.Equal(AiMode.Idle, husk.Ai.Mode);
    }

    [Fact]
    public void A_move_only_plan_walks_the_path()
    {
        var (sim, a, b) = Duel();
        Assert.True(sim.Execute(new DeclareIntent(1, a.Id, [new(1, 0), new(3, 0)])).Ok);
        Assert.True(sim.Execute(new EndTurn(2, b.Id)).Ok);
        Assert.Equal(new Pos(3, 0), a.Pos);
        Assert.Equal(sim.Profile(a).MaxAp - 2, a.Ap);
    }

    [Fact]
    public void A_pending_plan_survives_save_and_load()
    {
        var (sim, a, _) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        var loaded = ScavengeSave.Load(Sure, ScavengeSave.Save(sim.State));
        var plan = Assert.IsType<PlannedAction>(loaded.State.FindUnit(a.Id)!.Plan);
        Assert.Equal([new Pos(2, 1), new Pos(4, 1)], plan.Path);
        Assert.Equal("swing", plan.ActionId);
        Assert.Equal(15, ScavengeSave.Version);
    }

    [Fact]
    public void A_plan_whose_legs_were_hurt_after_declaring_still_saves_and_loads()
    {
        var (sim, a, _) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        a.Body.Injure(BodyPart.LeftLeg, 0.6);
        a.Body.Injure(BodyPart.RightLeg, 0.6);
        Assert.Equal(1, sim.Profile(a).Movement.Primary.Range); // the declared 2-tile steps are now out of one move
        var loaded = ScavengeSave.Load(Sure, ScavengeSave.Save(sim.State));
        Assert.Equal([new Pos(2, 1), new Pos(4, 1)], loaded.State.FindUnit(a.Id)!.Plan!.Path);
    }

    [Theory]
    [InlineData("downed")]
    [InlineData("unknown target")]
    [InlineData("step on the start tile")]
    [InlineData("step off the map")]
    public void A_save_with_a_broken_plan_is_refused(string broken)
    {
        var (sim, a, _) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        ScavengeSave.Load(Sure, ScavengeSave.Save(sim.State)); // the plan as declared loads
        var plan = a.Plan!;
        switch (broken)
        {
            case "downed": a.Status = UnitStatus.Downed; break;
            case "unknown target": plan.TargetId = 99; break;
            case "step on the start tile": plan.Path = [a.Pos, new(2, 1)]; break;
            case "step off the map": plan.Path = [new(-1, 1)]; break;
        }
        Assert.Contains("plan", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Sure, ScavengeSave.Save(sim.State))).Message);
    }

    [Fact]
    public void A_save_whose_plan_leaves_the_map_is_refused()
    {
        var (sim, a, _) = Duel();
        Assert.True(sim.Execute(Charge(a)).Ok);
        a.Plan!.AttackTile = new Pos(40, 1);
        Assert.Contains("plan", Assert.Throws<System.Text.Json.JsonException>(() => ScavengeSave.Load(Sure, ScavengeSave.Save(sim.State))).Message);
    }

    [Fact]
    public void Attack_events_carry_the_hit_tier()
    {
        var (sim, a, b) = Duel();
        a.Pos = new(4, 1);
        Assert.True(sim.Execute(new Attack(1, a.Id, b.Id, "swing")).Ok);
        var hit = Assert.Single(sim.State.Events, e => e.Type == ScavengeEventType.Attacked);
        Assert.Equal(HitTier.Hit, hit.Tier);
        Assert.Equal(b.MaxHp - b.Hp, hit.Amount!.Value, 9);
    }

    [Fact]
    public void Same_seed_and_commands_give_the_same_game()
    {
        string Play()
        {
            var (sim, a, b) = Duel(Real);
            sim.Execute(Charge(a));
            sim.Execute(new Move(2, b.Id, new(6, 1)));
            sim.Execute(new EndTurn(2, b.Id));
            sim.Execute(new EndTurn(1, a.Id));
            return ScavengeSave.Save(sim.State);
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void A_path_longer_than_the_budget_is_refused_before_it_is_walked()
    {
        var (sim, a, _) = Duel();
        var budget = PlannedActions.Budget(sim, a);
        // Back and forth past the budget, then a tile nobody can reach: the AP runs out first, so that is the error.
        var path = Enumerable.Range(0, budget + 1).Select(i => i % 2 == 0 ? new Pos(1, 1) : new Pos(0, 1)).Append(new Pos(9, 9)).ToList();
        Assert.Equal(ScavengeError.NotEnoughAp, sim.Execute(new DeclareIntent(1, a.Id, path)).Error);
        // A client flooding the path is refused on its length alone.
        var flood = Enumerable.Range(0, 100_000).Select(i => i % 2 == 0 ? new Pos(1, 1) : new Pos(0, 1)).ToList();
        Assert.Equal(ScavengeError.NotEnoughAp, sim.Execute(new DeclareIntent(1, a.Id, flood)).Error);
        Assert.Null(a.Plan);
        Assert.Equal(new Pos(0, 1), a.Pos);
    }
}
