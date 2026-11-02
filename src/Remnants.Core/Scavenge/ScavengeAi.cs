using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Deterministic AI for every unit no player controls. Ties always break on distance, then tile index,
/// and randomness only comes from the sim's RNG.
/// <list type="bullet">
/// <item>Husk: wander; aggro on sight or noise; close in and attack.</item>
/// <item>Linked: squad AI over the <see cref="Network"/>. It knows every hostile its group sees, the group shares one
/// focus (weakest / most exposed), and each unit first takes the best firing position (a shot at the focus, cover,
/// flanking tiles that strip the focus's cover), then shoots, or lays down suppressing fire when the odds are poor.
/// Badly hurt units fall back to cover while the squad holds. They use the fog: firing positions the focus's side would
/// not detect them on score higher (an ambush gets the bonus), and a Linked that knows of nobody, or still has AP after
/// closing in, waits hidden on overwatch. A jammed Linked is cut off and fights like a Husk.</item>
/// <item>Recruit: follow its leader.</item>
/// </list>
/// Enemies and turrets telegraph: their turn runs these same behaviours as a plan (<see cref="Planned"/>), from where
/// they stand with the AP a plan may use, and declares the path and the attack it chose (<see cref="PlannedActions"/>);
/// it resolves at the start of their next turn. An enemy that already has a target in reach strikes at once (only moving
/// then striking is telegraphed); a turret's plan is only its aim tile, always telegraphed; suppressing fire is planned as
/// such (it pins whoever stands on the tile then). Recruits act at once.
/// </summary>
internal static class ScavengeAi
{
    /// <summary>Safety net so a unit can never loop inside its own turn.</summary>
    private const int MaxStepsPerTurn = 12;

    public static void TakeTurn(ScavengeSim sim, Unit unit)
    {
        if (unit.FollowingId is not null) Follow(sim, unit);
        else if (unit.Kind == UnitKind.Device) Planned(sim, unit, () => Machine(sim, unit), telegraphOnly: true);
        else if (unit.Kind != UnitKind.Enemy) return;
        else if (Network.GroupOf(sim, unit) is { } group) Planned(sim, unit, () => Squad(sim, unit, group));
        else Planned(sim, unit, () => Fight(sim, unit));
        EnemyCards.Play(sim, unit);
    }

    /// <summary>
    /// What a planning turn has chosen so far: the moves (waypoints) and the one attack. While it is set, moves and
    /// attacks only change the unit's position and AP for the behaviour to look ahead with; nothing happens in the run.
    /// </summary>
    private sealed class Plan
    {
        public readonly List<Pos> Path = new();
        public Pos? AttackTile;
        public string? ActionId;
        public bool Suppress;
        public bool Lurk;
    }

    [ThreadStatic] private static Plan? _plan;

    /// <summary>
    /// Runs <paramref name="behaviour"/> as a plan: from where the unit stands, with the AP a plan may use
    /// (<see cref="PlannedActions.Budget"/>). Then the unit is put back and declares what it chose; with nothing to do it
    /// may wait on overwatch (a Linked's lurk) as before. A unit whose choice is to strike from where it stands (a target
    /// already in reach) takes its turn at once instead: only moving then striking is telegraphed. A turret
    /// (<paramref name="telegraphOnly"/>) always telegraphs its aim.
    /// </summary>
    private static void Planned(ScavengeSim sim, Unit unit, Action behaviour, bool telegraphOnly = false)
    {
        var (pos, ap, facing) = (unit.Pos, unit.Ap, unit.Facing);
        var plan = _plan = new Plan();
        unit.Ap = PlannedActions.Budget(sim, unit);
        try
        {
            behaviour();
        }
        finally
        {
            _plan = null;
            (unit.Pos, unit.Ap, unit.Facing) = (pos, ap, facing);
        }
        if (!unit.IsActive) return;
        if (!telegraphOnly && plan.Path.Count == 0 && plan.AttackTile is not null)
        {
            behaviour();
            return;
        }
        if (plan.Path.Count > 0 || plan.AttackTile is not null)
        {
            var declared = PlannedActions.Declare(sim, unit, new DeclareIntent(unit.PlayerId ?? 0, unit.Id, plan.Path, plan.AttackTile, plan.ActionId, plan.Suppress));
            if (declared.Ok) return;
        }
        if (plan.Lurk) Lurk(sim, unit);
    }

    /// <summary>An attack: carried out now, or while planning, chosen as the plan's attack (and the look-ahead ends).</summary>
    private static void Strike(ScavengeSim sim, Unit unit, ActionProfile action, Unit target, bool suppress = false)
    {
        if (_plan is { } plan)
        {
            plan.AttackTile = target.Pos;
            plan.ActionId = action.Id;
            plan.Suppress = suppress;
            unit.Ap = 0;
            return;
        }
        if (suppress) Combat.Suppress(sim, unit, action, target);
        else Combat.Resolve(sim, unit, action, target);
    }

    /// <summary>Husk behaviour (and a jammed Linked's): hunt the nearest hostile in sight, close in, attack.</summary>
    private static void Fight(ScavengeSim sim, Unit unit)
    {
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
                if (!Patrol(sim, unit)) Wander(sim, unit);
                return;
            }

            if (TryAttack(sim, unit, target)) continue;
            if (!StepToward(sim, unit, target.Pos)) return;
        }
    }

    /// <summary>A turret: fires at the nearest hostile of its side's it sees, never moves. A camera does nothing on a turn.</summary>
    private static void Machine(ScavengeSim sim, Unit unit)
    {
        for (var step = 0; step < MaxStepsPerTurn && unit.IsActive && unit.Ap > 0; step++)
            if (Spot(sim, unit) is not { } target || !TryAttack(sim, unit, target)) return;
    }

    /// <summary>One Linked unit's turn inside its network group (see the class summary).</summary>
    private static void Squad(ScavengeSim sim, Unit unit, NetworkGroup group)
    {
        var rules = sim.Data.Rules.Network;
        var positioned = false;
        var retreating = false;
        for (var step = 0; step < MaxStepsPerTurn && unit.IsActive && unit.Ap > 0; step++)
        {
            var known = Network.Known(sim, group);
            if (known.Count == 0)
            {
                // Nobody in the group sees anything: chase the last shared sighting, else wander.
                var last = group.Units.Where(u => u.Ai.Mode == AiMode.Hunting && u.Ai.LastKnown is not null)
                    .OrderBy(u => u.Id).Select(u => u.Ai.LastKnown).FirstOrDefault();
                if (last is { } goal && unit.Pos != goal && StepToward(sim, unit, goal)) continue;
                foreach (var u in group.Units) (u.Ai.Mode, u.Ai.LastKnown) = (AiMode.Idle, null);
                if (Patrol(sim, unit) || Lurk(sim, unit)) return;
                Wander(sim, unit);
                return;
            }

            var focus = Network.Focus(sim, group, known)!;
            Share(sim, group, focus);

            if (!positioned)
            {
                positioned = true;
                var others = group.Units.Count(u => u.Id != unit.Id && u.IsActive && u.Hp > u.MaxHp * rules.RetreatHpFraction);
                if (unit.Hp <= unit.MaxHp * rules.RetreatHpFraction && others > 0)
                {
                    retreating = true;
                    if (Retreat(sim, unit, known)) continue;
                }
                else if (Reposition(sim, unit, focus, known)) continue;
            }

            if (TryAttackAbove(sim, unit, focus, rules.SuppressBelowHitChance)) continue;
            if (known.Where(t => t.Id != focus.Id).Any(t => TryAttackAbove(sim, unit, t, rules.SuppressBelowHitChance))) continue;
            if (Suppress(sim, unit, focus)) return;
            if (retreating || !StepToward(sim, unit, focus.Pos)) break;
        }
        if (unit.IsActive && !retreating) Lurk(sim, unit);
    }

    /// <summary>A Linked with AP for it left at the end of its turn waits on overwatch (rules.fog.ai.linkedLurk).</summary>
    private static bool Lurk(ScavengeSim sim, Unit unit)
    {
        var fog = sim.Data.Rules.Fog;
        if (!fog.Ai.LinkedLurk || unit.Ap < fog.Overwatch.ApCost || unit.Stance != Stance.None) return false;
        if (_plan is { } plan)
        {
            // Planning: wait on overwatch only if the turn chose nothing else (decided after the look-ahead).
            plan.Lurk = true;
            return true;
        }
        var left = unit.Ap - fog.Overwatch.ApCost;
        unit.Ap = 0;
        ScavengeCommandHandlers.Enter(sim, unit, Stance.Overwatch, left);
        return true;
    }

    /// <summary>Every unit in the group now hunts the focus; one that was idle and does not see it learns of it through the Network.</summary>
    private static void Share(ScavengeSim sim, NetworkGroup group, Unit focus)
    {
        foreach (var u in group.Units.Where(u => u.IsActive))
        {
            if (u.Ai.Mode == AiMode.Idle)
            {
                var seesIt = Detection.Spots(sim.Data, sim.State, u, focus);
                sim.Log(ScavengeEventType.Spotted,
                    seesIt ? $"{u.Name} spotted {focus.Name}." : $"{u.Name} learns of {focus.Name} through the Network.", u, focus);
            }
            u.Ai.Mode = AiMode.Hunting;
            u.Ai.TargetId = focus.Id;
            u.Ai.LastKnown = focus.Pos;
        }
    }

    /// <summary>Attack the target with the best action whose hit chance is at least <paramref name="minChance"/>.</summary>
    private static bool TryAttackAbove(ScavengeSim sim, Unit unit, Unit target, double minChance)
    {
        var map = sim.State.Map;
        var best = sim.Profile(unit).Actions
            .Where(a => a.ApCost <= unit.Ap && Drones.CanStrike(a, target) && Combat.CheckReach(sim.Data, map, unit.Pos, a, target.Pos) == ScavengeError.None)
            .Select(a => (Action: a, Chance: Combat.ChanceToHit(sim, unit, a, target)))
            .Where(x => x.Chance + 1e-9 >= minChance)
            .OrderByDescending(x => x.Chance * (x.Action.MinDamage + x.Action.MaxDamage) / 2)
            .ThenBy(x => x.Action.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best.Action is null) return false;
        Strike(sim, unit, best.Action, target);
        return true;
    }

    /// <summary>Pin the target with the cheapest ranged action that reaches it (if it is not pinned already).</summary>
    private static bool Suppress(ScavengeSim sim, Unit unit, Unit target)
    {
        if (target.Suppressed) return false;
        var action = sim.Profile(unit).Actions
            .Where(a => a.Pattern != AttackPattern.Melee && a.ApCost <= unit.Ap && Drones.CanStrike(a, target)
                        && Combat.CheckReach(sim.Data, sim.State.Map, unit.Pos, a, target.Pos) == ScavengeError.None)
            .OrderBy(a => a.ApCost).ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (action is null) return false;
        Strike(sim, unit, action, target, suppress: true);
        return true;
    }

    /// <summary>
    /// Scores every tile the unit can reach (and staying put): the expected damage of its best shot at the focus from
    /// there with the AP left (an ambush from a hidden tile counts), plus cover against every known hostile, plus
    /// <see cref="FogAiRules.HiddenWeight"/> when no known hostile would detect it there, plus a little for AP saved.
    /// Moves to the best one.
    /// A move that strips the focus of cover it had is logged as a flank.
    /// </summary>
    private static bool Reposition(ScavengeSim sim, Unit unit, Unit focus, List<Unit> known)
    {
        var data = sim.Data;
        var map = sim.State.Map;
        var rules = data.Rules.Network;
        var profile = sim.Profile(unit);
        var focusProfile = sim.Profile(focus);

        double Score(Pos tile, int cost)
        {
            var ap = unit.Ap - cost;
            // Hidden: out of every known hostile's eyes, not in a flare's light, not revealed or scanned there.
            var hidden = !Detection.IsLit(sim.State, tile) && !known.Any(t =>
                Detection.Spots(data, sim.State, t, unit, targetAt: tile)
                || Detection.IsRevealed(sim.State, unit.Id, t.Side)
                || (Detection.ScanRadius(data, t) is var r && r > 0 && t.Pos.Chebyshev(tile) <= r));
            var ambush = hidden ? Combat.AmbushAgainst(data, sim.State, unit, focus, tile) : null;
            var shot = profile.Actions
                .Where(a => a.ApCost <= ap && Drones.CanStrike(a, focus) && Combat.CheckReach(data, map, tile, a, focus.Pos) == ScavengeError.None)
                .Select(a => Combat.HitChance(data, map, tile, a, focus.Pos, focusProfile, ambush)
                             * Math.Max(1, (a.MinDamage + a.MaxDamage) / 2 * (ambush?.DamageMultiplier ?? 1) - focusProfile.Armor))
                .DefaultIfEmpty(0).Max();
            var cover = known.Select(t => Combat.CoverAgainst(data, map, tile, t.Pos)).DefaultIfEmpty(0).Min();
            return shot + cover * rules.CoverWeight + ap * rules.ApWeight + (hidden ? data.Rules.Fog.Ai.HiddenWeight : 0);
        }

        double stay;
        (Pos Pos, int Ap, double Score) best;
        // Scoring only looks: every profile stays the same until the move.
        using (ProfileDeriver.Memo(data))
        {
            stay = Score(unit.Pos, 0);
            best = sim.Reachable(unit)
                .Where(kv => kv.Value <= unit.Ap)
                .Select(kv => (Pos: kv.Key, Ap: kv.Value, Score: Score(kv.Key, kv.Value)))
                .OrderByDescending(o => o.Score).ThenBy(o => o.Ap).ThenBy(o => map.Index(o.Pos))
                .FirstOrDefault();
        }
        if (best.Ap == 0 || best.Score <= stay + 1e-9) return false;

        var coverBefore = Combat.CoverAgainst(data, map, focus.Pos, unit.Pos);
        MoveTo(sim, unit, best.Pos, best.Ap);
        if (coverBefore > 0 && Combat.CoverAgainst(data, map, focus.Pos, best.Pos) <= 0
            && profile.Actions.Any(a => Combat.CheckReach(data, map, best.Pos, a, focus.Pos) == ScavengeError.None))
            sim.Log(ScavengeEventType.Flanked, $"{unit.Name} flanks {focus.Name}.", unit, focus);
        return true;
    }

    /// <summary>Fall back: the reachable tile farthest from the nearest known hostile, then with the most cover.</summary>
    private static bool Retreat(ScavengeSim sim, Unit unit, List<Unit> known)
    {
        var data = sim.Data;
        var map = sim.State.Map;
        int Distance(Pos p) => known.Min(t => t.Pos.Chebyshev(p));
        double Cover(Pos p) => known.Select(t => Combat.CoverAgainst(data, map, p, t.Pos)).Min();

        var best = sim.Reachable(unit)
            .Where(kv => kv.Value <= unit.Ap)
            .Select(kv => (Pos: kv.Key, Ap: kv.Value))
            .OrderByDescending(o => Distance(o.Pos)).ThenByDescending(o => Cover(o.Pos)).ThenBy(o => o.Ap).ThenBy(o => map.Index(o.Pos))
            .FirstOrDefault();
        if (best.Ap == 0 || (Distance(best.Pos), Cover(best.Pos)).CompareTo((Distance(unit.Pos), Cover(unit.Pos))) <= 0) return false;
        MoveTo(sim, unit, best.Pos, best.Ap);
        sim.Log(ScavengeEventType.Retreated, $"{unit.Name} falls back.", unit);
        return true;
    }

    /// <summary>
    /// Nearest conscious hostile it detects (or lit by a flare) and could hit at all (claws do not reach a drone); remembers
    /// it. Falls back to null (and keeps the memory).
    /// </summary>
    private static Unit? Spot(ScavengeSim sim, Unit unit)
    {
        var actions = sim.Profile(unit).Actions;
        var target = sim.State.Units
            .Where(u => u.IsActive && unit.HostileTo(u) && actions.Any(a => Drones.CanStrike(a, u))
                        && (Detection.IsLit(sim.State, u.Pos) || Detection.Spots(sim.Data, sim.State, unit, u)))
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
        var best = sim.Profile(unit).Actions
            .Where(a => a.ApCost <= unit.Ap && Drones.CanStrike(a, target) && Combat.CheckReach(sim.Data, map, unit.Pos, a, target.Pos) == ScavengeError.None)
            .OrderByDescending(a => Combat.ChanceToHit(sim, unit, a, target) * (a.MinDamage + a.MaxDamage) / 2)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best is null) return false;
        Strike(sim, unit, best, target);
        return true;
    }

    /// <summary>Move to the affordable reachable tile that is closest (by walking distance) to the goal, if that is closer than now.</summary>
    private static bool StepToward(ScavengeSim sim, Unit unit, Pos goal)
    {
        var profile = sim.Profile(unit).Movement;
        var field = Movement.DistanceField(sim.State, unit, profile, goal);
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

    /// <summary>A unit with a scripted patrol walks its waypoints in a loop (the same route every time: watch it once, then go round).</summary>
    private static bool Patrol(ScavengeSim sim, Unit unit)
    {
        if (unit.Patrol.Count == 0) return false;
        for (var tries = 0; tries < unit.Patrol.Count; tries++)
        {
            var goal = unit.Patrol[unit.PatrolIndex % unit.Patrol.Count];
            if (unit.Pos.Chebyshev(goal) <= 0 || !StepToward(sim, unit, goal) && unit.Pos.Chebyshev(goal) <= 1)
            {
                unit.PatrolIndex = (unit.PatrolIndex + 1) % unit.Patrol.Count;
                continue;
            }
            return true;
        }
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

    /// <summary>Recruits trail their leader; if the leader is gone they pick the nearest controllable unit of their side (their own player's first; an ally's takes them over).</summary>
    private static void Follow(ScavengeSim sim, Unit unit)
    {
        var leader = sim.State.FindUnit(unit.FollowingId!.Value);
        if (leader is not { IsActive: true })
        {
            leader = sim.State.Units
                .Where(u => u.IsActive && u.PlayerControlled && u.Side == unit.Side)
                .OrderBy(u => u.PlayerId == unit.PlayerId ? 0 : 1).ThenBy(u => u.Pos.Chebyshev(unit.Pos)).ThenBy(u => u.Id)
                .FirstOrDefault();
            if (leader is null) return;
            unit.FollowingId = leader.Id;
            unit.PlayerId = leader.PlayerId;
        }
        for (var step = 0; step < MaxStepsPerTurn && unit.Ap > 0 && unit.Pos.Chebyshev(leader.Pos) > 1; step++)
            if (!StepToward(sim, unit, leader.Pos)) return;
    }

    private static void MoveTo(ScavengeSim sim, Unit unit, Pos to, int ap)
    {
        if (_plan is { } plan)
        {
            plan.Path.Add(to);
            unit.Pos = to;
            unit.Ap -= ap;
            return;
        }
        var from = unit.Pos;
        unit.Pos = to;
        unit.Ap -= ap;
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Moved, $"{unit.Name} moved {from} -> {to}.", unit.Id, Pos: to));
        sim.Moved(unit, from);
    }
}
