using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>Enemy-side abilities: the Warden shoves, Cole smokes, by the enemy card priority.</summary>
public class EnemyCardAbilityTests
{
    /// <summary>The enemy deck holding exactly <paramref name="hand"/>, brought by <paramref name="by"/>.</summary>
    private static SquadDeck Deck(ScavengeSim sim, Unit by, params string[] hand)
    {
        var deck = new SquadDeck { PlayerId = EnemyCards.DeckId, Hand = hand.ToList(), Points = 3, Budget = 3, HandSize = 5, Brought = [by.Id] };
        sim.State.Decks.Add(deck);
        return deck;
    }

    [Fact]
    public void The_warden_and_cole_bring_ability_cards()
    {
        Assert.Contains(Real.Enemies["linked_warden"].Cards, id => Real.Cards.All[id].Effect.Push > 0);
        Assert.Contains(Real.Enemies["cole"].Cards, id => Real.Cards.All[id].Effect.Smoke > 0);
    }

    [Fact]
    public void The_warden_shoves_a_seen_enemy_in_reach_and_leaves_one_out_of_it_alone()
    {
        var u = SurvivorUnit(1, new(3, 1));
        var warden = EnemyUnit("linked_warden", new(4, 1));
        var sim = Arena(10, 3, [warden, u]); // the warden is up
        var deck = Deck(sim, warden, "shove");
        u.Pos = new(0, 1);
        EnemyCards.Play(sim, warden);
        Assert.Equal(["shove"], deck.Hand); // out of its reach: nothing to break
        u.Pos = new(3, 1);
        EnemyCards.Play(sim, warden);
        Assert.Empty(deck.Hand);
        Settle(sim);
        Assert.Equal(new Pos(1, 1), u.Pos); // the Warden's strength shoves two tiles
    }

    [Fact]
    public void Cole_throws_a_smoke_screen_toward_who_sees_him_when_hurt()
    {
        var u = SurvivorUnit(1, new(1, 1), weapon: "pistol");
        var cole = EnemyUnit("cole", new(6, 1));
        var sim = Arena(10, 3, [cole, u]); // cole is up
        var deck = Deck(sim, cole, "smoke_bomb");
        EnemyCards.Play(sim, cole);
        Assert.Equal(["smoke_bomb"], deck.Hand); // unhurt
        cole.Body.Injure(BodyPart.Torso, 0.5);
        EnemyCards.Play(sim, cole);
        Assert.Empty(deck.Hand);
        Settle(sim);
        var cloud = Assert.Single(sim.State.FieldEffects, f => f.Kind == FieldEffectKind.Smoke && f.Side == Unit.EnemySide);
        Assert.DoesNotContain(cole.Pos, cloud.Tiles);
        Assert.False(Detection.SideSees(sim.Data, sim.State, u.Side, cole));
    }
}
