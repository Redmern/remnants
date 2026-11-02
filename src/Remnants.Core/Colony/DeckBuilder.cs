using Remnants.Core.Scavenge;

namespace Remnants.Core.Colony;

/// <summary>A card in the deck builder: copies owned, in the deck, in slots, free, and the copies a deck may hold.</summary>
public sealed record DeckRow(string Id, string Name, CardCategory Category, CardRarity Rarity, string Text, int Owned, int InDeck, int InSlots, int Free, int MaxCopies);

/// <summary>A survivor's personal slots in the deck builder.</summary>
public sealed record SlotRow(int SurvivorId, string Name, int Slots, List<string> Cards);

/// <summary>A village deck upgrade: bought, available (its chain met), its cost.</summary>
public sealed record UpgradeRow(string Id, string Name, CardUpgradeStat Stat, int Amount, Dictionary<string, int> Cost, bool Bought, bool Available);

/// <summary>The deck builder: collection rows, the deck's size against its cap, slots per adult, the upgrades.</summary>
public sealed record DeckBuilderView(List<DeckRow> Rows, int DeckCount, int MaxDeck, int MaxSlotCards, List<SlotRow> Slots, List<UpgradeRow> Upgrades,
    SquadLoadout? Loadout);

/// <summary>The village deck builder's view model (plain C#; commands are SetDeck, SetCardSlots, BuyCardUpgrade).</summary>
public static class DeckBuilder
{
    public static DeckBuilderView View(ColonySim sim)
    {
        var data = sim.Scavenge ?? throw new InvalidOperationException("Card data is not loaded.");
        var cards = sim.State.Cards;
        var rules = data.Cards.Rules;
        int Slotted(string id) => cards.Slots.Values.Sum(s => s.Count(c => c == id));
        var rows = data.Cards.All.Values
            .Select(c =>
            {
                var owned = cards.Owned.GetValueOrDefault(c.Id);
                var inDeck = cards.Deck.Count(id => id == c.Id);
                var inSlots = Slotted(c.Id);
                return new DeckRow(c.Id, c.Name, c.Category, c.Rarity, c.Text, owned, inDeck, inSlots, Math.Max(0, owned - inDeck - inSlots), rules.MaxCopies);
            })
            .OrderByDescending(r => r.Owned > 0).ThenBy(r => r.Category).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        var slots = sim.State.Survivors.Where(s => !s.IsChild).OrderBy(s => s.Id)
            .Select(s => new SlotRow(s.Id, s.Name, CardSlots.Count(data, s), cards.Slots.GetValueOrDefault(s.Id) ?? [])).ToList();
        var upgrades = rules.Upgrades.Select(u => new UpgradeRow(u.Id, u.Name, u.Stat, u.Amount, u.Cost, cards.Upgrades.Contains(u.Id),
            u.After is null || cards.Upgrades.Contains(u.After))).ToList();
        return new DeckBuilderView(rows, cards.Deck.Count, rules.MaxDeck, rules.MaxSlotCards, slots, upgrades, ColonyCards.Loadout(sim));
    }
}
