using Remnants.Core.Colony;

namespace Remnants.Core.Items;

/// <summary>
/// The gear mods' side of the Schematics module: the gear schematics live in <c>data/schematics.gearmods.json</c> (merged
/// into the catalogue by <see cref="Schematics.SchematicCatalogLoader"/>), each unlocking the mod and kit recipes it names;
/// recipes no schematic unlocks are the default set. The Gear Bench and the bots ask through here.
/// </summary>
public static class GearModSchematics
{
    /// <summary>Whether the colony knows a gear schematic.</summary>
    public static bool Has(ColonySim colony, string schematicId) => Schematics.Schematics.Has(colony, schematicId);

    /// <summary>Whether a recipe's schematic (if it has one) is known.</summary>
    public static bool RecipeUnlocked(ColonySim colony, string recipeId) => Schematics.Schematics.RecipeUnlocked(colony, recipeId);
}
