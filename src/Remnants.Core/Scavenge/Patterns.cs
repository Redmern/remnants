namespace Remnants.Core.Scavenge;

/// <summary>The named shapes of a <see cref="CardPattern"/>: a stab forward, a slash across the front, a line through the origin, a square around it.</summary>
public enum PatternShape { Stab, Slash, Line, Area }

/// <summary>
/// Which tiles an attack or defence card covers (data/cards.json): a named <see cref="Shape"/> of <see cref="Length"/>, or
/// custom <see cref="Offsets"/> as [forward, side] pairs for a card facing along its direction (side + is clockwise).
/// </summary>
public sealed record CardPattern
{
    public PatternShape Shape { get; init; }
    public int Length { get; init; } = 1;
    public List<int[]>? Offsets { get; init; }
}

/// <summary>Pattern geometry: offsets laid out from an origin, rotated to any of the 8 directions. Rotating is free: it is only where the tiles fall.</summary>
public static class Patterns
{
    /// <summary>The pattern's tiles on the map from <paramref name="origin"/> toward <paramref name="direction"/>, in pattern order.</summary>
    public static List<Pos> Tiles(TileMap map, CardPattern pattern, Pos origin, Pos direction)
    {
        var dir = new Pos(Math.Sign(direction.X), Math.Sign(direction.Y));
        // A square has no front: only the shapes with one turn.
        if (dir == default || pattern is { Offsets: null, Shape: PatternShape.Area }) dir = new Pos(1, 0);
        return Offsets(pattern).Select(o => origin + Rotate(o.Forward, o.Side, dir)).Where(map.InBounds).Distinct().ToList();
    }

    private static IEnumerable<(int Forward, int Side)> Offsets(CardPattern pattern)
    {
        if (pattern.Offsets is { Count: > 0 } custom) return custom.Select(o => (o[0], o[1]));
        var n = pattern.Length;
        return pattern.Shape switch
        {
            PatternShape.Stab => Enumerable.Range(1, n).Select(i => (i, 0)),
            PatternShape.Slash => [(1, 0), (1, -1), (1, 1)],
            PatternShape.Line => Enumerable.Range(-n, 2 * n + 1).Select(i => (i, 0)),
            _ => Enumerable.Range(-n, 2 * n + 1).SelectMany(f => Enumerable.Range(-n, 2 * n + 1).Select(s => (f, s))),
        };
    }

    /// <summary>A quarter turn is exact; a diagonal shears the shape so a tile n steps out stays n steps out.</summary>
    private static Pos Rotate(int forward, int side, Pos dir)
    {
        if (dir.X == 0 || dir.Y == 0) return new Pos(forward * dir.X - side * dir.Y, forward * dir.Y + side * dir.X);
        var s = dir.X * dir.Y < 0 ? -side : side;
        var (x, y) = s >= 0 ? (forward - s, forward) : (forward, forward + s);
        return new Pos(x * dir.X, y * dir.Y);
    }
}
