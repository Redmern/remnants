using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>Attack reach, hit chance and damage: injuries to body parts, HP, Downed and Dead.</summary>
public static class Combat
{
    /// <summary>Whether <paramref name="action"/> can reach <paramref name="target"/> from <paramref name="from"/>.</summary>
    public static ScavengeError CheckReach(ScavengeData data, TileMap map, Pos from, ActionProfile action, Pos target)
    {
        var d = target - from;
        var inReach = action.Pattern switch
        {
            AttackPattern.Melee => from.Chebyshev(target) <= action.Reach,
            AttackPattern.Ranged => from.DistanceSquared(target) <= action.Reach * action.Reach,
            AttackPattern.Line => (d.X == 0 || d.Y == 0 || Math.Abs(d.X) == Math.Abs(d.Y)) && from.Chebyshev(target) <= action.Reach,
            _ => false,
        };
        if (!inReach || from == target) return ScavengeError.OutOfRange;
        if (from.Chebyshev(target) > 1 && !map.HasLineOfSight(data, from, target)) return ScavengeError.NoLineOfSight;
        return ScavengeError.None;
    }

    /// <summary>Cover a target gets from the tile next to it on the attacker's side. Nothing at point-blank range.</summary>
    public static double CoverAgainst(ScavengeData data, TileMap map, Pos target, Pos attacker)
    {
        if (target.Chebyshev(attacker) <= 1) return 0;
        var shield = target + new Pos(Math.Sign(attacker.X - target.X), Math.Sign(attacker.Y - target.Y));
        return map.InBounds(shield) ? data.Terrain(map[shield]).Cover : 0;
    }

    /// <summary>
    /// Action accuracy minus the target's dodge and cover, minus range falloff past the first tile,
    /// clamped to the rules' min/max.
    /// </summary>
    public static double HitChance(ScavengeData data, TileMap map, Pos from, ActionProfile action, Pos target, UnitProfile targetProfile)
    {
        var c = data.Rules.Combat;
        var chance = action.Accuracy
            - targetProfile.Dodge
            - CoverAgainst(data, map, target, from)
            - Math.Max(0, Math.Sqrt(from.DistanceSquared(target)) - 1) * c.RangeFalloff;
        return Math.Clamp(chance, c.MinHitChance, c.MaxHitChance);
    }

    internal static void Resolve(ScavengeSim sim, Unit attacker, ActionProfile action, Unit target)
    {
        var data = sim.Data;
        var state = sim.State;
        var chance = HitChance(data, state.Map, attacker.Pos, action, target.Pos, sim.Profile(target));
        attacker.Ap -= action.ApCost;
        sim.MakeNoise(attacker, action.Noise);

        if (sim.Rng.NextDouble() >= chance)
        {
            state.Log(new(state.Round, ScavengeEventType.Missed,
                $"{attacker.Name} missed {target.Name} ({action.Name}, {chance:P0}).", attacker.Id, target.Id, target.Pos));
            return;
        }

        var raw = action.MinDamage + (action.MaxDamage - action.MinDamage) * sim.Rng.NextDouble();
        var damage = Math.Max(1, raw - sim.Profile(target).Armor);
        var part = PickHitLocation(sim, target);
        state.Log(new(state.Round, ScavengeEventType.Attacked,
            $"{attacker.Name} hit {target.Name}'s {part} for {damage:0} ({action.Name}).", attacker.Id, target.Id, target.Pos));
        ApplyDamage(sim, target, part, damage);
    }

    /// <summary>Weighted by rules.combat.hitLocations; a hit on a missing limb lands on the torso.</summary>
    private static BodyPart PickHitLocation(ScavengeSim sim, Unit target)
    {
        var weights = sim.Data.Rules.Combat.HitLocations.OrderBy(kv => kv.Key).ToList();
        var roll = sim.Rng.NextDouble() * weights.Sum(kv => kv.Value);
        var part = weights[^1].Key;
        foreach (var (p, w) in weights)
        {
            if (roll < w) { part = p; break; }
            roll -= w;
        }
        return target.Body[part].Condition == PartCondition.Missing ? BodyPart.Torso : part;
    }

    /// <summary>Damage to one body part: injury severity, possible limb loss, then HP and Downed/Dead.</summary>
    public static void ApplyDamage(ScavengeSim sim, Unit target, BodyPart part, double damage)
    {
        var data = sim.Data;
        var state = sim.State;
        var c = data.Rules.Combat;

        var bodyPart = target.Body[part];
        if (bodyPart.Condition is PartCondition.Healthy or PartCondition.Injured)
        {
            var severity = bodyPart.Severity + damage / c.PartToughness[Body.RegionOf(part)];
            target.Body.Injure(part, severity);
            state.Log(new(state.Round, ScavengeEventType.Injured,
                $"{target.Name}'s {part} is injured ({target.Body[part].Severity:P0}).", target.Id, Pos: target.Pos));
            var limb = Body.RegionOf(part) is BodyRegion.Arms or BodyRegion.Legs;
            if (limb && severity >= 1 && sim.Rng.NextDouble() < c.LimbLossChance)
            {
                target.Body.Lose(part);
                state.Log(new(state.Round, ScavengeEventType.LimbLost, $"{target.Name} lost their {part}.", target.Id, Pos: target.Pos));
            }
        }

        var wasDowned = target.Status == UnitStatus.Downed;
        target.Hp -= damage;
        if (target.Hp > 0) return;

        if (wasDowned || !target.CanBeDowned || target.Hp <= c.InstantDeathHp)
        {
            sim.Kill(target, ScavengeEventType.Died, $"{target.Name} is dead.");
            return;
        }
        target.Status = UnitStatus.Downed;
        target.BleedOut = c.BleedOutRounds;
        target.Stabilized = false;
        target.Ap = 0;
        state.Log(new(state.Round, ScavengeEventType.Downed,
            $"{target.Name} is down and bleeding out ({c.BleedOutRounds} rounds).", target.Id, Pos: target.Pos));
    }
}
