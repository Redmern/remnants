using Remnants.Core.Colony;
using Remnants.Core.Data;

namespace Remnants.Core.Trade;

/// <summary>
/// Food as trade goods. Food items are Pantry items: ordinary inventory items whose lots (quality, tainted, time left
/// before they spoil) are the item instance data. Trading takes fresh units only, soonest to spoil first (as the Pantry
/// does for cooking), records the lots taken in <see cref="ItemExtras.Lots"/>, and the receiving colony adds them back as
/// lots with the same quality, taint and time left. A food line without lots arrives as fresh standard food.
/// </summary>
public static class FoodGoods
{
    public static bool IsFood(GameData data, string itemId) => data.Food?.IsFood(itemId) == true;

    /// <summary>How many of this good the colony can offer now (food: fresh units only).</summary>
    public static double Have(ColonyState state, GameData data, TradeGood g) =>
        g.Kind == GoodKind.Resource ? state.Stockpile.Get(g.Id)
        : IsFood(data, g.Id) ? Pantry.Available(state, data, g.Id)
        : state.Inventory.Count(g.Id);

    /// <summary>
    /// The lots <paramref name="count"/> fresh units would come from (soonest to spoil first), without taking them: what
    /// an offer shows before it is escrowed.
    /// </summary>
    public static List<FoodLotExtra> Preview(ColonyState state, GameData data, string itemId, int count)
    {
        var lots = new List<FoodLotExtra>();
        if (!IsFood(data, itemId)) return lots;
        Pantry.Reconcile(state, data);
        var left = count;
        foreach (var (_, lot) in Pantry.Candidates(state, data, id => id == itemId))
        {
            if (left <= 0) break;
            if (lot.Spoiled || lot.Count <= 0) continue;
            var n = Math.Min(left, lot.Count);
            lots.Add(new FoodLotExtra(n, lot.Quality, lot.Tainted, lot.TicksLeft));
            left -= n;
        }
        return lots;
    }

    /// <summary>A food line with the lots it would come from, or the line as it is for anything else.</summary>
    public static TradeGood WithLots(ColonyState state, GameData data, TradeGood g) =>
        g.Kind == GoodKind.Item && IsFood(data, g.Id)
            ? g with { Extras = (g.Extras ?? new ItemExtras()) with { Lots = Preview(state, data, g.Id, g.Count) } }
            : g;

    /// <summary>Takes a food line out of the Pantry (fresh units, soonest to spoil first); returns it with the lots taken, or null when short.</summary>
    internal static TradeGood? Take(ColonyState state, GameData data, TradeGood g)
    {
        if (Pantry.TakeIngredient(state, data, g.Id, g.Count) is not { } taken) return null;
        var lots = taken.Select(t => new FoodLotExtra(t.Count, t.Quality, t.Tainted, t.TicksLeft)).ToList();
        return g with { Extras = (g.Extras ?? new ItemExtras()) with { Lots = lots } };
    }

    /// <summary>Food arrives: each lot back into the Pantry with its quality, taint and time left.</summary>
    internal static void Put(ColonyState state, GameData data, TradeGood g)
    {
        var def = data.Food!.Items[g.Id];
        if (g.Extras?.Lots is not { Count: > 0 } lots)
        {
            Pantry.Add(state, data, g.Id, g.Count);
            return;
        }
        var fullShelf = def.ShelfDays * data.Rules.TicksPerDay;
        foreach (var lot in lots)
            Pantry.Add(state, data, g.Id, lot.Count, lot.Quality, lot.Tainted,
                lot.TicksLeft is { } left && fullShelf > 0 ? Math.Max(1e-6, left / fullShelf) : 1);
    }
}
