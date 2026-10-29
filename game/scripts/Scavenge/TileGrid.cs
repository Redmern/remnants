using Godot;
using Remnants.Core.Scavenge;
using TileMap = Remnants.Core.Scavenge.TileMap;

namespace Remnants.Game.Scavenge;

/// <summary>Tile ↔ world mapping. A tile is 2 m (the asset convention); X is east, +Z is south (Core's +Y).</summary>
public static class TileGrid
{
    public const float TileSize = 2f;

    public static Vector3 ToWorld(Pos p) => new(p.X * TileSize, 0, p.Y * TileSize);

    public static Pos FromWorld(Vector3 world) =>
        new(Mathf.RoundToInt(world.X / TileSize), Mathf.RoundToInt(world.Z / TileSize));

    /// <summary>Centre of the map in world space.</summary>
    public static Vector3 Center(TileMap map) => new((map.Width - 1) * TileSize / 2, 0, (map.Height - 1) * TileSize / 2);
}
