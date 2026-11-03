namespace Remnants.Core.Scavenge;

public enum FieldEffectKind { Barrier, Smoke }

/// <summary>
/// A card's mark on the map: a <see cref="FieldEffectKind.Barrier"/> (the tile is cover) or
/// <see cref="FieldEffectKind.Smoke"/> (the tiles block sight), through <see cref="UntilRound"/>, placed by
/// <see cref="Side"/>.
/// </summary>
public sealed class FieldEffect
{
    public FieldEffectKind Kind { get; set; }
    public List<Pos> Tiles { get; set; } = new();
    public int UntilRound { get; set; }
    public string Side { get; set; } = "";
}

/// <summary>
/// Barriers and smoke on the map (<see cref="ScavengeState.FieldEffects"/>). The terrain itself is never changed: they
/// lie over it (<see cref="TileMap.Overlay"/>, <see cref="TileMap.Smoke"/>), rebuilt from the state whenever the
/// effects change, when a run is loaded and when a client gets a view, so every rule that reads the map (cover,
/// movement, sight, line of fire) sees them. A barrier is <see cref="Terrain.Cover"/>; smoke blocks sight through its
/// tiles, and into or out of them beyond the next tile.
/// </summary>
public static class FieldEffects
{
    /// <summary>The terrain a barrier stands for.</summary>
    public const Terrain BarrierTerrain = Terrain.Cover;

    /// <summary>Whether a barrier may go up on the tile: open ground nobody stands on, not an exit, no barrier there yet.</summary>
    public static bool CanBarrier(ScavengeState state, Pos p) =>
        state.Map.InBounds(p) && state.Map[p] is Terrain.Floor or Terrain.Rubble && state.UnitAt(p) is null && !state.Map.IsExit(p);

    /// <summary>The tiles a smoke of <paramref name="radius"/> around <paramref name="center"/> covers (on the map).</summary>
    public static List<Pos> SmokeTiles(TileMap map, Pos center, int radius)
    {
        var tiles = new List<Pos>();
        for (var y = center.Y - radius; y <= center.Y + radius; y++)
            for (var x = center.X - radius; x <= center.X + radius; x++)
                if (map.InBounds(new Pos(x, y))) tiles.Add(new Pos(x, y));
        return tiles;
    }

    internal static void Add(ScavengeState state, FieldEffect effect)
    {
        state.FieldEffects.Add(effect);
        Sync(state);
    }

    /// <summary>Round start: what has run its rounds is gone.</summary>
    internal static void RoundStarted(ScavengeState state)
    {
        if (state.FieldEffects.RemoveAll(f => f.UntilRound < state.Round) > 0) Sync(state);
    }

    /// <summary>Lays the state's effects over its map.</summary>
    public static void Sync(ScavengeState state)
    {
        var map = state.Map;
        map.Overlay.Clear();
        map.Smoke.Clear();
        foreach (var f in state.FieldEffects)
            foreach (var p in f.Tiles.Where(map.InBounds))
                if (f.Kind == FieldEffectKind.Barrier) map.Overlay[map.Index(p)] = BarrierTerrain;
                else map.Smoke.Add(map.Index(p));
    }
}
