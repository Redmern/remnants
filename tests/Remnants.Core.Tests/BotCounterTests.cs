using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>AC-30: what the bots do with Defend, push and dash, going only by what their side can see.</summary>
public class BotCounterTests
{
    private static string[] Rows(params (int X, int Y)[] walls)
    {
        var rows = Enumerable.Repeat(new string('.', 14), 7).Select(r => r.ToCharArray()).ToArray();
        foreach (var (x, y) in walls) rows[y][x] = '#';
        return rows.Select(r => new string(r)).ToArray();
    }

    [Fact]
    public void It_pushes_a_neighbour_into_a_wall()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe], rows: Rows((6, 3)));
        var push = Assert.IsType<Push>(BotTactics.Counter(sim, a, 1));
        Assert.Equal(new Pos(1, 0), push.Direction);
    }

    [Fact]
    public void It_does_not_push_a_neighbour_it_can_see_walking_off()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var rival = SurvivorUnit(2, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, rival], rows: Rows((6, 3)));
        Assert.IsType<Push>(BotTactics.Counter(sim, a, 1));
        Assert.True(sim.Execute(new Move(2, rival.Id, new(5, 5))).Ok);
        Assert.Null(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void It_does_not_push_a_neighbour_that_walked_last_round()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe], rows: Rows((6, 3)));
        foe.LastMove = new Pos(1, 0);
        Assert.Null(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void A_healthy_unit_with_one_foe_on_it_has_no_counter_and_fights()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe]);
        foe.LastMove = new Pos(1, 0);
        Assert.Null(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void Now_and_then_it_guards_against_a_neighbour_that_stood_still()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, EnemyUnit("husk", new(5, 3))]);
        var kinds = new List<Type?>();
        var every = sim.Data.Rules.Bot.GuardEvery;
        for (var round = 1; round <= every; round++)
        {
            sim.State.Round = round;
            kinds.Add(BotTactics.Counter(sim, a, 1)?.GetType());
        }
        Assert.Equal(1, kinds.Count(k => k == typeof(Defend)));
        Assert.Equal(every - 1, kinds.Count(k => k is null));
    }

    [Fact]
    public void A_hurt_unit_dashes_clear_of_every_reach_when_it_can()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe]);
        a.Hp = a.MaxHp * 0.3;
        var dash = Assert.IsType<Dash>(BotTactics.Counter(sim, a, 1));
        var landing = Pushes.PlannedLanding(sim, a, dash.Direction, dash.Tiles);
        Assert.DoesNotContain(landing, Combat.ThreatZone(sim, foe));
    }

    [Fact]
    public void A_dash_does_not_land_where_a_friend_is_walking_to()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var friend = SurvivorUnit(1, new(1, 1), weapon: "pipe");
        var sim = Arena(14, 7, [a, friend, EnemyUnit("husk", new(5, 3))]);
        a.Hp = a.MaxHp * 0.3;
        var first = Assert.IsType<Dash>(BotTactics.Counter(sim, a, 1));
        var landing = Pushes.PlannedLanding(sim, a, first.Direction, first.Tiles);
        Plans.For(sim.State, friend).Steps.Add(new PlanStep { Kind = PlanStepKind.Move, To = landing, Path = [landing] });
        var again = Assert.IsType<Dash>(BotTactics.Counter(sim, a, 1));
        Assert.NotEqual(landing, Pushes.PlannedLanding(sim, a, again.Direction, again.Tiles));
    }

    [Fact]
    public void A_unit_that_dashed_last_round_does_not_dash_again()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe]);
        a.Hp = a.MaxHp * 0.3;
        Assert.True(sim.Execute(Assert.IsType<Dash>(BotTactics.Counter(sim, a, 1))).Ok);
        var round = sim.State.Round;
        Settle(sim);
        Assert.Equal(round, a.Ai.DashedRound);
        foe.Pos = a.Pos + new Pos(1, 0);
        Assert.IsNotType<Dash>(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void A_hurt_unit_that_walked_last_round_fights_instead_of_dashing_again()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe]);
        a.Hp = a.MaxHp * 0.3;
        a.LastMove = new Pos(1, 0);
        Assert.Null(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void What_counts_as_hurt_comes_from_the_data()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        foe.LastMove = new Pos(1, 0);
        var sim = Arena(14, 7, [a, foe], data: With(r => r with { Bot = r.Bot with { HurtBelow = 0.2 } }));
        a.Hp = a.MaxHp * 0.3;
        Assert.Null(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void A_foes_hidden_steps_do_not_change_what_a_bot_or_the_overlay_makes_of_it()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var rival = SurvivorUnit(2, new(6, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, rival]);
        a.Hp = a.MaxHp * 0.3;
        var before = (BotTactics.Threats(sim, a).Select(u => u.Id).ToList(), BotTactics.Counter(sim, a, 1), BotTactics.Position(sim, a, 1),
            TacticsView.DefendOdds(sim, a).Select(o => o.UnitId).ToList(), TacticsView.Threats(sim, a.Side).Keys.OrderBy(p => p.Y).ThenBy(p => p.X).ToList());

        // Everything a rival can plan out of sight: none of it moves it in the movement phase, so none of it is telegraphed.
        Plans.For(sim.State, rival).Steps.Add(new PlanStep { Kind = PlanStepKind.Dash, From = rival.Pos, Direction = new(-1, 0), Reach = 1, Landing = new(5, 3) });
        Plans.For(sim.State, rival).Steps.Add(new PlanStep { Kind = PlanStepKind.Defend, From = rival.Pos, Direction = new(-1, 0) });

        var after = (BotTactics.Threats(sim, a).Select(u => u.Id).ToList(), BotTactics.Counter(sim, a, 1), BotTactics.Position(sim, a, 1),
            TacticsView.DefendOdds(sim, a).Select(o => o.UnitId).ToList(), TacticsView.Threats(sim, a.Side).Keys.OrderBy(p => p.Y).ThenBy(p => p.X).ToList());
        Assert.Equal(before.Item1, after.Item1);
        Assert.Equal(before.Item2, after.Item2);
        Assert.Equal(before.Item3, after.Item3);
        Assert.Equal(before.Item4, after.Item4);
        Assert.Equal(before.Item5, after.Item5);
    }

    [Fact]
    public void A_hurt_unit_with_no_way_out_defends_toward_the_foe()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe], rows: Rows((3, 2), (4, 2), (5, 2), (3, 3), (3, 4), (4, 4), (5, 4)));
        a.Hp = a.MaxHp * 0.3;
        var defend = Assert.IsType<Defend>(BotTactics.Counter(sim, a, 1));
        Assert.Equal(new Pos(1, 0), defend.Direction);
    }

    [Fact]
    public void It_recovers_when_winded_and_nothing_can_reach_it()
    {
        var a = SurvivorUnit(1, new(1, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, EnemyUnit("husk", new(7, 3))]);
        a.Fatigue = sim.Profile(a).MaxStamina - 1;
        Assert.IsType<Recover>(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void A_foes_hidden_plan_does_not_change_the_counter()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var rival = SurvivorUnit(2, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, rival]);
        a.Hp = a.MaxHp * 0.3;
        var before = BotTactics.Counter(sim, a, 1);
        Assert.True(sim.Execute(new Defend(2, rival.Id, new(-1, 0))).Ok);
        Assert.Equal(before, BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void A_unit_with_its_action_planned_has_no_counter()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a, EnemyUnit("husk", new(5, 3))], rows: Rows((6, 3)));
        Assert.True(sim.Execute(new Defend(1, a.Id, new(1, 0))).Ok);
        Assert.Null(BotTactics.Counter(sim, a, 1));
    }

    [Fact]
    public void It_steps_behind_a_foe_for_the_blow_when_it_can()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(6, 3));
        foe.Facing = new Pos(-1, 0);
        var sim = Arena(14, 7, [a, foe]);
        var move = Assert.IsType<Move>(BotTactics.Position(sim, a, 1));
        Assert.NotEqual(FacingArc.Front, Combat.ArcOf(foe, move.To));
    }

    [Fact]
    public void It_does_not_keep_circling_a_foe_it_walked_up_to()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(6, 3));
        foe.Facing = new Pos(-1, 0);
        var sim = Arena(14, 7, [a, foe]);
        a.LastMove = new Pos(1, 0);
        Assert.Null(BotTactics.Position(sim, a, 1));
    }

    [Fact]
    public void It_stays_put_when_the_tile_it_is_on_is_already_the_best()
    {
        var a = SurvivorUnit(1, new(7, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(6, 3));
        foe.Facing = new Pos(-1, 0);
        var sim = Arena(14, 7, [a, foe]);
        Assert.Null(BotTactics.Position(sim, a, 1));
    }
}

public class BotUnblockTests
{
    private static string[] Doorway() =>
    [
        "..............",
        "..............",
        "....###.......",
        "....#.........",
        "....###.......",
        "..............",
        "..............",
    ];

    private static (ScavengeSim Sim, Unit A, Unit Stranger) Boxed()
    {
        var a = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var stranger = SurvivorUnit(2, new(6, 3), weapon: "pipe");
        stranger.Side = Unit.NeutralSide;
        return (Arena(14, 7, [a, stranger], rows: Doorway()), a, stranger);
    }

    [Fact]
    public void A_unit_boxed_in_by_someone_who_is_no_enemy_pushes_them_out_of_the_way()
    {
        var (sim, a, _) = Boxed();
        var push = Assert.IsType<Push>(BotTactics.Unblock(sim, a, 1, [new(10, 3)]));
        Assert.Equal(new Pos(1, 0), push.Direction);
    }

    [Fact]
    public void A_unit_boxed_in_by_its_own_follower_pushes_it_out_of_the_way()
    {
        var (sim, a, follower) = Boxed();
        follower.Side = a.Side;
        Assert.IsType<Push>(BotTactics.Unblock(sim, a, 1, [new(10, 3)]));
    }

    [Fact]
    public void The_push_clears_the_doorway()
    {
        var (sim, a, stranger) = Boxed();
        Assert.True(sim.Execute(BotTactics.Unblock(sim, a, 1, [new(10, 3)])!).Ok);
        Settle(sim);
        Assert.Equal(new Pos(7, 3), stranger.Pos);
    }

    [Fact]
    public void Nobody_in_the_way_means_no_push()
    {
        var a = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var sim = Arena(14, 7, [a], rows: Doorway());
        Assert.Null(BotTactics.Unblock(sim, a, 1, [new(10, 3)]));
    }

    [Fact]
    public void A_way_round_the_stranger_means_no_push()
    {
        var a = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var stranger = SurvivorUnit(2, new(6, 3), weapon: "pipe");
        stranger.Side = Unit.NeutralSide;
        var sim = Arena(14, 7, [a, stranger]);
        Assert.Null(BotTactics.Unblock(sim, a, 1, [new(10, 3)]));
    }
}

public class FollowerTests
{
    private static (ScavengeSim Sim, Unit Leader, Unit Follower) Pair(Pos followerAt)
    {
        var leader = SurvivorUnit(1, new(5, 3), weapon: "pipe");
        var follower = SurvivorUnit(1, followerAt, weapon: "pipe");
        follower.Kind = UnitKind.Npc;
        var sim = Arena(14, 7, [leader, follower]);
        follower.FollowingId = leader.Id;
        return (sim, leader, follower);
    }

    [Fact]
    public void A_follower_within_the_rule_stays_put_so_it_does_not_walk_back_into_a_doorway()
    {
        var (sim, leader, follower) = Pair(new(5, 1));
        Assert.True(sim.Execute(new EndTurn(1, leader.Id)).Ok);
        Settle(sim);
        Assert.Equal(new Pos(5, 1), follower.Pos);
    }

    [Fact]
    public void A_follower_further_off_closes_in()
    {
        var (sim, leader, follower) = Pair(new(10, 3));
        Assert.True(sim.Execute(new EndTurn(1, leader.Id)).Ok);
        Settle(sim);
        Assert.True(follower.Pos.X < 10);
    }
}

public class BotAimingTests
{
    private static (ScavengeSim Sim, Unit A, Unit B, Unit Foe) Field(params string[] hand)
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var b = SurvivorUnit(1, new(4, 4), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, b, foe]);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = hand.ToList(), Points = 3, Budget = 3, HandSize = 5 });
        return (sim, a, b, foe);
    }

    [Fact]
    public void A_foe_an_ally_is_about_to_push_counts_as_being_moved()
    {
        var (sim, a, b, foe) = Field();
        Assert.DoesNotContain(foe.Id, BotTactics.BeingMoved(sim, a.Side));
        Assert.True(sim.Execute(new Push(1, b.Id, new(1, -1))).Ok);
        Assert.Contains(foe.Id, BotTactics.BeingMoved(sim, a.Side));
    }

    [Fact]
    public void A_foe_an_ally_is_about_to_shove_counts_as_being_moved()
    {
        var (sim, a, b, foe) = Field("shove");
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "shove", foe.Id)).Ok);
        Assert.Contains(foe.Id, BotTactics.BeingMoved(sim, b.Side));
    }

    [Fact]
    public void It_does_not_shove_a_foe_it_can_see_walking_off()
    {
        var (sim, a, _, foe) = Field("shove");
        Assert.Equal("shove", Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1)).CardId);
        Plans.For(sim.State, foe).Steps.Add(new PlanStep { Kind = PlanStepKind.Move, To = new(5, 4), Path = [new(5, 4)] });
        Assert.NotEqual("shove", (BotTactics.Card(sim, a, 1) as PlayCard)?.CardId);
    }

    [Fact]
    public void It_does_not_shove_a_foe_an_ally_is_already_shoving()
    {
        var (sim, a, b, foe) = Field("shove", "shove");
        Assert.True(sim.Execute(new PlayCard(1, a.Id, "shove", foe.Id)).Ok);
        Assert.NotEqual("shove", (BotTactics.Card(sim, b, 1) as PlayCard)?.CardId);
    }
}
