using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The simple heuristics the playtest and server bots share: card play, dodging a telegraph, closing in to strike.</summary>
public class BotTacticsTests
{
    private static (ScavengeSim Sim, Unit A, Unit Husk, SquadDeck Deck) Field(params string[] hand)
    {
        var a = SurvivorUnit(1, new(1, 1), weapon: "pipe");
        var husk = EnemyUnit("husk", new(6, 1));
        var sim = Arena(12, 3, [a, husk]);
        var deck = new SquadDeck { PlayerId = 1, Hand = hand.ToList(), Points = 3, Budget = 3, HandSize = 5 };
        sim.State.Decks.Add(deck);
        return (sim, a, husk, deck);
    }

    [Fact]
    public void Cards_follow_a_simple_priority()
    {
        // In reach of the husk: an aim card first.
        var (sim, a, husk, _) = Field("take_cover", "steady_aim", "scout_ahead");
        husk.Pos = new(2, 1);
        Assert.Equal("steady_aim", Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1)).CardId);
        // Seen by a hostile but nothing in reach: a defense card.
        var (sim2, a2, _, _) = Field("scout_ahead", "take_cover", "steady_aim");
        Assert.Equal("take_cover", Assert.IsType<PlayCard>(BotTactics.Card(sim2, a2, 1)).CardId);
        // Hurt with nothing about: mend.
        var (sim3, a3, husk3, _) = Field("field_dressing", "take_cover");
        husk3.Status = UnitStatus.Dead;
        a3.Body.Injure(BodyPart.Torso, 0.5);
        Assert.Equal("field_dressing", Assert.IsType<PlayCard>(BotTactics.Card(sim3, a3, 1)).CardId);
        // Nothing worth it: no card.
        var (sim4, a4, husk4, _) = Field("roll_with_it");
        husk4.Status = UnitStatus.Dead;
        Assert.Null(BotTactics.Card(sim4, a4, 1));
    }

    [Fact]
    public void A_dead_hard_card_is_discarded()
    {
        var (sim, a, husk, _) = Field("hold_the_line");
        husk.Status = UnitStatus.Dead;
        Assert.IsType<DiscardDeadCard>(BotTactics.Card(sim, a, 1));
    }

    [Fact]
    public void Every_card_the_bot_picks_is_one_the_rules_accept()
    {
        foreach (var card in Real.Cards.All.Keys)
        {
            var (sim, a, husk, _) = Field(card);
            husk.Pos = new(2, 1);
            Ready(sim, husk, a.Pos, "claw");
            a.Body.Injure(BodyPart.LeftArm, 0.3);
            if (BotTactics.Card(sim, a, 1) is { } pick) Assert.True(sim.Execute(pick).Ok, $"{card}: {sim.Execute(pick).Message}");
        }
    }

    [Fact]
    public void Out_of_reach_the_bot_moves_to_where_its_attack_reaches_the_target()
    {
        var (sim, a, husk, _) = Field();
        husk.Pos = new(4, 1);
        var move = Assert.IsType<Move>(BotTactics.Approach(sim, a, 1, [husk]));
        Assert.True(sim.Execute(move).Ok);
        Assert.True(sim.Execute(StrikeOn(sim, a, husk, "swing")).Ok); // with the AP for the blow left
        Assert.Null(BotTactics.Approach(Field().Sim, a, 1, [])); // nobody to go for
    }

    [Fact]
    public void Stepping_toward_a_goal_walks_round_a_wall_instead_of_hugging_it()
    {
        // The goal is due east behind a wall open only at the bottom: straight-line steps would press against the wall.
        var u = SurvivorUnit(1, new(1, 2));
        var sim = Arena(9, 6, [u], rows: ["....#....", "....#....", "....#....", "....#....", "....#....", "........."]);
        var goal = new Pos(7, 2);
        var moves = 0;
        for (var turn = 0; turn < 8 && u.Pos != goal; turn++)
        {
            (u.Ap, u.Mp) = (sim.Profile(u).MaxAp, sim.Profile(u).MaxMp);
            while (BotTactics.StepToward(sim, u, 1, [goal]) is { } step)
            {
                Assert.True(Now(sim, step).Ok);
                moves++;
                if (u.Pos == goal) break;
            }
        }
        Assert.Equal(goal, u.Pos);
        Assert.True(moves > 0);
        Assert.Null(BotTactics.StepToward(sim, u, 1, [goal])); // there already
        Assert.Null(BotTactics.StepToward(sim, u, 1, []));
    }

    [Fact]
    public void An_enemy_with_the_unit_in_reach_is_spoiled_before_any_buff()
    {
        var (sim, a, husk, _) = Field("sidestep", "spoil");
        husk.Pos = new(2, 1);
        var card = Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1));
        Assert.Equal(("spoil", husk.Id), (card.CardId, card.TargetId));
    }

    [Fact]
    public void An_adjacent_enemy_is_shoved_away()
    {
        var (sim, a, husk, _) = Field("shove", "steady_aim");
        husk.Pos = new(2, 1);
        var card = Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1));
        Assert.Equal(("shove", husk.Id), (card.CardId, card.TargetId));
        Assert.True(Now(sim, card).Ok);
        Assert.Equal(new Pos(3, 1), husk.Pos);
    }

    [Fact]
    public void A_melee_fighter_flank_steps_round_to_a_seen_enemy_s_back()
    {
        var (sim, a, husk, _) = Field("flank_step");
        husk.Pos = new(3, 1);
        husk.Facing = new Pos(-1, 0); // facing a
        var card = Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1));
        Assert.Equal("flank_step", card.CardId);
        Assert.True(Now(sim, card).Ok);
        Assert.NotEqual(FacingArc.Front, Combat.ArcOf(husk, a.Pos));
        Assert.Equal(1, a.Pos.Chebyshev(husk.Pos));
    }

    private static ScavengeSim WithHand(IEnumerable<Unit> units, params string[] hand)
    {
        var sim = Arena(12, 7, units);
        sim.State.Decks.Add(new SquadDeck { PlayerId = 1, Hand = hand.ToList(), Points = 9, Budget = 9, HandSize = 9 });
        return sim;
    }

    [Fact]
    public void A_bot_guards_where_it_will_stand_once_someone_has_it_in_reach()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var foe = SurvivorUnit(2, new(4, 3), weapon: "pipe");
        var sim = WithHand([a, foe], "parry", "parry");
        var card = Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1));
        Assert.Equal("parry", card.CardId);
        Assert.Equal(a.Pos, card.Tile);
        Assert.True(sim.Execute(card).Ok);
        Assert.False(BotTactics.Card(sim, a, 1) is PlayCard { CardId: "parry" });
    }

    [Fact]
    public void A_bot_swings_a_pattern_at_the_most_hostiles_and_never_through_a_friend()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var one = SurvivorUnit(2, new(4, 3));
        var two = SurvivorUnit(2, new(4, 4));
        var lone = SurvivorUnit(2, new(2, 3));
        var sim = WithHand([a, one, two, lone], "sweep");
        var card = Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1));
        var tiles = TacticsView.PatternAim(sim, a, "sweep", null, card.Direction!.Value).Tiles;
        Assert.Contains(one.Pos, tiles);
        Assert.Contains(two.Pos, tiles);

        var b = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var friend = SurvivorUnit(1, new(4, 3));
        var foe = SurvivorUnit(2, new(4, 4));
        var sim2 = WithHand([b, friend, foe], "lunge");
        var aimed = BotTactics.Card(sim2, b, 1) as PlayCard;
        Assert.True(aimed is null || !TacticsView.PatternAim(sim2, b, "lunge", null, aimed.Direction!.Value).Tiles.Contains(friend.Pos));
    }

    [Fact]
    public void A_bot_does_not_read_a_rivals_queued_actions()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var foe = SurvivorUnit(2, new(4, 3), weapon: "pipe");
        var sim = WithHand([a, foe], "parry");
        var first = BotTactics.Card(sim, a, 1);
        Assert.True(sim.Execute(new Recover(2, foe.Id)).Ok);
        Assert.Equal(first, BotTactics.Card(sim, a, 1));
    }

    [Fact]
    public void A_bot_aims_where_a_seen_hostile_is_walking_to()
    {
        var a = SurvivorUnit(1, new(3, 3), weapon: "pipe");
        var foe = SurvivorUnit(2, new(6, 3));
        var sim = WithHand([a, foe]);
        Assert.Equal(foe.Pos, BotTactics.Expected(sim, a.Side, foe));
        Assert.True(sim.Execute(new Move(2, foe.Id, new(4, 3))).Ok);
        Assert.Equal(new Pos(4, 3), BotTactics.Expected(sim, a.Side, foe));
        var strike = TileStrikes.StrikeAt(sim, a, sim.Profile(a).Action("swing")!, foe, new Pos(4, 3));
        Assert.Equal(new Pos(4, 3), strike!.Tile);
        Assert.True(sim.Execute(strike).Ok);
        Settle(sim);
        Assert.Contains(sim.State.Events, e => e.UnitId == a.Id && e.TargetId == foe.Id && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
    }

    [Fact]
    public void A_threat_zone_is_from_where_a_seen_hostile_is_walking_to()
    {
        var a = SurvivorUnit(1, new(3, 3));
        var foe = SurvivorUnit(2, new(8, 3), weapon: "pipe");
        var sim = WithHand([a, foe]);
        Assert.DoesNotContain(new Pos(5, 3), Combat.ThreatZone(sim, foe));
        Assert.True(sim.Execute(new Move(2, foe.Id, new(6, 3))).Ok);
        Assert.Contains(new Pos(5, 3), Combat.ThreatZone(sim, foe));
    }
}
