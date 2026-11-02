using Remnants.Core.Scavenge;
using static Remnants.Core.Tests.ScavengeTestData;

namespace Remnants.Core.Tests;

/// <summary>The shared squad deck: draw pile, hand, used pile, the round's card points, reshuffles.</summary>
public class SquadDeckTests
{
    private static SquadLoadout Starter => new(Real.Cards.StarterDeck(), 5, null, 3);

    private static ScavengeSim Run(SquadLoadout? loadout = null, ulong seed = 11) =>
        NewScavenge.Create(Real, "outskirts", seed, [Party(1) with { Deck = loadout ?? Starter }]);

    [Fact]
    public void A_party_enters_with_its_deck_shuffled_and_a_full_opening_hand()
    {
        var sim = Run();
        sim.Start();
        var deck = Assert.Single(sim.State.Decks);
        Assert.Equal(1, deck.PlayerId);
        Assert.Equal(5, deck.Hand.Count);
        Assert.Equal(3, deck.Points);
        Assert.Equal(Starter.Cards.Count, deck.Hand.Count + deck.DrawPile.Count + deck.Used.Count);
        Assert.Equal(Starter.Cards.OrderBy(x => x).ToList(), deck.Hand.Concat(deck.DrawPile).OrderBy(x => x).ToList());
        Assert.NotEqual(Starter.Cards, deck.Hand.Concat(deck.DrawPile).ToList()); // shuffled
    }

    [Fact]
    public void Each_round_start_refills_the_points_and_the_hand()
    {
        Assert.Null(Real.Cards.Rules.Draw); // no cap in the data: the hand refills to its size
        var sim = Run();
        sim.Start();
        var deck = sim.State.Decks[0];
        deck.Points = 0;
        for (var i = 0; i < 4; i++)
        {
            deck.Used.Add(deck.Hand[0]);
            deck.Hand.RemoveAt(0);
        }
        SquadDecks.RoundStarted(sim);
        Assert.Equal(5, deck.Hand.Count);
        Assert.Equal(3, deck.Points);
    }

    [Fact]
    public void A_draw_cap_limits_the_cards_drawn_per_round()
    {
        var sim = Run(new SquadLoadout(Real.Cards.StarterDeck(), 5, 3, 3));
        sim.Start();
        var deck = sim.State.Decks[0];
        for (var i = 0; i < 4; i++)
        {
            deck.Used.Add(deck.Hand[0]);
            deck.Hand.RemoveAt(0);
        }
        SquadDecks.RoundStarted(sim);
        Assert.Equal(4, deck.Hand.Count); // three drawn, not four
        SquadDecks.RoundStarted(sim);
        Assert.Equal(5, deck.Hand.Count);
    }

    [Fact]
    public void An_empty_draw_pile_reshuffles_the_used_pile()
    {
        var sim = Run(new SquadLoadout(["dash", "dash", "take_cover", "steady_aim", "scout_ahead", "sidestep", "dash"], 5, 3, 3));
        sim.Start();
        var deck = sim.State.Decks[0];
        Assert.Equal(2, deck.DrawPile.Count);
        deck.Used.AddRange(deck.Hand);
        deck.Hand.Clear();
        SquadDecks.Draw(sim, deck, 4);
        Assert.Equal(4, deck.Hand.Count);
        Assert.Equal(7, deck.Hand.Count + deck.DrawPile.Count + deck.Used.Count);
        Assert.Empty(deck.Used);
        Assert.Contains(sim.State.Events, e => e.Type == ScavengeEventType.DeckShuffled);
    }

    [Fact]
    public void Drawing_with_nothing_left_anywhere_stops()
    {
        var sim = Run(new SquadLoadout(["dash", "take_cover"], 5, 3, 3));
        sim.Start();
        var deck = sim.State.Decks[0];
        Assert.Equal(2, deck.Hand.Count);
        SquadDecks.Draw(sim, deck, 3);
        Assert.Equal(2, deck.Hand.Count);
    }

    [Fact]
    public void Card_draws_never_touch_the_game_rng()
    {
        var with = Run();
        var without = NewScavenge.Create(Real, "outskirts", 11, [Party(1)]);
        with.Start();
        without.Start();
        Assert.Equal(without.State.RngState, with.State.RngState);
        Assert.Empty(without.State.Decks);
    }

    [Fact]
    public void The_same_seed_deals_the_same_cards()
    {
        var a = Run(seed: 5);
        var b = Run(seed: 5);
        a.Start();
        b.Start();
        Assert.Equal(a.State.Decks[0].Hand, b.State.Decks[0].Hand);
        var c = Run(seed: 6);
        c.Start();
        Assert.NotEqual(a.State.Decks[0].Hand.Concat(a.State.Decks[0].DrawPile), c.State.Decks[0].Hand.Concat(c.State.Decks[0].DrawPile));
    }

    [Fact]
    public void A_deck_with_an_unknown_card_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Run(new SquadLoadout(["no_such_card"], 5, 3, 3)));
    }

    [Fact]
    public void Decks_survive_save_and_load()
    {
        var sim = Run();
        sim.Start();
        var loaded = ScavengeSave.Load(Real, ScavengeSave.Save(sim.State));
        Assert.Equal(sim.State.Decks[0].Hand, loaded.State.Decks[0].Hand);
        Assert.Equal(sim.State.CardRngState, loaded.State.CardRngState);
    }
}
