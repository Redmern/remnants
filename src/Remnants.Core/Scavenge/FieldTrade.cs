using Remnants.Core.Trade;

namespace Remnants.Core.Scavenge;

/// <summary>
/// A trade between two allied players in a running mission (the multiplayer trade window, server side). Both offers are
/// items out of the party's packs; they swap atomically into the other party's packs (each item to the conscious
/// survivor with the most room) and go home to that player's colony on extraction, like any loot. No AP and no turn:
/// it is bartering over the radio while the fight goes on. Deterministic: the host calls it in its single-threaded order.
/// A reusable device keeps its charge: its spent charges (<see cref="Unit.ChargesUsed"/>) move with it, the most-spent
/// copy going first, so no trade recharges a device or drains the giver's other copies.
/// </summary>
public static class FieldTrade
{
    /// <summary>Why <paramref name="playerId"/> cannot put these items on the table, or null.</summary>
    public static string? CheckOffer(ScavengeSim sim, int playerId, IReadOnlyList<TradeGood> goods)
    {
        if (sim.State.FindParty(playerId) is not { Done: false }) return "Your party is out of the zone.";
        if (goods.Any(g => g.Kind != GoodKind.Item)) return "In the field you trade items from your packs.";
        var packs = Holders(sim, playerId).SelectMany(u => u.Pack).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        foreach (var g in TradeGoods.Normalize(goods))
            if (packs.GetValueOrDefault(g.Id) < g.Count)
                return $"Your party carries {packs.GetValueOrDefault(g.Id)} × {(sim.Data.Items.TryGetValue(g.Id, out var item) ? item.Name : g.Id)}.";
        return null;
    }

    /// <summary>Why the two cannot trade right now, or null: both in the mission and allied.</summary>
    public static string? CheckPair(ScavengeSim sim, int a, int b)
    {
        if (a == b) return "You cannot trade with yourself.";
        if (sim.State.FindParty(a) is not { Done: false } || sim.State.FindParty(b) is not { Done: false }) return "Both parties must still be in the zone.";
        return sim.State.Allied(a, b) ? null : "You can only trade with allies in the field.";
    }

    /// <summary>
    /// Swap <paramref name="aGives"/> (from <paramref name="a"/>'s packs) for <paramref name="bGives"/>: everything or
    /// nothing. Fails when either side no longer has its items, or the receiving party could not carry them.
    /// </summary>
    public static ScavengeResult Swap(ScavengeSim sim, int a, IReadOnlyList<TradeGood> aGives, int b, IReadOnlyList<TradeGood> bGives)
    {
        if (CheckPair(sim, a, b) is { } pair) return ScavengeResult.Fail(ScavengeError.InvalidTarget, pair);
        if ((CheckOffer(sim, a, aGives) ?? CheckOffer(sim, b, bGives)) is { } offer) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, offer);
        var aIds = TradeGoods.ItemIds(aGives);
        var bIds = TradeGoods.ItemIds(bGives);
        // Take both sides out first, so each party's room counts what it gives away.
        var aTaken = Take(sim, a, aIds);
        var bTaken = Take(sim, b, bIds);
        if (Place(sim, b, aIds) is not { } aPlaced || Place(sim, a, bIds) is not { } bPlaced)
        {
            foreach (var (unit, id, spent) in aTaken.Concat(bTaken)) Restore(unit, id, spent);
            return ScavengeResult.Fail(ScavengeError.CarryLimitExceeded, "The other party cannot carry all of that.");
        }
        // Each placed device takes the spent charges of the copy that left (matched by item id, in order).
        foreach (var (placed, taken) in new[] { (aPlaced, aTaken), (bPlaced, bTaken) })
        {
            var spentById = taken.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => new Queue<int>(g.Select(t => t.Spent)));
            foreach (var (unit, id) in placed) PutItem(unit, id, spentById[id].Dequeue());
        }
        var state = sim.State;
        var sides = new List<string> { state.FindParty(a)!.Side, state.FindParty(b)!.Side }.Distinct().ToList();
        string Names(List<string> ids) => ids.Count == 0 ? "nothing" : string.Join(", ", ids.Select(id => sim.Data.Items[id].Name));
        state.Log(new ScavengeEvent(state.Round, ScavengeEventType.ItemGiven,
            $"{state.PlayerName(a)} and {state.PlayerName(b)} trade: {Names(aIds)} for {Names(bIds)}.", Sides: sides));
        return ScavengeResult.Success("Trade done.");
    }

    private static IEnumerable<Unit> Holders(ScavengeSim sim, int playerId) =>
        sim.State.Units.Where(u => u.PlayerId == playerId && u.Kind == UnitKind.Survivor && u.OnMap)
            .OrderBy(u => u.Id);

    /// <summary>Takes the items out of the packs; a device takes the most-spent copy's charges with it (Spent).</summary>
    private static List<(Unit Unit, string Id, int Spent)> Take(ScavengeSim sim, int playerId, List<string> ids)
    {
        var taken = new List<(Unit, string, int)>();
        foreach (var id in ids)
        {
            var unit = Holders(sim, playerId).First(u => u.Pack.Contains(id));
            taken.Add((unit, id, TakeItem(sim, unit, id)));
        }
        return taken;
    }

    private static void Restore(Unit unit, string id, int spent) => PutItem(unit, id, spent);

    /// <summary>
    /// Takes one <paramref name="id"/> out of <paramref name="unit"/>'s pack and returns the charges spent on it: a device
    /// leaves with the most-spent copy's charges (up to one device's worth), so the giver's other copies keep theirs.
    /// Every hand-over (a field trade, a co-op Give) moves items with this and <see cref="PutItem"/>.
    /// </summary>
    internal static int TakeItem(ScavengeSim sim, Unit unit, string id)
    {
        unit.Pack.Remove(id);
        if (sim.Data.Items[id].Use is not { Charges: > 0 } use || unit.ChargesUsed.GetValueOrDefault(id) is not (var used and > 0)) return 0;
        var spent = Math.Min(used, use.Charges);
        if (used - spent > 0) unit.ChargesUsed[id] = used - spent;
        else unit.ChargesUsed.Remove(id);
        return spent;
    }

    /// <summary>Puts one <paramref name="id"/> in <paramref name="unit"/>'s pack with the charges spent on it (see <see cref="TakeItem"/>).</summary>
    internal static void PutItem(Unit unit, string id, int spent)
    {
        unit.Pack.Add(id);
        if (spent > 0) unit.ChargesUsed[id] = unit.ChargesUsed.GetValueOrDefault(id) + spent;
    }

    /// <summary>Where each item would go (the conscious survivor with the most room), or null if one does not fit.</summary>
    private static List<(Unit Unit, string Id)>? Place(ScavengeSim sim, int playerId, List<string> ids)
    {
        var room = Holders(sim, playerId).Where(u => u.IsActive)
            .ToDictionary(u => u, u => sim.Profile(u) is var p ? p.CarryCapacity - p.Load : 0);
        var placed = new List<(Unit, string)>();
        foreach (var id in ids.OrderByDescending(id => sim.Data.Items[id].Weight))
        {
            var weight = sim.Data.Items[id].Weight;
            if (room.Count == 0) return null;
            var best = room.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Id).First();
            if (best.Value + 1e-9 < weight) return null;
            room[best.Key] = best.Value - weight;
            placed.Add((best.Key, id));
        }
        return placed;
    }
}
