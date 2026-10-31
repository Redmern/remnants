using Remnants.Core.Data;

namespace Remnants.Core.Colony;

/// <summary>
/// The colony's food: per food item the lots it is in (how old, how long it keeps, its quality, tainted or not), plus the
/// portions being eaten. The items themselves are ordinary inventory items (<see cref="ColonyState.Inventory"/>); the
/// lots are their instance data and follow the inventory: anything that adds or takes food items there (loot, trade,
/// crafting) is picked up by <see cref="Pantry.Reconcile"/> (new items become a fresh lot, missing ones leave the oldest
/// lots first).
/// </summary>
public sealed class FoodState
{
    public SortedDictionary<string, List<FoodLot>> Lots { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Opened units being eaten (less than one left each), at most one per item id.</summary>
    public List<OpenPortion> Open { get; set; } = new();
}

/// <summary>Some units of one food made or found together.</summary>
public sealed class FoodLot
{
    public int Count { get; set; }

    /// <summary>Ticks of ageing so far (a Cold Cellar slows it down).</summary>
    public double Age { get; set; }

    /// <summary>Ticks it keeps; 0 never spoils.</summary>
    public double Shelf { get; set; }

    /// <summary>Quality tier id (crude, standard, fine): cooked food was rolled from the cook's skill.</summary>
    public string Quality { get; set; } = FoodQuality.Standard;

    /// <summary>Husk-zone food, or made from it and not cooked well: eating it can make people sick.</summary>
    public bool Tainted { get; set; }

    public bool Spoiled => Shelf > 0 && Age + 1e-9 >= Shelf;

    /// <summary>Ticks until it spoils (null: never; 0: already spoiled).</summary>
    public double? TicksLeft => Shelf <= 0 ? null : Math.Max(0, Shelf - Age);
}

/// <summary>An opened unit of food, partly eaten.</summary>
public sealed class OpenPortion
{
    public string ItemId { get; set; } = "";
    public double Left { get; set; }
    public double Age { get; set; }
    public double Shelf { get; set; }
    public string Quality { get; set; } = FoodQuality.Standard;
    public bool Tainted { get; set; }

    public bool Spoiled => Shelf > 0 && Age + 1e-9 >= Shelf;
}

/// <summary>One line of the pantry view.</summary>
public sealed record PantryEntry(string ItemId, int Count, int Fresh, int Spoiled, double FoodValue, double? SpoilsInTicks, int Tainted,
    IReadOnlyDictionary<string, int> ByQuality);

/// <summary>What was taken out of the pantry, lot by lot.</summary>
public sealed record TakenFood(string ItemId, int Count, string Quality, bool Tainted, bool Spoiled, double? TicksLeft);

/// <summary>Queries and changes on the pantry. Everything here is deterministic and keeps lots and inventory counts in step.</summary>
public static class Pantry
{
    /// <summary>Cost and reward key that means "this much food value" (trader prices, contracts): see <c>Schematics.Payment</c>.</summary>
    public const string FoodKey = "food";

    /// <summary>Item food value given as (trader prices, rewards): canned food, which keeps.</summary>
    public const string ProvisionItem = "canned_food";

    /// <summary>Adds at least <paramref name="value"/> food value as <see cref="ProvisionItem"/>.</summary>
    public static void AddFoodValue(ColonyState state, GameData data, double value)
    {
        var each = data.Food?.Items.GetValueOrDefault(ProvisionItem)?.FoodValue ?? 1;
        Add(state, data, ProvisionItem, (int)Math.Ceiling(value / Math.Max(each, 1e-9) - 1e-9));
    }

    /// <summary>How fast produce and meat age now (1 = full speed; a Cold Cellar slows it).</summary>
    public static double CellarRate(ColonyState state, GameData data) => FoodSystem.CellarRate(state, data);

    /// <summary>Food value in stock that is fit to eat (not spoiled), in the old food units.</summary>
    public static double FoodValue(ColonySim sim) => FoodValue(sim.State, sim.Data);

    public static double FoodValue(ColonyState state, GameData data)
    {
        if (data.Food is not { } food) return 0;
        Reconcile(state, data);
        var total = 0.0;
        foreach (var (id, lots) in state.Food.Lots)
            if (food.Items.TryGetValue(id, out var def))
            {
                var fresh = 0;
                foreach (var l in lots)
                    if (!l.Spoiled) fresh += l.Count;
                total += def.FoodValue * fresh;
            }
        foreach (var p in state.Food.Open)
            if (!p.Spoiled && food.Items.TryGetValue(p.ItemId, out var def)) total += def.FoodValue * p.Left;
        return total;
    }

    /// <summary>Takes at least <paramref name="amount"/> food value (whole items, freshest-to-spoil first, never spoiled). False and nothing taken when short.</summary>
    public static bool TakeFoodValue(ColonySim sim, double amount) => TakeFoodValue(sim.State, sim.Data, amount) is not null;

    public static List<TakenFood>? TakeFoodValue(ColonyState state, GameData data, double amount)
    {
        if (amount <= 1e-9) return [];
        if (data.Food is null || FoodValue(state, data) + 1e-9 < amount) return null;
        var taken = new List<TakenFood>();
        var left = amount;
        foreach (var (id, lot) in Candidates(state, data, _ => true))
        {
            var fv = data.Food.Items[id].FoodValue;
            if (fv <= 0 || lot.Spoiled) continue;
            var n = Math.Min(lot.Count, (int)Math.Ceiling(left / fv - 1e-9));
            if (n <= 0) continue;
            taken.Add(TakeFromLot(state, id, lot, n));
            left -= n * fv;
            if (left <= 1e-9) break;
        }
        // Open portions cover what whole items did not (only when stock was mostly opened food).
        foreach (var p in state.Food.Open.Where(p => !p.Spoiled).ToList())
        {
            if (left <= 1e-9) break;
            var fv = data.Food.Items[p.ItemId].FoodValue;
            if (fv <= 0) continue;
            var units = Math.Min(p.Left, left / fv);
            p.Left -= units;
            left -= units * fv;
            if (p.Left <= 1e-9) state.Food.Open.Remove(p);
        }
        Prune(state);
        return taken;
    }

    /// <summary>Food comes into the pantry: into the inventory and as a new lot.</summary>
    public static void Add(ColonyState state, GameData data, string itemId, int count, string? quality = null, bool? tainted = null,
        double shelfMultiplier = 1)
    {
        if (count <= 0) return;
        Reconcile(state, data);
        state.Inventory.Add(itemId, count);
        var def = data.Food?.Items.GetValueOrDefault(itemId);
        if (def is null) return;
        var lot = NewLot(data, def, count, shelfMultiplier);
        lot.Quality = quality ?? FoodQuality.Standard;
        lot.Tainted = tainted ?? def.Tainted;
        var lots = LotsOf(state, itemId);
        if (lots.Find(l => l.Age == 0 && l.Quality == lot.Quality && l.Tainted == lot.Tainted && Math.Abs(l.Shelf - lot.Shelf) < 1e-9) is { } same)
            same.Count += count;
        else lots.Add(lot);
    }

    /// <summary>Brings the lots in step with the inventory counts (see <see cref="FoodState"/>).</summary>
    public static void Reconcile(ColonyState state, GameData data)
    {
        if (data.Food is not { } food) return;
        if (InStep(state, food))
        {
            Prune(state);
            return;
        }
        var ids = state.Food.Lots.Keys.Concat(state.Inventory.Counts.Keys.Where(food.IsFood)).Distinct().ToList();
        foreach (var id in ids)
        {
            var have = state.Inventory.Count(id);
            var lots = LotsOf(state, id);
            var inLots = lots.Sum(l => l.Count);
            if (inLots < have && food.Items.TryGetValue(id, out var def))
            {
                var lot = NewLot(data, def, have - inLots, 1);
                lot.Tainted = def.Tainted;
                lots.Add(lot);
            }
            else
                for (var extra = inLots - have; extra > 0;)
                {
                    var oldest = lots.OrderByDescending(l => l.Age).First();
                    var n = Math.Min(extra, oldest.Count);
                    oldest.Count -= n;
                    extra -= n;
                    if (oldest.Count == 0) lots.Remove(oldest);
                }
        }
        Prune(state);
    }

    /// <summary>Whether every lot list already sums to its inventory count and every food in the inventory has lots (the usual case).</summary>
    private static bool InStep(ColonyState state, FoodData food)
    {
        foreach (var (id, lots) in state.Food.Lots)
        {
            var inLots = 0;
            foreach (var l in lots) inLots += l.Count;
            if (inLots != state.Inventory.Count(id)) return false;
        }
        foreach (var (id, have) in state.Inventory.Counts)
            if (have > 0 && !state.Food.Lots.ContainsKey(id) && food.IsFood(id)) return false;
        return true;
    }

    /// <summary>Per food item in stock: counts, food value, how soon the next unit spoils, quality mix.</summary>
    public static List<PantryEntry> Entries(ColonySim sim)
    {
        var state = sim.State;
        var data = sim.Data;
        Reconcile(state, data);
        if (data.Food is not { } food) return [];
        return state.Food.Lots
            .Where(kv => kv.Value.Count > 0)
            .Select(kv =>
            {
                var lots = kv.Value;
                var fresh = lots.Where(l => !l.Spoiled).ToList();
                var next = fresh.Select(l => l.TicksLeft).Where(t => t is not null).Select(t => t!.Value).DefaultIfEmpty(-1).Min();
                return new PantryEntry(kv.Key, lots.Sum(l => l.Count), fresh.Sum(l => l.Count), lots.Where(l => l.Spoiled).Sum(l => l.Count),
                    food.Items.GetValueOrDefault(kv.Key)?.FoodValue * fresh.Sum(l => l.Count) ?? 0,
                    next < 0 ? null : next, lots.Where(l => l.Tainted).Sum(l => l.Count),
                    lots.GroupBy(l => l.Quality).ToDictionary(g => g.Key, g => g.Sum(l => l.Count)));
            })
            .OrderBy(e => food.Items.GetValueOrDefault(e.ItemId)?.Meal is null ? 1 : 0)
            .ThenBy(e => e.ItemId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Units in stock (fresh only) that match a recipe input key (an item id or <c>tag:x</c>).</summary>
    public static int Available(ColonyState state, GameData data, string key)
    {
        if (data.Food is not { } food) return 0;
        Reconcile(state, data);
        return state.Food.Lots.Where(kv => food.Matches(kv.Key, key)).Sum(kv => kv.Value.Where(l => !l.Spoiled).Sum(l => l.Count));
    }

    /// <summary>Takes <paramref name="count"/> fresh units matching <paramref name="key"/>, soonest to spoil first. Null (nothing taken) when short.</summary>
    public static List<TakenFood>? TakeIngredient(ColonyState state, GameData data, string key, int count)
    {
        if (data.Food is not { } food || Available(state, data, key) < count) return null;
        var taken = new List<TakenFood>();
        var left = count;
        foreach (var (id, lot) in Candidates(state, data, id => food.Matches(id, key)))
        {
            if (left == 0) break;
            if (lot.Spoiled) continue;
            var n = Math.Min(left, lot.Count);
            taken.Add(TakeFromLot(state, id, lot, n));
            left -= n;
        }
        Prune(state);
        return taken;
    }

    /// <summary>Lots of items passing <paramref name="filter"/>, fresh ones soonest to spoil first (never-spoiling last), then the spoiled.</summary>
    internal static IEnumerable<(string Id, FoodLot Lot)> Candidates(ColonyState state, GameData data, Func<string, bool> filter) =>
        state.Food.Lots.Where(kv => filter(kv.Key))
            .SelectMany(kv => kv.Value.Select(l => (Id: kv.Key, Lot: l)))
            .OrderBy(x => x.Lot.Spoiled ? 1 : 0)
            .ThenBy(x => x.Lot.TicksLeft ?? double.MaxValue)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();

    internal static TakenFood TakeFromLot(ColonyState state, string itemId, FoodLot lot, int n)
    {
        lot.Count -= n;
        state.Inventory.TryTake(itemId, n);
        return new TakenFood(itemId, n, lot.Quality, lot.Tainted, lot.Spoiled, lot.TicksLeft);
    }

    internal static List<FoodLot> LotsOf(ColonyState state, string itemId)
    {
        if (!state.Food.Lots.TryGetValue(itemId, out var lots)) state.Food.Lots[itemId] = lots = new();
        return lots;
    }

    internal static FoodLot NewLot(GameData data, FoodItemDef def, int count, double shelfMultiplier) => new()
    {
        Count = count,
        Shelf = def.ShelfDays * data.Rules.TicksPerDay * shelfMultiplier,
    };

    internal static void Prune(ColonyState state)
    {
        if (!NeedsPrune(state)) return;
        foreach (var (id, lots) in state.Food.Lots.ToList())
        {
            lots.RemoveAll(l => l.Count <= 0);
            if (lots.Count == 0) state.Food.Lots.Remove(id);
        }
    }

    private static bool NeedsPrune(ColonyState state)
    {
        foreach (var lots in state.Food.Lots.Values)
        {
            if (lots.Count == 0) return true;
            foreach (var l in lots)
                if (l.Count <= 0) return true;
        }
        return false;
    }

    /// <summary>The lot <see cref="Candidates"/> would list first among an item's lots that still hold a unit, without sorting them all.</summary>
    internal static FoodLot? FirstLot(ColonyState state, string itemId)
    {
        if (!state.Food.Lots.TryGetValue(itemId, out var lots)) return null;
        FoodLot? best = null;
        foreach (var l in lots)
        {
            if (l.Count <= 0) continue;
            if (best is null) { best = l; continue; }
            int spoiled = l.Spoiled ? 1 : 0, bestSpoiled = best.Spoiled ? 1 : 0;
            if (spoiled != bestSpoiled)
            {
                if (spoiled < bestSpoiled) best = l;
                continue;
            }
            if ((l.TicksLeft ?? double.MaxValue).CompareTo(best.TicksLeft ?? double.MaxValue) < 0) best = l;
        }
        return best;
    }
}

/// <summary>Quality tiers of cooked food (the scavenge quality ids), their effect multiplier and the cook's roll.</summary>
public static class FoodQuality
{
    public const string Standard = "standard";

    public static double Effect(GameData data, string quality) =>
        data.Food?.Rules.Quality.Effect.GetValueOrDefault(quality, 1) ?? 1;

    /// <summary>
    /// Rolls a tier like crafted gear: <c>score + uniform(−qualitySpread, +qualitySpread)</c> against
    /// <c>crafting.qualityThresholds</c>; below them all, the lowest tier (the first in <c>quality.effect</c>).
    /// </summary>
    public static string Roll(GameData data, double score, Random.DeterministicRng rng)
    {
        var rolled = score + (rng.NextDouble() * 2 - 1) * data.Rules.Crafting.QualitySpread;
        return Pick(data, rolled);
    }

    public static string Pick(GameData data, double rolled)
    {
        var tiers = data.Food?.Rules.Quality.Effect.Keys.ToList() ?? [];
        if (tiers.Count == 0) return Standard;
        var best = tiers[0];
        foreach (var (tier, threshold) in data.Rules.Crafting.QualityThresholds.OrderBy(kv => kv.Value))
            if (rolled + 1e-9 >= threshold && tiers.Contains(tier)) best = tier;
        return best;
    }

    /// <summary>Chance of each tier for a score (the roll is uniform within ± qualitySpread).</summary>
    public static IReadOnlyList<(string Tier, double Chance)> Chances(GameData data, double score)
    {
        var tiers = data.Food?.Rules.Quality.Effect.Keys.ToList() ?? [];
        var spread = data.Rules.Crafting.QualitySpread;
        const int Steps = 200;
        var counts = tiers.ToDictionary(t => t, _ => 0);
        for (var i = 0; i < Steps; i++)
        {
            var rolled = score + (spread <= 0 ? 0 : -spread + 2 * spread * (i + 0.5) / Steps);
            counts[Pick(data, rolled)]++;
        }
        return tiers.Select(t => (t, counts[t] / (double)Steps)).ToList();
    }
}
