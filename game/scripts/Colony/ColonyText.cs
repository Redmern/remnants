using System.Collections.Generic;
using System.Linq;
using Remnants.Core.Colony;
using Remnants.Core.Data;
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
        s.AssignedBuildingId is { } id && sim.State.FindBuilding(id) is { } b ? sim.Data.Buildings[b.DefId].Name : "Idle";

    public static string BuildingName(ColonySim sim, Building b) => $"{sim.Data.Buildings[b.DefId].Name} L{b.Level}";

    /// <summary>One line per non-healthy body part, or "Healthy".</summary>
    public static string Body(Body body)
    {
        var hurt = body.Parts
            .Where(kv => kv.Value.Condition != PartCondition.Healthy)
            .Select(kv => kv.Value.Condition switch
            {
                PartCondition.Injured => $"{kv.Key}: injured ({kv.Value.Severity:P0})",
                PartCondition.Prosthetic => $"{kv.Key}: prosthetic ({kv.Value.ProstheticQuality:P0})",
                _ => $"{kv.Key}: {kv.Value.Condition.ToString().ToLowerInvariant()}",
            })
            .ToList();
        return hurt.Count == 0 ? "Healthy" : string.Join("\n", hurt);
    }

    public static string Needs(Survivor s, RulesDef rules)
    {
        var parts = new List<string>();
        if (s.StarvingTicks > 0) parts.Add($"starving {s.StarvingTicks}h");
        if (!s.HasBed) parts.Add($"homeless {s.HomelessTicks}/{rules.Housing.HomelessGraceTicks}h");
        return parts.Count == 0 ? "fed, housed" : string.Join(", ", parts);
    }
}
