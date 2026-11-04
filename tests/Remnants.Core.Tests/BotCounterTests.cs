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
        for (var round = 1; round <= BotTactics.GuardEvery; round++)
        {
            sim.State.Round = round;
            kinds.Add(BotTactics.Counter(sim, a, 1)?.GetType());
        }
        Assert.Equal(1, kinds.Count(k => k == typeof(Defend)));
        Assert.Equal(BotTactics.GuardEvery - 1, kinds.Count(k => k is null));
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
    public void A_unit_that_dashed_last_round_does_not_dash_again()
    {
        var a = SurvivorUnit(1, new(4, 3), weapon: "pipe");
        var foe = EnemyUnit("husk", new(5, 3));
        var sim = Arena(14, 7, [a, foe]);
        a.Hp = a.MaxHp * 0.3;
        Assert.IsType<Dash>(BotTactics.Counter(sim, a, 1));
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Moved, $"{a.Name} dashes to (4,5).", a.Id));
        sim.State.Round++;
        Assert.IsNotType<Dash>(BotTactics.Counter(sim, a, 1));
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
