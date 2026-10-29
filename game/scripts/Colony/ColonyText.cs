using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>View-model formatting of Core state for labels. Read-only; no rules.</summary>
public static class ColonyText
{
    public static string Cost(IReadOnlyDictionary<string, double> cost, GameData data) =>
        cost.Count == 0 ? "free" : string.Join(", ", cost.Select(kv => $"{kv.Value:0} {data.Resources[kv.Key].Name}"));

    public static string Clock(ColonySim sim)
    {
        var rules = sim.Data.Rules;
        var day = sim.State.Tick / rules.TicksPerDay + 1;
        var hour = sim.State.Tick % rules.TicksPerDay;
        return $"Day {day}, {hour:00}:00";
    }

    public static int AgeYears(Survivor s, RulesDef rules) => (int)(s.AgeTicks / rules.TicksPerYear);

    public static string Job(ColonySim sim, Survivor s) =>
        s.Away ? "Away on expedition"
        : s.IsInfant ? "Baby"
        : s.PatientAtBuildingId is not null ? "Patient"
        : s.AssignedBuildingId is { } id && sim.State.FindBuilding(id) is { } b ? sim.Data.Buildings[b.DefId].Name : "Idle";

    public static string BuildingName(ColonySim sim, Building b) => $"{sim.Data.Buildings[b.DefId].Name} L{b.Level}";

    /// <summary>One line per non-healthy body part, or "Healthy". Prosthetics are named when item data is given.</summary>
    public static string Body(Body body, ScavengeData? items = null)
    {
        var hurt = body.Parts
            .Where(kv => kv.Value.Condition != PartCondition.Healthy)
            .Select(kv => kv.Value.Condition switch
            {
                PartCondition.Injured => $"{kv.Key}: injured ({Percent(kv.Value.Severity)})",
                PartCondition.Prosthetic => kv.Value.ProstheticItemId is { } id && items?.Items.GetValueOrDefault(id) is { } item
                    ? $"{kv.Key}: {item.Name} ({Percent(kv.Value.ProstheticQuality)})"
                    : $"{kv.Key}: prosthetic ({Percent(kv.Value.ProstheticQuality)})",
                _ => $"{kv.Key}: {kv.Value.Condition.ToString().ToLowerInvariant()}",
            })
            .ToList();
        return hurt.Count == 0 ? "Healthy" : string.Join("\n", hurt);
    }

    public static string Percent(double fraction) => $"{System.Math.Round(fraction * 100):0}%";

    /// <summary>Hours as "5h" or "2d 3h".</summary>
    public static string Duration(int ticks, RulesDef rules) =>
        ticks < rules.TicksPerDay ? $"{ticks}h" : $"{ticks / rules.TicksPerDay}d {ticks % rules.TicksPerDay}h";

    /// <summary>A field profile in one line: primary move (and extra modes), AP, initiative, sight, load and attacks.</summary>
    public static string Profile(UnitProfile p)
    {
        var moves = string.Join(" / ", p.Movement.Modes.Select(m => $"{m.Pattern} {m.Range}" + (m.ApCost > 1 ? $" ({m.ApCost} AP)" : "")));
        var attacks = string.Join(", ", p.Actions.Select(a => $"{a.Name} {a.MinDamage:0}–{a.MaxDamage:0}"));
        return $"Moves {moves} · AP {p.MaxAp} · init {p.Initiative:0} · sight {p.Sight:0.#} · load {p.Load:0.#}/{p.CarryCapacity:0} · armor {p.Armor:0.#} · {attacks}";
    }

    /// <summary>A living survivor's name, a dead one's from the memorial ("Ada †"), else null (left the colony).</summary>
    public static string? PersonName(ColonyState state, int? id) =>
        id is not { } v ? null
        : state.FindSurvivor(v)?.Name ?? (state.Memorial.Find(m => m.SurvivorId == v) is { } dead ? $"{dead.Name} †" : null);

    /// <summary>"Infant 40%", "Child 75%" or "Adult".</summary>
    public static string Stage(Survivor s, RulesDef rules) =>
        s.IsChild ? $"{s.LifeStage} {Percent(Bonds.StageProgress(s, rules.Life))}" : "Adult";

    public static string Mood(Survivor s) =>
        s.Mood >= 80 ? "content" : s.Mood >= 55 ? "low" : s.Mood >= 30 ? "grieving" : "despairing";

        public static string Needs(Survivor s, RulesDef rules)
    {
        var parts = new List<string>();
        if (s.StarvingTicks > 0) parts.Add($"starving {s.StarvingTicks}h");
        if (!s.HasBed) parts.Add($"homeless {s.HomelessTicks}/{rules.Housing.HomelessGraceTicks}h");
        return parts.Count == 0 ? "fed, housed" : string.Join(", ", parts);
    }
}
