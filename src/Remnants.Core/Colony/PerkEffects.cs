using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Core.Colony;

/// <summary>
/// Named numbers perks give the scavenge mode. Core only stores and sums them; the scavenge rules read them through
/// <see cref="PerkEffects.For(Survivor, GameData)"/>. Values of several perks add up; a flag is "on" above 0.
/// Every id is documented in ARCHITECTURE.md ("Perks and study").
/// </summary>
public static class PerkHooks
{
    // Tech
    public const string HackChanceBonus = "hack_chance_bonus";
    public const string HackAlarmReduction = "hack_alarm_reduction";
    public const string CanHackLinked = "can_hack_linked";
    public const string FieldRepair = "field_repair";
    public const string HackApDiscount = "hack_ap_discount";

    // Drones
    public const string DroneRangeBonus = "drone_range_bonus";
    public const string DroneBatteryBonus = "drone_battery_bonus";
    public const string DroneCanMark = "drone_can_mark";
    public const string DronePayload = "drone_payload";

    // Combat
    public const string AccuracyBonus = "accuracy_bonus";
    public const string MeleeDamageBonus = "melee_damage_bonus";
    public const string OverwatchReactionsBonus = "overwatch_reactions_bonus";
    public const string BraceCoverBonus = "brace_cover_bonus";

    // Survival / scout
    public const string StealthBonus = "stealth_bonus";
    public const string DetectionBonus = "detection_bonus";
    public const string SightBonus = "sight_bonus";
    public const string CarryBonus = "carry_bonus";
    public const string LootBonus = "loot_bonus";

    // Mechanics (colony side: the Garage's best mechanic)
    public const string RepairPartsDiscount = "repair_parts_discount";
    public const string EngineTuneBonus = "engine_tune_bonus";

    // Medic
    public const string FieldHealBonus = "field_heal_bonus";
    public const string ReviveHpBonus = "revive_hp_bonus";
    public const string BleedoutRoundsBonus = "bleedout_rounds_bonus";
    public const string SurgerySuccessBonus = "surgery_success_bonus";
    public const string SurgerySpeedBonus = "surgery_speed_bonus";
    public const string CyberSurgery = "cyber_surgery";

    // Cybernetics (perks and fitted parts)
    public const string CyberInterferenceReduction = "cyber_interference_reduction";
    public const string ArmorBonus = "armor_bonus";
    public const string ThermalRadius = "thermal_radius";
    public const string NightDetectionBonus = "night_detection_bonus";

    /// <summary>Every hook id with what it means; perks.json may only name these.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [HackChanceBonus] = "Added to the chance to hack a device or unit (0.1 = +10 points).",
        [HackAlarmReduction] = "Share of the alarm/noise a hack raises that is removed (0.5 = half as loud).",
        [CanHackLinked] = "Flag: can hack Linked units, not only Husks and devices.",
        [FieldRepair] = "Flag/strength: can jury-rig gear and devices in the field (value = repair strength).",
        [HackApDiscount] = "AP taken off the cost of a hack.",
        [DroneRangeBonus] = "Tiles added to a scout drone's control range.",
        [DroneBatteryBonus] = "Rounds added to a scout drone's battery.",
        [DroneCanMark] = "Flag: the drone marks targets it sees for the whole party (revealed to the side).",
        [DronePayload] = "Flag: the drone can carry and drop a flare or motion sensor.",
        [AccuracyBonus] = "Added to hit chance with any attack (0.05 = +5 points).",
        [MeleeDamageBonus] = "Melee damage multiplier bonus (0.15 = +15%).",
        [OverwatchReactionsBonus] = "Extra overwatch reactions.",
        [BraceCoverBonus] = "Cover added while braced.",
        [StealthBonus] = "Added to stealth.",
        [DetectionBonus] = "Added to detection.",
        [SightBonus] = "Tiles added to sight.",
        [CarryBonus] = "Kg added to carry capacity.",
        [LootBonus] = "Extra share of items found in caches (0.2 = +20%).",
        [FieldHealBonus] = "Healing items restore this much more (0.25 = +25%).",
        [ReviveHpBonus] = "HP added when reviving a downed ally.",
        [RepairPartsDiscount] = "Share of spare parts saved on vehicle repairs at the Garage where this survivor works (0.3 = 30% fewer).",
        [EngineTuneBonus] = "Added to the quality scale of engine upgrades fitted at the Garage where this survivor works (0.15 = +15%).",
        [BleedoutRoundsBonus] = "Rounds added to a downed unit's bleed-out timer when this survivor stabilizes them.",
        [SurgerySuccessBonus] = "Colony: added to the success chance of a surgery this medic leads (0.1 = +10 points).",
        [SurgerySpeedBonus] = "Colony: surgery effort multiplier bonus when this medic leads it (0.25 = +25%).",
        [CyberSurgery] = "Colony flag: can lead surgery on cyborg-grade (tier 3+) prosthetics.",
        [CyberInterferenceReduction] = "Share of the Network interference chance on Network-grade parts that is removed (0.5 = half).",
        [ArmorBonus] = "Flat damage reduction added (armored legs).",
        [ThermalRadius] = "Hostiles within this many tiles are seen through walls and stealth, like a scanner (thermal eye).",
        [NightDetectionBonus] = "Detection added at night (hook for the day-night cycle; nothing reads it yet).",
    };
}

/// <summary>
/// The summed effects of a survivor's perks and fitted cybernetics: colony modifiers (job output, crafting quality, study speed, XP) and the
/// scavenge hooks (<see cref="PerkHooks"/>). Pure; build one whenever needed.
/// </summary>
public sealed class PerkEffects
{
    public static readonly PerkEffects None = new(new(), new(), new(StringComparer.Ordinal), 0, 0);

    private readonly Dictionary<string, double> _jobOutput;
    private readonly Dictionary<string, double> _craftQuality;
    private readonly Dictionary<string, double> _hooks;

    private PerkEffects(Dictionary<string, double> jobOutput, Dictionary<string, double> craftQuality,
        Dictionary<string, double> hooks, double studySpeed, double xpBonus)
    {
        _jobOutput = jobOutput;
        _craftQuality = craftQuality;
        _hooks = hooks;
        StudySpeed = studySpeed;
        XpBonus = xpBonus;
    }

    /// <summary>The effects of every perk this survivor has (perks missing from the data are ignored).</summary>
    public static PerkEffects For(Survivor s, GameData data) => For(s.Perks, data, s.Body);

    /// <summary>
    /// The effects of these perks, plus (with a <paramref name="body"/> and the cybernetics tree loaded) those of the
    /// body's working prosthetics and paired-limb sets (<c>cybernetics.json</c> node and set effects).
    /// </summary>
    public static PerkEffects For(IEnumerable<string> perkIds, GameData data, Body? body = null)
    {
        var jobOutput = new Dictionary<string, double>(StringComparer.Ordinal);
        var craftQuality = new Dictionary<string, double>(StringComparer.Ordinal);
        var hooks = new Dictionary<string, double>(StringComparer.Ordinal);
        double study = 0, xp = 0;
        foreach (var id in perkIds)
        {
            if (!data.Perks.TryGetValue(id, out var perk)) continue;
            var e = perk.Effects;
            foreach (var (k, v) in e.JobOutput) jobOutput[k] = jobOutput.GetValueOrDefault(k) + v;
            foreach (var (k, v) in e.CraftQuality) craftQuality[k] = craftQuality.GetValueOrDefault(k) + v;
            foreach (var (k, v) in e.Hooks) hooks[k] = hooks.GetValueOrDefault(k) + v;
            study += e.StudySpeed;
            xp += e.XpBonus;
        }
        if (body is not null && data.Cybernetics is { } cyber)
            foreach (var e in Cybernetics.WorkingEffects(cyber, body))
            {
                foreach (var (k, v) in e.JobOutput) jobOutput[k] = jobOutput.GetValueOrDefault(k) + v;
                foreach (var (k, v) in e.CraftQuality) craftQuality[k] = craftQuality.GetValueOrDefault(k) + v;
                foreach (var (k, v) in e.Hooks) hooks[k] = hooks.GetValueOrDefault(k) + v;
            }
        return new PerkEffects(jobOutput, craftQuality, hooks, study, xp);
    }

    public double StudySpeed { get; }
    public double XpBonus { get; }

    /// <summary>All scavenge hooks with a value, by id.</summary>
    public IReadOnlyDictionary<string, double> Hooks => _hooks;

    /// <summary>A scavenge hook's summed value (0 when no perk gives it).</summary>
    public double Hook(string id) => _hooks.GetValueOrDefault(id);

    /// <summary>A flag hook: true when any perk gives it a positive value.</summary>
    public bool Has(string id) => Hook(id) > 0;

    /// <summary>Output bonus at this job (0.1 = +10%).</summary>
    public double JobOutputBonus(string jobId) => _jobOutput.GetValueOrDefault(jobId);

    /// <summary>Added to the crafting quality score at this job.</summary>
    public double CraftQualityBonus(string jobId) => _craftQuality.GetValueOrDefault(jobId);
}
