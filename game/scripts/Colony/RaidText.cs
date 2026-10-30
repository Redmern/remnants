using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Colony;
using Remnants.Core.Raids;

namespace Remnants.Game.Colony;

/// <summary>The defense catalogue's tabs, in order: how the HUD groups <see cref="DefenseKind"/>s.</summary>
public enum DefenseCategory { Walls, Posts, Lights, Traps }

/// <summary>
/// Words, icons and groupings for the raid and defense HUD (no rules: every number comes from Core's queries). Shared by
/// the defense catalogue, the post panel, the raid banner, the prep screen, the raid report and the repair queue.
/// </summary>
public static class RaidText
{
    public static DefenseCategory Category(DefenseKind kind) => kind switch
    {
        DefenseKind.Wall or DefenseKind.Gate or DefenseKind.Barricade => DefenseCategory.Walls,
        DefenseKind.Post or DefenseKind.Tower => DefenseCategory.Posts,
        DefenseKind.Light or DefenseKind.Turret or DefenseKind.Alarm => DefenseCategory.Lights,
        _ => DefenseCategory.Traps,
    };

    public static string CategoryName(DefenseCategory c) => c switch
    {
        DefenseCategory.Walls => "Walls",
        DefenseCategory.Posts => "Posts",
        DefenseCategory.Lights => "Lights",
        _ => "Traps",
    };

    public static string CategoryTip(DefenseCategory c) => c switch
    {
        DefenseCategory.Walls => "Walls, gates and barricades: they soak the attack",
        DefenseCategory.Posts => "Sandbag nests and towers: guard posts, lead time",
        DefenseCategory.Lights => "Floodlights, turrets and the alarm bell",
        _ => "Traps: spring once per raid on the side they come from",
    };

    public static string CategoryIcon(DefenseCategory c) => $"def_cat_{c.ToString().ToLowerInvariant()}";

    /// <summary>The line icon for a defense: <c>def_{id}</c> if one exists, else its category's.</summary>
    public static string Icon(DefenseDef def) => $"def_{def.Id}";

    public static string IconFallback(DefenseDef def) => CategoryIcon(Category(def.Kind));

    public static string Side(Side side) => side.ToString().ToLowerInvariant();

    public static string ThreatIcon(ThreatKind kind) => kind == ThreatKind.Linked ? "stat_linked" : "stat_threat";

    /// <summary>Cost rows for <see cref="Hud.Widgets.CostChips"/>: resource, name, need, have.</summary>
    public static IEnumerable<(string Id, string Name, double Need, double Have)> Costs(ColonySim sim, IReadOnlyDictionary<string, double> cost) =>
        cost.Select(kv => (kv.Key, sim.Data.Resources.TryGetValue(kv.Key, out var r) ? r.Name : kv.Key, kv.Value, sim.State.Stockpile.Get(kv.Key)));

    /// <summary>A cost's signature for "rebuild the chips only when it changed".</summary>
    public static string Signature(ColonySim sim, IReadOnlyDictionary<string, double> cost) =>
        string.Join(",", cost.Select(kv => $"{kv.Key}:{kv.Value}:{sim.State.Stockpile.Get(kv.Key) >= kv.Value}"));

    /// <summary>Why the colony cannot build this kind of defense at all (schematic, Town Hall), or null.</summary>
    public static string? LockReason(ColonySim sim, RaidData raids, DefenseDef def)
    {
        if (!DefenseSchematics.Unlocked(sim, def.Id))
            return DefenseSchematics.GateOf(raids, def.Id) is { } gate ? $"Needs the schematic {gate.Name}" : "Needs its schematic";
        if (sim.TownHallLevel < def.MinTownHallLevel) return $"Needs Town Hall level {def.MinTownHallLevel}";
        return null;
    }

    /// <summary>"Soak 6 · 60 HP · range 4 · +6 h warning · 2 guards": what a defense does, for tooltips and the post panel.</summary>
    public static string Stats(DefenseDef def)
    {
        var parts = new List<string> { $"{def.Hp:0} HP" };
        if (def.Soak > 0) parts.Add($"soak {def.Soak:0.#}");
        if (def.Defense > 0) parts.Add($"defense +{def.Defense:0.#}");
        if (def.TrapDamage > 0) parts.Add($"hits for {def.TrapDamage:0.#}");
        if (def.Range > 0) parts.Add(def.Arc < 360 ? $"range {def.Range}, {def.Arc}° arc" : $"range {def.Range}");
        if (def.WarningHours > 0) parts.Add($"+{def.WarningHours:0.#} h warning");
        if (def.GuardSlots > 0) parts.Add($"{def.GuardSlots} guard{(def.GuardSlots == 1 ? "" : "s")}");
        if (def.Lights) parts.Add("lit at night");
        if (def.Wakes) parts.Add("wakes sleepers");
        if (def.Lure) parts.Add("lures Husks");
        return string.Join(" · ", parts);
    }

    /// <summary>The full tooltip of a catalogue entry.</summary>
    public static string Describe(ColonySim sim, DefenseDef def, string? lockReason)
    {
        var lines = new List<string> { $"{def.Name} — {def.Kind}", def.Description, Stats(def) };
        if (def.Hits.Count > 0) lines.Add($"Trips: {string.Join(", ", def.Hits)}");
        if (def.IsTrap) lines.Add(def.Rearmable ? $"Re-arm after a raid: {ColonyText.Cost(def.RearmCost, sim.Data)}" : "Used up when it springs.");
        if (def.Upkeep.Count > 0) lines.Add($"Upkeep per day: {ColonyText.Cost(def.Upkeep, sim.Data)} (unpaid: idle that day)");
        if (def.UpgradesTo is { } next && sim.Raids?.Defenses.GetValueOrDefault(next) is { } up) lines.Add($"Upgrades into: {up.Name}");
        if (lockReason is not null) lines.Add($"Locked: {lockReason}.");
        return string.Join("\n", lines.Where(l => l.Length > 0));
    }

    /// <summary>"in 6 h (night)": time left to a raid's arrival and the phase it lands in.</summary>
    public static string Arrival(ColonySim sim, Raid raid)
    {
        var hours = (int)System.Math.Max(0, raid.ArrivalTick - sim.State.Tick);
        var phase = DayNight.PhaseAt(raid.ArrivalTick, sim.Data.Rules).ToString().ToLowerInvariant();
        return hours <= 0 ? $"at the fence now ({phase})" : $"in {hours} h ({phase}, {DayNight.Hour(raid.ArrivalTick, sim.Data.Rules):00}:00)";
    }

    public static string Estimate(Raid raid) =>
        raid.EstimateMin == raid.EstimateMax ? $"about {raid.EstimateMin}" : $"{raid.EstimateMin}–{raid.EstimateMax}";

    /// <summary>A repair target's name: "Farm L2" or "Wooden Wall (north)".</summary>
    public static string TargetName(ColonySim sim, RaidData raids, RepairTarget target, int id)
    {
        if (target == RepairTarget.Building)
            return sim.State.FindBuilding(id) is { } b ? ColonyText.BuildingName(sim, b) : $"Building {id}";
        if (sim.State.Defense.Find(id) is not { } d || !raids.Defenses.TryGetValue(d.DefId, out var def)) return $"Defense {id}";
        return $"{def.Name} ({Side(DefenseGrid.Of(sim, raids).SideOf(d.X, d.Y))})";
    }

    /// <summary>The HP a repair target has and can have, for its bar.</summary>
    public static (double Hp, double Max) Health(ColonySim sim, RaidData raids, RepairTarget target, int id)
    {
        if (target == RepairTarget.Building && sim.State.FindBuilding(id) is { } b)
        {
            var max = RaidSystem.BuildingHp(raids, b);
            return (max - RaidSystem.Damage(sim.State, b), max);
        }
        if (sim.State.Defense.Find(id) is { } d && raids.Defenses.TryGetValue(d.DefId, out var def)) return (d.Hp, def.Hp);
        return (0, 1);
    }

    /// <summary>Every damaged building and defense whose repair is not queued yet.</summary>
    public static IEnumerable<(RepairTarget Target, int Id)> Unqueued(ColonySim sim, RaidData raids)
    {
        var queued = sim.State.Defense.Repairs.Select(r => (r.Target, r.TargetId)).ToHashSet();
        foreach (var b in sim.State.Buildings)
            if (RaidSystem.Missing(sim, raids, RepairTarget.Building, b.Id) > 1e-9 && !queued.Contains((RepairTarget.Building, b.Id)))
                yield return (RepairTarget.Building, b.Id);
        foreach (var d in sim.State.Defense.Defenses)
            if (RaidSystem.Missing(sim, raids, RepairTarget.Defense, d.Id) > 1e-9 && !queued.Contains((RepairTarget.Defense, d.Id)))
                yield return (RepairTarget.Defense, d.Id);
    }
}
