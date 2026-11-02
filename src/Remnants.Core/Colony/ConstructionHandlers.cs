namespace Remnants.Core.Colony;

/// <summary>Commands on construction sites (see <see cref="ConstructionSystem"/>).</summary>
internal static class ConstructionHandlers
{
    public static CommandResult AssignBuilder(ColonySim sim, AssignBuilder c)
    {
        var survivor = sim.State.FindSurvivor(c.SurvivorId);
        if (survivor is null) return CommandResult.Fail(CommandError.UnknownSurvivor, $"No survivor {c.SurvivorId}.");
        if (sim.State.Construction.Find(c.BuildingId) is not { } site)
            return CommandResult.Fail(CommandError.NotUnderConstruction, $"Nothing is being built at {c.BuildingId}.");
        var name = Name(sim, site);
        if (site.Builders.Contains(survivor.Id)) return CommandResult.Success($"{survivor.Name} already builds the {name}.", c.BuildingId);
        if (ConstructionSystem.CanBuild(sim, survivor) is { Ok: false } no) return no;
        if (site.Builders.Count >= ConstructionSystem.MaxBuilders(sim.Data, site))
            return CommandResult.Fail(CommandError.NoFreeSlot, $"The {name} site has room for no more builders.");

        // Moving from another site keeps the count; a first site is one more activity.
        if (!sim.State.Construction.Sites.Any(o => o.Builders.Contains(survivor.Id))
            && Activities.CanTakeOn(sim.State, sim.Data, survivor) is { Ok: false } busy) return busy;
        ConstructionSystem.Release(sim.State, survivor.Id);
        Raids.Guards.Release(sim.State, survivor.Id);
        site.Builders.Add(survivor.Id);
        return CommandResult.Success($"{survivor.Name} now builds the {name}.", c.BuildingId);
    }

    public static CommandResult Cancel(ColonySim sim, CancelConstruction c)
    {
        if (sim.State.Construction.Find(c.BuildingId) is not { } site)
            return CommandResult.Fail(CommandError.NotUnderConstruction, $"Nothing is being built at {c.BuildingId}.");
        var back = ConstructionSystem.Refund(sim, site);
        sim.State.Construction.Sites.Remove(site);
        var what = site.IsUpgrade ? $"the {Name(sim, site)} upgrade" : $"the {Name(sim, site)}";
        var got = back.Count == 0 ? "nothing came back" : string.Join(", ", back.Select(kv => $"{kv.Value:0} {kv.Key}")) + " came back";
        sim.State.Log(new(sim.State.Tick, ColonyEventType.SiteCancelled, $"Called off {what}: {got}.", BuildingId: c.BuildingId));
        return CommandResult.Success($"Called off {what}: {got}.", c.BuildingId);
    }

    public static CommandResult Pause(ColonySim sim, PauseConstruction c)
    {
        if (sim.State.Construction.Find(c.BuildingId) is not { } site)
            return CommandResult.Fail(CommandError.NotUnderConstruction, $"Nothing is being built at {c.BuildingId}.");
        site.Paused = c.Paused;
        if (c.Paused) site.Helpers.Clear();
        return CommandResult.Success(c.Paused ? $"Work on the {Name(sim, site)} stops." : $"Work on the {Name(sim, site)} goes on.", c.BuildingId);
    }

    public static CommandResult SetPriority(ColonySim sim, SetConstructionPriority c)
    {
        if (sim.State.Construction.Find(c.BuildingId) is not { } site)
            return CommandResult.Fail(CommandError.NotUnderConstruction, $"Nothing is being built at {c.BuildingId}.");
        site.Priority = c.Priority;
        return CommandResult.Success($"The {Name(sim, site)} site is now {c.Priority.ToString().ToLowerInvariant()} priority.", c.BuildingId);
    }

    public static CommandResult SetNightShift(ColonySim sim, SetSiteNightShift c)
    {
        if (sim.State.Construction.Find(c.BuildingId) is not { } site)
            return CommandResult.Fail(CommandError.NotUnderConstruction, $"Nothing is being built at {c.BuildingId}.");
        if (c.On && !ConstructionSystem.IsLit(sim, site))
            return CommandResult.Fail(CommandError.NeedsLight, $"The {Name(sim, site)} site is dark at night: only an upgrade of a lit building can be built at night.");
        site.NightShift = c.On;
        return CommandResult.Success(c.On ? $"Builders at the {Name(sim, site)} work into the night." : $"Builders at the {Name(sim, site)} stop at dusk.", c.BuildingId);
    }

    private static string Name(ColonySim sim, ConstructionSite site) => sim.Data.Buildings[site.DefId].Name;
}
