using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

/// <summary>Where a unit can get to in one move action, per its <see cref="MovementProfile"/>.</summary>
public static class Movement
{
    /// <summary>
    /// Every tile reachable in one move, with the cheapest MP cost among the unit's modes: the primary mode walks up to
    /// <paramref name="budget"/> MP of terrain (default: its range) and pays what it walked, any other mode its flat cost.
    /// Walking and lines pass through allies but not hostiles; nobody ends on an occupied tile.
    /// </summary>
    public static Dictionary<Pos, int> Reachable(ScavengeState state, Unit unit, MovementProfile profile, int? budget = null)
    {
        var result = new Dictionary<Pos, int>();
        void Offer(Pos p, int ap)
        {
            if (!result.TryGetValue(p, out var existing) || ap < existing) result[p] = ap;
        }

        if (budget <= 0) return result;
        foreach (var (mode, i) in profile.Modes.Select((m, i) => (m, i)))
        {
            var range = i == 0 ? budget ?? mode.Range : mode.Range;
            var walk = mode with { Range = range };
            IEnumerable<KeyValuePair<Pos, int>> tiles = mode.Pattern switch
            {
                MovePattern.King => Walk(state, unit, profile, walk),
                MovePattern.Rook => Lines(state, unit, profile, walk, Pos.Orthogonal),
                MovePattern.Bishop => Lines(state, unit, profile, walk, Pos.Diagonal),
                MovePattern.Queen => Lines(state, unit, profile, walk, Pos.Neighbours8),
                MovePattern.Knight => Jumps(state, unit, profile),
                _ => [],
            };
            foreach (var (p, walked) in tiles) Offer(p, i == 0 && mode.Pattern != MovePattern.Knight ? walked : mode.ApCost);
        }
        result.Remove(unit.Pos);
        return result;
    }

    private static bool Free(ScavengeState state, Unit mover, Pos p) =>
        state.UnitAt(p) is not { } other || other.Id == mover.Id;

    /// <summary>No hostile unit in the way and no device blocking the tile (a door lets its own side through).</summary>
    private static bool CanPass(ScavengeState state, Unit mover, Pos p) =>
        (state.UnitAt(p) is not { } other || !other.HostileTo(mover)) && !Hacking.Blocks(state, mover, p);

    /// <summary>
    /// Multi-step walk in 8 directions, summing terrain costs up to the range (Dijkstra). One step is always allowed,
    /// so a slow unit can still slog onto rubble or water instead of being stuck behind it.
    /// </summary>
    private static IEnumerable<KeyValuePair<Pos, int>> Walk(ScavengeState state, Unit unit, MovementProfile profile, MoveMode mode)
    {
        var map = state.Map;
        var best = new Dictionary<Pos, int> { [unit.Pos] = 0 };
        var queue = new PriorityQueue<Pos, (int, int)>();
        queue.Enqueue(unit.Pos, (0, map.Index(unit.Pos)));
        while (queue.TryDequeue(out var p, out var key))
        {
            if (key.Item1 > best[p]) continue;
            foreach (var d in Pos.Neighbours8)
            {
                var n = p + d;
                if (!map.InBounds(n) || profile.Cost(map[n]) is not { } step) continue;
                if (d.X != 0 && d.Y != 0 && !CornerOpen(map, profile, p, d)) continue;
                if (!CanPass(state, unit, n)) continue;
                var cost = mode.IgnoresTerrain ? key.Item1 + 1 : key.Item1 + step;
                // The first step is always possible: rough ground just uses up the whole move.
                if (p == unit.Pos) cost = Math.Min(cost, mode.Range);
                if (cost > mode.Range || (best.TryGetValue(n, out var b) && b <= cost)) continue;
                best[n] = cost;
                queue.Enqueue(n, (cost, map.Index(n)));
            }
        }
        return best.Where(kv => Free(state, unit, kv.Key));
    }

    /// <summary>No squeezing diagonally between two impassable tiles.</summary>
    private static bool CornerOpen(TileMap map, MovementProfile profile, Pos from, Pos d) =>
        profile.Cost(map[new Pos(from.X + d.X, from.Y)]) is not null || profile.Cost(map[new Pos(from.X, from.Y + d.Y)]) is not null;

    /// <summary>Straight lines: stops at impassable terrain or a hostile, costs summed up to the range (the first step always fits).</summary>
    private static IEnumerable<KeyValuePair<Pos, int>> Lines(ScavengeState state, Unit unit, MovementProfile profile, MoveMode mode, Pos[] directions)
    {
        var map = state.Map;
        foreach (var d in directions)
        {
            var cost = 0;
            var p = unit.Pos;
            while (true)
            {
                var n = p + d;
                if (!map.InBounds(n) || profile.Cost(map[n]) is not { } step) break;
                if (d.X != 0 && d.Y != 0 && !CornerOpen(map, profile, p, d)) break;
                cost += mode.IgnoresTerrain ? 1 : step;
                if (p == unit.Pos) cost = Math.Min(cost, mode.Range);
                if (cost > mode.Range || !CanPass(state, unit, n)) break;
                if (Free(state, unit, n)) yield return new(n, cost);
                p = n;
            }
        }
    }

    /// <summary>Knight jumps: land on any free passable tile an L away, whatever is in between.</summary>
    private static IEnumerable<KeyValuePair<Pos, int>> Jumps(ScavengeState state, Unit unit, MovementProfile profile) =>
        Pos.KnightJumps
            .Select(d => unit.Pos + d)
            .Where(n => state.Map.InBounds(n) && profile.Cost(state.Map[n]) is not null && Free(state, unit, n) && !Hacking.Blocks(state, unit, n))
            .Select(n => new KeyValuePair<Pos, int>(n, 0));

    /// <summary>
    /// Walking distance (terrain-cost weighted, ignoring units) from every tile to <paramref name="target"/>.
    /// The AI uses it to pick the move that closes in best.
    /// </summary>
    public static Dictionary<Pos, int> DistanceField(TileMap map, MovementProfile profile, Pos target) => DistanceField(map, profile, target, null);

    /// <summary>The walking distance field around the devices that block <paramref name="mover"/> (a locked door, a locker...).</summary>
    public static Dictionary<Pos, int> DistanceField(ScavengeState state, Unit mover, MovementProfile profile, Pos target) =>
        DistanceField(state.Map, profile, target, state.Devices.Count == 0 ? null : p => p != target && Hacking.Blocks(state, mover, p));

    private static Dictionary<Pos, int> DistanceField(TileMap map, MovementProfile profile, Pos target, Func<Pos, bool>? blocked)
    {
        var dist = new Dictionary<Pos, int> { [target] = 0 };
        var queue = new PriorityQueue<Pos, (int, int)>();
        queue.Enqueue(target, (0, map.Index(target)));
        while (queue.TryDequeue(out var p, out var key))
        {
            if (key.Item1 > dist[p]) continue;
            foreach (var d in Pos.Neighbours8)
            {
                var n = p + d;
                if (!map.InBounds(n) || profile.Cost(map[n]) is null || blocked?.Invoke(n) == true) continue;
                if (d.X != 0 && d.Y != 0 && !CornerOpen(map, profile, p, d)) continue;
                // Cost of stepping onto p from n, so the field reads "cost to walk from n to the target".
                var cost = key.Item1 + (profile.Cost(map[p]) ?? 1);
                if (dist.TryGetValue(n, out var b) && b <= cost) continue;
                dist[n] = cost;
                queue.Enqueue(n, (cost, map.Index(n)));
            }
        }
        return dist;
    }
}
