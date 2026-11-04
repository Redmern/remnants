namespace Remnants.Core.Scavenge;

/// <summary>
/// What a unit whose turn it is can still do, for the UI's auto end turn. Only looks; nothing here is a rule.
/// </summary>
public static class TurnAdvice
{
    /// <summary>
    /// Whether the unit has an affordable, useful order left: a move, a stance or lookout, a blow at a hostile its side sees
    /// in reach, an item it carries, a card it can play (hand or slot), extracting, picking up from a pile in reach, a hack,
    /// a drone order or salvage. Orders whose rules are not simple to restate are tried on a copy of the run (<see cref="Accepts"/>), so Core itself decides.
    /// </summary>
    public static bool CanStillAct(ScavengeSim sim, Unit unit)
    {
        if (!unit.IsActive || !unit.OnMap) return false;
        var ap = unit.Ap;
        var fog = sim.Data.Rules.Fog;
        if (sim.State.Map.IsExit(unit.Pos) || ParkedVehicles.Beside(sim.State, unit) is not null) return true;
        if (ap >= sim.Data.Rules.Turn.PickUpApCost && sim.State.Caches.Any(c => c.Items.Count > 0 && c.Pos.Chebyshev(unit.Pos) <= 1)) return true;
        if (CanPlayACard(sim, unit)) return true;
        if (CanHackOrSalvage(sim, unit) || CanUseDrone(sim, unit)) return true;
        if (ap > 0 && sim.Reachable(unit).Values.Any(cost => cost <= ap)) return true;
        if (unit.Stance == Stance.None && (fog.Brace.ApCost <= ap || Overwatches.OnArc(sim, unit, unit.PlayerId ?? 0) is not null)) return true;
        if (!unit.Lookout && fog.Lookout.ApCost <= ap) return true;
        if (unit.Pack.Any(id => sim.Data.Items.TryGetValue(id, out var item) && item.Use is { } use && FieldMedicine.ApCost(unit, use) <= ap))
            return true;
        var state = sim.State;
        foreach (var action in sim.Profile(unit).Actions)
        {
            var reach = TileStrikes.Reach(sim, unit, action);
            if (state.Units.Any(t => t.IsActive && t.OnMap && unit.HostileTo(t) && reach.Contains(t.Pos) && Drones.CanStrike(action, t)
                                     && Detection.SideSees(sim.Data, state, unit.Side, t)
                                     && Combat.ApCost(action, unit.Pos, t.Pos) - (TileStrikes.FreeCounter(sim, unit, [t.Pos]) ? TileStrikes.CounterWaiver(sim, unit) : 0) <= ap))
                return true;
        }
        return false;
    }

    /// <summary>A card from the party's hand or the unit's slots that Core would take now (a reaction: on any seen enemy's readied strike).</summary>
    private static bool CanPlayACard(ScavengeSim sim, Unit unit)
    {
        if (unit.PlayerId is not { } pid || SquadDecks.Of(sim.State, pid) is not { Hidden: false } deck) return false;
        var planners = sim.State.Units.Where(u => TileStrikes.Readied(sim.State, u) && unit.HostileTo(u)).Select(u => (int?)u.Id).DefaultIfEmpty(null).ToList();
        bool Playable(string id, bool slot) =>
            planners.Any(t => CardPlay.Check(sim, unit, new PlayCard(pid, unit.Id, id, t, FromSlot: slot)).Ok);
        return deck.Hand.Distinct().Any(id => Playable(id, false)) || unit.SlotCards.Where(id => !unit.SlotSpent.Contains(id)).Any(id => Playable(id, true));
    }

    /// <summary>Whether Core would take <paramref name="command"/> now: it is run on a copy of the run, never on the run.</summary>
    private static bool Accepts(ScavengeSim sim, IScavengeCommand command) =>
        ScavengeSave.Load(sim.Data, ScavengeSave.Save(sim.State)).Execute(command).Ok;

    private const int HackLookRange = 4;

    private static bool CanHackOrSalvage(ScavengeSim sim, Unit unit)
    {
        var state = sim.State;
        var pid = unit.PlayerId ?? 0;
        foreach (var node in state.SalvageNodes.Where(n => !n.Depleted && n.Pos.Chebyshev(unit.Pos) <= 1))
            if (sim.Data.Salvage.Node(node.Kind) is { } def && def.ApCost <= unit.Ap && Accepts(sim, new Salvage(pid, unit.Id, node.Id))) return true;
        if (Hacking.Tool(sim.Data, unit)?.HackTool is null || Hacking.ApCost(sim.Data, unit) > unit.Ap) return false;
        foreach (var device in state.Devices.Where(d => d.Pos.Chebyshev(unit.Pos) <= HackLookRange))
            if (Accepts(sim, new Hack(pid, unit.Id, device.Id))) return true;
        return state.Units.Where(u => u.IsActive && u.Kind == UnitKind.Enemy && unit.HostileTo(u) && u.Pos.Chebyshev(unit.Pos) <= HackLookRange)
            .Any(u => Accepts(sim, new Hack(pid, unit.Id, TargetId: u.Id)));
    }

    /// <summary>Whether the unit can give its drone an order now: deploy it, or (out) fly or recall it.</summary>
    public static bool CanUseDrone(ScavengeSim sim, Unit unit)
    {
        var rules = sim.Data.Rules.Drone;
        var pid = unit.PlayerId ?? 0;
        if (Drones.DroneOf(sim.State, unit) is null)
            return rules.DeployApCost <= unit.Ap && Pos.Neighbours8.Select(d => unit.Pos + d).Where(sim.State.Map.InBounds)
                .Any(at => Accepts(sim, new DeployDrone(pid, unit.Id, at)));
        if (rules.RecallApCost <= unit.Ap && Accepts(sim, new RecallDrone(pid, unit.Id))) return true;
        return rules.FlyApCost <= unit.Ap && Drones.FlightTargets(sim, unit).Take(1).Any(to => Accepts(sim, new FlyDrone(pid, unit.Id, to)));
    }
}
