using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Network interference with Network-grade cybernetics (<c>cybernetics.json</c> tiers with <c>interference</c>). At the
/// start of each round every such part of an active unit is unlocked and rolled again: with the chance below it locks for
/// the round (<see cref="BodyPartState.Interfered"/>: it works at 0, its hooks and set bonuses lapse).
/// <code>
/// chance = min(maxChance, perLinked · Linked on the Network within range
///                         + overrider if a hostile with Override (can_hack_linked) is within range
///                         + jammer if any signal jammer covers the unit, its own side's included)
///          · (1 − cyber_interference_reduction)
/// </code>
/// Nothing is drawn for a unit without such parts or without a source near it, so runs without them are unchanged.
/// </summary>
public static class Interference
{
    /// <summary>This unit's chance per Network-grade part to lock this round (0 without a source in range).</summary>
    public static double Chance(ScavengeSim sim, Unit unit)
    {
        if (sim.Data.Cybernetics is not { } cyber) return 0;
        var rules = cyber.Def.Interference;
        var linked = Network.Groups(sim).SelectMany(g => g.Units)
            .Count(u => u.HostileTo(unit) && u.Pos.Chebyshev(unit.Pos) <= rules.Range);
        var chance = linked * rules.PerLinked;
        if (sim.State.Units.Any(u => u.IsActive && u.HostileTo(unit) && u.Kind == UnitKind.Survivor
                                     && u.Hook(PerkHooks.CanHackLinked) > 0 && u.Pos.Chebyshev(unit.Pos) <= rules.Range))
            chance += rules.Overrider;
        if (sim.State.Units.Any(u => u.IsActive && Devices.Working(sim.Data, u).Any(d => d.Effect == DeviceEffect.Jammer && u.Pos.Chebyshev(unit.Pos) <= d.Radius)))
            chance += rules.Jammer;
        return Math.Min(rules.MaxChance, chance) * Math.Clamp(1 - unit.Hook(PerkHooks.CyberInterferenceReduction), 0, 1);
    }

    /// <summary>The unit's fitted Network-grade parts.</summary>
    public static IEnumerable<BodyPart> Exposed(ScavengeData data, Unit unit) =>
        data.Cybernetics is not { } cyber ? []
            : unit.Body.Parts.OrderBy(kv => kv.Key)
                .Where(kv => kv.Value.Condition == PartCondition.Prosthetic && cyber.Tier(cyber.TierOf(kv.Value)).Interference)
                .Select(kv => kv.Key);

    /// <summary>Unlocks every Network-grade part and rolls each again (the sim calls it as a round starts).</summary>
    public static void RoundStarted(ScavengeSim sim)
    {
        foreach (var unit in sim.State.Units.Where(u => u.Kind == UnitKind.Survivor).OrderBy(u => u.Id))
        {
            var parts = Exposed(sim.Data, unit).ToList();
            if (parts.Count == 0) continue;
            foreach (var p in parts) unit.Body[p].Interfered = false;
            if (!unit.IsActive) continue;
            var chance = Chance(sim, unit);
            if (chance <= 0) continue;
            foreach (var p in parts)
            {
                if (sim.Rng.NextDouble() >= chance) continue;
                unit.Body[p].Interfered = true;
                var name = sim.Data.Items.GetValueOrDefault(unit.Body[p].ProstheticItemId ?? "")?.Name ?? "implant";
                sim.Log(ScavengeEventType.LimbLocked, $"{unit.Name}'s {name} locks up: Network interference.", unit);
            }
        }
    }
}
