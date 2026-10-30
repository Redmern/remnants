using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Schematics;

/// <summary>
/// Reference values of goods in scrap, from the trader's price table (schematics.json <c>rules.resourceValues</c> and
/// <c>radio.prices</c>): what a fair swap looks like, for the trader screen and player trading. A resource is its listed
/// value; a schematic its cheapest price option; a crafted item its recipe's inputs per unit (items valued the same way)
/// × <see cref="CraftedMarkup"/>; a material its salvage; anything else 5 × its tier. Quality variants scale by their
/// stat multiplier.
/// </summary>
public static class TradeValues
{
    public const double CraftedMarkup = 1.25;

    /// <summary>One unit of a resource, in scrap (0 when it has no listed value).</summary>
    public static double OfResource(GameData data, string resourceId) =>
        data.Schematics.Rules.ResourceValues.GetValueOrDefault(resourceId);

    /// <summary>A bundle of resources, in scrap.</summary>
    public static double Of(GameData data, IReadOnlyDictionary<string, double> resources) =>
        resources.Sum(kv => OfResource(data, kv.Key) * kv.Value);

    /// <summary>One item (or schematic copy), in scrap.</summary>
    public static double Of(GameData data, ScavengeData items, string itemId) => Of(data, items, itemId, 0);

    /// <summary>One item, in scrap, for a colony (convenience).</summary>
    public static double Of(ColonySim colony, string itemId) =>
        Of(colony.Data, colony.Scavenge ?? throw new InvalidOperationException("Scavenge data is not loaded."), itemId);

    private static double Of(GameData data, ScavengeData items, string itemId, int depth)
    {
        if (depth > 6 || !items.Items.TryGetValue(itemId, out var item)) return 0;
        var quality = item.BaseId is null ? 1 : items.QualityOf(item)?.StatMultiplier ?? 1;
        if (item.BaseId is { } baseId) return Math.Round(Of(data, items, baseId, depth + 1) * quality, 1);
        if (data.Schematics.Find(item.Schematic ?? "") is { } s)
        {
            var prices = data.Schematics.Rules.Radio.Prices;
            return s.Tier <= prices.Count && prices[s.Tier - 1].Count > 0 ? prices[s.Tier - 1].Min(o => Of(data, o.Cost)) : 0;
        }
        // Food: its food value at the price of food (resourceValues "food"), cooked and preserved food with the markup.
        if (data.Food?.Items.GetValueOrDefault(itemId) is { FoodValue: > 0 } food)
            return Math.Round(food.FoodValue * OfResource(data, Colony.Pantry.FoodKey) * (food.Meal is null ? 1 : CraftedMarkup), 1);
        if (data.Recipes.Values.FirstOrDefault(r => r.Item == itemId) is { } recipe)
        {
            var inputs = Of(data, recipe.Inputs) + recipe.ItemInputs.Sum(kv => Of(data, items, kv.Key, depth + 1) * kv.Value);
            return Math.Round(inputs / Math.Max(1, recipe.Amount) * CraftedMarkup, 1);
        }
        if (item.Salvage is { } salvage) return OfResource(data, salvage.Resource) * salvage.Amount;
        return 5 * item.Tier;
    }
}
