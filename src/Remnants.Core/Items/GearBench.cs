using Remnants.Core.Scavenge;

namespace Remnants.Core.Items;

/// <summary>A piece of gear's numbers for a before/after comparison (the Gear Bench).</summary>
public sealed record GearStats(
    string Name,
    int Mk,
    int SlotsUsed,
    int SlotsOpen,
    double Accuracy,
    double MinDamage,
    double MaxDamage,
    int Reach,
    int ApCost,
    int Noise,
    int Reveal,
    double Weight,
    double Armor,
    IReadOnlyDictionary<Survivors.BodyRegion, double> ArmorByRegion,
    double MeleeArmor,
    double InjuryResist,
    double Stealth,
    double Carry,
    int MoveNoise,
    IReadOnlyList<string> Tags);

/// <summary>
/// The rules of the Gear Bench, shared by the colony (InstallMod, RemoveMod, UpgradeGear) and the field (FieldMod): what a
/// build becomes when a mod goes on or comes off or a kit is fitted. Pure: each takes an item id and answers the new id
/// (and what is handed back), or an error. Removing a mod gives it back intact; fitting a mod into a taken slot swaps the
/// old one out.
/// </summary>
public static class GearBench
{
    public enum Error { None, NoMods, NotModdable, NotAMod, DoesNotFit, NoFreeSlot, EmptySlot, NotAKit, WrongKit, MaxMk }

    public sealed record Outcome(Error Error, string Message, string? ItemId = null, string? Returned = null)
    {
        public bool Ok => Error == Error.None;
    }

    private static Outcome Fail(Error e, string message) => new(e, message);

    /// <summary>Puts a mod on <paramref name="gearId"/>; a mod already in its slot comes off (<see cref="Outcome.Returned"/>).</summary>
    public static Outcome Install(ScavengeData data, string gearId, string modId)
    {
        if (data.GearMods is not { } mods) return Fail(Error.NoMods, "There are no gear mods.");
        if (!data.Items.TryGetValue(gearId, out var gear)) return Fail(Error.NotModdable, $"No item '{gearId}'.");
        if (mods.Moddable(gear.DefinedId) is not { } moddable || mods.TypeOf(gear.DefinedId) is not { } type)
            return Fail(Error.NotModdable, $"{gear.Name} takes no mods.");
        if (!data.Items.TryGetValue(modId, out var modItem) || mods.Mod(modId) is not { } mod) return Fail(Error.NotAMod, $"'{modId}' is not a mod.");
        if (!mod.Types.Contains(moddable.Type)) return Fail(Error.DoesNotFit, $"{modItem.Name} does not fit {gear.Name}.");
        var id = GearModsData.Parse(gearId);
        var returned = id.Mods.FirstOrDefault(m => mods.SlotOf(m) == mod.Slot);
        var next = id.Mods.Where(m => m != returned).Append(modId).ToList();
        if (next.Count > mods.SlotCount(type, id.Mk))
            return Fail(Error.NoFreeSlot, $"{gear.Name} has no free mod slot (Mk {id.Mk}: {mods.SlotCount(type, id.Mk)}).");
        var result = mods.Canonical(id.BaseId, id.Mk, next);
        return new(Error.None, $"{modItem.Name} fitted to {gear.Name}" + (returned is null ? "." : $"; {data.Items[returned].Name} taken off."),
            result, returned);
    }

    /// <summary>Takes the mod in <paramref name="slot"/> off; it is handed back intact.</summary>
    public static Outcome Remove(ScavengeData data, string gearId, string slot)
    {
        if (data.GearMods is not { } mods) return Fail(Error.NoMods, "There are no gear mods.");
        if (!data.Items.TryGetValue(gearId, out var gear)) return Fail(Error.NotModdable, $"No item '{gearId}'.");
        var id = GearModsData.Parse(gearId);
        var mod = id.Mods.FirstOrDefault(m => mods.SlotOf(m) == slot);
        if (mod is null) return Fail(Error.EmptySlot, $"{gear.Name} has nothing in its {mods.SlotName(slot)} slot.");
        var result = mods.Canonical(id.BaseId, id.Mk, id.Mods.Where(m => m != mod));
        return new(Error.None, $"{data.Items[mod].Name} taken off {gear.Name}.", result, mod);
    }

    /// <summary>Fits an upgrade kit: the gear goes up one Mk, keeping its mods.</summary>
    public static Outcome Upgrade(ScavengeData data, string gearId, string kitId)
    {
        if (data.GearMods is not { } mods) return Fail(Error.NoMods, "There are no gear mods.");
        if (!data.Items.TryGetValue(gearId, out var gear) || mods.Moddable(gear.DefinedId) is null)
            return Fail(Error.NotModdable, $"'{gearId}' cannot be upgraded.");
        if (mods.Kit(kitId) is not { } kit) return Fail(Error.NotAKit, $"'{kitId}' is not an upgrade kit.");
        var id = GearModsData.Parse(gearId);
        if (mods.KitFor(gear.DefinedId, id.Mk) is not { } needed)
            return Fail(Error.MaxMk, $"{gear.Name} cannot go past Mk {id.Mk}.");
        if (needed != kitId) return Fail(Error.WrongKit, $"{gear.Name} needs a {data.Items[needed].Name}, not a {kit.Name}.");
        var result = mods.Canonical(id.BaseId, id.Mk + 1, id.Mods);
        return new(Error.None, $"{gear.Name} upgraded to {mods.Mark(id.Mk + 1)?.Name ?? $"Mk {id.Mk + 1}"}.", result);
    }

    /// <summary>Mods in the colony inventory (or a pack) that fit this gear, by slot.</summary>
    public static IEnumerable<string> FittingMods(ScavengeData data, string gearId, IEnumerable<string> carried)
    {
        if (data.GearMods is not { } mods || !data.Items.TryGetValue(gearId, out var gear) || mods.Moddable(gear.DefinedId) is not { } m) return [];
        return carried.Distinct().Where(id => mods.Mod(id) is { } mod && mod.Types.Contains(m.Type)).OrderBy(id => id, StringComparer.Ordinal);
    }

    /// <summary>The gear's numbers: its first attack (weapons) and what it does worn.</summary>
    public static GearStats Stats(ScavengeData data, string itemId)
    {
        var item = data.Items[itemId];
        var mods = data.GearMods;
        var type = mods?.TypeOf(item.DefinedId);
        var a = item.Actions.FirstOrDefault();
        var regions = Enum.GetValues<Survivors.BodyRegion>().ToDictionary(r => r,
            r => item.Coverage is null ? item.Armor : item.Armor * Math.Clamp(item.Coverage.GetValueOrDefault(r), 0, 1));
        var tags = new List<string>();
        if (a is not null)
        {
            foreach (var b in (a.Bane is { } bane ? [bane] : Enumerable.Empty<BaneDef>()).Concat(a.ExtraBanes)) tags.Add($"vs {b.Tag}");
            if (a.CloseApPenalty > 0) tags.Add($"+{a.CloseApPenalty} AP within {a.CloseRange}");
            if (a.ArmorPierce > 0) tags.Add($"pierce {a.ArmorPierce:0.#}");
            if (a.InjuryMultiplier > 1.001) tags.Add($"injury ×{a.InjuryMultiplier:0.##}");
            if (a.NetworkFeedback > 0) tags.Add($"EMP feedback {a.NetworkFeedback:P0}");
            if (a.MovedPenaltyReduction > 0) tags.Add($"steady on the move {a.MovedPenaltyReduction:P0}");
        }
        if (item.Light is { } l) tags.Add($"light {l.Range:0.#} tiles (seen in the dark)");
        if (item.Camo is { } c) tags.Add($"{c.Biome} camo +{c.Stealth:0}");
        if (item.ThermalMask > 0) tags.Add($"thermal mask +{item.ThermalMask:0}");
        if (item.NetworkWeave is { } w) tags.Add($"Network weave −{w.InterferenceReduction:P0} interference");
        if (item.WeatherProtection.Count > 0) tags.Add($"protects from {string.Join(", ", item.WeatherProtection)}");
        return new GearStats(item.Name, item.Mk, item.Mods.Count, type is null ? 0 : mods!.SlotCount(type, item.Mk),
            a?.Accuracy ?? 0, a?.MinDamage ?? 0, a?.MaxDamage ?? 0, a?.Reach ?? 0, a?.ApCost ?? 0, a?.Noise ?? 0, a?.Reveal ?? 0,
            item.Weight, item.Armor, regions, item.MeleeArmor, item.InjuryResist, item.Stealth, item.CarryBonus, item.MoveNoise, tags);
    }
}
