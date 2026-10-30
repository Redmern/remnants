using Remnants.Core.Data;
using Remnants.Core.Random;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// What a party packs as rations. <paramref name="Prefer"/>: a food item to take first (a meal for the road, field rations);
/// the rest is filled automatically. Null = all automatic.
/// </summary>
public sealed record RationChoice(string? Prefer = null);

/// <summary>
/// The food packed for an expedition: <see cref="Need"/> food value (the ration rules: base, per travel day, waiting),
/// the items picked, their weight, the share of the need covered by risky food (spoiled, tainted, or spoiling before
/// the party is home), and the buff a good meal gives every member for the road.
/// </summary>
public sealed record RationBill(double Need, double Covered, SortedDictionary<string, int> Items, double Weight, double RiskShare, string? FieldBuff)
{
    public bool Enough => Covered + 1e-9 >= Need;
}

/// <summary>
/// Expedition rations from the pantry. The amount needed stays a food-value number (<see cref="WorldMap.RationsPerMember"/>
/// and the wait, so other rules, like vehicles' per-day costs, can change it); this decides which food fills it. Order:
/// the preferred item, then food that keeps until the party is home (the soonest to spoil of it first, best food value
/// per kilo on ties), then anything else (risky). Raw meat and spoiled food go only when nothing else is left.
/// </summary>
public static class Rations
{
    public static RationBill Plan(ColonySim sim, double need, int members, long tripTicks, RationChoice? choice = null)
    {
        var data = sim.Data;
        var state = sim.State;
        var items = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (data.Food is not { } food) return new(need, need, items, 0, 0, null);
        Pantry.Reconcile(state, data);
        var covered = 0.0;
        var risky = 0.0;
        var weight = 0.0;
        foreach (var (id, lot, risk) in Order(sim, food, tripTicks, choice))
        {
            if (covered + 1e-9 >= need) break;
            var def = food.Items[id];
            var n = Math.Min(lot.Count, (int)Math.Ceiling((need - covered) / def.FoodValue - 1e-9));
            if (n <= 0) continue;
            items[id] = items.GetValueOrDefault(id) + n;
            covered += n * def.FoodValue;
            if (risk) risky += n * def.FoodValue;
            weight += n * (sim.Scavenge?.Items.GetValueOrDefault(id)?.Weight ?? 0);
        }
        return new(need, covered, items, weight, need <= 0 ? 0 : Math.Min(1, risky / need), FieldBuff(sim, food, items, members));
    }

    /// <summary>Takes the planned items out of the pantry, in the same order as <see cref="Plan"/>.</summary>
    internal static void Take(ColonySim sim, double need, long tripTicks, RationChoice? choice)
    {
        if (sim.Data.Food is not { } food) return;
        var covered = 0.0;
        foreach (var (id, lot, _) in Order(sim, food, tripTicks, choice))
        {
            if (covered + 1e-9 >= need) break;
            var def = food.Items[id];
            var n = Math.Min(lot.Count, (int)Math.Ceiling((need - covered) / def.FoodValue - 1e-9));
            if (n <= 0) continue;
            Pantry.TakeFromLot(sim.State, id, lot, n);
            covered += n * def.FoodValue;
        }
        Pantry.Prune(sim.State);
    }

    /// <summary>
    /// Members set out: risky rations may make each sick (chance <c>sickness.rationChance</c> × risky share; they lose
    /// <c>rationHealthLoss</c> health and have Food Poisoning), and a good meal packed for everyone gives its field buff
    /// until <c>rations.fieldBuffHours</c> after arrival.
    /// </summary>
    internal static void SetOut(ColonySim sim, RationBill bill, IEnumerable<Survivor> members, long arrivalTick, DeterministicRng rng)
    {
        if (sim.Data.Food is not { } food) return;
        foreach (var s in members)
        {
            if (bill.RiskShare > 0 && rng.NextDouble() < food.Rules.Sickness.RationChance * bill.RiskShare)
            {
                s.Health = Math.Max(1, s.Health - food.Rules.Sickness.RationHealthLoss);
                FoodSickness.Apply(sim.State, sim.Data, s, "bad rations", 1 + (double)(arrivalTick - sim.State.Tick) / sim.Data.Rules.TicksPerDay);
            }
            if (bill.FieldBuff is { } buff)
                FoodEffects.Give(s, buff, arrivalTick + (long)Math.Round(food.Rules.Rations.FieldBuffHours * sim.Data.Rules.TicksPerDay / 24.0), 1);
        }
    }

    private static IEnumerable<(string Id, FoodLot Lot, bool Risky)> Order(ColonySim sim, FoodData food, long tripTicks, RationChoice? choice)
    {
        var lots = sim.State.Food.Lots
            .Where(kv => food.Items.TryGetValue(kv.Key, out var d) && d.FoodValue > 0)
            .SelectMany(kv => kv.Value.Where(l => l.Count > 0).Select(l => (Id: kv.Key, Lot: l, Def: food.Items[kv.Key])))
            .Select(x => (x.Id, x.Lot, x.Def, Risky: x.Lot.Spoiled || x.Lot.Tainted || x.Lot.TicksLeft is { } t && t < tripTicks, Raw: !x.Def.Edible && x.Def.Meal is null))
            .ToList();
        var weights = sim.Scavenge?.Items;
        double PerKilo((string Id, FoodLot Lot, FoodItemDef Def, bool Risky, bool Raw) x) =>
            x.Def.FoodValue / Math.Max(0.05, weights?.GetValueOrDefault(x.Id)?.Weight ?? 1);
        return lots
            .OrderBy(x => x.Raw || x.Lot.Spoiled ? 2 : x.Risky ? 1 : 0)
            .ThenBy(x => choice?.Prefer == x.Id ? 0 : 1)
            .ThenBy(x => x.Lot.TicksLeft ?? double.MaxValue)
            .ThenByDescending(PerKilo)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => (x.Id, x.Lot, x.Risky || x.Raw))
            .ToList();
    }

    /// <summary>The buff of a packed meal there is at least one of per member (at the minimum quality), best by its order in food.json.</summary>
    private static string? FieldBuff(ColonySim sim, FoodData food, SortedDictionary<string, int> items, int members)
    {
        var min = food.Rules.Quality.Effect.Keys.ToList().IndexOf(food.Rules.Quality.FieldBuffMin);
        var tiers = food.Rules.Quality.Effect.Keys.ToList();
        foreach (var (id, count) in items)
        {
            if (food.Items[id].Meal?.FieldBuff is not { } buff || count < Math.Max(1, members)) continue;
            var good = sim.State.Food.Lots.GetValueOrDefault(id)?.Where(l => !l.Spoiled && tiers.IndexOf(l.Quality) >= min).Sum(l => l.Count) ?? 0;
            if (good >= Math.Max(1, members)) return buff;
        }
        return null;
    }
}
