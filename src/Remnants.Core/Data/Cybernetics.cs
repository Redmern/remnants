using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Data;

/// <summary>Which limbs a cybernetic replaces: either arm, either leg, or the eyes.</summary>
public enum CyberSlot { Arm, Leg, Eye }

/// <summary>
/// The cybernetics tree (data/cybernetics.json): tiers, the prosthetic designs (nodes) that form a tree per slot,
/// paired-limb sets, and the rules for surgery, humanity, upkeep and Network interference. Each node is a prosthetic
/// item (items.json, same id) made by the recipe that outputs it (recipes.json). The field stats of a fitted prosthetic
/// (armor, stealth, sight, its own attacks, extra move modes, a built-in hacking tool) live on that item; the node adds
/// its tier, specialisation, tree requirements and the effects that read like perks (job output, crafting quality,
/// scavenge hooks). See ARCHITECTURE.md "Cybernetics" and BALANCE.md "Cybernetics".
/// </summary>
public sealed record CyberneticsDef
{
    public List<CyberTierDef> Tiers { get; init; } = new();
    public List<CyberSpecDef> Specs { get; init; } = new();
    public List<CyberNodeDef> Nodes { get; init; } = new();
    public List<CyberSetDef> Sets { get; init; } = new();
    public SurgeryRules Surgery { get; init; } = new();
    public HumanityRules Humanity { get; init; } = new();
    public UpkeepRules Upkeep { get; init; } = new();
    public InterferenceRules Interference { get; init; } = new();
}

/// <summary>
/// A tier: T0 Crude, T1 Mechanical, T2 Powered, T3 Cyborg, T4 Network-grade. Humanity cost of a replacement and of an
/// elective augmentation (null = not allowed electively), upkeep, and the look until tier models exist.
/// </summary>
public sealed record CyberTierDef
{
    public int Tier { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Humanity a prosthetic of this tier costs when it replaces a lost limb (or an older prosthetic).</summary>
    public double Humanity { get; init; }

    /// <summary>Humanity it costs fitted on a healthy part; null when the tier cannot be fitted electively.</summary>
    public double? ElectiveHumanity { get; init; }

    /// <summary>Days between services; 0 = never needs it (crude and mechanical parts).</summary>
    public double ServiceDays { get; init; }

    /// <summary>Stockpile resources one service takes.</summary>
    public Dictionary<string, double> ServiceCost { get; init; } = new();

    /// <summary>Network-grade: subject to interference in the field (<see cref="InterferenceRules"/>).</summary>
    public bool Interference { get; init; }

    /// <summary>Tint of the stand-in model (#rrggbb) until per-tier models exist; null = the model's own colours.</summary>
    public string? Tint { get; init; }
}

/// <summary>A specialisation branch (Labor, Precision, Runner, ...): a name and what it is for, shown in the tree.</summary>
public sealed record CyberSpecDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public CyberSlot Slot { get; init; }
    public string Description { get; init; } = "";
}

/// <summary>The perk-like effects of a node or a set: summed like perk effects (see <c>PerkEffects</c>).</summary>
public sealed record CyberEffectsDef
{
    /// <summary>Job id → output bonus (0.1 = +10%) at that job.</summary>
    public Dictionary<string, double> JobOutput { get; init; } = new();

    /// <summary>Job id → added to the crafting quality score when the wearer is the building's best worker.</summary>
    public Dictionary<string, double> CraftQuality { get; init; } = new();

    /// <summary>Scavenge hook id (<c>PerkHooks</c>) → value, added to the wearer's perk hooks while the part works.</summary>
    public Dictionary<string, double> Hooks { get; init; } = new();

    /// <summary>Sets only: a movement modifier while the whole set works (a jumper pair jumps cheaper).</summary>
    public MovementModifier? Movement { get; init; }
}

/// <summary>
/// One design in the tree. Its id is the prosthetic item's id. The way in is one of <see cref="Requires"/> (nodes of a
/// lower tier) known to the colony — crafted, fitted or owned; tier 0 is known from the start — or, instead, its
/// <see cref="Blueprint"/> studied (a blueprint skips the tier below). On top: the perk someone at home must have.
/// Building and level come from the recipe that makes it; components are that recipe's item inputs.
/// </summary>
public sealed record CyberNodeDef
{
    public string Id { get; init; } = "";
    public CyberSlot Slot { get; init; }
    public int Tier { get; init; }

    /// <summary>Specialisation id (<see cref="CyberSpecDef"/>); null for the standard part of a tier.</summary>
    public string? Spec { get; init; }

    public List<string> Requires { get; init; } = new();
    public string? Perk { get; init; }

    /// <summary>A blueprint that opens this design without knowing the tier below.</summary>
    public string? Blueprint { get; init; }

    /// <summary>
    /// Model variant: the game shows <c>prosthetic_{arm|leg}_{model}_{l|r}</c> (and falls back to the cyborg one), tinted
    /// by the tier. The planned per-tier meshes drop in by changing this id.
    /// </summary>
    public string Model { get; init; } = "cyborg";

    public string Description { get; init; } = "";
    public CyberEffectsDef Effects { get; init; } = new();
}

/// <summary>Paired limbs: <see cref="Count"/> parts of the slot with this specialisation (of at least <see cref="MinTier"/>) give a bonus.</summary>
public sealed record CyberSetDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public CyberSlot Slot { get; init; }
    public string Spec { get; init; } = "";
    public int Count { get; init; } = 2;
    public int MinTier { get; init; } = 2;
    public string Description { get; init; } = "";
    public CyberEffectsDef Effects { get; init; } = new();
}

/// <summary>
/// Fitting a prosthetic at the Med Bay. Crude and mechanical parts (below <see cref="FromTier"/>) strap on at once
/// (<c>FitProsthetic</c>); from it on they take surgery: the patient lies in a Med Bay bed while its medics work
/// <see cref="Work"/>[tier] effort, using medicine. Then a roll:
/// <code>
/// effort/tick = Σ medics (performance · tool · health · mood · perks) · bay level multiplier · (1 + best medic's surgery_speed_bonus)
/// success     = baseChance + perMedicine · best Medicine skill + best medic's surgery_success_bonus − tierPenalty[tier]
///               − (elective ? electivePenalty : 0), clamped to [minChance, maxChance]
/// </code>
/// A failed surgery still fits the part but leaves a complication: a torso wound of <see cref="ComplicationSeverity"/>
/// (an infection) that keeps the patient in bed and needs medicine. Never death on the table.
/// </summary>
public sealed record SurgeryRules
{
    public int FromTier { get; init; } = 2;

    /// <summary>From this tier on the operating medic needs the <c>cyber_surgery</c> hook (Cyber-Surgeon).</summary>
    public int CyberSurgeonFromTier { get; init; } = 3;

    /// <summary>Elective augmentation is allowed from this tier on (and only for adults).</summary>
    public int ElectiveFromTier { get; init; } = 3;

    /// <summary>Work per tier (index = tier).</summary>
    public List<double> Work { get; init; } = new();

    /// <summary>Success penalty per tier (index = tier).</summary>
    public List<double> TierPenalty { get; init; } = new();

    public double MedicinePerTick { get; init; } = 0.02;
    public double BaseChance { get; init; } = 0.7;
    public double PerMedicine { get; init; } = 0.004;
    public double ElectivePenalty { get; init; }
    public double MinChance { get; init; } = 0.3;
    public double MaxChance { get; init; } = 0.97;

    /// <summary>[min, max] severity of the torso infection a failed surgery leaves.</summary>
    public double[] ComplicationSeverity { get; init; } = [0.3, 0.55];
}

/// <summary>
/// Humanity (100 − the fitted prosthetics' costs). Below <see cref="MoodCapFrom"/> a survivor's mood is capped, linearly
/// down to <see cref="MoodCapAtZero"/> at humanity 0. Below <see cref="Detached"/> their partner's bond erodes by
/// <see cref="PartnerStrainPerTick"/> · (detached − humanity) / detached per tick (the couple splits under
/// <see cref="BreakupBelow"/>) and their children's mood is capped at <see cref="ChildMoodCap"/>. Every
/// <see cref="LineEveryTicks"/> each detached survivor at home has <see cref="LineChance"/> of a line in the log.
/// </summary>
public sealed record HumanityRules
{
    public double MoodCapFrom { get; init; } = 70;
    public double MoodCapAtZero { get; init; } = 40;
    public double Detached { get; init; } = 50;
    public double PartnerStrainPerTick { get; init; } = 0.05;
    public double BreakupBelow { get; init; } = 40;
    public double ChildMoodCap { get; init; } = 70;
    public int LineEveryTicks { get; init; } = 48;
    public double LineChance { get; init; } = 0.3;

    /// <summary>Log lines; <c>{name}</c> is the survivor.</summary>
    public List<string> Lines { get; init; } = new();
}

/// <summary>
/// Upkeep of powered parts: every tier's <c>serviceDays</c> the colony pays its <c>serviceCost</c> from the stockpile
/// (automatically). While it cannot, the part loses <see cref="DecayPerDay"/> upkeep a day down to <see cref="MinUpkeep"/>;
/// the next paid service restores it.
/// </summary>
public sealed record UpkeepRules
{
    public double DecayPerDay { get; init; } = 0.05;
    public double MinUpkeep { get; init; } = 0.6;
}

/// <summary>
/// Network interference with Network-grade parts in the field. At the start of each round every such part of an active
/// unit is rolled: it locks up for the round (works at 0) with
/// <code>
/// chance = min(maxChance, perLinked · Linked on the Network within range + overrider (a hostile with Override within range)
///          + jammer (any signal jammer covering the unit, its own side's too)) · (1 − cyber_interference_reduction)
/// </code>
/// </summary>
public sealed record InterferenceRules
{
    public int Range { get; init; } = 6;
    public double PerLinked { get; init; } = 0.06;
    public double Overrider { get; init; } = 0.1;
    public double Jammer { get; init; } = 0.15;
    public double MaxChance { get; init; } = 0.35;
}

/// <summary>Lookups over a loaded <see cref="CyberneticsDef"/>.</summary>
public sealed class CyberneticsData
{
    public CyberneticsDef Def { get; }
    public IReadOnlyDictionary<string, CyberNodeDef> Nodes { get; }
    public IReadOnlyDictionary<string, CyberSpecDef> Specs { get; }

    public CyberneticsData(CyberneticsDef def)
    {
        Def = def;
        var nodes = new Dictionary<string, CyberNodeDef>(StringComparer.Ordinal);
        foreach (var n in def.Nodes)
            if (string.IsNullOrWhiteSpace(n.Id) || !nodes.TryAdd(n.Id, n))
                throw new GameDataException($"Cybernetics: node id '{n.Id}' is empty or used twice.");
        Nodes = nodes;
        Specs = def.Specs.ToDictionary(s => s.Id, StringComparer.Ordinal);
        Validate();
    }

    public SurgeryRules Surgery => Def.Surgery;
    public HumanityRules Humanity => Def.Humanity;

    public CyberTierDef Tier(int tier) => Def.Tiers.Find(t => t.Tier == tier) ?? new CyberTierDef { Tier = tier, Name = $"T{tier}" };

    public CyberNodeDef? Node(string? itemId) => itemId is not null && Nodes.TryGetValue(itemId, out var n) ? n : null;

    public static IEnumerable<BodyPart> Parts(CyberSlot slot) => slot switch
    {
        CyberSlot.Arm => [BodyPart.LeftArm, BodyPart.RightArm],
        CyberSlot.Leg => [BodyPart.LeftLeg, BodyPart.RightLeg],
        _ => [BodyPart.Eyes],
    };

    public static CyberSlot? SlotOf(BodyPart part) => part switch
    {
        BodyPart.LeftArm or BodyPart.RightArm => CyberSlot.Arm,
        BodyPart.LeftLeg or BodyPart.RightLeg => CyberSlot.Leg,
        BodyPart.Eyes => CyberSlot.Eye,
        _ => null,
    };

    /// <summary>
    /// The tier of a fitted part: its node's, or for a prosthetic outside the tree (older saves) T0 below quality 1 and
    /// T3 from there.
    /// </summary>
    public int TierOf(BodyPartState part) => Node(part.ProstheticItemId)?.Tier ?? (part.ProstheticQuality < 1 ? 0 : 3);

    /// <summary>Humanity a part of this tier costs: as a replacement, or electively (null when not allowed).</summary>
    public double? HumanityCost(int tier, bool elective) => elective ? Tier(tier).ElectiveHumanity : Tier(tier).Humanity;

    /// <summary>Sets whose condition the body meets (enough working-or-not parts of the spec), with the parts they use.</summary>
    public IEnumerable<(CyberSetDef Set, List<BodyPart> Parts)> ActiveSets(Body body)
    {
        foreach (var set in Def.Sets)
        {
            var parts = Parts(set.Slot)
                .Where(p => body[p].Condition == PartCondition.Prosthetic && Node(body[p].ProstheticItemId) is { } n
                            && n.Spec == set.Spec && n.Tier >= set.MinTier)
                .ToList();
            if (parts.Count >= set.Count) yield return (set, parts);
        }
    }

    private void Validate()
    {
        var tiers = Def.Tiers.Select(t => t.Tier).ToHashSet();
        foreach (var n in Def.Nodes)
        {
            var where = $"Cybernetics node '{n.Id}'";
            if (!tiers.Contains(n.Tier)) throw new GameDataException($"{where}: unknown tier {n.Tier}.");
            if (n.Spec is { } spec && (!Specs.TryGetValue(spec, out var s) || s.Slot != n.Slot))
                throw new GameDataException($"{where}: unknown specialisation '{spec}' for its slot.");
            foreach (var r in n.Requires)
                if (!Nodes.TryGetValue(r, out var req) || req.Slot != n.Slot && n.Slot != CyberSlot.Eye || req.Tier >= n.Tier)
                    throw new GameDataException($"{where}: requirement '{r}' must be a node of a lower tier (and its slot).");
            if (n.Tier > 0 && n.Requires.Count == 0) throw new GameDataException($"{where}: only tier 0 has no requirement.");
        }
        foreach (var set in Def.Sets)
            if (!Specs.TryGetValue(set.Spec, out var s) || s.Slot != set.Slot || set.Count < 1 || set.Count > Parts(set.Slot).Count())
                throw new GameDataException($"Cybernetics set '{set.Id}': unknown specialisation or impossible count.");
        var surgery = Def.Surgery;
        var maxTier = tiers.DefaultIfEmpty(0).Max();
        if (surgery.Work.Count <= maxTier || surgery.TierPenalty.Count <= maxTier)
            throw new GameDataException("Cybernetics surgery: work and tierPenalty need an entry per tier.");
        if (surgery.ComplicationSeverity.Length != 2 || surgery.ComplicationSeverity[0] > surgery.ComplicationSeverity[1])
            throw new GameDataException("Cybernetics surgery: complicationSeverity must be [min, max].");
    }
}
