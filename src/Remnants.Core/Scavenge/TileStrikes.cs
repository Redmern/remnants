using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// A planned tile attack (<see cref="AttackTile"/>): who strikes, with what, from where its plan ended, and the tiles it
/// covers. Hidden from everyone but its side until it comes down.
/// </summary>
public sealed class TileStrike
{
    public int UnitId { get; set; }
    public string ActionId { get; set; } = "";
    public StrikeMode? Mode { get; set; }
    public Pos From { get; set; }
    public Pos Tile { get; set; }
    public Pos Direction { get; set; }
    public List<Pos> Tiles { get; set; } = new();

    /// <summary>Suppressing fire (an AI's): it pins whoever stands on the tile when it comes down, with no damage roll.</summary>
    public bool Suppress { get; set; }
}

/// <summary>
/// Tile-first attacks. A unit aims at a tile and a direction from where its planned moves end: a weapon with stab / slash
/// patterns covers the tiles of its <see cref="StrikeMode"/> (<see cref="Combat.Covered"/>), any other action the one tile
/// aimed at (in reach, as <see cref="Combat.CheckReach"/> says). The AP is paid when it is queued. It comes down in the
/// action phase, by priority: whoever hostile stands on a covered tile in the attacker's line of sight then is struck, each with
/// its own roll. Nobody there is a miss. If the attacker is somewhere else by then (stopped short, pushed), the pattern
/// keeps its shape and direction, re-anchored on the unit. A unit that is down or gone loses it (<see cref="Lose"/>).
/// </summary>
public static class TileStrikes
{
    internal static ScavengeResult Declare(ScavengeSim sim, Unit unit, AttackTile c, bool suppress = false)
    {
        var state = sim.State;
        var from = Plans.Anchor(state, unit);
        var action = sim.Profile(unit).Action(c.ActionId);
        if (action is null) return ScavengeResult.Fail(ScavengeError.UnknownAction, $"{unit.Name} cannot use '{c.ActionId}' now.");
        if (Aim(sim, unit, action, c.Tile, c.Mode, c.Direction, from) is not { } aim)
            return c.Mode is { } m && action.Modes?.Contains(m) != true
                ? ScavengeResult.Fail(ScavengeError.InvalidTarget, $"{action.Name} cannot {m.ToString().ToLowerInvariant()}.")
                : ScavengeResult.Fail(ScavengeError.OutOfRange, $"{c.Tile} is not in reach of {action.Name} from {from}.");
        var cost = Combat.ApCost(action, from, c.Tile);
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP for {action.Name} (has {unit.Ap}).");

        unit.Ap -= cost;
        Plans.For(state, unit).Steps.Add(new PlanStep
        {
            Kind = PlanStepKind.Strike,
            Strike = new TileStrike { UnitId = unit.Id, ActionId = action.Id, Mode = aim.Mode, From = from, Tile = c.Tile, Direction = aim.Direction, Tiles = aim.Tiles, Suppress = suppress },
        });
        var how = aim.Mode is { } mode ? $"a {mode.ToString().ToLowerInvariant()}" : "an attack";
        return ScavengeResult.Success($"{unit.Name} plans {how} at {c.Tile}.");
    }

    /// <summary>
    /// Where a strike aimed at <paramref name="tile"/> would come down from <paramref name="from"/> (default: where the unit's
    /// plan ends): the mode (the action's first if none is given; null for an action without patterns), the direction (default:
    /// the step toward the tile) and the covered tiles, which must include the tile. Null when that cannot be aimed.
    /// </summary>
    public static (StrikeMode? Mode, Pos Direction, List<Pos> Tiles)? Aim(ScavengeSim sim, Unit unit, ActionProfile action, Pos tile,
        StrikeMode? mode = null, Pos? direction = null, Pos? from = null)
    {
        var at = from ?? Plans.Anchor(sim.State, unit);
        var map = sim.State.Map;
        var dir = direction ?? Combat.Face(at, tile);
        if (dir == default || Math.Abs(dir.X) > 1 || Math.Abs(dir.Y) > 1) return null;
        if (!action.Patterned)
        {
            if (mode is not null || Combat.CheckReach(sim.Data, map, at, action, tile) != ScavengeError.None) return null;
            return (null, dir, [tile]);
        }
        var m = mode ?? action.Modes![0];
        var tiles = Combat.Covered(map, action, m, at, dir).ToList();
        return tiles.Contains(tile) ? (m, dir, tiles) : null;
    }

    /// <summary>Whether the unit has a tile strike planned.</summary>
    public static bool Readied(ScavengeState state, Unit unit) => Plans.Of(state, unit)?.Steps.Any(s => s.Kind == PlanStepKind.Strike) == true;

    /// <summary>The unit's queued actions are off, saying why. What they paid stays spent.</summary>
    internal static void Lose(ScavengeSim sim, Unit unit, string why)
    {
        if (Plans.Of(sim.State, unit) is not { } plan || !plan.Steps.Exists(s => s.Kind != PlanStepKind.Move)) return;
        foreach (var card in plan.Steps.Where(s => s.Kind == PlanStepKind.Card)) CardPlay.Lost(sim, unit, card);
        plan.Steps.RemoveAll(s => s.Kind != PlanStepKind.Move);
        sim.State.Log(new(sim.State.Round, ScavengeEventType.IntentCanceled, $"{unit.Name}'s plan is off: {why}.", unit.Id, Pos: unit.Pos));
    }

    /// <summary>
    /// The aim at <paramref name="target"/>'s tile that covers the most hostiles the unit's side sees (in line of sight),
    /// ties to the action's mode order, then the direction order; null when no mode covers it.
    /// </summary>
    public static (StrikeMode? Mode, Pos Direction)? BestAim(ScavengeSim sim, Unit unit, ActionProfile action, Unit target)
    {
        var state = sim.State;
        var from = Plans.Anchor(state, unit);
        var best = ((StrikeMode? Mode, Pos Direction)?)null;
        var bestScore = 0;
        foreach (var mode in action.Modes ?? [])
            foreach (var dir in Pos.Neighbours8)
            {
                if (Aim(sim, unit, action, target.Pos, mode, dir) is not { } aim) continue;
                var tiles = aim.Tiles.Where(p => Combat.Clear(sim.Data, state.Map, from, p)).ToList();
                if (!tiles.Contains(target.Pos)) continue;
                var score = TargetsOn(state, unit, action, tiles).Count(t => unit.HostileTo(t) && Detection.SideSees(sim.Data, state, unit.Side, t));
                if (score <= bestScore) continue;
                (best, bestScore) = ((mode, dir), score);
            }
        return best;
    }

    /// <summary>
    /// The tile strike at <paramref name="target"/>'s tile that a bot, the AI or auto-play issues: a stab / slash aimed to
    /// cover the most hostiles (<see cref="BestAim"/>), any other action at the one tile in reach. Null when it cannot reach.
    /// </summary>
    public static AttackTile? StrikeAt(ScavengeSim sim, Unit unit, ActionProfile action, Unit target)
    {
        var pid = unit.PlayerId ?? 0;
        if (!action.Patterned)
            return Aim(sim, unit, action, target.Pos) is null ? null : new AttackTile(pid, unit.Id, target.Pos, action.Id);
        return BestAim(sim, unit, action, target) is { } aim ? new AttackTile(pid, unit.Id, target.Pos, action.Id, aim.Mode, aim.Direction) : null;
    }

    /// <summary>Every tile a unit could strike from where its plan ends with <paramref name="action"/> in <paramref name="mode"/>, any direction.</summary>
    public static HashSet<Pos> Reach(ScavengeSim sim, Unit unit, ActionProfile action, StrikeMode? mode = null)
    {
        var from = Plans.Anchor(sim.State, unit);
        if (!action.Patterned) return Combat.Hittable(sim.Data, sim.State.Map, action, from, unit.Facing).ToHashSet();
        var m = mode ?? action.Modes![0];
        return Pos.Neighbours8.SelectMany(d => Combat.Covered(sim.State.Map, action, m, from, d)).ToHashSet();
    }

    /// <summary>The strike comes up: re-anchored on the unit, it comes down on whoever is there now.</summary>
    internal static void Resolve(ScavengeSim sim, Unit unit, TileStrike strike)
    {
        var state = sim.State;
        void Fizzle(string why) =>
            state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{unit.Name}'s strike falls through: {why}.", unit.Id, Pos: unit.Pos));
        if (sim.Profile(unit).Action(strike.ActionId) is not { } action) { Fizzle($"can no longer use {strike.ActionId}"); return; }

        var shift = unit.Pos - strike.From;
        var tiles = strike.Tiles.Select(p => p + shift).Where(state.Map.InBounds).ToList();
        var at = new TileStrike { Tiles = tiles, Tile = strike.Tile + shift, Direction = strike.Direction };
        var targets = strike.Suppress ? Pin(sim, unit, action, at) : Strike(sim, unit, action, tiles, at.Tile, strike.Direction);
        if (targets.Count == 0) Fizzle($"{action.Name} hits nothing");
        else state.Log(new(state.Round, ScavengeEventType.IntentResolved, $"{unit.Name}'s {action.Name} comes down.", unit.Id, Pos: unit.Pos));
    }

    /// <summary>Suppressing fire comes down: it pins the first one on the tiles in sight of the unit; with nobody there it only makes its noise.</summary>
    private static List<Unit> Pin(ScavengeSim sim, Unit unit, ActionProfile action, TileStrike strike)
    {
        var clear = strike.Tiles.Where(p => Combat.Clear(sim.Data, sim.State.Map, unit.Pos, p)).ToList();
        unit.Facing = strike.Direction;
        if (TargetsOn(sim.State, unit, action, clear).FirstOrDefault() is not { } target)
        {
            sim.MakeNoise(unit, action.Noise);
            return [];
        }
        Combat.Suppress(sim, unit, action, target);
        return [target];
    }

    /// <summary>
    /// The blow itself, AP already paid: the unit faces <paramref name="direction"/> and strikes every hostile on the
    /// <paramref name="tiles"/> in its line of sight (plus an area action's splash around <paramref name="tile"/>); with
    /// nobody there it only makes its noise. Returns who was struck.
    /// </summary>
    internal static List<Unit> Strike(ScavengeSim sim, Unit unit, ActionProfile action, List<Pos> tiles, Pos tile, Pos direction)
    {
        var state = sim.State;
        var clear = tiles.Where(p => Combat.Clear(sim.Data, state.Map, unit.Pos, p)).ToList();
        var targets = TargetsOn(state, unit, action, clear).ToList();
        unit.Facing = direction;
        if (targets.Count == 0)
        {
            sim.MakeNoise(unit, action.Noise);
            return targets;
        }
        targets.AddRange(Combat.Splash(sim, unit, action, tile, targets));
        Combat.Hit(sim, unit, action, targets);
        return targets;
    }

    /// <summary>The conscious hostiles on <paramref name="tiles"/> the action can strike, tile by tile, then by id.</summary>
    private static IEnumerable<Unit> TargetsOn(ScavengeState state, Unit attacker, ActionProfile action, List<Pos> tiles) =>
        state.Units.Where(u => u.IsActive && u.OnMap && u.CarriedById is null && attacker.HostileTo(u)
                               && tiles.Contains(u.Pos) && Drones.CanStrike(action, u))
            .OrderBy(u => tiles.IndexOf(u.Pos)).ThenBy(u => u.Id);
}
