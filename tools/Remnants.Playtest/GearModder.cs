using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Playtest;

/// <summary>What the bot expects of a run, for choosing mods: at night, and whether it means to fight the Linked.</summary>
public sealed record ModContext(bool Night = false, bool FightLinked = false, bool Stealthy = false);

/// <summary>
/// A sensible player at the Gear Bench: for each piece of gear, fit the mods that score best for the run ahead, one per
/// slot, as many as the Mk allows. Weapons are scored by expected damage per AP against a plain target (plus a bane where
/// it would bite, less noise for a stealthy party); a weapon light only at night, and never for a party trying to stay
/// hidden; worn gear by armor, padding, carry and stealth, with plates' weight and noise counted against them.
/// </summary>
public static class GearModder
{
    public static double Score(ScavengeData data, string itemId, ModContext context)
    {
        var item = data.Items[itemId];
        var score = 0.0;
        foreach (var a in item.Actions)
        {
            var hit = Math.Clamp(a.Accuracy, 0.05, 0.95);
            var dmg = (a.MinDamage + a.MaxDamage) / 2.0 + a.ArmorPierce * 0.5;
            var banes = (a.Bane is { } b ? [b] : Enumerable.Empty<BaneDef>()).Concat(a.ExtraBanes).ToList();
            var vsHusk = banes.Where(x => x.Tag == "husk").Sum(x => x.DamageMultiplier - 1);
            var vsNet = banes.Where(x => x.Tag == "network").Sum(x => x.DamageMultiplier - 1);
            dmg *= 1 + vsHusk * 0.5 + vsNet * (context.FightLinked ? 0.6 : 0.1);
            dmg *= 1 + (a.InjuryMultiplier - 1) * 0.15;
            var ap = a.ApCost + (a.CloseApPenalty > 0 ? 0.3 * a.CloseApPenalty : 0);
            var perAp = hit * dmg / ap + a.Reach * 0.05 + a.MovedPenaltyReduction * 0.4;
            score = Math.Max(score, perAp - a.Noise * (context.Stealthy ? 0.12 : 0.02) - a.NetworkFeedback * 2);
        }
        score += item.Armor * 1.2 + item.MeleeArmor * 0.8 + item.InjuryResist * 6 + item.CarryBonus * 0.08
                 + item.Stealth * (context.Stealthy ? 0.12 : 0.04) - item.MoveNoise * (context.Stealthy ? 1.0 : 0.3) - item.Weight * 0.15;
        if (item.Light is not null) score += context.Night && !context.Stealthy ? 1.5 : -1.5;
        if (item.ThermalMask > 0) score += context.Night && context.FightLinked ? 0.8 : 0;
        if (item.NetworkWeave is not null) score += context.FightLinked ? 0.6 : -0.4;
        return score;
    }

    /// <summary>
    /// The best build of <paramref name="gearId"/> from the mods on hand (its own mods count as on hand): greedy, slot by
    /// slot, keeping a mod only when it beats leaving the slot for the next one. Returns the id and the mods it used up
    /// (the gear's old mods that it no longer wears are handed back in <c>Freed</c>).
    /// </summary>
    public static (string Id, List<string> Used, List<string> Freed) Best(ScavengeData data, string gearId, IEnumerable<string> onHand, ModContext context)
    {
        if (data.GearMods is not { } mods || !data.Items.TryGetValue(gearId, out var gear) || mods.TypeOf(gear.DefinedId) is not { } type)
            return (gearId, [], []);
        var id = GearModsData.Parse(gearId);
        var pool = onHand.Concat(id.Mods).ToList();
        var bare = mods.Canonical(id.BaseId, id.Mk, []);
        var current = bare;
        var chosen = new List<string>();
        for (var n = 0; n < mods.SlotCount(type, id.Mk); n++)
        {
            string? best = null;
            var bestScore = Score(data, current, context) + 1e-6;
            foreach (var m in pool.Distinct().Where(m => !chosen.Contains(m) && chosen.All(c => mods.SlotOf(c) != mods.SlotOf(m))))
            {
                var outcome = GearBench.Install(data, current, m);
                if (!outcome.Ok || outcome.Returned is not null) continue;
                var s = Score(data, outcome.ItemId!, context);
                if (s > bestScore) (best, bestScore) = (m, s);
            }
            if (best is null) break;
            chosen.Add(best);
            pool.Remove(best);
            current = GearBench.Install(data, current, best).ItemId!;
        }
        var used = chosen.Where(m => !id.Mods.Contains(m)).ToList();
        var freed = id.Mods.Where(m => !chosen.Contains(m)).ToList();
        return (current, used, freed);
    }

    /// <summary>The best build with the mods given, for a zone trial's kit (no stock to draw from).</summary>
    public static string Fit(ScavengeData data, string gearId, IEnumerable<string> mods, ModContext context) => Best(data, gearId, mods, context).Id;
}
