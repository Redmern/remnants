using Remnants.Core.Colony;
using Remnants.Core.Items;

namespace Remnants.Core.Trade;

/// <summary>
/// Schematic copies for trading (SCHEMATICS.md "Duplicates"): a colony that knows a schematic can make a tradeable copy
/// of it; the seller keeps the knowledge, and the buyer learns the copy the normal way once it is home.
/// <para>
/// Adapter over today's blueprints (a known blueprint item id is the schematic, its item is the copy, and the buyer
/// studies it as usual). The names match story-schematics' <c>Schematics.MakeCopy</c> / <c>Schematics.Receive</c>, so the
/// switch-over is one line each: <see cref="MakeCopy"/> → <c>Schematics.MakeCopy</c> (Research Data cost from
/// schematics.json) and <see cref="Receive"/> → <c>Schematics.Receive</c> for schematic items.
/// </para>
/// </summary>
public static class SchematicCopies
{
    /// <summary>The schematics this colony could copy: every known one.</summary>
    public static IEnumerable<string> Copyable(ColonySim colony) => colony.State.KnownBlueprints;

    public static bool Has(ColonySim colony, string schematicId) => colony.State.KnownBlueprints.Contains(schematicId);

    /// <summary>The price options for a copy of <paramref name="schematicId"/> (empty when it is not a schematic).</summary>
    public static IReadOnlyList<Dictionary<string, double>> CopyCost(ColonySim colony, TradeRules rules, string schematicId)
    {
        if (colony.Scavenge?.Items.GetValueOrDefault(schematicId) is not { Kind: ItemKind.Blueprint } item || rules.CopyCost.Count == 0) return [];
        return TradeRules.At(rules.CopyCost, item.Tier);
    }

    /// <summary>
    /// Make a tradeable copy of a known schematic: pays the first affordable cost option, and the copy goes into the
    /// inventory (to give, post or trade). The colony keeps the schematic.
    /// </summary>
    public static CommandResult MakeCopy(ColonySim colony, TradeRules rules, string schematicId)
    {
        var state = colony.State;
        if (colony.Scavenge?.Items.GetValueOrDefault(schematicId) is not { Kind: ItemKind.Blueprint } item)
            return CommandResult.Fail(CommandError.NotABlueprint, $"'{schematicId}' is not a schematic.");
        if (!Has(colony, schematicId)) return CommandResult.Fail(CommandError.MissingItems, $"The colony does not know {item.Name}.");
        var options = CopyCost(colony, rules, schematicId);
        if (options.FirstOrDefault(state.Stockpile.CanAfford) is not { } cost)
            return CommandResult.Fail(CommandError.CannotAfford, $"A copy of {item.Name} costs {string.Join(" or ", options.Select(o => Describe(colony, o)))}.");
        state.Stockpile.Spend(cost);
        state.Inventory.Add(item.Id);
        var message = $"Made a copy of {item.Name} ({Describe(colony, cost)}).";
        state.Log(new(state.Tick, ColonyEventType.SchematicCopied, message));
        return CommandResult.Success(message);
    }

    /// <summary>An item that reached home through a trade: into the inventory (a schematic copy waits there to be studied).</summary>
    public static void Receive(ColonySim colony, string itemId) => colony.State.Inventory.Add(itemId);

    private static string Describe(ColonySim colony, IReadOnlyDictionary<string, double> cost) =>
        string.Join(" + ", cost.Select(kv => $"{kv.Value:0.#} {colony.Data.Resources.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}"));
}
