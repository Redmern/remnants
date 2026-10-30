using Remnants.Core.Data;

namespace Remnants.Core.Colony;

/// <summary>
/// Farms, greenhouses and hunting lodges (data/food.json <c>harvests</c>). Each works one harvest (picked with
/// <see cref="SetHarvest"/>; the building's first one by default). Workers on shift add their job output, times the
/// harvest's yield and (outdoors, for weather-sensitive harvests) <see cref="Crops.Multiplier"/>, to the standing crop
/// of each output item by its share; once the harvest is ready (<c>growDays</c> after it was picked) whole items go
/// into the pantry as they add up, with their byproducts (hides → cloth) into the stockpile. Workers train as at any job.
/// </summary>
internal static class HarvestSystem
{
    public static void Run(ColonyState state, GameData data)
    {
        if (data.Food is null) return;
        foreach (var building in state.Buildings)
        {
            if (Harvesting.Current(data, building) is not { } harvest) continue;
            var def = data.Buildings[building.DefId];
            var job = data.Jobs[def.Job!];
            var factor = harvest.Yield * Harvesting.WeatherMultiplier(state, data, building, harvest);
            foreach (var (worker, shift) in Schedule.Crew(state, data, building))
            {
                var effort = JobOutput.PerTick(def, building.Level, job, worker, data.Rules.Output, data) * shift * factor;
                foreach (var (item, share) in harvest.Outputs)
                    building.HarvestProgress[item] = building.HarvestProgress.GetValueOrDefault(item) + effort * share;
                Training.Work(state, data, job, worker);
            }
            if (state.Tick < building.HarvestReadyTick) continue;
            foreach (var (item, progress) in building.HarvestProgress.ToList())
            {
                var whole = (int)Math.Floor(progress + 1e-9);
                if (whole <= 0) continue;
                building.HarvestProgress[item] = Math.Max(0, progress - whole);
                Pantry.Add(state, data, item, whole);
                foreach (var (res, per) in harvest.Byproducts) state.Stockpile.Add(res, per * whole);
            }
        }
    }
}

/// <summary>Harvest queries for the UI, the bot and other systems.</summary>
public static class Harvesting
{
    /// <summary>The harvest a building works, or null when it does not harvest.</summary>
    public static HarvestDef? Current(GameData data, Building building)
    {
        if (data.Food is not { } food) return null;
        var options = food.HarvestsFor(building.DefId);
        if (options.Count == 0 || data.Buildings[building.DefId].Job is null) return null;
        if (building.Harvest is { } id && food.Harvests.TryGetValue(id, out var picked) && options.Contains(picked) && picked.MinLevel <= building.Level)
            return picked;
        return options[0];
    }

    /// <summary>What a building can be set to (empty when it does not harvest).</summary>
    public static IReadOnlyList<HarvestDef> Options(GameData data, string buildingDefId) =>
        data.Food?.HarvestsFor(buildingDefId) ?? [];

    /// <summary>The weather terms on this harvest here (none indoors, for weather-proof harvests, or without weather).</summary>
    public static IReadOnlyList<CropModifier> Modifiers(ColonyState state, GameData data, Building building, HarvestDef harvest) =>
        harvest.Weather && data.Buildings[building.DefId].Outdoor ? Crops.Modifiers(state, data) : [];

    public static double WeatherMultiplier(ColonyState state, GameData data, Building building, HarvestDef harvest) =>
        Crops.Multiplier(Modifiers(state, data, building, harvest));

    /// <summary>Food value one unit of effort yields on this harvest, with the weather now (for food-per-day estimates).</summary>
    public static double FoodValuePerEffort(ColonyState state, GameData data, Building building, HarvestDef harvest) =>
        harvest.Yield * WeatherMultiplier(state, data, building, harvest)
        * harvest.Outputs.Sum(kv => kv.Value * (data.Food?.Items.GetValueOrDefault(kv.Key)?.FoodValue ?? 0));

    /// <summary>Ticks until the harvest comes in (0 when it is already coming in).</summary>
    public static long TicksUntilReady(ColonyState state, Building building) => Math.Max(0, building.HarvestReadyTick - state.Tick);

    /// <summary>A building of a harvesting type was just built: its default harvest starts growing.</summary>
    internal static void Planted(ColonyState state, GameData data, Building building)
    {
        if (Current(data, building) is not { } harvest) return;
        building.HarvestReadyTick = state.Tick + (long)Math.Round(harvest.GrowDays * data.Rules.TicksPerDay);
    }
}

internal static class HarvestHandlers
{
    public static CommandResult Set(ColonySim sim, SetHarvest c)
    {
        var state = sim.State;
        var building = state.FindBuilding(c.BuildingId);
        if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        var options = Harvesting.Options(sim.Data, def.Id);
        if (options.FirstOrDefault(h => h.Id == c.HarvestId) is not { } harvest)
            return CommandResult.Fail(CommandError.UnknownHarvest,
                options.Count == 0 ? $"{def.Name} does not grow or hunt anything." : $"{def.Name} cannot work '{c.HarvestId}'.");
        if (harvest.MinLevel > building.Level)
            return CommandResult.Fail(CommandError.UnknownHarvest, $"{harvest.Name} needs {def.Name} level {harvest.MinLevel}.");
        if (Harvesting.Current(sim.Data, building)?.Id == harvest.Id)
            return CommandResult.Success($"{def.Name} is already on {harvest.Name}.", building.Id);

        building.Harvest = harvest.Id;
        building.HarvestProgress.Clear();
        building.HarvestReadyTick = state.Tick + (long)Math.Round(harvest.GrowDays * sim.Data.Rules.TicksPerDay);
        var ready = harvest.GrowDays > 0 ? $" (first harvest in {harvest.GrowDays:0.#} days)" : "";
        var message = $"{def.Name} now works {harvest.Name}{ready}.";
        state.Log(new(state.Tick, ColonyEventType.HarvestChanged, message, BuildingId: building.Id));
        return CommandResult.Success(message, building.Id);
    }
}
