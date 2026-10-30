using System.Text.Json;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;

namespace Remnants.Core.Trade;

/// <summary>Player trading, tuned in <c>data/trade.json</c>. Every field has a default, so the file is optional.</summary>
public sealed record TradeRules
{
    public TradeLimits Limits { get; init; } = new();
    public CourierRules Courier { get; init; } = new();

    /// <summary>Per schematic tier (1-based), the price options for a copy; the first affordable one is paid.</summary>
    public List<List<Dictionary<string, double>>> CopyCost { get; init; } = new();

    /// <summary>Reference value of one unit of a resource, in scrap.</summary>
    public Dictionary<string, double> ResourceValues { get; init; } = new();

    /// <summary>Reference value of a schematic (blueprint) item per tier, in scrap.</summary>
    public List<double> SchematicValues { get; init; } = new();

    public MarketRules Market { get; init; } = new();
    public LiveTradeRules Live { get; init; } = new();

    public static TradeRules Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "trade.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : new TradeRules();
    }

    public static TradeRules Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TradeRules>(json, Json.Options) ?? new TradeRules();
        }
        catch (JsonException e)
        {
            throw new GameDataException($"trade.json: {e.Message}");
        }
    }

    /// <summary>The value at a 1-based tier, clamped to the table.</summary>
    public static T At<T>(IReadOnlyList<T> table, int tier) => table[Math.Clamp(tier, 1, table.Count) - 1];
}

public sealed record TradeLimits
{
    public int MaxLines { get; init; } = 8;
    public int MaxResourceAmount { get; init; } = 500;
    public int MaxItemCount { get; init; } = 20;
    public int SuspiciousResourceAmount { get; init; } = 200;
    public int SuspiciousItemCount { get; init; } = 10;
}

public sealed record CourierRules
{
    public double BaseDays { get; init; } = 0.5;
    public double DaysPerLeg { get; init; } = 0.5;
    public int MaxLegs { get; init; } = 4;
    public double MinDays { get; init; } = 0.25;

    /// <summary>A building the colony needs to receive lobby trades, when the game data has it (the Radio Mast).</summary>
    public string? RequiresBuilding { get; init; }

    /// <summary>Building id → time factor while the colony has one (a Garage's vehicles make the trip faster).</summary>
    public Dictionary<string, double> BuildingFactors { get; init; } = new();
}

public sealed record MarketRules
{
    public double ListingHours { get; init; } = 72;
    public double MaxListingHours { get; init; } = 168;
    public int MaxOpenListings { get; init; } = 8;
    public int PostsPerMinute { get; init; } = 4;
    public int RequestsPerMinute { get; init; } = 120;
    public int MaxListingsSent { get; init; } = 200;
}

public sealed record LiveTradeRules
{
    public double IdleSeconds { get; init; } = 180;
}

/// <summary>
/// Reference values in scrap, for a fair-swap hint (no currency: trades are barter). From <c>trade.json</c> until the
/// trader's price table (story-schematics' <c>TradeValues</c>) is in; the rules mirror it: a resource its listed value, a
/// schematic its tier price, a crafted item its recipe inputs × 1.25, a material its salvage, anything else 5 × tier;
/// quality variants scale by their stat multiplier.
/// </summary>
public static class TradePrices
{
    public const double CraftedMarkup = 1.25;

    public static double Of(TradeGood good, TradeRules rules, GameData data, ScavengeData items) =>
        good.Count * (good.Kind == GoodKind.Resource ? OfResource(rules, good.Id) : OfItem(rules, data, items, good.Id, 0));

    public static double Of(IEnumerable<TradeGood> goods, TradeRules rules, GameData data, ScavengeData items) =>
        Math.Round(goods.Sum(g => Of(g, rules, data, items)), 1);

    public static double OfResource(TradeRules rules, string resourceId) => rules.ResourceValues.GetValueOrDefault(resourceId);

    private static double OfItem(TradeRules rules, GameData data, ScavengeData items, string itemId, int depth)
    {
        if (depth > 6 || !items.Items.TryGetValue(itemId, out var item)) return 0;
        if (item.BaseId is { } baseId)
            return Math.Round(OfItem(rules, data, items, baseId, depth + 1) * (items.QualityOf(item)?.StatMultiplier ?? 1), 1);
        if (TradeGoods.IsSchematic(item) && rules.SchematicValues.Count > 0) return TradeRules.At(rules.SchematicValues, item.Tier);
        if (data.Recipes.Values.FirstOrDefault(r => r.Item == itemId) is { } recipe)
        {
            var inputs = recipe.Inputs.Sum(kv => OfResource(rules, kv.Key) * kv.Value)
                         + recipe.ItemInputs.Sum(kv => OfItem(rules, data, items, kv.Key, depth + 1) * kv.Value);
            return Math.Round(inputs / Math.Max(1, recipe.Amount) * CraftedMarkup, 1);
        }
        if (item.Salvage is { } salvage) return OfResource(rules, salvage.Resource) * salvage.Amount;
        return 5 * item.Tier;
    }
}
