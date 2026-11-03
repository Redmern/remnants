namespace Remnants.Core.Scavenge;

/// <summary>
/// What a unit whose turn it is can still do, for the UI's auto end turn. Only looks; nothing here is a rule.
/// </summary>
public static class TurnAdvice
{
    /// <summary>
    /// Whether the unit has an affordable, useful order left: a move, a stance or lookout, a blow at a hostile its side sees
    /// in reach, an item it carries, a card it can play (hand or slot), extracting, or picking up from a pile in reach. A
    /// plan (free to declare, paid next turn) does not count: a unit with nothing else may still plan, but its turn is as
    /// good as over. Hacks, drones and salvage are not looked at: they need AP, and a unit with AP can always move or
    /// take a stance in practice.
    /// </summary>
    public static bool CanStillAct(ScavengeSim sim, Unit unit)
    {
        if (!unit.IsActive || !unit.OnMap) return false;
        var ap = unit.Ap;
        var fog = sim.Data.Rules.Fog;
        if (sim.State.Map.IsExit(unit.Pos) || ParkedVehicles.Beside(sim.State, unit) is not null) return true;
        if (ap >= sim.Data.Rules.Turn.PickUpApCost && sim.State.Caches.Any(c => c.Items.Count > 0 && c.Pos.Chebyshev(unit.Pos) <= 1)) return true;
        if (CanPlayACard(sim, unit)) return true;
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

    /// <summary>A card from the party's hand or the unit's slots that Core would take now (a reaction: on any seen enemy plan).</summary>
    private static bool CanPlayACard(ScavengeSim sim, Unit unit)
    {
        if (unit.PlayerId is not { } pid || SquadDecks.Of(sim.State, pid) is not { Hidden: false } deck) return false;
        var planners = sim.State.Units.Where(u => u.Plan is not null && unit.HostileTo(u)).Select(u => (int?)u.Id).DefaultIfEmpty(null).ToList();
        bool Playable(string id, bool slot) =>
            planners.Any(t => CardPlay.Check(sim, unit, new PlayCard(pid, unit.Id, id, t, FromSlot: slot)).Ok);
        return deck.Hand.Distinct().Any(id => Playable(id, false)) || unit.SlotCards.Where(id => !unit.SlotSpent.Contains(id)).Any(id => Playable(id, true));
    }
}
