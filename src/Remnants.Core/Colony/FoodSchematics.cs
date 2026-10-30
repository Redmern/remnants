using Remnants.Core.Data;

namespace Remnants.Core.Colony;

/// <summary>
/// Adapter between food content and schematics. The gates come from data/schematics.food.json (the story-schematics
/// catalogue format). Until that branch merges, a schematic is known when its pre-merge item (items.json <c>sch_*</c>
/// Blueprint, studied like any blueprint) is in <see cref="ColonyState.KnownBlueprints"/>. After the merge
/// <see cref="Has"/> becomes <c>Schematics.Has(sim, id)</c>, the gates move to <c>Schematics.RecipeUnlocked</c> /
/// <c>LevelUnlocked</c> (the file is read by its catalogue loader), and the <c>sch_*</c> items leave items.json.
/// </summary>
public static class FoodSchematics
{
    public static bool Has(ColonySim sim, string schematicId) => sim.State.KnownBlueprints.Contains(schematicId);

    public static bool RecipeUnlocked(ColonySim sim, string recipeId) =>
        sim.Data.Food?.GateOfRecipe(recipeId) is not { } gate || Has(sim, gate);

    public static bool LevelUnlocked(ColonySim sim, string buildingId, int level) =>
        sim.Data.Food?.GateOfBuildingLevel(buildingId, level) is not { } gate || Has(sim, gate);

    /// <summary>The schematic name a locked recipe or building level waits on (for messages and the UI), or null.</summary>
    public static string? MissingFor(ColonySim sim, string? recipeId = null, string? buildingId = null, int level = 1)
    {
        var gate = recipeId is not null ? sim.Data.Food?.GateOfRecipe(recipeId) : buildingId is not null ? sim.Data.Food?.GateOfBuildingLevel(buildingId, level) : null;
        if (gate is null || Has(sim, gate)) return null;
        return sim.Data.Food?.Schematic(gate)?.Name ?? gate;
    }

    internal static CommandResult? RecipeGate(ColonySim sim, RecipeDef recipe) =>
        MissingFor(sim, recipeId: recipe.Id) is { } name
            ? CommandResult.Fail(CommandError.RecipeLocked, $"{recipe.Name} needs the {name} schematic.")
            : null;

    internal static CommandResult? LevelGate(ColonySim sim, BuildingDef def, int level) =>
        MissingFor(sim, buildingId: def.Id, level: level) is { } name
            ? CommandResult.Fail(CommandError.SchematicNeeded, level == 1 ? $"{def.Name} needs the {name} schematic." : $"{def.Name} level {level} needs the {name} schematic.")
            : null;

    /// <summary>What studying a schematic item unlocked, by name (for the study message).</summary>
    internal static IEnumerable<string> Unlocks(ColonySim sim, string schematicId) =>
        sim.Data.Food?.Schematic(schematicId)?.Unlocks.Select(u => u.Kind switch
        {
            "Recipe" => sim.Data.Recipes.GetValueOrDefault(u.Id)?.Name ?? u.Id,
            "Building" => sim.Data.Buildings.GetValueOrDefault(u.Id)?.Name ?? u.Id,
            _ => $"{sim.Data.Buildings.GetValueOrDefault(u.Id)?.Name ?? u.Id} L{u.Level}",
        }) ?? [];
}
