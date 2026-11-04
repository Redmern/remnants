using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>What the planning UI reads from Core, and the worked scenario of the rework (AC-10).</summary>
public class PlanningViewTests
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

    /// <summary>AC-10: A sees B moving toward A, moves in range and plans a push; A has priority, pushes B; B's attack still goes off from B's pushed-to tile; A gains adrenaline.</summary>
    [Fact]
    public void AC10_a_sees_b_coming_and_pushes_it_b_still_strikes_from_where_it_lands_and_a_gains_adrenaline()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var mate = SurvivorUnit(1, new(6, 4));
        var b = SurvivorUnit(2, new(7, 3), weapon: "pipe");
        var sim = Table([a, mate, b]);
        a.PriorityBonus = 20;

        // B plans: walk toward A, then a blow at the tile south of where it will stand.
        Assert.True(sim.Execute(new Move(2, b.Id, new(5, 3))).Ok);
        Assert.True(sim.Execute(new AttackTile(2, b.Id, new(5, 4), "swing")).Ok);
        // A sees the walk, and nothing of the blow.
        var seen = Assert.Single(TacticsView.Telegraphs(sim, a.Side));
        Assert.Equal((b.Id, new Pos(5, 3)), (seen.UnitId, seen.To));
        Assert.DoesNotContain(ThreatKind.Strike, TacticsView.Threats(sim, a.Side).Values);

        // A walks into range of where B will be and plans the push.
        Assert.True(sim.Execute(new Move(1, a.Id, new(4, 3))).Ok);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        var hp = (a.Hp, mate.Hp, b.Hp);
        var before = sim.State.Events.Count;
        Settle(sim);

        Assert.Equal(new Pos(4, 3), a.Pos);
        Assert.Equal(new Pos(6, 3), b.Pos); // pushed from (5,3)
        var after = sim.State.Events.Skip(before).ToList();
        var pushed = after.FindIndex(e => e.Type == ScavengeEventType.Displaced && e.TargetId == b.Id);
        var blow = after.FindIndex(e => e.Type == ScavengeEventType.Attacked && e.UnitId == b.Id);
        Assert.InRange(pushed, 0, int.MaxValue);
        Assert.True(blow > pushed, "the push comes first: A has priority");
        // The blow aimed at (5,4) fell on (6,4), re-anchored with B, on the one standing there; A was never in it.
        Assert.True(mate.Hp < hp.Item2);
        Assert.Equal(hp.Item1, a.Hp);
        Assert.Equal(sim.Data.Rules.Adrenaline.GainPush, a.Adrenaline, 6);
    }

    [Fact]
    public void Own_units_have_a_rank_by_priority_and_rivals_none()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var c = SurvivorUnit(1, new(2, 4));
        var foe = SurvivorUnit(2, new(4, 2));
        var sim = Table([a, c, foe]);
        c.PriorityBonus = 5;
        var ranks = TacticsView.Ranks(sim, a.Side);
        Assert.Equal(1, ranks[c.Id]);
        Assert.Equal(2, ranks[a.Id]);
        Assert.False(ranks.ContainsKey(foe.Id));
    }

    [Fact]
    public void The_plan_preview_shows_each_step_and_warns_of_allies_on_action_tiles()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var friend = SurvivorUnit(1, new(5, 3));
        var sim = Table([a, friend, SurvivorUnit(2, new(11, 6))], "sweep");
        Assert.True(sim.Execute(new Move(1, a.Id, new(4, 3))).Ok);
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "sweep", Direction: East)).Ok);
        var preview = TacticsView.PlanPreview(sim, a);
        Assert.Equal(new Pos(4, 3), preview.Path[^1]);
        Assert.Equal(new Pos(4, 3), preview.End);
        Assert.Contains(new Pos(5, 2), preview.ActionTiles);
        Assert.Contains(new Pos(5, 4), preview.ActionTiles);
        Assert.Equal([friend.Pos], preview.AllyTiles);
    }

    [Fact]
    public void The_plan_preview_covers_a_strike_a_push_and_a_dash()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var sim = Table([a, SurvivorUnit(2, new(11, 6))]);
        Assert.True(sim.Execute(new Dash(1, a.Id, East, 2)).Ok);
        Assert.Equal(new Pos(4, 3), TacticsView.PlanPreview(sim, a).End);
        Assert.Equal([new Pos(3, 3), new Pos(4, 3)], TacticsView.PlanPreview(sim, a).Path);
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.Contains(new Pos(5, 3), TacticsView.PlanPreview(sim, a).ActionTiles);

        var b = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var sim2 = Table([b, SurvivorUnit(2, new(11, 6))]);
        Assert.True(sim2.Execute(new AttackTile(1, b.Id, new(3, 3), "swing")).Ok);
        Assert.Contains(new Pos(3, 3), TacticsView.PlanPreview(sim2, b).ActionTiles);
    }

    [Fact]
    public void A_self_aimed_pattern_card_lists_the_eight_ways_it_can_turn_from_where_the_plan_ends()
    {
        var a = SurvivorUnit(1, new(2, 3), weapon: "pipe");
        var sim = Table([a, SurvivorUnit(2, new(11, 6))], "sweep");
        Assert.True(sim.Execute(new Move(1, a.Id, new(4, 3))).Ok);
        var options = TacticsView.Targets(sim, 1, a.Id, "sweep");
        Assert.Equal(8, options.Count);
        Assert.All(options, o => Assert.Equal(1, o.Tile.Chebyshev(new Pos(4, 3))));
        Assert.All(options, o => Assert.True(o.Ok, o.Reason));
    }

    [Fact]
    public void A_tile_card_lists_tiles_in_range_of_where_the_plan_ends()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var sim = Table([a, SurvivorUnit(2, new(11, 6))], "parry");
        Assert.True(sim.Execute(new Move(1, a.Id, new(5, 3))).Ok);
        var tiles = TacticsView.Targets(sim, 1, a.Id, "parry").Select(o => o.Tile).ToList();
        Assert.Contains(new Pos(6, 3), tiles);
        Assert.DoesNotContain(new Pos(2, 3), tiles);
    }

    [Fact]
    public void The_dash_aim_runs_straight_by_terrain_alone()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var hidden = SurvivorUnit(2, new(3, 3));
        var sim = Table([a, hidden]);
        Assert.Equal([new Pos(3, 3), new Pos(4, 3), new Pos(5, 3)], TacticsView.DashAim(sim, a, East));
        sim.State.Map[new Pos(4, 3)] = Terrain.Wall;
        Assert.Equal([new Pos(3, 3)], TacticsView.DashAim(sim, a, East));
    }

    [Fact]
    public void A_player_plans_their_own_first_uncommitted_unit_whatever_other_players_do()
    {
        var rival = SurvivorUnit(2, new(9, 3));
        var a = SurvivorUnit(1, new(2, 3));
        var c = SurvivorUnit(1, new(2, 5));
        var sim = Table([rival, a, c]);
        Assert.Equal(a.Id, TacticsView.PlanningUnit(sim.State, 1)!.Id);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(c.Id, TacticsView.PlanningUnit(sim.State, 1)!.Id);
        Assert.True(sim.Execute(new EndTurn(1, c.Id)).Ok);
        Assert.Null(TacticsView.PlanningUnit(sim.State, 1));
        Assert.Equal(rival.Id, TacticsView.PlanningUnit(sim.State, 2)!.Id);
    }

    [Fact]
    public void A_player_can_go_back_to_any_of_their_units_and_change_its_plan_until_the_round_resolves()
    {
        var rival = SurvivorUnit(2, new(9, 3));
        var a = SurvivorUnit(1, new(2, 3));
        var sim = Table([a, rival]);
        Assert.True(sim.Execute(new EndTurn(1, a.Id)).Ok);
        Assert.Equal(a.Id, TacticsView.PlanningUnit(sim.State, 1, a.Id)!.Id);
        Assert.Null(TacticsView.PlanningUnit(sim.State, 1, rival.Id));
        Assert.True(sim.Execute(new Move(1, a.Id, new(3, 3))).Ok);
        Assert.Equal(a.Id, TacticsView.PlanningUnit(sim.State, 1)!.Id);
    }

    [Fact]
    public void An_aim_points_at_its_tile_from_where_the_plan_ends_and_turns_an_eighth_at_a_time()
    {
        var a = SurvivorUnit(1, new(2, 3));
        var sim = Table([a, SurvivorUnit(2, new(11, 6))], "shield_wall");
        Assert.True(sim.Execute(new Move(1, a.Id, new(4, 3))).Ok);
        Assert.Equal(East, TacticsView.AimDirection(sim, a, new(5, 3)));
        Assert.Equal(new Pos(1, 1), TacticsView.AimDirection(sim, a, new(5, 3), 1));
        Assert.Equal(new Pos(0, 1), TacticsView.AimDirection(sim, a, new(5, 3), 2));
        Assert.Equal(East, TacticsView.AimDirection(sim, a, new(5, 3), 8));
        var straight = TacticsView.PatternAim(sim, a, "shield_wall", new(5, 3), East).Tiles;
        var turned = TacticsView.PatternAim(sim, a, "shield_wall", new(5, 3), TacticsView.AimDirection(sim, a, new(5, 3), 2)).Tiles;
        Assert.NotEqual(straight, turned);
    }

    [Fact]
    public void A_shove_into_a_friends_queued_blow_says_who_will_hit_the_target()
    {
        var friend = SurvivorUnit(1, new(5, 2), weapon: "knife");
        var b = SurvivorUnit(2, new(3, 2));
        var a = SurvivorUnit(1, new(2, 2));
        var sim = Table([friend, b, a], "shove");
        Assert.True(sim.Execute(new AttackTile(1, friend.Id, new(4, 2), "cut", StrikeMode.Stab)).Ok);
        var onB = Assert.Single(TacticsView.Targets(sim, 1, a.Id, "shove"), o => o.UnitId == b.Id);
        Assert.Contains($"in {friend.Name}'s blow", onB.Outcome);
    }

    [Fact]
    public void An_enemy_low_on_stamina_plans_only_what_it_can_pay_for()
    {
        var data = With(r => r with { Turn = r.Turn with { BaseStamina = 4, StaminaRegen = 0, MinStaminaRegen = 0 } });
        var survivor = SurvivorUnit(1, new(2, 2));
        var husk = EnemyUnit("husk", new(6, 2));
        husk.Ai.Mode = AiMode.Hunting;
        var sim = Arena(12, 6, [survivor, husk], data: data);
        sim.Start();
        Assert.Equal(4, sim.Profile(husk).MaxStamina);
        var steps = Plans.Of(sim.State, husk)?.Steps ?? [];
        Assert.NotEmpty(steps);
        var cost = steps.Sum(s => s.Kind == PlanStepKind.Move ? s.Path.Count : s.Strike is not null ? 2 : 0);
        Assert.InRange(cost, 1, 4);
    }

    [Theory]
    [InlineData("steady_aim")]
    [InlineData("shove")]
    public void Zero_stamina_means_no_cards_even_free_ones(string card)
    {
        var a = SurvivorUnit(1, new(2, 3));
        var foe = SurvivorUnit(2, new(3, 3));
        var sim = Table([a, foe], card);
        a.Fatigue = sim.Profile(a).MaxStamina;
        var target = sim.Data.Cards.Find(card)!.Target == CardTarget.Enemy ? foe.Id : (int?)null;
        Assert.False(sim.Execute(new PlayCard(1, a.Id, card, target)).Ok);
    }

    [Theory]
    [InlineData("shove")]
    [InlineData("yank")]
    public void A_card_that_pushes_costs_one_ap(string card) => Assert.Equal(1, Sure.Cards.Find(card)!.ApCost);

    [Fact]
    public void A_card_dash_given_a_long_direction_still_runs_its_own_length()
    {
        var a = SurvivorUnit(1, new(1, 3), weapon: "pipe");
        var sim = Table([a, SurvivorUnit(2, new(11, 6))], "rushing_strike");
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "rushing_strike", Direction: new Pos(4, 0))).Ok);
        Assert.Equal(East, Plans.Of(sim.State, a)!.Steps[^1].Direction);
        Assert.Equal(new Pos(1 + sim.Data.Cards.Find("rushing_strike")!.Effect.Dash, 3), TacticsView.PlanPreview(sim, a).End);
    }

    /// <summary>Plans both, then evens out their priority so the two actions tie.</summary>
    private static void Tie(ScavengeSim sim, Unit a, Unit b)
    {
        var (first, second) = (Plans.Of(sim.State, a)!.Steps[0], Plans.Of(sim.State, b)!.Steps[0]);
        b.PriorityBonus += Priority.Action(sim, a, first) - Priority.Action(sim, b, second);
        Assert.True(sim.Execute(new EndTurn(a.PlayerId!.Value, a.Id)).Ok);
        Assert.True(sim.Execute(new EndTurn(b.PlayerId!.Value, b.Id)).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Clashed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_tied_blow_lands_before_a_tied_dash_whichever_unit_is_older(bool strikerOlder)
    {
        var older = SurvivorUnit(strikerOlder ? 1 : 2, new(strikerOlder ? 2 : 3, 3), weapon: strikerOlder ? "pipe" : null);
        var younger = SurvivorUnit(strikerOlder ? 2 : 1, new(strikerOlder ? 3 : 2, 3), weapon: strikerOlder ? null : "pipe");
        var (striker, dasher) = strikerOlder ? (older, younger) : (younger, older);
        var sim = Table([older, younger]);
        Assert.True(sim.Execute(new AttackTile(1, striker.Id, dasher.Pos, "swing")).Ok);
        Assert.True(sim.Execute(new Dash(2, dasher.Id, East, 3)).Ok);
        Tie(sim, striker, dasher);
        Assert.True(dasher.Hp < dasher.MaxHp);
        Assert.True(dasher.Pos.X > 3); // shorter than untied: a tie cuts its reach too
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_unit_pushed_in_a_tie_dashes_from_where_the_push_left_it(bool pusherOlder)
    {
        var older = SurvivorUnit(pusherOlder ? 1 : 2, new(pusherOlder ? 2 : 3, 3));
        var younger = SurvivorUnit(pusherOlder ? 2 : 1, new(pusherOlder ? 3 : 2, 3));
        var (pusher, dasher) = pusherOlder ? (older, younger) : (younger, older);
        var sim = Table([older, younger]);
        Assert.True(sim.Execute(new Push(1, pusher.Id, East)).Ok);
        Assert.True(sim.Execute(new Dash(2, dasher.Id, East, 3)).Ok);
        Tie(sim, pusher, dasher);
        Assert.True(dasher.Pos.X > 4); // pushed to (4,3), then on from there
    }

    /// <summary>Two tied hostiles walk for one tile in the given round; who got there.</summary>
    private static int RaceWinner(int round)
    {
        var mine = SurvivorUnit(1, new(2, 3));
        var theirs = SurvivorUnit(2, new(6, 3));
        var sim = Table([mine, theirs]);
        sim.State.Round = round;
        Assert.True(sim.Execute(new Move(1, mine.Id, new(4, 3))).Ok);
        Assert.True(sim.Execute(new Move(2, theirs.Id, new(4, 3))).Ok);
        Assert.Equal(Priority.Movement(sim, mine), Priority.Movement(sim, theirs));
        Settle(sim);
        return mine.Pos == new Pos(4, 3) ? 1 : theirs.Pos == new Pos(4, 3) ? 2 : 0;
    }

    [Fact]
    public void A_tied_race_for_a_tile_is_replayable_and_not_always_won_by_the_same_side()
    {
        var rounds = Enumerable.Range(1, 40).ToList();
        var winners = rounds.Select(RaceWinner).ToList();
        Assert.Equal(winners, rounds.Select(RaceWinner).ToList());
        Assert.DoesNotContain(0, winners);
        Assert.InRange(winners.Count(w => w == 1), 10, 30);
    }

    [Fact]
    public void The_tie_break_is_a_pure_function_of_round_and_unit()
    {
        Assert.Equal(Priority.TieBreak(7, 3), Priority.TieBreak(7, 3));
        Assert.NotEqual(Priority.TieBreak(7, 3), Priority.TieBreak(8, 3));
        Assert.NotEqual(Priority.TieBreak(7, 3), Priority.TieBreak(7, 4));
    }

    [Fact]
    public void Own_ranks_show_the_order_a_tie_will_really_go_in()
    {
        var a = SurvivorUnit(1, new(2, 2));
        var c = SurvivorUnit(1, new(2, 4));
        var sim = Table([a, c, SurvivorUnit(2, new(9, 2))]);
        foreach (var round in Enumerable.Range(1, 12))
        {
            sim.State.Round = round;
            var first = Priority.TieBreak(round, a.Id) < Priority.TieBreak(round, c.Id) ? a : c;
            Assert.Equal(1, TacticsView.Ranks(sim, a.Side)[first.Id]);
        }
    }

    /// <summary>Three hostiles on one score, whatever their plans are: A shoves B, B yanks C, C swings.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void A_three_way_clash_never_leaves_two_units_on_one_tile(int round)
    {
        var a = SurvivorUnit(1, new(2, 3));
        var b = SurvivorUnit(2, new(3, 3));
        var c = SurvivorUnit(3, new(5, 3), weapon: "pipe");
        var sim = Table([a, b, c], "yank");
        sim.State.Round = round;
        Assert.True(sim.Execute(new Push(1, a.Id, East)).Ok);
        Assert.True(sim.Execute(new PlayCard(2, b.Id, "yank", c.Id)).Ok);
        Assert.True(sim.Execute(new AttackTile(3, c.Id, new(4, 3), "swing")).Ok);
        var score = Priority.Action(sim, a, Plans.Of(sim.State, a)!.Steps[0]);
        b.PriorityBonus += score - Priority.Action(sim, b, Plans.Of(sim.State, b)!.Steps[0]);
        c.PriorityBonus += score - Priority.Action(sim, c, Plans.Of(sim.State, c)!.Steps[0]);
        foreach (var u in new[] { a, b, c }) Assert.True(sim.Execute(new EndTurn(u.PlayerId!.Value, u.Id)).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.Clashed);
        var tiles = new[] { a, b, c }.Where(u => u.OnMap).Select(u => u.Pos).ToList();
        Assert.Equal(tiles.Count, tiles.Distinct().Count());
    }
}
