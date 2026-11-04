using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Push and dash are hidden actions resolved in priority order; a pushed unit's queued action goes off from where it lands.</summary>
public class PushDashTests
{
    private static readonly ScavengeData Sure = With(r => r with
    {
        Combat = r.Combat with { MinHitChance = 1, MaxHitChance = 1, Tiers = r.Combat.Tiers with { GrazeBelow = 0, SolidFrom = 1 } },
    });

    private static readonly Pos East = new(1, 0);

    private static ScavengeSim Table(IEnumerable<Unit> units, params string[] hand)
    {
        var sim = Arena(12, 7, units, data: Sure);
        foreach (var pid in units.Where(u => u.PlayerId is not null).Select(u => u.PlayerId!.Value).Distinct())
            sim.State.Decks.Add(new SquadDeck { PlayerId = pid, Hand = hand.ToList(), Points = 9, Budget = 9, HandSize = 9 });
        return sim;
    }

    private static void Wall(ScavengeSim sim, Pos at) => sim.State.Map[at] = Terrain.Wall;

    [Fact]
    public void A_push_moves_who_is_in_front_a_tile_away_from_the_pusher()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table([a, b]);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.Equal(new Pos(4, 3), b.Pos);
        Settle(sim);
        Assert.Equal(new Pos(5, 3), b.Pos);
        Assert.Equal(new Pos(3, 3), a.Pos);
    }

    [Fact]
    public void A_push_costs_one_ap_and_two_stamina_is_hidden_and_taken_back_in_full()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var sim = Table([a, SurvivorUnit(2, new(4, 3))]);
        var (ap, stamina, events) = (a.Ap, Economy.Stamina(sim, a), sim.State.Events.Count);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.Equal((ap - 1, stamina - 2), (a.Ap, Economy.Stamina(sim, a)));
        Assert.Equal(events, sim.State.Events.Count);
        Assert.True(sim.Execute(new UndoPlan(1, a.Id)).Ok);
        Assert.Equal((ap, stamina), (a.Ap, Economy.Stamina(sim, a)));
    }

    [Fact]
    public void A_pushed_units_queued_blow_goes_off_from_where_it_landed()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3), weapon: "pipe");
        var there = SurvivorUnit(1, new(4, 5));
        var sim = Table([a, b, there]);
        a.PriorityBonus = 20;
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        // b aims south at a tile next to it; pushed east, the same blow falls one tile east of that.
        var victim = SurvivorUnit(1, new(5, 4));
        sim.State.Units.Add(victim);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, new(4, 4), "swing")).Ok);
        Settle(sim);
        Assert.Equal(new Pos(5, 3), b.Pos);
        Assert.True(victim.Hp < victim.MaxHp);
    }

    [Fact]
    public void Whoever_comes_up_first_acts_first_a_slower_push_finds_the_blow_already_landed()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3), weapon: "pipe");
        var sim = Table([a, b]);
        b.PriorityBonus = 20;
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Settle(sim);
        Assert.True(a.Hp < a.MaxHp);
        Assert.Equal(new Pos(5, 3), b.Pos);
    }

    [Fact]
    public void A_wall_stops_a_push_with_collision_damage()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table([a, b]);
        Wall(sim, new(5, 3));
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(4, 3), b.Pos);
        Assert.Equal(b.MaxHp - sim.Data.Rules.Combat.Push.CollisionDamage, b.Hp, 6);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Collided && e.TargetId == b.Id);
    }

    [Fact]
    public void A_unit_in_the_way_stops_a_push_hurting_both_and_is_not_pushed_on()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3));
        var c = SurvivorUnit(2, new(5, 3));
        var sim = Table([a, b, c]);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Settle(sim);
        Assert.Equal((new Pos(4, 3), new Pos(5, 3)), (b.Pos, c.Pos));
        var push = sim.Data.Rules.Combat.Push;
        Assert.Equal(b.MaxHp - push.CollisionDamage, b.Hp, 6);
        Assert.Equal(c.MaxHp - push.BlockerDamage, c.Hp, 6);
    }

    [Fact]
    public void A_heavy_card_pushes_every_unit_on_its_pattern_n_tiles_stopping_at_a_wall_part_way()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var free = SurvivorUnit(2, new(4, 3));
        var walled = SurvivorUnit(2, new(4, 4));
        var sim = Table([a, free, walled], "bull_rush");
        Wall(sim, new(6, 6));
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "bull_rush", Direction: East)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(6, 3), free.Pos);
        Assert.Equal(new Pos(5, 5), walled.Pos); // away from the pusher: diagonally, and the wall is one step short
        Assert.Equal(free.MaxHp, free.Hp);
        Assert.True(walled.Hp < walled.MaxHp);
    }

    [Fact]
    public void A_defended_tile_takes_one_off_a_push_and_a_one_tile_push_then_does_nothing()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table([a, b], "parry", "bull_rush");
        Wall(sim, new(5, 3));
        Assert.True(sim.Execute(new PlayCard(2, b.Id, "parry", Tile: b.Pos)).Ok);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(4, 3), b.Pos);
        Assert.Equal(b.MaxHp, b.Hp); // not moved, so no collision

        var c = SurvivorUnit(1, new(3, 3));
        var d = SurvivorUnit(2, new(4, 3));
        var sim2 = Table([c, d], "parry", "bull_rush");
        Assert.True(sim2.Execute(new PlayCard(2, d.Id, "parry", Tile: d.Pos)).Ok);
        Assert.True(sim2.Execute(new PlayCard(1, c.Id, "bull_rush", Direction: East)).Ok);
        Settle(sim2);
        Assert.Equal(new Pos(5, 3), d.Pos);
    }

    [Fact]
    public void A_push_at_nobody_only_makes_it_known_it_fell_through()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var sim = Table([a, SurvivorUnit(2, new(9, 3))]);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.IntentFizzled && e.UnitId == a.Id);
    }

    [Fact]
    public void A_push_pushes_an_ally_on_the_tile_and_the_aim_warns_of_it()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var friend = SurvivorUnit(1, new(4, 3));
        var sim = Table([a, friend, SurvivorUnit(2, new(9, 3))]);
        Assert.Equal([friend.Pos], TacticsView.PushAim(sim, a, East).AllyTiles);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(5, 3), friend.Pos);
    }

    [Fact]
    public void A_tied_push_still_moves_but_its_collision_is_softer()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table([a, b]);
        Wall(sim, new(5, 3));
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new Push(2, b.Id, new(-1, 0))).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Clashed);
        Assert.Equal(b.MaxHp - sim.Data.Rules.Combat.Push.CollisionDamage * sim.Data.Rules.Priority.TieEffect, b.Hp, 6);
        Assert.Equal(new Pos(2, 3), a.Pos);
    }

    [Fact]
    public void Push_and_dash_are_quick()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var sim = Table([a]);
        var quick = sim.Data.Rules.Priority.QuickWeight;
        Assert.Equal(Priority.Movement(sim, a) + quick, Priority.Action(sim, a, new PlanStep { Kind = PlanStepKind.Push }), 6);
        Assert.Equal(Priority.Movement(sim, a) + quick, Priority.Action(sim, a, new PlanStep { Kind = PlanStepKind.Dash }), 6);
    }

    [Fact]
    public void A_dash_goes_straight_in_the_action_phase_and_is_not_telegraphed()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var sim = Table([a, SurvivorUnit(2, new(9, 3))]);
        var (ap, stamina, events) = (a.Ap, Economy.Stamina(sim, a), sim.State.Events.Count);
        Assert.True(sim.Execute(new Dash(1, a.Id, East, 3)).Ok);
        Assert.Equal((new Pos(2, 3), ap - 1, stamina - 2, events), (a.Pos, a.Ap, Economy.Stamina(sim, a), sim.State.Events.Count));
        Settle(sim);
        Assert.Equal(new Pos(5, 3), a.Pos);
        Assert.Equal(East, a.Facing);
    }

    [Fact]
    public void A_dash_stops_next_to_who_is_in_the_way_and_never_passes_through()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table([a, b]);
        Assert.True(sim.Execute(new Dash(1, a.Id, East, 3)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(3, 3), a.Pos);
    }

    [Fact]
    public void A_dash_is_refused_past_its_reach_into_a_wall_or_with_no_direction_but_a_hidden_unit_gives_nothing_away()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var hidden = SurvivorUnit(2, new(3, 3));
        var sim = Table([a, hidden]);
        Assert.False(sim.Execute(new Dash(1, a.Id, East, sim.Data.Rules.Combat.Dash.Tiles + 1)).Ok);
        Assert.False(sim.Execute(new Dash(1, a.Id, default, 1)).Ok);
        Assert.True(sim.Execute(new Dash(1, a.Id, East, 2)).Ok); // someone stands there: the plan is not told
        Settle(sim);
        Assert.Equal(new Pos(2, 3), a.Pos);

        var c = SurvivorUnit(1, new(2, 3));
        var sim2 = Table([c]);
        Wall(sim2, new(3, 3));
        Assert.False(sim2.Execute(new Dash(1, c.Id, East, 2)).Ok);
    }

    [Fact]
    public void Moves_are_planned_before_a_dash_and_a_blow_after_it_is_aimed_from_its_end()
    {
        var a = SurvivorUnit(1, new(1, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(6, 3));
        var sim = Table([a, b]);
        Assert.True(sim.Execute(new Move(1, a.Id, new(2, 3))).Ok);
        Assert.True(sim.Execute(new Dash(1, a.Id, East, 3)).Ok);
        Assert.Equal(new Pos(5, 3), Plans.Anchor(sim.State, a));
        Assert.False(sim.Execute(new Move(1, a.Id, new(5, 4))).Ok);
        a.Ap = 2;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, b.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Equal(new Pos(5, 3), a.Pos);
        Assert.True(b.Hp < b.MaxHp);
    }

    [Fact]
    public void A_blow_after_a_dash_cut_short_falls_short_with_it()
    {
        var a = SurvivorUnit(1, new(1, 3), weapon: "pipe");
        var blocker = SurvivorUnit(2, new(3, 3));
        var where = SurvivorUnit(2, new(5, 3));
        var sim = Table([a, blocker, where]);
        Assert.True(sim.Execute(new Dash(1, a.Id, East, 3)).Ok);
        a.Ap = 2;
        Assert.True(sim.Execute(new AttackTile(1, a.Id, where.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Equal(new Pos(2, 3), a.Pos);
        Assert.Equal(where.MaxHp, where.Hp);
    }

    [Fact]
    public void A_dash_card_swings_at_the_end_with_a_hit_bonus_for_the_run_up()
    {
        var a = SurvivorUnit(1, new(1, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(5, 3));
        var sim = Table([a, b], "rushing_strike");
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "rushing_strike", Direction: East)).Ok);
        Assert.Equal(new Pos(4, 3), Plans.Anchor(sim.State, a));
        Settle(sim);
        Assert.Equal(new Pos(4, 3), a.Pos);
        Assert.True(b.Hp < b.MaxHp);
    }

    [Fact]
    public void A_dash_alone_never_attacks()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var b = SurvivorUnit(2, new(4, 3));
        var sim = Table([a, b]);
        Assert.True(sim.Execute(new Dash(1, a.Id, East, 3)).Ok);
        Settle(sim);
        Assert.Equal(b.MaxHp, b.Hp);
    }

    [Fact]
    public void The_shove_card_into_a_wall_is_a_collision_and_leaves_the_victims_plan_alone()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var b = SurvivorUnit(2, new(4, 3), weapon: "pipe");
        var sim = Table([a, b], "shove");
        Wall(sim, new(5, 3));
        a.PriorityBonus = 20;
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "shove", b.Id)).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing")).Ok);
        Settle(sim);
        Assert.Equal(new Pos(4, 3), b.Pos);
        Assert.True(b.Hp < b.MaxHp);
        Assert.True(a.Hp < a.MaxHp);
    }

    [Fact]
    public void The_same_seed_and_plans_give_the_same_pushes()
    {
        List<string> Run()
        {
            var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
            var b = SurvivorUnit(2, new(4, 3), weapon: "pipe");
            var c = SurvivorUnit(2, new(5, 3));
            var sim = Table([a, b, c]);
            sim.Execute(new Push(1, a.Id, East));
            sim.Execute(new AttackTile(2, b.Id, a.Pos, "swing"));
            sim.Execute(new Dash(2, c.Id, East, 2));
            sim.Execute(new EndTurn(1, a.Id));
            sim.Execute(new EndTurn(2, b.Id));
            sim.Execute(new EndTurn(2, c.Id));
            return sim.State.Events.Select(e => e.Message).ToList();
        }
        Assert.Equal(Run(), Run());
    }
}
