using Remnants.Core.Data;

namespace Remnants.Core.Colony;

internal static class CommandHandlers
{
    public static CommandResult AssignJob(ColonySim sim, AssignJob c)
    {
        var survivor = sim.State.FindSurvivor(c.SurvivorId);
        if (survivor is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        var building = sim.State.FindBuilding(c.BuildingId);
        if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        if (def.Job is null) return CommandResult.Fail(CommandError.NoJobAtBuilding, $"{def.Name} has no job.");
        if (survivor.Away) return CommandResult.Fail(CommandError.SurvivorAway, $"{survivor.Name} is away on an expedition.");
        if (survivor.PatientAtBuildingId is not null)
            return CommandResult.Fail(CommandError.IsPatient, $"{survivor.Name} is a patient; discharge them first.");
        if (Schematics.Research.IsResearching(sim.State, survivor.Id))
            return CommandResult.Fail(CommandError.IsResearching, $"{survivor.Name} is researching at the Town Hall; stop the research first.");
        if (survivor.IsInfant) return CommandResult.Fail(CommandError.TooYoung, $"{survivor.Name} is a baby.");
        if (survivor.IsChild && !sim.Data.Jobs[def.Job].ChildrenAllowed)
            return CommandResult.Fail(CommandError.NotForChildren, $"{sim.Data.Jobs[def.Job].Name} is too hard for a child.");
        if (Raids.RaidSystem.IsRubble(sim, building))
            return CommandResult.Fail(CommandError.Wrecked, $"{def.Name} was wrecked in a raid; repair it first.");
        if (survivor.Jobs.Contains(building.Id))
            return CommandResult.Success($"{survivor.Name} already works at {def.Name}.", building.Id);
        if (sim.FreeSlots(building) <= 0)
            return CommandResult.Fail(CommandError.NoFreeSlot, $"{def.Name} has no free worker slot.");
        if (Activities.CanTakeOn(sim.State, sim.Data, survivor) is { Ok: false } busy) return busy;

        survivor.Jobs.Add(building.Id);
        Raids.Guards.Release(sim.State, survivor.Id);
        return CommandResult.Success($"{survivor.Name} now works at {def.Name}.", building.Id);
    }

    public static CommandResult Unassign(ColonySim sim, Unassign c)
    {
        var survivor = sim.State.FindSurvivor(c.SurvivorId);
        if (survivor is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (c.BuildingId is { } only)
        {
            if (survivor.Jobs.Remove(only)) return CommandResult.Success($"{survivor.Name} left their job there.", only);
            if (sim.State.Construction.Find(only) is { } site && site.Builders.Remove(survivor.Id))
                return CommandResult.Success($"{survivor.Name} stopped building there.", only);
            return CommandResult.Fail(CommandError.NotAssigned, $"{survivor.Name} neither works nor builds there.");
        }
        if (survivor.AssignedBuildingId is null && sim.State.Construction.Sites.Any(s => s.Builders.Contains(survivor.Id)))
        {
            ConstructionSystem.Release(sim.State, survivor.Id);
            return CommandResult.Success($"{survivor.Name} stopped building and is now idle.");
        }
        if (survivor.AssignedBuildingId is null)
            return CommandResult.Fail(CommandError.NotAssigned, $"{survivor.Name} has no job.");
        survivor.AssignedBuildingId = null;
        return CommandResult.Success($"{survivor.Name} is now idle.");
    }

    public static CommandResult Construct(ColonySim sim, Construct c)
    {
        if (!sim.Data.Buildings.TryGetValue(c.BuildingDefId, out var def))
            return CommandResult.Fail(CommandError.UnknownBuildingType, $"No building type '{c.BuildingDefId}'.");
        if (def.Unique && (sim.State.Buildings.Any(b => b.DefId == def.Id) || sim.State.Construction.Sites.Any(s => s.DefId == def.Id)))
            return CommandResult.Fail(CommandError.AlreadyExists, $"Only one {def.Name} allowed.");
        if (!IsTownHall(sim, def) && sim.TownHallLevel < 1)
            return CommandResult.Fail(CommandError.NoTownHall, "Build a Town Hall first.");
        if (!IsTownHall(sim, def) && sim.TownHallLevel < def.MinTownHallLevel)
            return CommandResult.Fail(CommandError.TownHallLevelTooLow,
                $"{def.Name} needs Town Hall level {def.MinTownHallLevel} (have {sim.TownHallLevel}).");
        if (!Schematics.Schematics.LevelUnlocked(sim, def.Id, 1))
            return CommandResult.Fail(CommandError.SchematicNeeded, $"{def.Name} needs the {SchematicName(sim, def.Id, 1)} schematic.");

        int plot;
        if (c.Plot is { } wanted)
        {
            if (wanted < 0 || wanted >= sim.Data.Rules.Layout.PlotCount)
                return CommandResult.Fail(CommandError.InvalidPlot, $"Plot {wanted} is outside the town.");
            if (!sim.IsPlotFree(wanted))
                return CommandResult.Fail(CommandError.PlotOccupied, $"Plot {wanted} is already built on.");
            plot = wanted;
        }
        else if (sim.FreePlots().FirstOrDefault(-1) is var first and >= 0) plot = first;
        else return CommandResult.Fail(CommandError.NoFreePlot, "No free plot left.");

        var cost = def.Level(1).Cost;
        if (!sim.State.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {def.Name}: {Describe(sim.State.Stockpile.Shortfall(cost))}.");

        sim.State.Stockpile.Spend(cost);
        var id = sim.State.NextBuildingId++;
        return ConstructionSystem.Place(sim, def, 1, id, plot) is null
            ? CommandResult.Success($"Built {def.Name}.", id)
            : CommandResult.Success($"Started building {def.Name}: assign builders to the site.", id);
    }

    public static CommandResult Upgrade(ColonySim sim, Upgrade c)
    {
        var building = sim.State.FindBuilding(c.BuildingId);
        if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        var next = building.Level + 1;
        if (sim.State.Construction.Find(building.Id) is { } running)
            return CommandResult.Fail(CommandError.UnderConstruction, $"{def.Name} is already being upgraded to level {running.Level}.");
        if (next > def.MaxLevel)
            return CommandResult.Fail(CommandError.MaxLevel, $"{def.Name} is already at max level {def.MaxLevel}.");
        if (next > sim.MaxUpgradeLevel(def))
            return CommandResult.Fail(CommandError.TownHallLevelTooLow,
                $"{def.Name} level {next} needs Town Hall level {next} (have {sim.TownHallLevel}).");
        if (!Schematics.Schematics.LevelUnlocked(sim, def.Id, next))
            return CommandResult.Fail(CommandError.SchematicNeeded, $"{def.Name} level {next} needs the {SchematicName(sim, def.Id, next)} schematic.");

        var cost = def.Level(next).Cost;
        if (!sim.State.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {def.Name} level {next}: {Describe(sim.State.Stockpile.Shortfall(cost))}.");

        sim.State.Stockpile.Spend(cost);
        return ConstructionSystem.Place(sim, def, next, building.Id, building.Plot) is null
            ? CommandResult.Success($"{def.Name} reached level {next}.", building.Id)
            : CommandResult.Success($"Started upgrading {def.Name} to level {next}; it keeps working meanwhile.", building.Id);
    }

    private static string SchematicName(ColonySim sim, string buildingId, int level) =>
        sim.Data.Schematics.All[sim.Data.Schematics.GateOfBuildingLevel(buildingId, level)!].Name;

    internal static bool IsTownHall(ColonySim sim, BuildingDef def) => def.Id == sim.Data.Rules.TownHallBuildingId;

    internal static string Describe(Dictionary<string, double> shortfall) =>
        "need " + string.Join(", ", shortfall.Select(kv => $"{Math.Ceiling(kv.Value)} more {kv.Key}"));
}
