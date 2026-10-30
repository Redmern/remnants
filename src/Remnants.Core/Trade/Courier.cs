using Remnants.Core.Colony;

namespace Remnants.Core.Trade;

/// <summary>One factor on a courier's travel time (below 1 = faster), with a label for the UI.</summary>
public sealed record CourierFactor(string Label, double Factor);

/// <summary>How long a courier takes and why (base + legs, then every factor).</summary>
public sealed record CourierPlan(int Legs, double BaseDays, IReadOnlyList<CourierFactor> Factors, double Days);

/// <summary>
/// Lobby trades reach the colony by courier over the Radio Mast. Travel time = <c>baseDays + daysPerLeg × legs</c>, then
/// times every modifier: the data's building factors (a Garage) and anything registered in <see cref="Modifiers"/>
/// (weather, a vehicle or caravan upgrade). Legs stand in for the distance between the two colonies, which the game does
/// not place on one map: a stable number 1..<c>maxLegs</c> from the two players' identities, the same for both sides.
/// </summary>
public static class Courier
{
    /// <summary>
    /// Extra travel-time factors other systems add (weather: a storm slows the road; vehicles: a caravan upgrade). Each
    /// returns null when it does not apply. Pure functions of the colony, so the plan stays deterministic.
    /// </summary>
    public static readonly List<Func<ColonySim, CourierFactor?>> Modifiers = new();

    public static CourierPlan Plan(ColonySim colony, TradeRules rules, int legs)
    {
        var r = rules.Courier;
        legs = Math.Clamp(legs, 1, Math.Max(1, r.MaxLegs));
        var factors = new List<CourierFactor>();
        foreach (var (building, factor) in r.BuildingFactors.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (colony.State.Buildings.Any(b => b.DefId == building))
                factors.Add(new CourierFactor(colony.Data.Buildings.GetValueOrDefault(building)?.Name ?? building, factor));
        foreach (var modifier in Modifiers)
            if (modifier(colony) is { } f) factors.Add(f);
        var baseDays = r.BaseDays + r.DaysPerLeg * legs;
        var days = Math.Max(r.MinDays, factors.Aggregate(baseDays, (d, f) => d * f.Factor));
        return new CourierPlan(legs, baseDays, factors, Math.Round(days, 2));
    }

    /// <summary>The distance in legs between two players (order does not matter): FNV-1a over both keys.</summary>
    public static int Legs(string a, string b, int maxLegs)
    {
        var (lo, hi) = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        var hash = 2166136261u;
        foreach (var c in lo + "|" + hi)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return 1 + (int)(hash % (uint)Math.Max(1, maxLegs));
    }

    /// <summary>Why this colony cannot receive couriers yet (no Radio Mast, when the game has one), or null.</summary>
    public static string? Blocked(ColonySim colony, TradeRules rules)
    {
        if (rules.Courier.RequiresBuilding is not { } id || !colony.Data.Buildings.TryGetValue(id, out var def)) return null;
        return colony.State.Buildings.Any(b => b.DefId == id) ? null : $"Build a {def.Name} to trade with other colonies.";
    }
}
