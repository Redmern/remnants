using Remnants.Core.Colony;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Overwatch, committed to one tile or the front arc of one direction (<see cref="CommitOverwatch"/>). Committing costs
/// nothing and ends the unit's turn for the round: the AP it has left is held for shots, until the round ends. It fires
/// when a hostile it sees moves onto a watched tile by its own feet or commits a tile strike covering one, one shot for
/// each such event, at reduced accuracy; each shot pays its strike AP, and a perk's extra shots
/// (<see cref="PerkHooks.OverwatchReactionsBonus"/>) fire once the AP has run out. A shot is an ordinary strike at the
/// hostile's tile (<see cref="TileStrikes.Strike"/>), so anyone shoved onto the tiles it covers is hit too. It drops when
/// the holder is stunned, goes down or moves, or can no longer pay for a shot.
/// </summary>
public static class Overwatches
{
    internal static ScavengeResult Commit(ScavengeSim sim, Unit unit, CommitOverwatch c)
    {
        var fail = Check(sim, unit, c);
        if (fail is not null) return fail;
        unit.Stance = Stance.Overwatch;
        unit.Watch = new OverwatchWatch { Tile = c.Tile, Arc = c.Arc };
        unit.Facing = c.Arc ?? Combat.Face(unit.Pos, c.Tile!.Value);
        unit.Reactions = (int)Math.Round(unit.Hook(PerkHooks.OverwatchReactionsBonus));
        var what = c.Tile is { } tile ? $"{tile}" : "an arc";
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Overwatch, $"{unit.Name} goes on overwatch, watching {what}.", unit.Id, Pos: unit.Pos));
        return ScavengeResult.Success($"{unit.Name} is on overwatch.");
    }

    private static ScavengeResult? Check(ScavengeSim sim, Unit unit, CommitOverwatch c)
    {
        if ((c.Tile is null) == (c.Arc is null)) return ScavengeResult.Fail(ScavengeError.InvalidTarget, "Overwatch needs one tile or one arc to watch.");
        var actions = sim.Profile(unit).Actions;
        if (actions.Count == 0) return ScavengeResult.Fail(ScavengeError.InvalidStance, $"{unit.Name} has nothing to fire on overwatch.");
        if (actions.Min(a => a.ApCost) > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} has no AP to hold for a shot.");
        if (c.Tile is { } tile && !InReach(sim, unit).Contains(tile))
            return ScavengeResult.Fail(ScavengeError.OutOfRange, $"{tile} is not in reach of {unit.Name}'s attacks.");
        if (c.Arc is { } arc)
        {
            if (arc == default || Math.Abs(arc.X) > 1 || Math.Abs(arc.Y) > 1) return ScavengeResult.Fail(ScavengeError.InvalidTarget, "An arc needs a direction.");
            if (ArcTiles(sim, unit, arc).Count == 0) return ScavengeResult.Fail(ScavengeError.OutOfRange, $"{unit.Name} reaches nothing that way.");
        }
        return null;
    }

    /// <summary>The tiles the unit watches now, in map order; none when it holds no watch.</summary>
    public static List<Pos> Watched(ScavengeSim sim, Unit unit) =>
        unit.Watch switch
        {
            { Tile: { } tile } => [tile],
            { Arc: { } arc } => ArcTiles(sim, unit, arc),
            _ => [],
        };

    /// <summary>Every tile a unit could stand on that it could strike from where it stands, any action, mode or direction, in line of sight.</summary>
    public static HashSet<Pos> InReach(ScavengeSim sim, Unit unit)
    {
        var map = sim.State.Map;
        var tiles = new HashSet<Pos>();
        foreach (var action in sim.Profile(unit).Actions)
            tiles.UnionWith(action.Patterned
                ? action.Modes!.SelectMany(m => TileStrikes.Reach(sim, unit, action, m))
                : Combat.Tiles(map, action, unit.Pos, null));
        tiles.RemoveWhere(p => !sim.Data.Terrain(map[p]).Passable || !Combat.Clear(sim.Data, map, unit.Pos, p));
        return tiles;
    }

    private static List<Pos> ArcTiles(ScavengeSim sim, Unit unit, Pos arc) =>
        InReach(sim, unit).Where(p => Combat.ArcOf(unit.Pos, arc, p) == FacingArc.Front).OrderBy(sim.State.Map.Index).ToList();

    /// <summary>
    /// The overwatch a scripted unit takes when it has no better idea: the arc toward the nearest hostile its side sees, else
    /// where it last knew of one, else where it faces, else the map's middle. Null when it could not hold one.
    /// </summary>
    public static IScavengeCommand? OnArc(ScavengeSim sim, Unit unit, int playerId)
    {
        if (unit.Stance != Stance.None) return null;
        var c = new CommitOverwatch(playerId, unit.Id, Arc: DefaultArc(sim, unit));
        return Check(sim, unit, c) is null ? c : null;
    }

    private static Pos DefaultArc(ScavengeSim sim, Unit unit)
    {
        var state = sim.State;
        var seen = state.Units.Where(u => u.IsActive && u.OnMap && unit.HostileTo(u) && Detection.SideSees(sim.Data, state, unit.Side, u))
            .OrderBy(u => u.Pos.Chebyshev(unit.Pos)).ThenBy(u => u.Id).FirstOrDefault();
        var toward = seen?.Pos ?? unit.Ai.LastKnown;
        if (toward is { } at && Combat.Face(unit.Pos, at) is var face && face != default) return face;
        if (unit.Facing is { } facing && facing != default) return facing;
        var middle = Combat.Face(unit.Pos, new Pos(state.Map.Width / 2, state.Map.Height / 2));
        return middle != default ? middle : new Pos(1, 0);
    }

    /// <summary>A hostile moved onto <paramref name="unit"/>'s tile by its own feet: whoever watches that tile fires.</summary>
    internal static void Entered(ScavengeSim sim, Unit unit) => Fire(sim, unit, watched => watched.Contains(unit.Pos));

    /// <summary><paramref name="unit"/> committed a strike over <paramref name="tiles"/>: whoever watches one of them fires at it.</summary>
    internal static void StruckInto(ScavengeSim sim, Unit unit, List<Pos> tiles) => Fire(sim, unit, watched => tiles.Any(watched.Contains));

    /// <summary>Every holder hostile to the target whose watch is tripped and who sees it fires one shot, by id, while the target stands.</summary>
    private static void Fire(ScavengeSim sim, Unit target, Func<List<Pos>, bool> tripped)
    {
        if (!target.IsActive) return;
        var state = sim.State;
        foreach (var watcher in state.Units.Where(u => u.IsActive && u.Stance == Stance.Overwatch && u.HostileTo(target)).OrderBy(u => u.Id).ToList())
        {
            if (!target.IsActive) return;
            if (watcher.Stance != Stance.Overwatch || !tripped(Watched(sim, watcher))) continue;
            var sees = Detection.Spots(sim.Data, state, watcher, target) || Detection.SeesRevealed(sim.Data, state, watcher, target)
                       || ElectronicWarfare.SharesOverwatch(sim, watcher, target);
            if (sees) Shoot(sim, watcher, target);
        }
    }

    private static void Shoot(ScavengeSim sim, Unit watcher, Unit target)
    {
        var state = sim.State;
        var shot = sim.Profile(watcher).Actions
            .Where(a => Drones.CanStrike(a, target) && (Combat.ApCost(a, watcher.Pos, target.Pos) <= watcher.Ap || watcher.Reactions > 0))
            .Select(a => (Action: a, Aim: Aimed(sim, watcher, a, target)))
            .Where(s => s.Aim is not null)
            .OrderByDescending(s => Combat.ChanceToHit(sim, watcher, s.Action, target) * (s.Action.MinDamage + s.Action.MaxDamage) / 2)
            .ThenBy(s => s.Action.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (shot.Action is not { } action) return;

        var cost = Combat.ApCost(action, watcher.Pos, target.Pos);
        if (cost <= watcher.Ap) watcher.Ap -= cost;
        else watcher.Reactions--;
        state.Log(new(state.Round, ScavengeEventType.Overwatch, $"{watcher.Name} fires on overwatch: {action.Name} at {target.Name}!", watcher.Id, target.Id, target.Pos,
            Anonymous: $"Overwatch fire from the fog at {target.Name}!"));
        var (_, direction, tiles) = shot.Aim!.Value;
        TileStrikes.Strike(sim, watcher, action, tiles, target.Pos, direction, reaction: true);
        if (watcher.IsActive && watcher.Reactions <= 0 && sim.Profile(watcher).Actions.Select(a => a.ApCost).DefaultIfEmpty(int.MaxValue).Min() > watcher.Ap)
            Drop(sim, watcher, "out of AP");
    }

    private static (Items.StrikeMode? Mode, Pos Direction, List<Pos> Tiles)? Aimed(ScavengeSim sim, Unit watcher, ActionProfile action, Unit target)
    {
        if (!action.Patterned) return TileStrikes.Aim(sim, watcher, action, target.Pos);
        return TileStrikes.BestAim(sim, watcher, action, target) is { } best ? TileStrikes.Aim(sim, watcher, action, target.Pos, best.Mode, best.Direction) : null;
    }

    /// <summary>The unit's overwatch is off, saying why. The AP it held stays with it.</summary>
    internal static void Drop(ScavengeSim sim, Unit unit, string why)
    {
        if (unit.Stance != Stance.Overwatch) return;
        Clear(unit);
        sim.State.Log(new(sim.State.Round, ScavengeEventType.IntentCanceled, $"{unit.Name} is off overwatch: {why}.", unit.Id, Pos: unit.Pos));
    }

    /// <summary>The round is over: every watch lapses.</summary>
    internal static void RoundEnded(ScavengeState state)
    {
        foreach (var unit in state.Units.Where(u => u.Stance == Stance.Overwatch)) Clear(unit);
    }

    private static void Clear(Unit unit)
    {
        unit.Stance = Stance.None;
        unit.Watch = null;
        unit.Reactions = 0;
    }
}
