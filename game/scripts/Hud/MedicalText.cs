using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Game.Hud;

/// <summary>
/// Words, icons and colours for medicine (data/medicine.json): conditions on a body, what a medical item does, and the
/// Med Bay's treatment priorities. Read-only; Core decides everything.
/// </summary>
public static class MedicalText
{
    public static string Icon(ConditionDef? def) => def?.Kind switch
    {
        ConditionKind.Infection => "cond_infection",
        ConditionKind.Fracture => "cond_fracture",
        ConditionKind.Sickness => "cond_sickness",
        ConditionKind.Dependency or ConditionKind.Effect => "cond_pill",
        _ => "stat_warning",
    };

    /// <summary>Danger for what can kill, warn for what slows them, muted for passing effects.</summary>
    public static Color Tint(ConditionDef? def, BodyCondition c) => def?.Kind switch
    {
        ConditionKind.Effect => Palette.TextMuted,
        ConditionKind.Fracture when c.Treated => Palette.Amber,
        _ when def is not null && c.Severity + 1e-9 >= def.HealthDrainFrom => Palette.Danger,
        ConditionKind.Infection => Palette.Danger,
        _ => Palette.Warn,
    };

    /// <summary>"Infected wound (left arm) 35% · fever" / "Fracture (right leg), splinted" / "Painkillers, 12 h left".</summary>
    public static string Line(MedicineData med, BodyCondition c, bool field = false)
    {
        var def = med.Condition(c.Id);
        var name = def?.Name ?? c.Id;
        var where = c.Part is { } part ? $" ({CyberText.Part(part).ToLowerInvariant()})" : "";
        return def?.Kind switch
        {
            ConditionKind.Fracture => $"{name}{where}{(c.Treated ? ", splinted" : "")}",
            ConditionKind.Effect => $"{name}, {c.Remaining} {(field ? "rounds" : "h")} left",
            _ => $"{name}{where} {c.Severity * 100:0}%{(def?.Fever == true ? " · fever" : "")}",
        };
    }

    /// <summary>Short tags for a unit card or roster row: "infected, fractured, in pain".</summary>
    public static List<string> Tags(MedicineData? med, Body body)
    {
        var tags = new List<string>();
        if (med is null) return tags;
        foreach (var c in body.Conditions)
            switch (med.Condition(c.Id)?.Kind)
            {
                case ConditionKind.Infection: tags.Add("infected"); break;
                case ConditionKind.Fracture: tags.Add(c.Treated ? "splinted" : "fractured"); break;
                case ConditionKind.Sickness: tags.Add("sick"); break;
                case ConditionKind.Dependency: tags.Add("dependent"); break;
                case ConditionKind.Effect: tags.Add("numbed"); break;
            }
        if (body.Conditions.Any(c => med.Condition(c.Id)?.Fever == true)) tags.Add("fever");
        if (Treatment.InPain(med, body)) tags.Add("in pain");
        return tags.Distinct().ToList();
    }

    /// <summary>What a medical item does, in short phrases (empty for anything else).</summary>
    public static List<string> Effects(UseEffect use)
    {
        var parts = new List<string>();
        if (use.ReviveHp > 0) parts.Add($"revives the downed with {use.ReviveHp:0} HP");
        else if (use.Stabilize) parts.Add("stops a downed survivor bleeding out");
        if (use.Heal > 0) parts.Add($"heals {use.Heal:0} HP");
        if (use.Dress == Dressing.Clean) parts.Add(use.DressAll ? "cleans every wound (no infection)" : "cleans a wound (no infection)");
        else if (use.Dress == Dressing.Bandaged) parts.Add(use.DressAll ? "bandages every wound" : "bandages the worst wound");
        if (use.Cures.Count > 0) parts.Add($"cures {string.Join(" and ", use.Cures.Select(c => c.Replace('_', ' ')))}{(use.CureStrength < 1 - 1e-9 ? $" ({use.CureStrength * 100:0}%)" : "")}");
        if (use.Splint) parts.Add("splints a fracture");
        if (use.PainRelief > 0) parts.Add($"numbs {use.PainRelief * 100:0}% of the pain ({use.PainRounds} rounds / {use.PainTicks} h)");
        if (use.StimAp > 0) parts.Add($"+{use.StimAp} AP now, −{use.CrashAp} next turn");
        if (use.CalmRounds > 0) parts.Add($"cannot be pinned down for {use.CalmRounds} rounds");
        if (use.HumanityCost > 0) parts.Add($"costs {use.HumanityCost:0} humanity");
        return parts;
    }

    /// <summary>A tooltip for an item: name, quality, what it does, AP and reach for consumables.</summary>
    public static string ItemTip(ScavengeData data, ItemDef item)
    {
        var lines = new List<string> { item.Name };
        if (item.Use is { } use)
        {
            var effects = Effects(use);
            if (effects.Count > 0) lines.Add(string.Join(" · ", effects));
            lines.Add($"{use.ApCost} AP · {(use.Range == 0 ? "on yourself" : "on yourself or someone next to you")}");
        }
        if (item.Medical && data.QualityOf(item) is { } q) lines.Add($"Quality: {q.Name}");
        return string.Join("\n", lines);
    }

    public static string Priority(TreatPriority p) => p switch
    {
        TreatPriority.Bleeding => "Bleeding wounds",
        TreatPriority.Infection => "Infections",
        TreatPriority.Fracture => "Fractures",
        TreatPriority.Sickness => "Sickness",
        _ => "Pain",
    };

    public static string PriorityTip(TreatPriority p) => p switch
    {
        TreatPriority.Bleeding => "Dress open wounds (a clean dressing or antiseptic first, then bandages) so they do not go bad.",
        TreatPriority.Infection => "Give antibiotics to the infected.",
        TreatPriority.Fracture => "Splint broken limbs.",
        TreatPriority.Sickness => "Give antidotes for food and Husk sickness.",
        _ => "Painkillers for severe injuries (never to someone close to a dependency).",
    };
}
