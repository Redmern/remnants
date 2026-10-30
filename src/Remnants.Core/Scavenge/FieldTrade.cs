using Remnants.Core.Trade;

namespace Remnants.Core.Scavenge;

/// <summary>
/// A trade between two allied players in a running mission (the multiplayer trade window, server side). Both offers are
/// items out of the party's packs; they swap atomically into the other party's packs (each item to the conscious
/// survivor with the most room) and go home to that player's colony on extraction, like any loot. No AP and no turn:
/// it is bartering over the radio while the fight goes on. Deterministic: the host calls it in its single-threaded order.
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
            foreach (var (unit, id) in aTaken) unit.Pack.Add(id);
            foreach (var (unit, id) in bTaken) unit.Pack.Add(id);
            return ScavengeResult.Fail(ScavengeError.CarryLimitExceeded, "The other party cannot carry all of that.");
        }
        foreach (var (unit, id) in aPlaced.Concat(bPlaced)) unit.Pack.Add(id);
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

    private static List<(Unit Unit, string Id)> Take(ScavengeSim sim, int playerId, List<string> ids)
    {
        var taken = new List<(Unit, string)>();
        foreach (var id in ids)
        {
            var unit = Holders(sim, playerId).First(u => u.Pack.Contains(id));
            unit.Pack.Remove(id);
            taken.Add((unit, id));
        }
        return taken;
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
