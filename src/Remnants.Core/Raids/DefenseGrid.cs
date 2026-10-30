using Remnants.Core.Colony;
using Remnants.Core.Data;

namespace Remnants.Core.Raids;

/// <summary>
/// The defense grid's geometry: the town's plots at <see cref="GridRules.CellsPerPlot"/> cells each, inside a margin. Cell
/// (0, 0) is the north-west corner; x runs east, y south. A cell is one tactical tile, so a raid's battle map is this grid
/// (built plots become their buildings' blocks). Pure.
/// </summary>
public sealed class DefenseGrid(GridRules grid, LayoutRules layout)
{
    public int Width => layout.PlotColumns * grid.CellsPerPlot + 2 * grid.Margin;
    public int Height => layout.PlotRows * grid.CellsPerPlot + 2 * grid.Margin;
    public int Margin => grid.Margin;
    public int CellsPerPlot => grid.CellsPerPlot;

    public static DefenseGrid Of(ColonySim sim, RaidData raids) => new(raids.Def.Grid, sim.Data.Rules.Layout);

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>The plot whose block holds this cell, or -1 (margin and outside).</summary>
    public int PlotAt(int x, int y)
    {
        int px = x - Margin, py = y - Margin;
        if (px < 0 || py < 0) return -1;
        int col = px / CellsPerPlot, row = py / CellsPerPlot;
        return col >= layout.PlotColumns || row >= layout.PlotRows ? -1 : row * layout.PlotColumns + col;
    }

    /// <summary>The cells of a plot's block (the building stands on them when the plot is built).</summary>
    public IEnumerable<(int X, int Y)> PlotCells(int plot)
    {
        int x0 = Margin + plot % layout.PlotColumns * CellsPerPlot, y0 = Margin + plot / layout.PlotColumns * CellsPerPlot;
        for (var y = y0; y < y0 + CellsPerPlot; y++)
            for (var x = x0; x < x0 + CellsPerPlot; x++) yield return (x, y);
    }

    /// <summary>The cell nearest the middle of a plot's block.</summary>
    public (int X, int Y) PlotCenter(int plot) =>
        (Margin + plot % layout.PlotColumns * CellsPerPlot + CellsPerPlot / 2, Margin + plot / layout.PlotColumns * CellsPerPlot + CellsPerPlot / 2);

    /// <summary>Which side of the colony a cell is on: the edge it is nearest to, relative to the grid's middle.</summary>
    public Side SideOf(int x, int y)
    {
        double dx = (x + 0.5) / Width - 0.5, dy = (y + 0.5) / Height - 0.5;
        return Math.Abs(dx) > Math.Abs(dy) ? dx > 0 ? Side.East : Side.West : dy > 0 ? Side.South : Side.North;
    }

    /// <summary>Distance from a cell to an edge of the grid (0 on the edge row/column).</summary>
    public int DistanceToEdge(int x, int y, Side side) => side switch
    {
        Side.North => y,
        Side.South => Height - 1 - y,
        Side.West => x,
        _ => Width - 1 - x,
    };

    /// <summary>Whether a defense covers a cell: within its range (Chebyshev), and for a turret within its arc around the facing.</summary>
    public static bool Covers(PlacedDefense d, DefenseDef def, int x, int y, int rangeBonus = 0)
    {
        int dx = x - d.X, dy = y - d.Y;
        var range = def.Range + rangeBonus;
        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) > range) return false;
        if (def.Arc >= 360 || dx == 0 && dy == 0) return true;
        var (fx, fy) = Facing(d.Facing);
        var angle = Math.Acos((dx * fx + dy * fy) / Math.Sqrt(dx * dx + dy * dy)) * 180 / Math.PI;
        return angle <= def.Arc / 2.0 + 1e-9;
    }

    /// <summary>Unit vector of a side (North = (0, −1)).</summary>
    public static (int X, int Y) Facing(Side side) => side switch
    {
        Side.North => (0, -1),
        Side.South => (0, 1),
        Side.West => (-1, 0),
        _ => (1, 0),
    };

    /// <summary>Every cell a defense covers (for the coverage / fire-arc overlay).</summary>
    public IEnumerable<(int X, int Y)> Coverage(PlacedDefense d, DefenseDef def, int rangeBonus = 0)
    {
        var r = def.Range + rangeBonus;
        for (var y = Math.Max(0, d.Y - r); y <= Math.Min(Height - 1, d.Y + r); y++)
            for (var x = Math.Max(0, d.X - r); x <= Math.Min(Width - 1, d.X + r); x++)
                if (Covers(d, def, x, y, rangeBonus)) yield return (x, y);
    }

    /// <summary>Whether a defense faces or reaches the given side: on that half, or its coverage touches the side's outer band.</summary>
    public bool Faces(PlacedDefense d, DefenseDef def, Side side, int rangeBonus = 0)
    {
        if (SideOf(d.X, d.Y) == side) return true;
        return Coverage(d, def, rangeBonus).Any(c => DistanceToEdge(c.X, c.Y, side) < Margin);
    }
}
