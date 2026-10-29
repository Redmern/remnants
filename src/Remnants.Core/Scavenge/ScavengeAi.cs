using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Deterministic AI for every unit no player controls. Ties always break on distance, then tile index,
/// and randomness only comes from the sim's RNG.
/// <list type="bullet">
/// <item>Husk: wander; aggro on sight or noise; close in and attack.</item>
/// <item>Linked (stub, full squad AI is M8): like a Husk, but with a gun it first steps into cover that still has a shot.</item>
/// <item>Recruit: follow its leader.</item>
/// </list>
/// </summary>
internal static class ScavengeAi
{
    /// <summary>Safety net so a unit can never loop inside its own turn.</summary>
    private const int MaxStepsPerTurn = 12;

    public static void TakeTurn(ScavengeSim sim, Unit unit)
    {
        if (unit.FollowingId is not null) Follow(sim, unit);
        else if (unit.Kind == UnitKind.Enemy) Fight(sim, unit);
    }

    private static void Fight(ScavengeSim sim, Unit unit)
    {
        var def = unit.EnemyDefId is { } id ? sim.Data.Enemies[id] : null;
        var tookCover = false;
        for (var step = 0; step < MaxStepsPerTurn && unit.IsActive && unit.Ap > 0; step++)
        {
            var target = Spot(sim, unit);
            if (target is null)
            {
                if (unit.Ai.Mode == AiMode.Hunting && unit.Ai.LastKnown is { } last)
                {
                    if (unit.Pos == last || !StepToward(sim, unit, last))
                    {
                        unit.Ai.Mode = AiMode.Idle;
                        unit.Ai.LastKnown = null;
                    }
                    continue;
                }
                Wander(sim, unit);
                return;
            }

            if (def?.Ai == AiKind.Linked && !tookCover)
            {
                tookCover = true;
                if (TakeCover(sim, unit, target)) continue;
            }
            if (TryAttack(sim, unit, target)) continue;
            if (!StepToward(sim, unit, target.Pos)) return;
        }
    }

    /// <summary>Nearest conscious hostile in sight; remembers it. Falls back to null (and keeps the memory).</summary>
    private static Unit? Spot(ScavengeSim sim, Unit unit)
    {
        var sight = sim.Profile(unit).Sight;
        var target = sim.State.Units
            .Where(u => u.IsActive && unit.HostileTo(u) && Visibility.CanSee(sim.Data, sim.State.Map, unit.Pos, sight, u.Pos))
            .OrderBy(u => u.Pos.Chebyshev(unit.Pos))
            .ThenBy(u => u.Id)
            .FirstOrDefault();
        if (target is null) return null;
        if (unit.Ai.Mode == AiMode.Idle)
            sim.Log(ScavengeEventType.Spotted, $"{unit.Name} spotted {target.Name}.", unit, target);
        unit.Ai.Mode = AiMode.Hunting;
        unit.Ai.TargetId = target.Id;
        unit.Ai.LastKnown = target.Pos;
        return target;
    }

    /// <summary>Attack with the affordable, in-reach action that has the best expected damage.</summary>
    private static bool TryAttack(ScavengeSim sim, Unit unit, Unit target)
    {
        var map = sim.State.Map;
        var targetProfile = sim.Profile(target);
        var best = sim.Profile(unit).Actions
            .Where(a => a.ApCost <= unit.Ap && Combat.CheckReach(sim.Data, map, unit.Pos, a, target.Pos) == ScavengeError.None)
            .OrderByDescending(a => Combat.HitChance(sim.Data, map, unit.Pos, a, target.Pos, targetProfile) * (a.MinDamage + a.MaxDamage) / 2)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best is null) return false;
        Combat.Resolve(sim, unit, best, target);
        return true;
    }

    /// <summary>Move to the affordable reachable tile that is closest (by walking distance) to the goal, if that is closer than now.</summary>
    private static bool StepToward(ScavengeSim sim, Unit unit, Pos goal)
    {
        var profile = sim.Profile(unit).Movement;
        var field = Movement.DistanceField(sim.State.Map, profile, goal);
        var here = field.GetValueOrDefault(unit.Pos, int.MaxValue);
        var options = sim.Reachable(unit)
            .Where(kv => kv.Value <= unit.Ap && field.ContainsKey(kv.Key))
            .Select(kv => (Pos: kv.Key, Ap: kv.Value, Dist: field[kv.Key]))
            .Where(o => o.Dist < here)
            .OrderBy(o => o.Dist)
            .ThenBy(o => o.Ap)
            .ThenBy(o => sim.State.Map.Index(o.Pos))
            .ToList();
        if (options.Count == 0) return false;
        MoveTo(sim, unit, options[0].Pos, options[0].Ap);
        return true;
    }

    /// <summary>Linked: if exposed, step to the nearest reachable covered tile that still has a ranged shot at the target.</summary>
    private static bool TakeCover(ScavengeSim sim, Unit unit, Unit target)
    {
        var data = sim.Data;
        var map = sim.State.Map;
        var ranged = sim.Profile(unit).Actions.Where(a => a.Pattern != AttackPattern.Melee).ToList();
        if (ranged.Count == 0 || Combat.CoverAgainst(data, map, unit.Pos, target.Pos) > 0) return false;

        var spot = sim.Reachable(unit)
            .Where(kv => ranged.Any(a => kv.Value + a.ApCost <= unit.Ap
                                         && Combat.CheckReach(data, map, kv.Key, a, target.Pos) == ScavengeError.None))
            .Where(kv => Combat.CoverAgainst(data, map, kv.Key, target.Pos) > 0)
            .OrderBy(kv => kv.Key.Chebyshev(unit.Pos))
            .ThenBy(kv => map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key)
            .FirstOrDefault();
        if (spot is not { } p) return false;
        MoveTo(sim, unit, p, sim.Reachable(unit)[p]);
        return true;
    }

    private static void Wander(ScavengeSim sim, Unit unit)
    {
        if (sim.Rng.NextDouble() >= sim.Data.Rules.Ai.WanderChance) return;
        var options = sim.Reachable(unit).Where(kv => kv.Value <= unit.Ap)
            .OrderBy(kv => sim.State.Map.Index(kv.Key)).ToList();
        if (options.Count == 0) return;
        var pick = options[sim.Rng.Next(0, options.Count)];
        MoveTo(sim, unit, pick.Key, pick.Value);
    }

    /// <summary>Recruits trail their leader; if the leader is gone they pick the nearest controllable ally.</summary>
    private static void Follow(ScavengeSim sim, Unit unit)
    {
        var leader = sim.State.FindUnit(unit.FollowingId!.Value);
        if (leader is not { IsActive: true })
        {
            leader = sim.State.Units
                .Where(u => u.IsActive && u.PlayerControlled && u.PlayerId == unit.PlayerId)
                .OrderBy(u => u.Pos.Chebyshev(unit.Pos)).ThenBy(u => u.Id)
                .FirstOrDefault();
            if (leader is null) return;
            unit.FollowingId = leader.Id;
        }
        for (var step = 0; step < MaxStepsPerTurn && unit.Ap > 0 && unit.Pos.Chebyshev(leader.Pos) > 1; step++)
            if (!StepToward(sim, unit, leader.Pos)) return;
    }

    private static void MoveTo(ScavengeSim sim, Unit unit, Pos to, int ap)
    {
        var from = unit.Pos;
        unit.Pos = to;
        unit.Ap -= ap;
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Moved, $"{unit.Name} moved {from} -> {to}.", unit.Id, Pos: to));
    }
}
