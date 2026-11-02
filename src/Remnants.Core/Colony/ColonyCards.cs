using Remnants.Core.Scavenge;

namespace Remnants.Core.Colony;

/// <summary>
/// The colony's cards (cards.json): the collection (<see cref="Owned"/>: copies per card id), the squad deck a party takes
/// out (<see cref="Deck"/>), cards held in survivors' personal slots (<see cref="Slots"/>), and the deck upgrades bought
/// (<see cref="Upgrades"/>). The deck and the slots are a selection from the collection: together they never use more
/// copies of a card than are owned.
/// </summary>
public sealed class CardCollection
{
    public SortedDictionary<string, int> Owned { get; set; } = new(StringComparer.Ordinal);
    public List<string> Deck { get; set; } = new();
    public SortedDictionary<int, List<string>> Slots { get; set; } = new();
    public SortedSet<string> Upgrades { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Building the squad deck at the village, the starter set, and cards coming into and leaving the collection.</summary>
public static class ColonyCards
{
    /// <summary>The starter set into the collection, and as the deck when there is none yet (new and migrated colonies).</summary>
    public static void GrantStarter(ColonyState state, CardCatalog cards)
    {
        foreach (var (id, n) in cards.Rules.Starter) Add(state, id, n);
        if (state.Cards.Deck.Count == 0) state.Cards.Deck = cards.StarterDeck();
    }

    public static void Add(ColonyState state, string id, int count = 1)
    {
        if (count <= 0) return;
        state.Cards.Owned[id] = state.Cards.Owned.GetValueOrDefault(id) + count;
    }

    /// <summary>
    /// Copies leave the collection (played consumables, a trade): fewer owned, and the deck, then the slots, give up copies
    /// they can no longer have.
    /// </summary>
    public static void Remove(ColonyState state, string id, int count = 1)
    {
        var cards = state.Cards;
        var left = Math.Max(0, cards.Owned.GetValueOrDefault(id) - count);
        if (left == 0) cards.Owned.Remove(id);
        else cards.Owned[id] = left;
        while (Used(cards, id) > left)
        {
            if (cards.Deck.Remove(id)) continue;
            var slot = cards.Slots.First(kv => kv.Value.Contains(id));
            slot.Value.Remove(id);
            if (slot.Value.Count == 0) cards.Slots.Remove(slot.Key);
        }
    }

    private static int Used(CardCollection cards, string id) =>
        cards.Deck.Count(c => c == id) + cards.Slots.Values.Sum(s => s.Count(c => c == id));

    /// <summary>The party's squad deck with the bought upgrades in; null when the deck is empty (the party goes without cards).</summary>
    public static SquadLoadout? Loadout(ColonySim sim)
    {
        if (sim.Scavenge is not { } data || sim.State.Cards.Deck.Count == 0) return null;
        var rules = data.Cards.Rules;
        int Bonus(CardUpgradeStat stat) => rules.Upgrades.Where(u => u.Stat == stat && sim.State.Cards.Upgrades.Contains(u.Id)).Sum(u => u.Amount);
        return new SquadLoadout(sim.State.Cards.Deck.Where(id => data.Cards.Find(id) is not null).ToList(),
            rules.HandSize + Bonus(CardUpgradeStat.HandSize), rules.Draw + Bonus(CardUpgradeStat.Draw), rules.Budget + Bonus(CardUpgradeStat.Budget));
    }

    public static List<string> SlotCards(ColonyState state, int survivorId) => new(state.Cards.Slots.GetValueOrDefault(survivorId) ?? []);

    internal static CommandResult SetDeck(ColonySim sim, SetDeck c)
    {
        var slots = sim.State.Cards.Slots.Values.SelectMany(s => s);
        if (Check(sim, c.Cards.Concat(slots)) is { } bad) return bad;
        sim.State.Cards.Deck = c.Cards.ToList();
        return CommandResult.Success($"The squad deck has {c.Cards.Count} cards.");
    }

    internal static CommandResult SetSlots(ColonySim sim, SetCardSlots c)
    {
        if (sim.State.FindSurvivor(c.SurvivorId) is not { } s) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        var others = sim.State.Cards.Slots.Where(kv => kv.Key != c.SurvivorId).SelectMany(kv => kv.Value);
        if (Check(sim, c.Cards.Concat(sim.State.Cards.Deck).Concat(others)) is { } bad) return bad;
        if (c.Cards.Count == 0) sim.State.Cards.Slots.Remove(c.SurvivorId);
        else sim.State.Cards.Slots[c.SurvivorId] = c.Cards.ToList();
        return CommandResult.Success($"{s.Name} holds {c.Cards.Count} card{(c.Cards.Count == 1 ? "" : "s")}.");
    }

    internal static CommandResult BuyUpgrade(ColonySim sim, BuyCardUpgrade c)
    {
        if (sim.Scavenge is not { } data) return CommandResult.Fail(CommandError.NoScavengeData, "Card data is not loaded.");
        var cards = sim.State.Cards;
        var upgrade = data.Cards.Rules.Upgrades.Find(u => u.Id == c.UpgradeId);
        if (upgrade is null || upgrade.After is { } after && !cards.Upgrades.Contains(after))
            return CommandResult.Fail(CommandError.UnknownCardUpgrade, $"No deck upgrade '{c.UpgradeId}' to buy.");
        if (cards.Upgrades.Contains(upgrade.Id)) return CommandResult.Fail(CommandError.AlreadyExists, $"{upgrade.Name} is built already.");
        var cost = upgrade.Cost.ToDictionary(kv => kv.Key, kv => (double)kv.Value);
        if (!sim.State.Stockpile.CanAfford(cost)) return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {upgrade.Name}.");
        sim.State.Stockpile.Spend(cost);
        cards.Upgrades.Add(upgrade.Id);
        return CommandResult.Success($"{upgrade.Name}: +{upgrade.Amount} {upgrade.Stat}.");
    }

    /// <summary>Every card known and no card used more often than it is owned.</summary>
    private static CommandResult? Check(ColonySim sim, IEnumerable<string> uses)
    {
        if (sim.Scavenge is not { } data) return CommandResult.Fail(CommandError.NoScavengeData, "Card data is not loaded.");
        foreach (var g in uses.GroupBy(id => id, StringComparer.Ordinal))
        {
            if (data.Cards.Find(g.Key) is null) return CommandResult.Fail(CommandError.UnknownCard, $"No card '{g.Key}'.");
            if (g.Count() > sim.State.Cards.Owned.GetValueOrDefault(g.Key))
                return CommandResult.Fail(CommandError.NotEnoughCards, $"Only {sim.State.Cards.Owned.GetValueOrDefault(g.Key)} '{g.Key}' owned.");
        }
        return null;
    }
}
