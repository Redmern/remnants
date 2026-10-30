using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// What gear mods do in the field beyond a unit's profile: weapon lights in the dark, camo by biome, thermal masking,
/// Network weave, protection from weather, plates that clank, and an EMP coil's feedback on its own side's implants.
/// All of it is read from the items a unit wears and carries (equipped gear and working fitted parts), so a modded item
/// needs no state of its own, and none of it draws from the RNG except the feedback roll of an EMP shot.
/// </summary>
public static class GearEffects
{
    /// <summary>Equipped items and working fitted parts.</summary>
    public static IEnumerable<ItemDef> Worn(ScavengeData data, Unit unit) =>
        unit.Equipment.OrderBy(kv => kv.Key).Select(kv => data.Items[kv.Value]).Concat(data.FittedParts(unit.Body).Select(f => f.Item));

    /// <summary>Whether it is dark for lights and thermal masking (night, dusk, or weather that blinds people).</summary>
    public static bool IsDark(ScavengeData data, ScavengeState state) =>
        data.GearMods is { } mods && FieldEnvironment.Effects(data, state).SightFor(SenseKind.Human) < mods.Def.Rules.DarkBelowSight;

    /// <summary>The unit's weapon light while it is dark and the unit is up, else null.</summary>
    public static LightDef? ActiveLight(ScavengeData data, ScavengeState state, Unit unit) =>
        unit.IsActive && IsDark(data, state) ? Worn(data, unit).Select(i => i.Light).FirstOrDefault(l => l is not null) : null;

    /// <summary>Whether <paramref name="observer"/>'s light (at <paramref name="from"/>) falls on <paramref name="to"/>: in range and in its cone.</summary>
    public static bool Illuminates(ScavengeData data, ScavengeState state, Unit observer, Pos from, Pos to)
    {
        if (ActiveLight(data, state, observer) is not { } light) return false;
        if (from.DistanceSquared(to) > light.Range * light.Range + 1e-9) return false;
        return InCone(observer.Facing, from, to, light.ConeDegrees);
    }

    /// <summary>Whether <paramref name="to"/> lies within a cone toward <paramref name="facing"/> (no facing yet: all round).</summary>
    public static bool InCone(Pos? facing, Pos from, Pos to, double degrees)
    {
        if (facing is not { } f || f == new Pos(0, 0) || from == to || degrees >= 360) return true;
        var d = to - from;
        var dot = f.X * d.X + f.Y * d.Y;
        var cos = dot / (Math.Sqrt(f.X * f.X + f.Y * f.Y) * Math.Sqrt(d.X * d.X + d.Y * d.Y));
        return cos + 1e-9 >= Math.Cos(degrees / 2 * Math.PI / 180);
    }

    /// <summary>
    /// Stealth the target's gear adds or costs against this observer right now: camo in a zone of its biome, minus a lit
    /// light, thermal masking in the dark against the senses that see through it, Network weave against the Linked's senses.
    /// </summary>
    public static double StealthAgainst(ScavengeData data, ScavengeState state, Unit target, Unit observer)
    {
        if (data.GearMods is not { } mods) return 0;
        var worn = Worn(data, target).ToList();
        var bonus = 0.0;
        if (mods.Def.ZoneBiomes.TryGetValue(state.ZoneId, out var biome))
            bonus += worn.Where(i => i.Camo?.Biome == biome).Select(i => i.Camo!.Stealth).DefaultIfEmpty(0).Max();
        if (ActiveLight(data, state, target) is { } light) bonus -= light.StealthPenalty;
        var sense = FieldEnvironment.SenseOf(data, state, observer);
        if (IsDark(data, state) && FieldEnvironment.Effects(data, state).StealthIgnoredBy.Contains(sense)) bonus += worn.Sum(i => i.ThermalMask);
        if (sense is SenseKind.Linked or SenseKind.Camera or SenseKind.Turret)
            bonus += worn.Select(i => i.NetworkWeave?.LinkedStealth ?? 0).DefaultIfEmpty(0).Max();
        return bonus;
    }

    /// <summary>A lit beacon light gives its carrier away to anyone with line of sight within their sight before the dark.</summary>
    public static bool BeaconSeenBy(ScavengeData data, ScavengeState state, Unit observer, Unit target) =>
        ActiveLight(data, state, target) is { Beacon: true }
        && Visibility.CanSee(data, state.Map, observer.Pos, ProfileDeriver.Derive(data, observer).Sight, target.Pos);

    /// <summary>Thermal masking hides the wearer from thermal scans (a thermal eye), not from a scanner device.</summary>
    public static bool ThermalMasked(ScavengeData data, Unit unit) => Worn(data, unit).Any(i => i.ThermalMask > 0);

    /// <summary>Whether the unit's gear keeps a weather's effects off its senses.</summary>
    public static bool Protected(ScavengeData data, Unit unit, string weather) =>
        data.GearMods is not null && Worn(data, unit).Any(i => i.WeatherProtection.Contains(weather));

    /// <summary>Tiles added to how far this unit is heard moving (plates; quiet fabric takes some off).</summary>
    public static int MoveNoise(ScavengeData data, Unit unit) => data.GearMods is null ? 0 : Worn(data, unit).Sum(i => i.MoveNoise);

    /// <summary>Tiles further Husks hear anything this unit does (Network weave draws them).</summary>
    public static int HuskLure(ScavengeData data, Unit unit) =>
        data.GearMods is null ? 0 : Worn(data, unit).Select(i => i.NetworkWeave?.HuskLure ?? 0).DefaultIfEmpty(0).Max();

    /// <summary>Share of Network interference (and EMP feedback) the unit's weave takes off its implants.</summary>
    public static double InterferenceReduction(ScavengeData data, Unit unit) =>
        data.GearMods is null ? 0 : Worn(data, unit).Select(i => i.NetworkWeave?.InterferenceReduction ?? 0).DefaultIfEmpty(0).Max();

    /// <summary>Accuracy an attack loses because its unit moved this turn (0 for the Linked, machines, and melee).</summary>
    public static double MovedPenalty(ScavengeData data, Unit unit, ActionDef action)
    {
        if (data.GearMods is not { } mods || !unit.Moved || action.Pattern == AttackPattern.Melee) return 0;
        if (unit.EnemyDefId is { } e && mods.Def.Rules.MovedPenaltyIgnoredBy.Contains(data.Enemies[e].Ai)) return 0;
        if (unit.Kind is UnitKind.Drone or UnitKind.Device) return 0;
        return mods.Def.Rules.MovedAccuracyPenalty * (1 - Math.Clamp(action.MovedPenaltyReduction, 0, 1));
    }

    /// <summary>
    /// An EMP shot's feedback: each Network-grade part of the attacker's side within 1 tile (the attacker's own included)
    /// locks up this round with the action's chance, less its wearer's weave. Rolled by unit id, then part.
    /// </summary>
    internal static void Feedback(ScavengeSim sim, Unit attacker, ActionProfile action)
    {
        if (action.NetworkFeedback <= 0) return;
        foreach (var unit in sim.State.Units.Where(u => u.IsActive && u.Side == attacker.Side && u.Pos.Chebyshev(attacker.Pos) <= 1).OrderBy(u => u.Id))
        {
            var chance = action.NetworkFeedback * (1 - InterferenceReduction(sim.Data, unit));
            foreach (var p in Interference.Exposed(sim.Data, unit).Where(p => !unit.Body[p].Interfered).ToList())
            {
                if (sim.Rng.NextDouble() >= chance) continue;
                unit.Body[p].Interfered = true;
                var name = sim.Data.Items.GetValueOrDefault(unit.Body[p].ProstheticItemId ?? "")?.Name ?? "implant";
                sim.Log(ScavengeEventType.LimbLocked, $"{unit.Name}'s {name} locks up: EMP feedback.", unit);
            }
        }
    }
}
