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
        if (survivor.IsInfant) return CommandResult.Fail(CommandError.TooYoung, $"{survivor.Name} is a baby.");
        if (StudySystem.ClassOf(sim.State, survivor.Id) is not null)
            return CommandResult.Fail(CommandError.InClass, $"{survivor.Name} is in class; end the study first.");
        if (survivor.IsChild && !sim.Data.Jobs[def.Job].ChildrenAllowed)
            return CommandResult.Fail(CommandError.NotForChildren, $"{sim.Data.Jobs[def.Job].Name} is too hard for a child.");
        if (survivor.AssignedBuildingId == building.Id)
            return CommandResult.Success($"{survivor.Name} already works at {def.Name}.", building.Id);
        if (sim.FreeSlots(building) <= 0)
            return CommandResult.Fail(CommandError.NoFreeSlot, $"{def.Name} has no free worker slot.");

        survivor.AssignedBuildingId = building.Id;
        return CommandResult.Success($"{survivor.Name} now works at {def.Name}.", building.Id);
    }

    public static CommandResult Unassign(ColonySim sim, Unassign c)
    {
        var survivor = sim.State.FindSurvivor(c.SurvivorId);
        if (survivor is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (survivor.AssignedBuildingId is null)
            return CommandResult.Fail(CommandError.NotAssigned, $"{survivor.Name} has no job.");
        survivor.AssignedBuildingId = null;
        return CommandResult.Success($"{survivor.Name} is now idle.");
    }

    public static CommandResult Construct(ColonySim sim, Construct c)
    {
        if (!sim.Data.Buildings.TryGetValue(c.BuildingDefId, out var def))
            return CommandResult.Fail(CommandError.UnknownBuildingType, $"No building type '{c.BuildingDefId}'.");
        if (def.Unique && sim.State.Buildings.Any(b => b.DefId == def.Id))
            return CommandResult.Fail(CommandError.AlreadyExists, $"Only one {def.Name} allowed.");
        if (!IsTownHall(sim, def) && sim.TownHallLevel < 1)
            return CommandResult.Fail(CommandError.NoTownHall, "Build a Town Hall first.");
        if (!IsTownHall(sim, def) && sim.TownHallLevel < def.MinTownHallLevel)
            return CommandResult.Fail(CommandError.TownHallLevelTooLow,
                $"{def.Name} needs Town Hall level {def.MinTownHallLevel} (have {sim.TownHallLevel}).");
        if (FoodSchematics.LevelGate(sim, def, 1) is { } gate) return gate;

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
        var building = new Building { Id = sim.State.NextBuildingId++, DefId = def.Id, Level = 1, Plot = plot };
        sim.State.Buildings.Add(building);
        Harvesting.Planted(sim.State, sim.Data, building);
        sim.State.Log(new(sim.State.Tick, ColonyEventType.Constructed, $"Built {def.Name}.", BuildingId: building.Id));
        return CommandResult.Success($"Built {def.Name}.", building.Id);
    }

    public static CommandResult Upgrade(ColonySim sim, Upgrade c)
    {
        var building = sim.State.FindBuilding(c.BuildingId);
        if (building is null) return CommandResult.Fail(CommandError.UnknownBuilding, $"No building {c.BuildingId}.");
        var def = sim.Data.Buildings[building.DefId];
        var next = building.Level + 1;
        if (next > def.MaxLevel)
            return CommandResult.Fail(CommandError.MaxLevel, $"{def.Name} is already at max level {def.MaxLevel}.");
        if (next > sim.MaxUpgradeLevel(def))
            return CommandResult.Fail(CommandError.TownHallLevelTooLow,
                $"{def.Name} level {next} needs Town Hall level {next} (have {sim.TownHallLevel}).");
        if (FoodSchematics.LevelGate(sim, def, next) is { } gate) return gate;

        var cost = def.Level(next).Cost;
        if (!sim.State.Stockpile.CanAfford(cost))
            return CommandResult.Fail(CommandError.CannotAfford, $"Cannot afford {def.Name} level {next}: {Describe(sim.State.Stockpile.Shortfall(cost))}.");

        sim.State.Stockpile.Spend(cost);
        building.Level = next;
        sim.State.Log(new(sim.State.Tick, ColonyEventType.Upgraded, $"{def.Name} reached level {next}.", BuildingId: building.Id));
        return CommandResult.Success($"{def.Name} reached level {next}.", building.Id);
    }

    internal static bool IsTownHall(ColonySim sim, BuildingDef def) => def.Id == sim.Data.Rules.TownHallBuildingId;

    internal static string Describe(Dictionary<string, double> shortfall) =>
        "need " + string.Join(", ", shortfall.Select(kv => $"{Math.Ceiling(kv.Value)} more {kv.Key}"));
}
