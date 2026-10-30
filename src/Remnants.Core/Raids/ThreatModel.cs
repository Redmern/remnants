using Remnants.Core.Colony;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Raids;

/// <summary>
/// What draws each threat to the colony, and how much warning the colony gets. Pure queries over the colony; the
/// scheduler (<see cref="RaidSystem"/>) adds the pressure each hour.
/// <code>
/// pressure/hour = (basePerHour + perInputPerHour · input) · weather.pressure[kind]
/// Husk input    = noise: survivors · perSurvivor + night shifts + lit floodlights at night + recent returns + noisy items − lures
/// Linked input  = Network presence: Σ threat of known zones with Linked, · (1 + day / 30)
/// Raider input  = wealth: (stockpile + pantry food value) / 50 + non-food items / 5
/// </code>
/// </summary>
public static class ThreatModel
{
    public static double Input(ColonySim sim, RaidData raids, ThreatKind kind) => kind switch
    {
        ThreatKind.Husk => Noise(sim, raids),
        ThreatKind.Linked => NetworkPresence(sim),
        _ => Wealth(sim),
    };

    public static double PressurePerHour(ColonySim sim, RaidData raids, ThreatKind kind)
    {
        var t = raids.Threat(kind);
        if (DayNight.Day(sim.State.Tick, sim.Data.Rules) < t.MinDay) return 0;
        var weather = RaidWeather.Modifiers(sim, raids, sim.State.Tick).Pressure.GetValueOrDefault(kind, 1);
        return (t.BasePerHour + t.PerInputPerHour * Input(sim, raids, kind)) * weather;
    }

    /// <summary>The noise Husks hear (never below 0).</summary>
    public static double Noise(ColonySim sim, RaidData raids)
    {
        var n = raids.Def.Noise;
        var state = sim.State;
        var noise = state.Survivors.Count(s => !s.Away) * n.PerSurvivor;
        noise += state.Buildings.Count(b => b.NightShift) * n.PerNightShift;
        if (sim.Phase == DayPhase.Night)
            noise += (state.Buildings.Count(b => b.Floodlight)
                      + state.Defense.Defenses.Count(d => raids.Defenses.GetValueOrDefault(d.DefId)?.Lights == true && Working(sim, d))) * n.PerFloodlight;
        noise += state.Defense.Returns.Count(t => state.Tick - t < n.ReturnNoiseHours) * n.PerReturn;
        foreach (var (item, perItem) in n.Items) noise += state.Inventory.Count(item) * perItem;
        noise -= state.Defense.Defenses.Count(d => raids.Defenses.GetValueOrDefault(d.DefId)?.Lure == true && !d.Sprung) * n.PerLure;
        return Math.Max(0, noise);
    }

    /// <summary>How far the Network reaches toward the colony: the threat of every known zone with Linked in it, growing with time.</summary>
    public static double NetworkPresence(ColonySim sim)
    {
        if (sim.Scavenge is not { } data) return 0;
        var presence = data.Zones.Values
            .Where(z => WorldMap.IntelOf(sim.State, z) != ZoneIntel.Unknown
                        && z.EnemyMix.Any(m => data.Enemies.GetValueOrDefault(m.Id)?.Ai == AiKind.Linked))
            .Sum(z => z.Threat);
        return presence * (1 + DayNight.Day(sim.State.Tick, sim.Data.Rules) / 30.0);
    }

    /// <summary>What raiders smell: the stockpile, the pantry's food value (as the stock it replaced) and the other items.</summary>
    public static double Wealth(ColonySim sim)
    {
        var food = sim.Data.Food;
        var items = sim.State.Inventory.Counts.Where(kv => food is null || !food.Items.ContainsKey(kv.Key)).Sum(kv => kv.Value);
        return (sim.State.Stockpile.Amounts.Values.Sum() + Pantry.FoodValue(sim)) / 50 + items / 5.0;
    }

    /// <summary>A placed defense does its job: standing, upkeep paid, and (traps) armed.</summary>
    public static bool Working(ColonySim sim, PlacedDefense d) => d.Hp > 0 && !d.Sprung && !sim.State.Defense.Unpaid.Contains(d.Id);
}

/// <summary>One source of lead time, for the warning tooltip.</summary>
public sealed record WarningSource(string Name, double Hours);

/// <summary>
/// How far ahead the colony sees a raid coming: <c>warning.baseHours</c> plus the best tower, the watching buildings, guards
/// on duty, a drone patrol, alarms and tripwires; times the weather (dust storms hide approaches); capped at
/// <c>warning.maxHours</c>. The size estimate's error shrinks with it.
/// </summary>
public static class RaidWarning
{
    public static IReadOnlyList<WarningSource> Sources(ColonySim sim, RaidData raids)
    {
        var w = raids.Def.Warning;
        var state = sim.State;
        var list = new List<WarningSource> { new("Lookouts at the fence", w.BaseHours) };
        var towers = state.Defense.Defenses
            .Where(d => ThreatModel.Working(sim, d) && raids.Defenses.GetValueOrDefault(d.DefId)?.Kind is DefenseKind.Tower)
            .Select(d => (d, def: raids.Defense(d.DefId))).OrderByDescending(t => t.def.WarningHours).FirstOrDefault();
        if (towers.def is { } tower) list.Add(new(tower.Name, tower.WarningHours));
        foreach (var d in state.Defense.Defenses.Where(d => ThreatModel.Working(sim, d)))
            if (raids.Defenses.GetValueOrDefault(d.DefId) is { Kind: not DefenseKind.Tower, WarningHours: > 0 } def)
                list.Add(new(def.Name, def.WarningHours));
        foreach (var (id, perLevel) in w.BuildingHoursPerLevel)
            if (sim.Data.Buildings.TryGetValue(id, out var bdef)
                && state.Buildings.Where(b => b.DefId == id && !RaidSystem.IsRubble(sim, b)).Select(b => b.Level).DefaultIfEmpty(0).Max() is var level and > 0)
                list.Add(new(bdef.Name, perLevel * level));
        var guards = Guards.OnDuty(sim).ToList();
        if (guards.Count > 0)
        {
            var sentry = guards.Sum(g => PerkEffects.For(g.Survivor, sim.Data).Hook(PerkHooks.SentryLeadHours));
            list.Add(new($"{guards.Count} on guard", Math.Min(w.MaxGuardHours, guards.Count * w.PerGuardHours) + sentry));
        }
        if (state.Inventory.Has(w.DroneItem)
            && state.Survivors.Any(s => !s.Away && !s.IsChild && s.Get(Survivors.Skill.DronePiloting) >= w.DronePilotSkill))
            list.Add(new("Drone patrol", w.DroneHours));
        return list;
    }

    /// <summary>Total lead time in whole hours, after the weather.</summary>
    public static int LeadHours(ColonySim sim, RaidData raids)
    {
        var hours = Sources(sim, raids).Sum(s => s.Hours) * RaidWeather.Modifiers(sim, raids, sim.State.Tick).Warning;
        return (int)Math.Clamp(Math.Round(hours), 0, raids.Def.Warning.MaxHours);
    }

    /// <summary>The ± share of the size estimate for this lead time.</summary>
    public static double Error(RaidData raids, int leadHours)
    {
        var w = raids.Def.Warning;
        var t = Math.Clamp((double)leadHours / Math.Max(1, w.MaxHours), 0, 1);
        return w.MaxError + (w.MinError - w.MaxError) * t;
    }

    /// <summary>The reported size range: the true size ± the error, rounded outward, never below 1.</summary>
    public static (int Min, int Max) Estimate(RaidData raids, int size, int leadHours)
    {
        var e = Error(raids, leadHours);
        return (Math.Max(1, (int)Math.Floor(size * (1 - e))), (int)Math.Ceiling(size * (1 + e)));
    }
}
