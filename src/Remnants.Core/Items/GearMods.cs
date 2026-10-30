using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Items;

/// <summary>gear_mods.json: mod slots per gear type, the mods, Mk upgrades and their kits, recipes and extra loot.</summary>
public sealed record GearModsDef
{
    public GearModRules Rules { get; init; } = new();
    public List<ModSlotDef> Slots { get; init; } = new();
    public Dictionary<string, GearTypeDef> Types { get; init; } = new();

    /// <summary>Moddable gear: item id → its type and upgrade family.</summary>
    public Dictionary<string, ModdableDef> Gear { get; init; } = new();
    public List<MarkDef> Marks { get; init; } = new();
    public Dictionary<string, GearFamilyDef> Families { get; init; } = new();
    public List<GearModDef> Mods { get; init; } = new();
    public List<UpgradeKitDef> Kits { get; init; } = new();

    /// <summary>Plain items that come with the mods (headgear).</summary>
    public List<ItemDef> Items { get; init; } = new();

    /// <summary>Zone id → biome, for camo patterns.</summary>
    public Dictionary<string, string> ZoneBiomes { get; init; } = new();

    /// <summary>Bane tags added per enemy id, merged into enemies.json's.</summary>
    public Dictionary<string, List<string>> EnemyTags { get; init; } = new();

    /// <summary>Extra kill drops per enemy id, merged into enemies.json's.</summary>
    public Dictionary<string, List<EnemyDropDef>> Drops { get; init; } = new();

    public List<RecipeDef> Recipes { get; init; } = new();
}

public sealed record GearModRules
{
    /// <summary>Building types with a Gear Bench: mods go on and come off there.</summary>
    public List<string> Benches { get; init; } = new();

    /// <summary>Building types where Mk kits are fitted.</summary>
    public List<string> UpgradeAt { get; init; } = new();

    /// <summary>Perk hook needed to fit or remove a mod in the field (Jury-rig).</summary>
    public string FieldHook { get; init; } = "field_repair";
    public int FieldApCost { get; init; } = 2;

    /// <summary>Accuracy a ranged attack loses after its unit moved this turn.</summary>
    public double MovedAccuracyPenalty { get; init; }

    /// <summary>Who aims on the move: the Linked (<see cref="AiKind.Linked"/>) and machines (<see cref="AiKind.Device"/>).</summary>
    public List<AiKind> MovedPenaltyIgnoredBy { get; init; } = new();

    /// <summary>Human sight multiplier below which a light or thermal masking counts as "in the dark".</summary>
    public double DarkBelowSight { get; init; } = 0.95;
}

public sealed record ModSlotDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

public sealed record GearTypeDef
{
    public List<string> Slots { get; init; } = new();

    /// <summary>How many slots may be filled at Mk I, II, III (the last entry holds past the list).</summary>
    public List<int> SlotsByMark { get; init; } = new() { 1 };
}

public sealed record ModdableDef
{
    public string Type { get; init; } = "";

    /// <summary>Upgrade family (its kits); null: no Mk upgrades (an integrated gun arm).</summary>
    public string? Family { get; init; }
}

/// <summary>A Mk upgrade's stat step. Weapons take damage and accuracy; worn gear armor and coverage.</summary>
public sealed record MarkDef
{
    public int Mk { get; init; }
    public string Name { get; init; } = "";
    public double DamageMultiplier { get; init; } = 1;
    public double AccuracyBonus { get; init; }
    public double ArmorBonus { get; init; }
    public double ArmorMultiplier { get; init; } = 1;
    public double CoverageBonus { get; init; }
    public double WeightMultiplier { get; init; } = 1;
}

public sealed record GearFamilyDef
{
    public string Name { get; init; } = "";

    /// <summary>Mk (as a string key, JSON) → kit item id.</summary>
    public Dictionary<string, string> Kits { get; init; } = new();
}

public sealed record UpgradeKitDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Family { get; init; } = "";
    public int Mk { get; init; }
    public double Weight { get; init; }
    public int Tier { get; init; } = 1;
    public double LootWeight { get; init; }
    public string? Model { get; init; }
}

public sealed record GearModDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Slot { get; init; } = "";

    /// <summary>Gear types it fits.</summary>
    public List<string> Types { get; init; } = new();
    public double Weight { get; init; }
    public int Tier { get; init; } = 1;
    public double LootWeight { get; init; }

    /// <summary>Network-grade (cyan): made from Linked parts.</summary>
    public bool Network { get; init; }

    /// <summary>Model id fitted on the gear's slot node (null: nothing shows).</summary>
    public string? Model { get; init; }
    public ModEffect Effect { get; init; } = new();
}

/// <summary>
/// What a mod does. Weapon numbers change the gear's attacks (ranged mods its ranged attacks, melee mods its melee ones);
/// worn numbers change the piece. At a quality tier benefits scale by its multiplier and drawbacks shrink by it.
/// </summary>
public sealed record ModEffect
{
    // Attacks.
    public double Accuracy { get; init; }
    public Dictionary<string, double> AccuracyByAction { get; init; } = new();
    public double DamageMultiplier { get; init; } = 1;
    public int Reach { get; init; }
    public double NoiseMultiplier { get; init; } = 1;
    public int Reveal { get; init; }
    public int ApCost { get; init; }
    public double StrengthScaling { get; init; }
    public double ArmorPierce { get; init; }
    public double InjuryMultiplier { get; init; } = 1;
    public int CloseRange { get; init; }
    public int CloseApPenalty { get; init; }
    public double MovedPenaltyReduction { get; init; }
    public List<BaneDef> Banes { get; init; } = new();
    public double NetworkFeedback { get; init; }

    // The piece.
    public double Armor { get; init; }
    public double MeleeArmor { get; init; }
    public double InjuryResist { get; init; }
    public double Stealth { get; init; }
    public double Carry { get; init; }
    public double Sight { get; init; }
    public double Detection { get; init; }
    public int MoveNoise { get; init; }
    public CamoDef? Camo { get; init; }
    public List<string> WeatherProtection { get; init; } = new();
    public double ThermalMask { get; init; }
    public WeaveDef? NetworkWeave { get; init; }
    public LightDef? Light { get; init; }

    /// <summary>
    /// This effect at quality multiplier <paramref name="m"/>: what helps × m, what hurts ÷ m (a Fine scope aims better and
    /// is less clumsy up close; a Crude suppressor is louder and loses more damage).
    /// </summary>
    public ModEffect AtQuality(double m)
    {
        if (Math.Abs(m - 1) < 1e-9) return this;
        double Good(double v) => v >= 0 ? v * m : v / m;
        double Mult(double v, bool lowerIsBetter = false) =>
            (v < 1) == lowerIsBetter ? 1 + (v - 1) * m : 1 + (v - 1) / m;
        int Int(int v, bool positiveIsGood = true) => (v > 0) == positiveIsGood ? (int)Math.Round(v * m) : (int)Math.Round(v / m);
        return this with
        {
            Accuracy = Good(Accuracy),
            AccuracyByAction = AccuracyByAction.ToDictionary(kv => kv.Key, kv => Good(kv.Value)),
            DamageMultiplier = Mult(DamageMultiplier),
            Reach = Int(Reach),
            NoiseMultiplier = Mult(NoiseMultiplier, lowerIsBetter: true),
            Reveal = Int(Reveal, positiveIsGood: false),
            ApCost = Int(ApCost, positiveIsGood: false),
            StrengthScaling = Good(StrengthScaling),
            ArmorPierce = Good(ArmorPierce),
            InjuryMultiplier = Mult(InjuryMultiplier),
            CloseApPenalty = Int(CloseApPenalty, positiveIsGood: false),
            MovedPenaltyReduction = Math.Min(1, Good(MovedPenaltyReduction)),
            Banes = Banes.Select(b => b with
            {
                HitBonus = b.HitBonus * m,
                DamageMultiplier = 1 + (b.DamageMultiplier - 1) * m,
                ArmorPierce = b.ArmorPierce * m,
            }).ToList(),
            NetworkFeedback = NetworkFeedback / m,
            Armor = Good(Armor),
            MeleeArmor = Good(MeleeArmor),
            InjuryResist = Math.Min(0.9, Good(InjuryResist)),
            Stealth = Good(Stealth),
            Carry = Good(Carry),
            Sight = Good(Sight),
            Detection = Good(Detection),
            MoveNoise = Int(MoveNoise, positiveIsGood: false),
            Camo = Camo is { } c ? c with { Stealth = c.Stealth * m } : null,
            ThermalMask = ThermalMask * m,
            NetworkWeave = NetworkWeave is { } w ? w with
            {
                InterferenceReduction = Math.Min(1, w.InterferenceReduction * m),
                LinkedStealth = w.LinkedStealth * m,
                HuskLure = (int)Math.Round(w.HuskLure / m),
            } : null,
            Light = Light is { } l ? l with { Range = l.Range * m, StealthPenalty = l.StealthPenalty / m } : null,
        };
    }
}

/// <summary>A gear item's id taken apart: the item it was made from (with its quality), its Mk, and its mods.</summary>
public sealed record ModdedId(string BaseId, int Mk, IReadOnlyList<string> Mods)
{
    public const char Separator = '+';

    public override string ToString() => GearModsData.Compose(BaseId, Mk, Mods);
}

/// <summary>
/// Gear mods over the item catalogue: which gear takes which mods, what an id with mods means, and the item it resolves to.
/// A modded item's id is <c>base[@quality][+mk2|+mk3][+mod[@quality]]...</c> with the mods in the gear type's slot order,
/// so the same build always has the same id and stacks in an inventory.
/// </summary>
public sealed class GearModsData
{
    public GearModsDef Def { get; }
    private readonly Dictionary<string, GearModDef> _mods;
    private readonly Dictionary<string, UpgradeKitDef> _kits;

    public GearModsData(GearModsDef def)
    {
        Def = def;
        _mods = def.Mods.ToDictionary(m => m.Id, StringComparer.Ordinal);
        _kits = def.Kits.ToDictionary(k => k.Id, StringComparer.Ordinal);
    }

    public GearModDef? Mod(string modItemId) => _mods.GetValueOrDefault(Base(modItemId));
    public UpgradeKitDef? Kit(string itemId) => _kits.GetValueOrDefault(itemId);
    public ModdableDef? Moddable(string definedId) => Def.Gear.GetValueOrDefault(definedId);
    public GearTypeDef? TypeOf(string definedId) => Moddable(definedId) is { } g ? Def.Types.GetValueOrDefault(g.Type) : null;
    public string SlotName(string slot) => Def.Slots.Find(s => s.Id == slot)?.Name ?? slot;
    public MarkDef? Mark(int mk) => Def.Marks.Find(m => m.Mk == mk);
    public int MaxMk => Def.Marks.Count == 0 ? 1 : Def.Marks.Max(m => m.Mk);

    /// <summary>How many of the gear type's slots may hold a mod at this Mk.</summary>
    public int SlotCount(GearTypeDef type, int mk) =>
        type.SlotsByMark.Count == 0 ? 0 : type.SlotsByMark[Math.Clamp(mk - 1, 0, type.SlotsByMark.Count - 1)];

    /// <summary>The kit that takes gear of this defined id from <paramref name="mk"/> to the next Mk, or null.</summary>
    public string? KitFor(string definedId, int mk) =>
        Moddable(definedId)?.Family is { } family && Def.Families.TryGetValue(family, out var f)
        && f.Kits.TryGetValue((mk + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), out var kit) ? kit : null;

    /// <summary>The id without a quality suffix.</summary>
    public static string Base(string id) => id.Split('@')[0];

    public static bool IsComposite(string id) => id.Contains(ModdedId.Separator);

    /// <summary>Parses an id; a plain id parses to itself at Mk I with no mods.</summary>
    public static ModdedId Parse(string id)
    {
        var parts = id.Split(ModdedId.Separator);
        var mk = 1;
        var mods = new List<string>();
        foreach (var p in parts.Skip(1))
        {
            if (p.Length > 2 && p.StartsWith("mk", StringComparison.Ordinal) && int.TryParse(p.AsSpan(2), out var n)) mk = n;
            else mods.Add(p);
        }
        return new ModdedId(parts[0], mk, mods);
    }

    /// <summary>Composes the id (mods as given; use <see cref="Canonical"/> to order them).</summary>
    public static string Compose(string baseId, int mk, IEnumerable<string> mods)
    {
        var parts = new List<string> { baseId };
        if (mk > 1) parts.Add($"mk{mk}");
        parts.AddRange(mods);
        return string.Join(ModdedId.Separator, parts);
    }

    /// <summary>The canonical id: mods ordered by their slot in the gear type, then by id.</summary>
    public string Canonical(string baseId, int mk, IEnumerable<string> mods)
    {
        var type = TypeOf(Base(baseId));
        var ordered = mods.OrderBy(m => type?.Slots.IndexOf(Mod(m)?.Slot ?? "") ?? 0).ThenBy(m => m, StringComparer.Ordinal);
        return Compose(baseId, mk, ordered);
    }

    /// <summary>The slot a mod sits in on this gear, or null.</summary>
    public string? SlotOf(string modItemId) => Mod(modItemId)?.Slot;

    /// <summary>
    /// Why the build is not allowed (null when it is): the base must be moddable gear, the Mk within its family's kits, each
    /// mod a known mod that fits the gear type, one per slot, and no more than the Mk's slot count.
    /// </summary>
    public string? Check(IReadOnlyDictionary<string, ItemDef> items, ModdedId id)
    {
        if (!items.TryGetValue(id.BaseId, out var baseItem) || baseItem.IsModded) return $"'{id.BaseId}' is not an item.";
        if (Moddable(baseItem.DefinedId) is not { } gear || Def.Types.GetValueOrDefault(gear.Type) is not { } type)
            return $"{baseItem.Name} takes no mods.";
        if (id.Mk < 1 || id.Mk > 1 && (gear.Family is null || Mark(id.Mk) is null)) return $"{baseItem.Name} has no Mk {id.Mk}.";
        if (id.Mods.Count > SlotCount(type, id.Mk)) return $"{baseItem.Name} Mk {id.Mk} takes at most {SlotCount(type, id.Mk)} mods.";
        var slots = new HashSet<string>();
        foreach (var m in id.Mods)
        {
            if (Mod(m) is not { } mod || !items.ContainsKey(m)) return $"'{m}' is not a mod.";
            if (!mod.Types.Contains(gear.Type)) return $"{mod.Name} does not fit {baseItem.Name}.";
            if (!slots.Add(mod.Slot)) return $"{baseItem.Name} has one {SlotName(mod.Slot)} slot.";
        }
        return null;
    }

    /// <summary>The item a build resolves to (call <see cref="Check"/> first).</summary>
    public ItemDef Build(IReadOnlyDictionary<string, ItemDef> items, ModdedId id, IReadOnlyList<QualityTierDef> tiers)
    {
        var item = items[id.BaseId];
        var canonical = Canonical(id.BaseId, id.Mk, id.Mods);
        var weight = item.Weight;
        var names = new List<string>();
        var effects = new List<(ModEffect Effect, GearModDef Def)>();
        foreach (var m in id.Mods)
        {
            var mod = Mod(m)!;
            var modItem = items[m];
            var mult = modItem.Quality is { } q ? tiers.FirstOrDefault(t => t.Id == q)?.StatMultiplier ?? 1 : 1;
            effects.Add((mod.Effect.AtQuality(mult), mod));
            weight += modItem.Weight;
            names.Add(modItem.Name);
        }

        var mark = Mark(id.Mk);
        if (mark is not null) weight = item.Weight * mark.WeightMultiplier + (weight - item.Weight);
        var worn = item.Kind is ItemKind.Armor or ItemKind.Clothing or ItemKind.Headgear;

        var result = item with
        {
            Id = canonical,
            Name = (mark is null ? item.Name : $"{item.Name} {mark.Name}") + (names.Count > 0 ? $" ({string.Join(", ", names)})" : ""),
            BaseId = item.DefinedId,
            UnmoddedId = item.Id,
            Mk = id.Mk,
            Mods = id.Mods.OrderBy(m => Def.Types[Moddable(item.DefinedId)!.Type].Slots.IndexOf(Mod(m)!.Slot)).ToList(),
            LootWeight = 0,
            Weight = Math.Round(weight, 2),
            Actions = item.Actions.Select(a => Action(a, mark, effects)).ToList(),
        };
        if (mark is not null && worn)
            result = result with
            {
                Armor = (result.Armor + mark.ArmorBonus) * mark.ArmorMultiplier,
                Coverage = result.Coverage?.ToDictionary(kv => kv.Key, kv => Math.Min(1, kv.Value + mark.CoverageBonus)),
            };
        foreach (var (e, _) in effects)
            result = result with
            {
                Armor = result.Armor + e.Armor,
                MeleeArmor = result.MeleeArmor + e.MeleeArmor,
                InjuryResist = 1 - (1 - result.InjuryResist) * (1 - e.InjuryResist),
                Stealth = result.Stealth + e.Stealth,
                CarryBonus = result.CarryBonus + e.Carry,
                SightBonus = result.SightBonus + e.Sight,
                Detection = result.Detection + e.Detection,
                MoveNoise = result.MoveNoise + e.MoveNoise,
                Camo = e.Camo ?? result.Camo,
                WeatherProtection = result.WeatherProtection.Union(e.WeatherProtection).ToList(),
                ThermalMask = result.ThermalMask + e.ThermalMask,
                NetworkWeave = e.NetworkWeave ?? result.NetworkWeave,
                Light = e.Light ?? result.Light,
            };
        return result;
    }

    /// <summary>An attack with the Mk step and the mods that apply to it (ranged mods to ranged attacks, melee mods to melee).</summary>
    private static ActionDef Action(ActionDef a, MarkDef? mark, List<(ModEffect Effect, GearModDef Def)> effects)
    {
        var minD = (double)a.MinDamage;
        var maxD = (double)a.MaxDamage;
        var accuracy = a.Accuracy;
        if (mark is not null)
        {
            minD *= mark.DamageMultiplier;
            maxD *= mark.DamageMultiplier;
            accuracy += mark.AccuracyBonus;
        }
        var reach = a.Reach;
        var noise = (double)a.Noise;
        var reveal = a.Reveal;
        var ap = a.ApCost;
        var strength = a.StrengthScaling;
        var pierce = a.ArmorPierce;
        var injury = a.InjuryMultiplier;
        var close = a.CloseRange;
        var closeAp = a.CloseApPenalty;
        var moved = a.MovedPenaltyReduction;
        var feedback = a.NetworkFeedback;
        var banes = a.ExtraBanes.ToList();
        var melee = a.Pattern == AttackPattern.Melee;
        foreach (var (e, def) in effects)
        {
            var meleeMod = def.Types.Contains("melee");
            if (meleeMod != melee) continue;
            accuracy += e.AccuracyByAction.TryGetValue(a.Id, out var specific) ? specific : e.Accuracy;
            minD *= e.DamageMultiplier;
            maxD *= e.DamageMultiplier;
            reach += e.Reach;
            noise *= e.NoiseMultiplier;
            reveal += e.Reveal;
            ap += e.ApCost;
            strength += e.StrengthScaling;
            pierce += e.ArmorPierce;
            injury *= e.InjuryMultiplier;
            close = Math.Max(close, e.CloseRange);
            closeAp += e.CloseApPenalty;
            moved = 1 - (1 - moved) * (1 - e.MovedPenaltyReduction);
            feedback = 1 - (1 - feedback) * (1 - e.NetworkFeedback);
            banes.AddRange(e.Banes);
        }
        return a with
        {
            MinDamage = (int)Math.Round(minD),
            MaxDamage = (int)Math.Round(Math.Max(minD, maxD)),
            Accuracy = accuracy,
            Reach = Math.Max(1, reach),
            Noise = Math.Max(0, (int)Math.Round(noise)),
            Reveal = Math.Max(0, reveal),
            ApCost = Math.Max(1, ap),
            StrengthScaling = strength,
            ArmorPierce = pierce,
            InjuryMultiplier = injury,
            CloseRange = close,
            CloseApPenalty = Math.Max(0, closeAp),
            MovedPenaltyReduction = moved,
            NetworkFeedback = feedback,
            ExtraBanes = banes,
        };
    }

    /// <summary>Mod, kit and headgear items to add to the catalogue.</summary>
    public IEnumerable<ItemDef> ExtraItems()
    {
        foreach (var item in Def.Items) yield return item;
        foreach (var m in Def.Mods)
            yield return new ItemDef { Id = m.Id, Name = m.Name, Kind = ItemKind.Mod, Weight = m.Weight, Tier = m.Tier, LootWeight = m.LootWeight };
        foreach (var k in Def.Kits)
            yield return new ItemDef { Id = k.Id, Name = k.Name, Kind = ItemKind.Material, Weight = k.Weight, Tier = k.Tier, LootWeight = k.LootWeight };
    }

    /// <summary>Data checks against the item catalogue (before mods are resolved).</summary>
    public void Validate(IReadOnlyDictionary<string, ItemDef> items, ScavengeData scavenge)
    {
        var slots = Def.Slots.Select(s => s.Id).ToHashSet();
        foreach (var (id, t) in Def.Types)
        {
            if (t.Slots.Any(s => !slots.Contains(s))) throw new GameDataException($"gear_mods.json: type '{id}' names an unknown slot.");
            if (t.SlotsByMark.Count == 0 || t.SlotsByMark.Any(n => n < 0 || n > t.Slots.Count))
                throw new GameDataException($"gear_mods.json: type '{id}' needs slotsByMark between 0 and its slot count.");
        }
        foreach (var (id, g) in Def.Gear)
        {
            if (!items.TryGetValue(id, out var item) || item.Natural) throw new GameDataException($"gear_mods.json: gear '{id}' is not an item.");
            if (item.Slot is null && item.Prosthetic is null) throw new GameDataException($"gear_mods.json: gear '{id}' is neither equippable nor a prosthetic.");
            if (!Def.Types.ContainsKey(g.Type)) throw new GameDataException($"gear_mods.json: gear '{id}' has unknown type '{g.Type}'.");
            if (g.Family is { } f && !Def.Families.ContainsKey(f)) throw new GameDataException($"gear_mods.json: gear '{id}' has unknown family '{f}'.");
        }
        foreach (var m in Def.Mods)
        {
            if (!slots.Contains(m.Slot)) throw new GameDataException($"gear_mods.json: mod '{m.Id}' has unknown slot '{m.Slot}'.");
            if (m.Types.Count == 0 || m.Types.Any(t => !Def.Types.TryGetValue(t, out var type) || !type.Slots.Contains(m.Slot)))
                throw new GameDataException($"gear_mods.json: mod '{m.Id}' must fit types that have its slot '{m.Slot}'.");
            if (m.Types.Contains("melee") && m.Types.Any(t => t != "melee"))
                throw new GameDataException($"gear_mods.json: mod '{m.Id}' is either a melee mod or not.");
            if (m.Effect.Banes.Any(b => string.IsNullOrWhiteSpace(b.Tag))) throw new GameDataException($"gear_mods.json: mod '{m.Id}' has a bane without a tag.");
        }
        foreach (var (family, f) in Def.Families)
            foreach (var (mk, kit) in f.Kits)
                if (!int.TryParse(mk, out var n) || Mark(n) is null || Kit(kit) is not { } k || k.Family != family || k.Mk != n)
                    throw new GameDataException($"gear_mods.json: family '{family}' kit '{kit}' must be a kit of that family at a defined Mk.");
        foreach (var (zone, _) in Def.ZoneBiomes)
            if (!scavenge.Zones.ContainsKey(zone)) throw new GameDataException($"gear_mods.json: zoneBiomes names unknown zone '{zone}'.");
    }
}
