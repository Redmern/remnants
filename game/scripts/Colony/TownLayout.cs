using System.Linq;
using Godot;
using Remnants.Core.Data;

namespace Remnants.Game.Colony;

/// <summary>Maps Core plot indices (row-major, see rules.layout) to world positions on the ground plane and back.</summary>
public sealed class TownLayout(LayoutRules rules)
{
    /// <summary>Plot pitch in meters. Generated buildings are up to 6 m wide (3×3 tiles of 2 m).</summary>
    public const float PlotSize = 8f;

    public int Columns => rules.PlotColumns;
    public int Rows => rules.PlotRows;
    public int Count => rules.PlotCount;

    public Vector2 Extent => new(Columns * PlotSize, Rows * PlotSize);

    public Vector3 Center(int plot)
    {
        var col = plot % Columns;
        var row = plot / Columns;
        return new Vector3((col + 0.5f) * PlotSize - Extent.X / 2, 0, (row + 0.5f) * PlotSize - Extent.Y / 2);
    }

    /// <summary>
    /// The town's commons beside a plot (the Town Hall's): a campfire and a playground just outside the grid, off the
    /// grid edge nearest to that plot, so they never sit on a buildable plot.
    /// </summary>
    public (Vector3 Campfire, Vector3 Playground) Commons(int plot)
    {
        var center = Center(plot);
        var half = Extent / 2;
        var outward = new[]
        {
            (center.X + half.X, new Vector3(-1, 0, 0)), (half.X - center.X, new Vector3(1, 0, 0)),
            (center.Z + half.Y, new Vector3(0, 0, -1)), (half.Y - center.Z, new Vector3(0, 0, 1)),
        }.MinBy(e => e.Item1).Item2;
        var along = new Vector3(outward.Z, 0, -outward.X);
        var edge = new Vector3(outward.X != 0 ? outward.X * half.X : center.X, 0, outward.Z != 0 ? outward.Z * half.Y : center.Z);
        return (edge + outward * 4.5f + along * 1.5f, edge + outward * 3f - along * 2.5f);
    }

    /// <summary>The nearest gutter line (plot border) to a coordinate along X or Z; streets for walkers.</summary>
    public float GutterX(float x) => Mathf.Clamp(Mathf.Round((x + Extent.X / 2) / PlotSize), 0, Columns) * PlotSize - Extent.X / 2;
    public float GutterZ(float z) => Mathf.Clamp(Mathf.Round((z + Extent.Y / 2) / PlotSize), 0, Rows) * PlotSize - Extent.Y / 2;

    public bool InsideGrid(Vector3 p) => Mathf.Abs(p.X) < Extent.X / 2 && Mathf.Abs(p.Z) < Extent.Y / 2;

    /// <summary>The plot under a ground point, or -1 outside the grid.</summary>
    public int PlotAt(Vector3 point)
    {
        var col = Mathf.FloorToInt((point.X + Extent.X / 2) / PlotSize);
        var row = Mathf.FloorToInt((point.Z + Extent.Y / 2) / PlotSize);
        return col < 0 || row < 0 || col >= Columns || row >= Rows ? -1 : row * Columns + col;
    }
}
