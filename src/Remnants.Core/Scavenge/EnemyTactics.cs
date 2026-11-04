using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Where an enemy puts a blow, by its type's <see cref="EnemyTactics.Aim"/>. Enemies plan when the round opens, before any
/// player does, so there is no walk of this round to read: they go by where a target stands and what it did last round,
/// which anyone watching saw. Never by a plan: that is what leaves a read open, and a bait.
/// </summary>
public static class EnemyAim
{
    private static EnemyTactics? Of(ScavengeSim sim, Unit unit) =>
        unit.EnemyDefId is { } id && sim.Data.Enemies.TryGetValue(id, out var def) ? def.Tactics : null;

    /// <summary>The tile to aim <paramref name="action"/> at: ahead of a led target as far as a clear shot goes, else where it stands.</summary>
    public static Pos Tile(ScavengeSim sim, Unit unit, ActionProfile action, Unit target)
    {
        if (Of(sim, unit) is not { Aim: AimKind.Lead } tactics || target.LastMove == default) return target.Pos;
        var (state, from) = (sim.State, Plans.Anchor(sim.State, unit));
        var way = new Pos(Math.Sign(target.LastMove.X), Math.Sign(target.LastMove.Y));
        var best = target.Pos;
        for (var i = 1; i <= Math.Min(tactics.LeadTiles, target.Pos.Chebyshev(target.Pos + target.LastMove)); i++)
        {
            var tile = new Pos(target.Pos.X + way.X * i, target.Pos.Y + way.Y * i);
            if (!Open(sim, unit, tile) || !Combat.Clear(sim.Data, state.Map, from, tile) || TileStrikes.Aim(sim, unit, action, tile) is null) break;
            best = tile;
        }
        return best;
    }

    /// <summary>
    /// A <see cref="AimKind.Closest"/> type's blow when its target is near but out of reach: at the next tile toward it, the one
    /// it would step on to close in. Null when the type does not do that, or the target is farther off than it bothers with.
    /// </summary>
    internal static Pos? Swing(ScavengeSim sim, Unit unit, Unit target)
    {
        if (Of(sim, unit) is not { Aim: AimKind.Closest } tactics) return null;
        var from = Plans.Anchor(sim.State, unit);
        if (from.Chebyshev(target.Pos) > tactics.SwingRange) return null;
        var tile = from + Combat.Face(from, target.Pos);
        return Open(sim, unit, tile) ? tile : null;
    }

    /// <summary>On the map, walkable, and no friend standing there to hit.</summary>
    private static bool Open(ScavengeSim sim, Unit unit, Pos tile) =>
        sim.State.Map.InBounds(tile) && sim.Data.Terrain(sim.State.Map[tile]).Passable
        && (sim.State.UnitAt(tile) is not { } there || unit.HostileTo(there));
}

/// <summary>
/// A type's habits (<see cref="EnemyTactics.Habits"/>), tried in order against what the unit can see: a little interpreter, so
/// behaviour stays in the data with no per-type code. A habit that fires fills the action slot and the unit stands to do it. Each
/// is meant to be read: no guard or dash the very next round after one, so what it just did says what it does next.
/// </summary>
internal static class EnemyHabits
{
    internal static bool Act(ScavengeSim sim, Unit unit, IReadOnlyList<Unit> seen)
    {
        if (seen.Count == 0 || unit.EnemyDefId is not { } id || !sim.Data.Enemies.TryGetValue(id, out var def) || def.Tactics is null) return false;
        foreach (var habit in def.Tactics.Habits)
        {
            if (!Holds(sim, unit, habit, seen)) continue;
            if (habit.Do == HabitDo.Strike) return false;
            if (Do(sim, unit, habit, seen)) return true;
        }
        return false;
    }

    /// <summary>Whether a round remembered was the last one (0: never).</summary>
    private static bool JustNow(ScavengeSim sim, int round) => round > 0 && round == sim.State.Round - 1;

    private static bool Holds(ScavengeSim sim, Unit unit, EnemyHabit habit, IReadOnlyList<Unit> seen) => habit.When switch
    {
        HabitWhen.Hurt => unit.Hp < unit.MaxHp * habit.HpBelow,
        HabitWhen.Threatened => seen.Any(h => Combat.ThreatFrom(sim, h, h.Pos).Contains(unit.Pos)),
        HabitWhen.AfterGuard => JustNow(sim, unit.Ai.GuardedRound),
        // The two below are settled by whether there is a push or a dash to make: see Do.
        _ => true,
    };

    private static bool Do(ScavengeSim sim, Unit unit, EnemyHabit habit, IReadOnlyList<Unit> seen)
    {
        var round = sim.State.Round;
        var pid = unit.PlayerId ?? 0;
        switch (habit.Do)
        {
            case HabitDo.Defend:
                if (JustNow(sim, unit.Ai.GuardedRound)) return false;
                if (!Defences.Plan(sim, unit, new Defend(pid, unit.Id, Combat.Face(unit.Pos, seen[0].Pos))).Ok) return false;
                unit.Ai.GuardedRound = round;
                return true;
            case HabitDo.Push:
                return PushWay(sim, unit, seen, cornered: habit.When == HabitWhen.Cornered) is { } way && Pushes.Plan(sim, unit, new Push(pid, unit.Id, way)).Ok;
            case HabitDo.Dash:
                if (JustNow(sim, unit.Ai.DashedRound) || FlankDash(sim, unit, seen[0]) is not { } dash) return false;
                if (!Pushes.Plan(sim, unit, new Dash(pid, unit.Id, dash.Way, dash.Tiles)).Ok) return false;
                unit.Ai.DashedRound = round;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The way to push the nearest hostile in the push's pattern; when <paramref name="cornered"/>, only one a wall stops.</summary>
    private static Pos? PushWay(ScavengeSim sim, Unit unit, IReadOnlyList<Unit> seen, bool cornered)
    {
        var distance = sim.Data.Rules.Combat.Push.Tiles;
        foreach (var h in seen.Where(h => !h.Structure && !CardAbilities.Fixed(h) && h.Pos != unit.Pos))
        {
            var way = Combat.Face(unit.Pos, h.Pos);
            if (!Pushes.Tiles(sim, unit, way).Contains(h.Pos) || !Combat.Clear(sim.Data, sim.State.Map, unit.Pos, h.Pos)) continue;
            // By the terrain alone: who else may stand there is not for it to know.
            if (cornered && CardAbilities.Slide(sim, h, way, distance, terrainOnly: true).Chebyshev(h.Pos) >= distance) continue;
            return way;
        }
        return null;
    }

    /// <summary>A dash that takes a unit in front of <paramref name="target"/> off its front with a shot still on, the best arc first.</summary>
    private static (Pos Way, int Tiles)? FlankDash(ScavengeSim sim, Unit unit, Unit target)
    {
        if (Combat.ArcOf(target, unit.Pos) != FacingArc.Front) return null;
        var actions = sim.Profile(unit).Actions;
        var options = new List<(Pos Way, int Tiles, Pos Landing)>();
        foreach (var way in Pos.Neighbours8)
            for (var tiles = 1; tiles <= sim.Data.Rules.Combat.Dash.Tiles; tiles++)
                if (Pushes.PlannedLanding(sim, unit, way, tiles) is var landing && landing != unit.Pos && landing != target.Pos && landing.Chebyshev(unit.Pos) == tiles
                    && Combat.ArcOf(target, landing) != FacingArc.Front
                    && actions.Any(a => Combat.CheckReach(sim.Data, sim.State.Map, landing, a, target.Pos) == ScavengeError.None))
                    options.Add((way, tiles, landing));
        if (options.Count == 0) return null;
        var best = options.OrderByDescending(o => Combat.ArcOf(target, o.Landing)).ThenBy(o => o.Tiles).ThenBy(o => sim.State.Map.Index(o.Landing)).First();
        return (best.Way, best.Tiles);
    }
}

/// <summary>
/// An enemy whose blow or push came down on an empty tile has wasted its round and shown its back: next round it acts later and
/// starts short of breath. In the open for anyone who sees it, and what a bait pays. Numbers: <see cref="ExposedRules"/>.
/// </summary>
public static class Exposed
{
    /// <summary>Paying for it now, or about to next round.</summary>
    public static bool Active(ScavengeState state, Unit unit) => unit.ExposedRound >= state.Round;

    /// <summary>Its action found nobody.</summary>
    internal static void Mark(ScavengeSim sim, Unit unit)
    {
        if (unit.Kind == UnitKind.Enemy) unit.ExposedRound = sim.State.Round + 1;
    }

    /// <summary>The round it pays for opens.</summary>
    internal static void RoundStarted(ScavengeSim sim, Unit unit)
    {
        if (unit.ExposedRound != sim.State.Round) return;
        var rules = sim.Data.Rules.Combat.Exposed;
        unit.PriorityBonus -= rules.PriorityPenalty;
        Economy.Drain(sim, unit, rules.Stamina);
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Exposed, $"{unit.Name} is exposed after hitting nothing: it acts later and is short of breath.", unit.Id, Pos: unit.Pos));
    }
}
