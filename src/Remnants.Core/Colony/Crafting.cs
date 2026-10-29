using Remnants.Core.Data;

namespace Remnants.Core.Colony;

/// <summary>
/// Production buildings work their chosen recipe: a batch's inputs are paid when it starts, workers add their
/// job output as effort each tick (and train), and a finished batch yields its item or resource.
/// A building with no recipe, no workers or unaffordable inputs is idle. Full crafting tiers come in M7.
/// </summary>
internal static class CraftingSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        foreach (var building in state.Buildings.Where(b => b.RecipeId is not null))
        {
            var def = data.Buildings[building.DefId];
            if (def.Job is null || def.Recipes.Find(r => r.Id == building.RecipeId) is not { } recipe) continue;
            var workers = state.WorkersAt(building.Id).OrderBy(w => w.Id).ToList();
            if (workers.Count == 0) continue;
            if (!building.CraftPaid)
            {
                if (!state.Stockpile.CanAfford(recipe.Inputs)) continue;
                state.Stockpile.Spend(recipe.Inputs);
                building.CraftPaid = true;
            }

            var job = data.Jobs[def.Job];
            foreach (var w in workers)
            {
                building.CraftProgress += JobOutput.PerTick(def, building.Level, job, w, data.Rules.Output);
                Training.TrainTick(job, w, data.Rules.Training);
            }
            if (building.CraftProgress + 1e-9 < recipe.Work) continue;

            if (recipe.Item is { } item) state.Inventory.Add(item, (int)Math.Round(recipe.Amount));
            else state.Stockpile.Add(recipe.Resource!, recipe.Amount);
            building.CraftProgress = 0;
            building.CraftPaid = false;
            state.Log(new(state.Tick, ColonyEventType.Crafted, $"{def.Name} made {recipe.Amount:0.##} {recipe.Name}.", BuildingId: building.Id));
        }
    }

    /// <summary>Effort per tick the building's current workers add (for an ETA in the UI).</summary>
    public static double EffortPerTick(ColonyState state, GameData data, Building building)
    {
        var def = data.Buildings[building.DefId];
        if (def.Job is null) return 0;
        var job = data.Jobs[def.Job];
        return state.WorkersAt(building.Id).Sum(w => JobOutput.PerTick(def, building.Level, job, w, data.Rules.Output));
    }
}

internal static class CraftingHandlers
{
    public static CommandResult SetRecipe(ColonySim sim, SetRecipe c)
    {
        var state = sim.State;
        var building = state.FindBuilding(c.BuildingId);
        if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        RecipeDef? recipe = null;
        if (c.RecipeId is not null)
        {
            recipe = def.Recipes.Find(r => r.Id == c.RecipeId);
            if (recipe is null) return CommandResult.Fail(CommandError.UnknownRecipe, $"{def.Name} has no recipe '{c.RecipeId}'.");
            if (recipe.MinLevel > building.Level)
                return CommandResult.Fail(CommandError.RecipeLevelTooLow, $"{recipe.Name} needs {def.Name} level {recipe.MinLevel}.");
            if (recipe.Item is not null && sim.Scavenge is null) return ExpeditionHandlers.NoData();
        }
        if (building.RecipeId == c.RecipeId) return CommandResult.Success($"{def.Name} is already on that.", building.Id);

        // Switching drops the batch in progress; its paid inputs go back to the stockpile.
        if (building.CraftPaid && def.Recipes.Find(r => r.Id == building.RecipeId) is { } old)
            foreach (var (res, amount) in old.Inputs) state.Stockpile.Add(res, amount);
        building.RecipeId = c.RecipeId;
        building.CraftProgress = 0;
        building.CraftPaid = false;
        var message = recipe is null ? $"{def.Name} stopped working." : $"{def.Name} now makes {recipe.Name}.";
        state.Log(new(state.Tick, ColonyEventType.RecipeChanged, message, BuildingId: building.Id));
        return CommandResult.Success(message, building.Id);
    }
}
