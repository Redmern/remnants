using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Trade;

/// <summary>What a trade line moves: a stockpile resource (wood, scrap, Research Data…) or whole items.</summary>
public enum GoodKind { Resource, Item }

/// <summary>
/// Per-item data that must survive a trade beyond the item id (gear quality is already part of the id: a quality
/// variant is its own item). <see cref="Lots"/>: for food, the Pantry lots the units come from (quality, tainted, time
/// left before they spoil), so a trade hands over the same food, not fresh food. Weapon mods and the Mk level ride along
/// for the weapon-mods work; the colony inventory, which only counts ids, does not keep them yet.
/// </summary>
public sealed record ItemExtras(List<string>? Mods = null, int? Mk = null, List<FoodLotExtra>? Lots = null)
{
    public bool IsEmpty => (Mods is null || Mods.Count == 0) && Mk is null && (Lots is null || Lots.Count == 0);

    /// <summary>A stable text for grouping lines (lines with different extras stay apart).</summary>
    public string Signature => string.Join(",", Mods ?? []) + "|" + Mk + "|" +
        string.Join(";", (Lots ?? []).Select(l => $"{l.Count}:{l.Quality}:{l.Tainted}:{l.TicksLeft:0.##}"));
}

/// <summary>Some units of a food item from one Pantry lot: <paramref name="TicksLeft"/> until it spoils (null = keeps).</summary>
public sealed record FoodLotExtra(int Count, string Quality, bool Tainted, double? TicksLeft);

/// <summary>
/// One line of a trade offer: <paramref name="Count"/> of a resource or of one item id. Schematic copies are items (a
/// blueprint or schematic item id), so they trade like any other item and the buyer learns them the normal way.
/// </summary>
public sealed record TradeGood(GoodKind Kind, string Id, int Count, ItemExtras? Extras = null)
{
    public static TradeGood Resource(string id, int amount) => new(GoodKind.Resource, id, amount);
    public static TradeGood Item(string id, int count = 1) => new(GoodKind.Item, id, count);

    public string Key => $"{Kind}:{Id}";
}

/// <summary>Validation, naming and merging of trade lines against the game data (shared by Core, server and UI).</summary>
public static class TradeGoods
{
    /// <summary>Lines merged by kind + id (extras kept per line, so modded items never merge with plain ones), in a stable order.</summary>
    public static List<TradeGood> Normalize(IEnumerable<TradeGood> goods) =>
        goods.Where(g => g.Count > 0)
            .GroupBy(g => (g.Kind, g.Id, Extras: g.Extras is { IsEmpty: false } e ? e.Signature : ""))
            .Select(grp => grp.First() with { Count = grp.Sum(g => g.Count) })
            .OrderBy(g => g.Kind).ThenBy(g => g.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Why these lines are not a valid offer, or null: known ids, positive counts within the caps, at most
    /// <see cref="TradeLimits.MaxLines"/> lines, no natural (body) items. Empty offers are valid (a gift).
    /// </summary>
    public static string? Validate(IReadOnlyList<TradeGood>? goods, TradeRules rules, GameData data, ScavengeData items) =>
        Validate(goods, rules, data.Resources.ContainsKey, items);

    /// <inheritdoc cref="Validate(IReadOnlyList{TradeGood}?, TradeRules, GameData, ScavengeData)"/>
    public static string? Validate(IReadOnlyList<TradeGood>? goods, TradeRules rules, Func<string, bool> isResource, ScavengeData items)
    {
        if (goods is null) return "No offer.";
        if (goods.Count > rules.Limits.MaxLines) return $"At most {rules.Limits.MaxLines} kinds of goods per offer.";
        foreach (var g in goods)
        {
            if (g is null || string.IsNullOrEmpty(g.Id)) return "An offer line is empty.";
            if (g.Count <= 0) return $"'{g.Id}': the amount must be positive.";
            switch (g.Kind)
            {
                case GoodKind.Resource:
                    if (!isResource(g.Id)) return $"Unknown resource '{g.Id}'.";
                    if (g.Count > rules.Limits.MaxResourceAmount) return $"At most {rules.Limits.MaxResourceAmount} {g.Id} per line.";
                    if (g.Extras is not null) return $"'{g.Id}' is a resource.";
                    break;
                case GoodKind.Item:
                    if (!items.Items.TryGetValue(g.Id, out var item)) return $"Unknown item '{g.Id}'.";
                    if (item.Natural) return $"{item.Name} cannot be traded.";
                    if (g.Count > rules.Limits.MaxItemCount) return $"At most {rules.Limits.MaxItemCount} × {item.Name} per line.";
                    if (g.Extras is { Mods.Count: > 8 }) return $"{item.Name}: too many mods.";
                    if (g.Extras is { Mk: < 0 or > 10 }) return $"{item.Name}: implausible Mk level.";
                    if (g.Extras?.Lots is { Count: > 0 } lots
                        && (item.Kind != ItemKind.Food || lots.Count > 8 || lots.Sum(l => l.Count) != g.Count
                            || lots.Any(l => l.Count <= 0 || string.IsNullOrWhiteSpace(l.Quality) || l.Quality.Length > 24 || l.TicksLeft < 0)))
                        return $"{item.Name}: the food lots do not add up.";
                    break;
                default:
                    return "Unknown kind of goods.";
            }
        }
        if (goods.GroupBy(g => g.Key).Any(grp => grp.Count() > 1 && grp.All(g => g.Extras is null or { IsEmpty: true })))
            return "List each good once.";
        return null;
    }

    /// <summary>Lines big enough to log (still allowed): a hint of a modified client.</summary>
    public static string? Suspicious(IEnumerable<TradeGood> goods, TradeRules rules) =>
        goods.FirstOrDefault(g => g.Kind == GoodKind.Resource ? g.Count > rules.Limits.SuspiciousResourceAmount : g.Count > rules.Limits.SuspiciousItemCount)
            is { } big ? $"{big.Count} × {big.Id}" : null;

    public static string Name(TradeGood g, GameData data, ScavengeData items) => g.Kind switch
    {
        GoodKind.Resource => data.Resources.GetValueOrDefault(g.Id)?.Name ?? g.Id,
        _ => items.Items.GetValueOrDefault(g.Id)?.Name ?? g.Id,
    };

    public static string Describe(IEnumerable<TradeGood> goods, GameData data, ScavengeData items)
    {
        var parts = goods.Select(g => g.Count == 1 && g.Kind == GoodKind.Item ? Name(g, data, items) : $"{g.Count} {Name(g, data, items)}").ToList();
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    /// <summary>Whether the colony has all of these in its stockpile and inventory right now.</summary>
    public static bool Has(ColonyState state, GameData data, IEnumerable<TradeGood> goods) =>
        Normalize(goods).GroupBy(g => (g.Kind, g.Id)).All(grp => FoodGoods.Have(state, data, grp.First()) >= grp.Sum(g => g.Count));

    /// <summary>Items as a flat list of ids (field trades work on pack contents).</summary>
    public static List<string> ItemIds(IEnumerable<TradeGood> goods) =>
        goods.Where(g => g.Kind == GoodKind.Item).SelectMany(g => Enumerable.Repeat(g.Id, g.Count)).ToList();

    public static bool IsSchematic(ItemDef item) => item.Kind == ItemKind.Schematic;
}
