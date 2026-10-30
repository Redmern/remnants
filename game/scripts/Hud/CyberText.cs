using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Game.Hud;

/// <summary>
/// Words for cybernetics shared by the colony and the scavenge HUDs: perk/cyber hook values, a part's tier and
/// specialisation, a prosthetic item's field stats and a node's effects, and the humanity bands. Reads Core data only.
/// </summary>
public static class CyberText
{
    /// <summary>"Hack chance +10", "Thermal sight 2 tiles": one hook value as a player reads it.</summary>
    public static string Hook(string hook, double v) => hook switch
    {
        PerkHooks.HackChanceBonus => $"Hack chance +{v * 100:0}",
        PerkHooks.HackAlarmReduction => $"Hacks {v * 100:0}% quieter",
        PerkHooks.CanHackLinked => "Can hack Linked",
        PerkHooks.FieldRepair => "Field repairs",
        PerkHooks.HackApDiscount => $"Hacks cost {v:0} AP less",
        PerkHooks.DroneRangeBonus => $"Drone range +{v:0}",
        PerkHooks.DroneBatteryBonus => $"Drone battery +{v:0}",
        PerkHooks.DroneCanMark => "Drone marks targets",
        PerkHooks.DronePayload => "Drone payload",
        PerkHooks.AccuracyBonus => $"Accuracy +{v * 100:0}",
        PerkHooks.MeleeDamageBonus => $"Melee damage +{v * 100:0}%",
        PerkHooks.OverwatchReactionsBonus => $"+{v:0} overwatch reaction",
        PerkHooks.BraceCoverBonus => $"Braced cover +{v * 100:0}%",
        PerkHooks.StealthBonus => $"Stealth +{v:0}",
        PerkHooks.DetectionBonus => $"Detection +{v:0}",
        PerkHooks.SightBonus => $"Sight +{v:0}",
        PerkHooks.CarryBonus => $"Carry +{v:0} kg",
        PerkHooks.LootBonus => $"Loot +{v * 100:0}%",
        PerkHooks.FieldHealBonus => $"Healing items +{v * 100:0}%",
        PerkHooks.ReviveHpBonus => $"Revive +{v:0} HP",
        PerkHooks.BleedoutRoundsBonus => $"Bleed-out +{v:0} round",
        PerkHooks.SurgerySuccessBonus => $"Surgery odds +{v * 100:0}",
        PerkHooks.SurgerySpeedBonus => $"Surgery {v * 100:0}% faster",
        PerkHooks.CyberSurgery => "Fits cyborg-grade parts",
        PerkHooks.CyberInterferenceReduction => $"Network interference −{v * 100:0}%",
        PerkHooks.ArmorBonus => $"Armor +{v:0}",
        PerkHooks.ThermalRadius => $"Thermal sight {v:0} tiles",
        PerkHooks.NightDetectionBonus => $"Detection +{v:0} at night",
        _ => $"{hook} {v:0.##}",
    };

    /// <summary>"T3 Cyborg".</summary>
    public static string Tier(CyberneticsData cyber, int tier) => $"T{tier} {cyber.Tier(tier).Name}";

    /// <summary>"T3 Cyborg · Runner", or "T3 Cyborg" for a standard part.</summary>
    public static string TierAndSpec(CyberneticsData cyber, CyberNodeDef node) =>
        Tier(cyber, node.Tier) + (node.Spec is { } spec && cyber.Specs.TryGetValue(spec, out var s) ? $" · {s.Name}" : "");

    /// <summary>The tier's stand-in tint (white when it has none).</summary>
    public static Color Tint(CyberneticsData cyber, int tier) => cyber.Tier(tier).Tint is { } hex ? new Color(hex) : Colors.White;

    /// <summary>A prosthetic item's field stats as short phrases (armor, stealth, sight, attacks, jumps, hacking).</summary>
    public static IEnumerable<string> ItemStats(ItemDef item)
    {
        if (item.Armor != 0) yield return $"Armor {Fmt.Delta(item.Armor)}";
        if (item.Stealth != 0) yield return $"Stealth {Fmt.Delta(item.Stealth)}";
        if (item.SightBonus != 0) yield return $"Sight {Fmt.Delta(item.SightBonus)}";
        if (item.Detection != 0) yield return $"Detection {Fmt.Delta(item.Detection)}";
        if (item.Initiative != 0) yield return $"Initiative {Fmt.Delta(item.Initiative)}";
        if (item.CarryBonus != 0) yield return $"Carry {Fmt.Delta(item.CarryBonus)} kg";
        foreach (var a in item.Actions) yield return $"{a.Name} {a.MinDamage}–{a.MaxDamage} ({a.Pattern.ToString().ToLowerInvariant()}, reach {a.Reach}, {a.ApCost} AP)";
        foreach (var m in item.Movement?.AddModes ?? []) yield return $"{m.Pattern} jump ({m.ApCost} AP)";
        if (item.HackTool is { } tool) yield return $"Built-in hacking tool (range {tool.Range}, +{tool.ChanceBonus * 100:0})";
    }

    /// <summary>A node's or set's effects as phrases; jobs are named when colony data is given.</summary>
    public static IEnumerable<string> Effects(CyberEffectsDef e, GameData? data = null)
    {
        string Job(string id) => data?.Jobs.GetValueOrDefault(id)?.Name ?? id;
        foreach (var (job, v) in e.JobOutput) yield return $"+{v * 100:0}% {Job(job)} output";
        foreach (var (job, v) in e.CraftQuality) yield return $"+{v * 100:0} {Job(job)} quality";
        foreach (var (hook, v) in e.Hooks) yield return Hook(hook, v);
        if (e.Movement is { } m)
        {
            if (m.RangeBonus != 0) yield return $"Move range {Fmt.Delta(m.RangeBonus)}";
            if (m.MaxRange is { } cap) yield return $"Move range at most {cap}";
            foreach (var mode in m.AddModes) yield return $"{mode.Pattern} jump ({mode.ApCost} AP)";
        }
    }

    /// <summary>Humanity band name and colour: whole (no mood cap), strained (mood capped), detached (bonds suffer).</summary>
    public static (string Name, Color Color) Humanity(HumanityRules rules, double humanity) =>
        humanity >= rules.MoodCapFrom ? ("whole", Palette.Good)
        : humanity >= rules.Detached ? ("strained", Palette.Warn)
        : ("detached", Palette.Danger);

    /// <summary>"Left leg", "Eyes".</summary>
    public static string Part(BodyPart part) => part switch
    {
        BodyPart.LeftArm => "Left arm",
        BodyPart.RightArm => "Right arm",
        BodyPart.LeftLeg => "Left leg",
        BodyPart.RightLeg => "Right leg",
        _ => part.ToString(),
    };

    /// <summary>Upkeep line for a powered part: "upkeep 100% · service in 6d" / "worn 72% — needs parts".</summary>
    public static string? Upkeep(CyberneticsData cyber, BodyPartState part, int ticksPerDay)
    {
        var tier = cyber.Tier(cyber.TierOf(part));
        if (tier.ServiceDays <= 0) return null;
        var due = tier.ServiceDays * ticksPerDay - part.ServiceTicks;
        if (part.Upkeep < 1 - 1e-9) return $"worn {Fmt.Percent(part.Upkeep)} — service overdue";
        return due > 0 ? $"service in {due / (double)ticksPerDay:0.#}d" : "service due";
    }

    /// <summary>The cyber slot a body part belongs to (null for head and torso).</summary>
    public static CyberSlot? Slot(BodyPart part) => CyberneticsData.SlotOf(part);

    /// <summary>The prosthetic item's node, or null.</summary>
    public static CyberNodeDef? Node(CyberneticsData? cyber, BodyPartState part) =>
        part.Condition == PartCondition.Prosthetic ? cyber?.Node(part.ProstheticItemId) : null;

    /// <summary>Field effects (hooks, moves) of every working node and set on the body, joined (for a compact field line).</summary>
    public static string WorkingEffects(CyberneticsData cyber, Body body) =>
        string.Join(" · ", Cybernetics.WorkingEffects(cyber, body).SelectMany(e => Effects(e with { JobOutput = new(), CraftQuality = new() })).Distinct());
}
