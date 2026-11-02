using Remnants.Core.Random;

namespace Remnants.Core.Scavenge;

/// <summary>
/// What a party brings in card terms: its deck (card ids, copies repeated) and the deck's numbers with the village's
/// upgrades in (hand size, cards drawn per round, card-point budget).
/// </summary>
public sealed record SquadLoadout(List<string> Cards, int HandSize, int Draw, int Budget);

/// <summary>
/// A party's shared squad deck in the field. Cards are drawn from <see cref="DrawPile"/> into <see cref="Hand"/>; played
/// ones go to <see cref="Used"/> (shuffled back when the draw pile runs out) or, if consumable, to <see cref="Consumed"/>
/// (gone, also from the colony's collection). <see cref="Points"/>: card points left this round, refilled to
/// <see cref="Budget"/> at each round start. <see cref="Pouch"/>: cards found in the run, kept only if the party gets out.
/// A view for another player carries no card ids, only <see cref="HandCount"/>, <see cref="DrawCount"/> and
/// <see cref="UsedCount"/> (<see cref="Hidden"/> set).
/// </summary>
public sealed class SquadDeck
{
    public int PlayerId { get; set; }
    public List<string> DrawPile { get; set; } = new();
    public List<string> Hand { get; set; } = new();
    public List<string> Used { get; set; } = new();
    public List<string> Consumed { get; set; } = new();
    public List<string> Pouch { get; set; } = new();
    public int Points { get; set; }
    public int Budget { get; set; }
    public int HandSize { get; set; }
    public int DrawPerRound { get; set; }

    /// <summary>The round a dead hard card was last discarded in (once per round).</summary>
    public int DeadDiscardRound { get; set; }

    public bool Hidden { get; set; }
    public int HandCount { get; set; }
    public int DrawCount { get; set; }
    public int UsedCount { get; set; }
}

/// <summary>
/// Deck mechanics: building a party's deck, the round-start refill and draw, drawing and reshuffling. Shuffles draw from
/// the run's own card stream (<see cref="ScavengeState.CardRngState"/>), never the game's RNG, so card play leaves
/// every other roll where it was.
/// </summary>
public static class SquadDecks
{
    public const ulong StreamSalt = 0xCA4D_5EED_0000_0003UL;

    public static SquadDeck? Of(ScavengeState state, int playerId) => state.Decks.Find(d => d.PlayerId == playerId);

    internal static void Create(ScavengeState state, int playerId, SquadLoadout loadout)
    {
        var deck = new SquadDeck
        {
            PlayerId = playerId, DrawPile = new(loadout.Cards), HandSize = Math.Max(1, loadout.HandSize),
            DrawPerRound = Math.Max(0, loadout.Draw), Budget = Math.Max(0, loadout.Budget),
        };
        Shuffle(state, deck.DrawPile);
        // The opening hand is a full one.
        var opening = Math.Min(deck.HandSize, deck.DrawPile.Count);
        deck.Hand.AddRange(deck.DrawPile.TakeLast(opening).Reverse());
        deck.DrawPile.RemoveRange(deck.DrawPile.Count - opening, opening);
        state.Decks.Add(deck);
    }

    /// <summary>
    /// Round start: every party still in the run gets its full card points back and draws up to its hand size, at most
    /// its draw per round (the opening hand, dealt as the party enters, is a full one). Personal slot cards played last
    /// round come back.
    /// </summary>
    public static void RoundStarted(ScavengeSim sim)
    {
        var state = sim.State;
        foreach (var deck in state.Decks.OrderBy(d => d.PlayerId))
        {
            if (state.FindParty(deck.PlayerId) is { Done: true }) continue;
            deck.Points = deck.Budget;
            var room = deck.HandSize - deck.Hand.Count;
            Draw(sim, deck, Math.Min(room, deck.DrawPerRound));
        }
        foreach (var u in state.Units.Where(u => u.SlotSpent.Count > 0)) u.SlotSpent.Clear();
    }

    /// <summary>Draws up to <paramref name="count"/> cards; an empty draw pile first takes the used pile back, shuffled.</summary>
    public static void Draw(ScavengeSim sim, SquadDeck deck, int count)
    {
        var state = sim.State;
        for (var i = 0; i < count; i++)
        {
            if (deck.DrawPile.Count == 0)
            {
                if (deck.Used.Count == 0) return;
                deck.DrawPile.AddRange(deck.Used);
                deck.Used.Clear();
                Shuffle(state, deck.DrawPile);
                state.Log(new(state.Round, ScavengeEventType.DeckShuffled, $"{state.PlayerName(deck.PlayerId)}'s used cards are shuffled back in.",
                    Side: state.FindParty(deck.PlayerId)?.Side));
            }
            deck.Hand.Add(deck.DrawPile[^1]);
            deck.DrawPile.RemoveAt(deck.DrawPile.Count - 1);
        }
    }

    /// <summary>Fisher–Yates on the card stream.</summary>
    private static void Shuffle(ScavengeState state, List<string> cards)
    {
        var rng = new DeterministicRng { State = state.CardRngState == 0 ? new DeterministicRng(StreamSalt).State : state.CardRngState };
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = rng.Next(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
        state.CardRngState = rng.State;
    }
}
