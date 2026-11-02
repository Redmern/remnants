using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The simple heuristics the playtest and server bots share: card play, dodging a telegraph, declaring a plan.</summary>
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
    public void A_telegraphed_blow_at_the_unit_is_answered_with_a_reaction_card_then_dodged()
    {
        var (sim, a, husk, _) = Field("sidestep", "take_cover");
        husk.Plan = new PlannedAction { Path = [new(2, 1)], AttackTile = a.Pos, ActionId = "claw", Round = 1 };
        var card = Assert.IsType<PlayCard>(BotTactics.Card(sim, a, 1));
        Assert.Equal(("sidestep", husk.Id), (card.CardId, card.TargetId));
        Assert.True(sim.Execute(card).Ok);

        var dodge = Assert.IsType<Move>(BotTactics.Dodge(sim, a, 1));
        Assert.NotEqual(a.Pos, dodge.To);
        Assert.True(sim.Execute(dodge).Ok);
        Assert.Null(BotTactics.Dodge(sim, a, 1)); // off the tile now
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
            husk.Plan = new PlannedAction { Path = [], AttackTile = a.Pos, ActionId = "claw", Round = 1 };
            husk.Pos = new(2, 1);
            a.Body.Injure(BodyPart.LeftArm, 0.3);
            if (BotTactics.Card(sim, a, 1) is { } pick) Assert.True(sim.Execute(pick).Ok, $"{card}: {sim.Execute(pick).Message}");
        }
    }

    [Fact]
    public void Out_of_reach_the_bot_declares_a_move_and_an_attack_on_the_target()
    {
        var (sim, a, husk, _) = Field();
        husk.Pos = new(4, 1);
        var plan = Assert.IsType<DeclareIntent>(BotTactics.Plan(sim, a, 1, [husk]));
        Assert.Equal(husk.Pos, plan.AttackTile);
        Assert.Single(plan.Path);
        Assert.True(sim.Execute(plan).Ok);
        Assert.Null(BotTactics.Plan(Field().Sim, a, 1, [])); // nobody to go for
    }
}
