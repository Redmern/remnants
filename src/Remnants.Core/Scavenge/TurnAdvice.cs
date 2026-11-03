namespace Remnants.Core.Scavenge;

/// <summary>
/// What a unit whose turn it is can still do, for the UI's auto end turn. Only looks; nothing here is a rule.
/// </summary>
public static class TurnAdvice
{
    /// <summary>
    /// Whether the unit has an affordable, useful order left: a move, a stance or lookout, a blow at a hostile its side sees
    /// in reach, or an item it carries. A plan (free to declare, paid next turn) does not count: a unit with nothing else
    /// may still plan, but its turn is as good as over.
    /// </summary>
    public static bool CanStillAct(ScavengeSim sim, Unit unit)
    {
        if (!unit.IsActive || !unit.OnMap) return false;
        var ap = unit.Ap;
        var fog = sim.Data.Rules.Fog;
        if (ap > 0 && sim.Reachable(unit).Values.Any(cost => cost <= ap)) return true;
        if (unit.Stance == Stance.None && Math.Min(fog.Overwatch.ApCost, fog.Brace.ApCost) <= ap) return true;
        if (!unit.Lookout && fog.Lookout.ApCost <= ap) return true;
        if (unit.Pack.Any(id => sim.Data.Items.TryGetValue(id, out var item) && item.Use is { } use && FieldMedicine.ApCost(unit, use) <= ap))
            return true;
        var state = sim.State;
        foreach (var action in sim.Profile(unit).Actions)
        {
            var reach = TileStrikes.Reach(sim, unit, action);
            if (state.Units.Any(t => t.IsActive && t.OnMap && unit.HostileTo(t) && reach.Contains(t.Pos) && Drones.CanStrike(action, t)
                                     && Detection.SideSees(sim.Data, state, unit.Side, t)
                                     && (Combat.ApCost(action, unit.Pos, t.Pos) <= ap || TileStrikes.FreeCounter(sim, unit, [t.Pos]))))
                return true;
        }
        return false;
    }
}
