using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>
/// A tile attack waiting for the end of its sub-round (<see cref="AttackTile"/>): who strikes, with what, from where, and
/// the tiles it covers. <see cref="TargetId"/> (the hostile seen on a covered tile when it was declared) is the server's own.
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
    public int? TargetId { get; set; }

    /// <summary>A free counter (<see cref="TileStrikes.FreeCounter"/>): dodging it earns no counter back, so they never chain.</summary>
    public bool Counter { get; set; }
}

/// <summary>
/// Tile-first attacks. A unit aims at a tile and a direction: a weapon with stab / slash patterns covers the tiles of its
/// <see cref="StrikeMode"/> (<see cref="Combat.Covered"/>), any other action the one tile aimed at (in reach, as
/// <see cref="Combat.CheckReach"/> says). The AP is paid when it is declared and the unit's sub-turn ends. When the
/// sub-round ends the strikes come down in initiative order: whoever hostile stands on a covered tile in the attacker's
/// line of sight then is struck, each with its own roll, so a tile an enemy steps onto can be struck before it gets there.
/// Nobody there is a miss; the one seen there when it was declared, if it stepped off, dodged it
/// (<see cref="Unit.OutplayedId"/>). A strike whose attacker went down, left or was moved off its tile falls through.
/// </summary>
public static class TileStrikes
{
    internal static ScavengeResult Declare(ScavengeSim sim, Unit unit, AttackTile c)
    {
        var state = sim.State;
        var action = sim.Profile(unit).Action(c.ActionId);
        if (action is null) return ScavengeResult.Fail(ScavengeError.UnknownAction, $"{unit.Name} cannot use '{c.ActionId}' now.");
        if (Aim(sim, unit, action, c.Tile, c.Mode, c.Direction) is not { } aim)
            return c.Mode is { } m && action.Modes?.Contains(m) != true
                ? ScavengeResult.Fail(ScavengeError.InvalidTarget, $"{action.Name} cannot {m.ToString().ToLowerInvariant()}.")
                : ScavengeResult.Fail(ScavengeError.OutOfRange, $"{c.Tile} is not in reach of {action.Name} from {unit.Pos}.");
        var free = FreeCounter(sim, unit, aim.Tiles);
        var cost = free ? 0 : Combat.ApCost(action, unit.Pos, c.Tile);
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP for {action.Name} (has {unit.Ap}).");

        unit.Ap -= cost;
        unit.Facing = aim.Direction;
        if (free && state.FindUnit(unit.FreeCounterId!.Value) is { } dodged)
        {
            unit.FreeCounterId = null;
            state.Log(new(state.Round, ScavengeEventType.Countered, $"{unit.Name} turns {dodged.Name}'s miss against them: a free counter.",
                unit.Id, dodged.Id, unit.Pos));
        }
        var seen = TargetsOn(state, unit, action, aim.Tiles).FirstOrDefault(t => Detection.SideSees(sim.Data, state, unit.Side, t));
        state.Strikes.Add(new TileStrike
        {
            UnitId = unit.Id, ActionId = action.Id, Mode = aim.Mode, From = unit.Pos, Tile = c.Tile, Direction = aim.Direction,
            Tiles = aim.Tiles, TargetId = seen?.Id, Counter = free,
        });
        var how = aim.Mode is { } mode ? $"a {mode.ToString().ToLowerInvariant()} at {c.Tile}" : $"an attack on {c.Tile}";
        state.Log(new(state.Round, ScavengeEventType.IntentDeclared, $"{unit.Name} readies {how} ({action.Name}).", unit.Id, Pos: unit.Pos));
        return ScavengeResult.Success($"{unit.Name} readies {how}.");
    }

    /// <summary>
    /// Where a strike aimed at <paramref name="tile"/> would come down from <paramref name="from"/> (default: where the unit
    /// stands): the mode (the action's first if none is given; null for an action without patterns), the direction (default:
    /// the step toward the tile) and the covered tiles, which must include the tile. Null when that cannot be aimed.
    /// </summary>
    public static (StrikeMode? Mode, Pos Direction, List<Pos> Tiles)? Aim(ScavengeSim sim, Unit unit, ActionProfile action, Pos tile,
        StrikeMode? mode = null, Pos? direction = null, Pos? from = null)
    {
        var at = from ?? unit.Pos;
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

    /// <summary>
    /// Whether a tile strike of the unit's over <paramref name="tiles"/> is its free counter: it dodged a telegraphed blow of
    /// a hostile still standing on one of them in its side's sight (<see cref="Unit.FreeCounterId"/>, good through the round
    /// after the dodge; a strike at a tile in the fog never comes back free, so the cost gives nothing away).
    /// </summary>
    public static bool FreeCounter(ScavengeSim sim, Unit unit, IReadOnlyCollection<Pos> tiles) =>
        unit.FreeCounterId is { } id && sim.State.Round <= unit.OutplayedUntilRound
        && sim.State.FindUnit(id) is { IsActive: true, OnMap: true } attacker && unit.HostileTo(attacker) && tiles.Contains(attacker.Pos)
        && Detection.SideSees(sim.Data, sim.State, unit.Side, attacker);

    /// <summary>Whether the unit has a tile strike waiting for this sub-round's end.</summary>
    public static bool Readied(ScavengeState state, Unit unit) => state.Strikes.Any(s => s.UnitId == unit.Id);

    /// <summary>
    /// The aim at <paramref name="target"/>'s tile that covers the most hostiles the unit's side sees (in line of sight),
    /// ties to the action's mode order, then the direction order; null when no mode covers it.
    /// </summary>
    public static (StrikeMode? Mode, Pos Direction)? BestAim(ScavengeSim sim, Unit unit, ActionProfile action, Unit target)
    {
        var state = sim.State;
        var best = ((StrikeMode? Mode, Pos Direction)?)null;
        var bestScore = 0;
        foreach (var mode in action.Modes ?? [])
            foreach (var dir in Pos.Neighbours8)
            {
                if (Aim(sim, unit, action, target.Pos, mode, dir) is not { } aim) continue;
                var tiles = aim.Tiles.Where(p => Combat.Clear(sim.Data, state.Map, unit.Pos, p)).ToList();
                if (!tiles.Contains(target.Pos)) continue;
                var score = TargetsOn(state, unit, action, tiles).Count(t => Detection.SideSees(sim.Data, state, unit.Side, t));
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

    /// <summary>Every tile a unit could strike now with <paramref name="action"/> in <paramref name="mode"/>, any direction.</summary>
    public static HashSet<Pos> Reach(ScavengeSim sim, Unit unit, ActionProfile action, StrikeMode? mode = null)
    {
        if (!action.Patterned) return Combat.Hittable(sim.Data, sim.State.Map, action, unit.Pos, unit.Facing).ToHashSet();
        var m = mode ?? action.Modes![0];
        return Pos.Neighbours8.SelectMany(d => Combat.Covered(sim.State.Map, action, m, unit.Pos, d)).ToHashSet();
    }

    /// <summary>Strikes everything declared this sub-round, in initiative order, then clears the list.</summary>
    internal static void ResolveAll(ScavengeSim sim)
    {
        var state = sim.State;
        if (state.Strikes.Count == 0) return;
        var order = state.TurnOrder.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i);
        var strikes = state.Strikes.Select((s, i) => (s, i))
            .OrderBy(t => order.GetValueOrDefault(t.s.UnitId, int.MaxValue)).ThenBy(t => t.i).Select(t => t.s).ToList();
        state.Strikes.Clear();
        foreach (var strike in strikes) Resolve(sim, strike);
    }

    private static void Resolve(ScavengeSim sim, TileStrike strike)
    {
        var state = sim.State;
        if (state.FindUnit(strike.UnitId) is not { IsActive: true, OnMap: true } unit) return;
        void Fizzle(string why) =>
            state.Log(new(state.Round, ScavengeEventType.IntentFizzled, $"{unit.Name}'s strike falls through: {why}.", unit.Id, Pos: unit.Pos));
        if (unit.Pos != strike.From) { Fizzle("moved off their tile"); return; }
        if (sim.Profile(unit).Action(strike.ActionId) is not { } action) { Fizzle($"can no longer use {strike.ActionId}"); return; }

        var targets = Strike(sim, unit, action, strike.Tiles, strike.Tile, strike.Direction);
        if (targets.Count == 0) Fizzle($"{action.Name} hits nothing at {strike.Tile}");
        else state.Log(new(state.Round, ScavengeEventType.IntentResolved, $"{unit.Name}'s {action.Name} comes down on {strike.Tile}.", unit.Id, Pos: unit.Pos));
        if (strike.TargetId is { } aimed && targets.All(t => t.Id != aimed) && state.FindUnit(aimed) is { IsActive: true } dodger
            && !strike.Tiles.Contains(dodger.Pos))
            Combat.Dodged(sim, dodger, unit, counter: !strike.Counter);
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
        state.Units.Where(u => u.IsActive && u.OnMap && u.CarriedById is null && attacker.HostileTo(u) && tiles.Contains(u.Pos) && Drones.CanStrike(action, u))
            .OrderBy(u => tiles.IndexOf(u.Pos)).ThenBy(u => u.Id);
}
