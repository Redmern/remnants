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
    public const string PowerEfficiency = "power_efficiency";
    public const string DisassemblyYieldBonus = "disassembly_yield_bonus";

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
    public const string MedicalApDiscount = "medical_ap_discount";

    // Cybernetics (perks and fitted parts)
    public const string CyberInterferenceReduction = "cyber_interference_reduction";
    public const string ArmorBonus = "armor_bonus";
    public const string ThermalRadius = "thermal_radius";
    public const string NightDetectionBonus = "night_detection_bonus";

    // Defense (colony raids)
    public const string TrapCostReduction = "trap_cost_reduction";
    public const string RepairSpeedBonus = "repair_speed_bonus";
    public const string PostDefenseBonus = "post_defense_bonus";
    public const string SentryLeadHours = "sentry_lead_hours";
    // Food
    public const string ShelfLifeBonus = "shelf_life_bonus";

    // Story traits (unique survivors; docs/story/survivors.md)
    public const string DecryptWorkMult = "decrypt_work_mult";
    public const string NoScavenge = "no_scavenge";
    public const string LinkPort = "link_port";
    public const string RefusesTier = "refuses_tier";
    public const string Mentor = "mentor";
    public const string PartyCarryMult = "party_carry_mult";
    public const string MoodFoodSurplus = "mood_food_surplus";
    public const string HackRange = "hack_range";
    public const string HackWithoutKit = "hack_without_kit";
    public const string OverrideImmune = "override_immune";
    public const string HumanityGrowthPerDay = "humanity_growth_per_day";
    public const string NearbyMood = "nearby_mood";
    public const string NetworkEchoChance = "network_echo_chance";
    public const string MoodFollowsHumanity = "mood_follows_humanity";
    public const string MoodOnLinkedKilled = "mood_on_linked_killed";

    /// <summary>Every hook id with what it means; perks.json may only name these.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [HackChanceBonus] = "Added to the chance to hack a device or unit (0.1 = +10 points).",
        [HackAlarmReduction] = "Share of the alarm/noise a hack raises that is removed (0.5 = half as loud).",
        [CanHackLinked] = "Flag: can hack Linked units, not only Husks and devices.",
        [FieldRepair] = "Flag/strength: can jury-rig gear and devices in the field (value = repair strength).",
        [HackApDiscount] = "AP taken off the cost of a hack.",
        [PowerEfficiency] = "Colony: share taken off every consumer's power draw while this survivor is home (the best one counts; 0.15 = −15%).",
        [DisassemblyYieldBonus] = "Colony: chance per component of one more when this survivor is the bench's best worker at a disassembly (0.25 = +25%).",
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
        [MedicalApDiscount] = "AP taken off the cost of a medical item in the field (never below 1).",
        [ShelfLifeBonus] = "Colony: preserved food this cook makes keeps this much longer (0.5 = +50% shelf life).",
        [CyberInterferenceReduction] = "Share of the Network interference chance on Network-grade parts that is removed (0.5 = half).",
        [ArmorBonus] = "Flat damage reduction added (armored legs).",
        [ThermalRadius] = "Hostiles within this many tiles are seen through walls and stealth, like a scanner (thermal eye).",
        [NightDetectionBonus] = "Detection added at night (hook for the day-night cycle; nothing reads it yet).",
        [TrapCostReduction] = "Colony: share off the build and re-arm cost of traps while this survivor is home (best counts, at most 0.5).",
        [RepairSpeedBonus] = "Colony: repairs go this much faster while this survivor is home (best counts; 0.3 = +30%).",
        [PostDefenseBonus] = "Colony raids: a guard on duty at a post fights this much better (0.25 = +25%).",
        [SentryLeadHours] = "Colony raids: hours of warning added while this survivor is on guard duty.",
        [DecryptWorkMult] = "Colony: decrypt orders take this share less work while this survivor is home (0.5 = half the work).",
        [NoScavenge] = "Colony flag: never goes on a scavenge unless the story mission needs them.",
        [LinkPort] = "Story flag: has a Link port, so can carry the key (M16).",
        [RefusesTier] = "Colony: will not lead surgery on parts of this tier or higher.",
        [Mentor] = "Colony: teaches Medicine up to this skill level at the Med Bay (as a Study teacher).",
        [PartyCarryMult] = "Field: carry capacity bonus for every unit in this survivor's party (0.15 = +15%).",
        [MoodFoodSurplus] = "Colony: mood this much higher while the colony has a day's food per mouth, this much lower when not.",
        [HackRange] = "Field: can hack a device this many tiles away (1 = adjacent, as everyone).",
        [HackWithoutKit] = "Field flag: hacks with no kit, as if carrying a hacking kit.",
        [OverrideImmune] = "Field flag: the Linked cannot Override this unit.",
        [HumanityGrowthPerDay] = "Colony: humanity regained per day at home (Seven), up to the trait's cap of 60.",
        [NearbyMood] = "Colony: everyone else's mood changes by this much while this survivor's humanity is under 30.",
        [NetworkEchoChance] = "Field: chance a story mission gets an extra Linked patrol when this survivor is in the party.",
        [MoodFollowsHumanity] = "Colony flag: mood is capped at the colony's average humanity + 30.",
        [MoodOnLinkedKilled] = "Colony: mood change when this survivor's party kills Linked (once per mission).",
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
    public static PerkEffects For(Survivor s, GameData data) => For(s.Perks, data, s.Body, s.TraitHooks);

    /// <summary>
    /// The effects of these perks, plus (with a <paramref name="body"/> and the cybernetics tree loaded) those of the
    /// body's working prosthetics and paired-limb sets (<c>cybernetics.json</c> node and set effects).
    /// </summary>
    public static PerkEffects For(IEnumerable<string> perkIds, GameData data, Body? body = null, IReadOnlyDictionary<string, double>? traitHooks = null)
    {
        // Called many times a tick: the maps are made only when something goes in them, and a body without prosthetics
        // skips the cybernetics walk (its effects need a working prosthetic).
        Dictionary<string, double>? jobOutput = null, craftQuality = null, hooks = null;
        double study = 0, xp = 0;
        static void Add(ref Dictionary<string, double>? to, Dictionary<string, double> from)
        {
            if (from.Count == 0) return;
            to ??= new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (k, v) in from) to[k] = to.GetValueOrDefault(k) + v;
        }
        foreach (var id in perkIds)
        {
            if (!data.Perks.TryGetValue(id, out var perk)) continue;
            var e = perk.Effects;
            Add(ref jobOutput, e.JobOutput);
            Add(ref craftQuality, e.CraftQuality);
            Add(ref hooks, e.Hooks);
            study += e.StudySpeed;
            xp += e.XpBonus;
        }
        if (body is not null && data.Cybernetics is { } cyber && HasProsthetic(body))
            foreach (var e in Cybernetics.WorkingEffects(cyber, body))
            {
                Add(ref jobOutput, e.JobOutput);
                Add(ref craftQuality, e.CraftQuality);
                Add(ref hooks, e.Hooks);
            }
        if (traitHooks is { Count: > 0 })
        {
            hooks ??= new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (k, v) in traitHooks) hooks[k] = hooks.GetValueOrDefault(k) + v;
        }
        if (jobOutput is null && craftQuality is null && hooks is null && study == 0 && xp == 0) return None;
        return new PerkEffects(jobOutput ?? Empty, craftQuality ?? Empty, hooks ?? Empty, study, xp);
    }

    /// <summary>Shared by every effects set with nothing of a kind; never written.</summary>
    private static readonly Dictionary<string, double> Empty = new(StringComparer.Ordinal);

    private static bool HasProsthetic(Body body)
    {
        foreach (var part in body.Parts.Values)
            if (part.Condition == PartCondition.Prosthetic) return true;
        return false;
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
