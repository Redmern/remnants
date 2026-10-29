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

    /// <summary>The plot under a ground point, or -1 outside the grid.</summary>
    public int PlotAt(Vector3 point)
    {
        var col = Mathf.FloorToInt((point.X + Extent.X / 2) / PlotSize);
        var row = Mathf.FloorToInt((point.Z + Extent.Y / 2) / PlotSize);
        return col < 0 || row < 0 || col >= Columns || row >= Rows ? -1 : row * Columns + col;
    }
}
