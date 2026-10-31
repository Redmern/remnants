using System.Linq;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The hover card over a construction site in the town: what is built, work left, builders, time left and the materials
/// on site (<see cref="SiteText.Tooltip"/>). Follows the mouse; a debug pin holds it over one plot for screenshots.
/// </summary>
public partial class SiteTooltip : PanelContainer
{
    private readonly ColonySession _session;
    private readonly TownView _town;
    private readonly Label _text = Widgets.Label("", "Small");
    private int? _pinned;
    private double _refresh;

    public SiteTooltip(ColonySession session, TownView town)
    {
        _session = session;
        _town = town;
        Name = "SiteTooltip";
        ThemeTypeVariation = "Card";
        MouseFilter = MouseFilterEnum.Ignore;
        _text.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_text);
        Visible = false;
    }

    /// <summary>Debug: keep the card up over this plot.</summary>
    public void Pin(int plot) => _pinned = plot;

    public override void _Process(double delta)
    {
        var plot = _pinned ?? _town.HoveredPlot;
        var sim = _session.Sim;
        var site = plot < 0 || _town.PlacingDefId is not null ? null : sim.State.Construction.Sites.FirstOrDefault(s => s.Plot == plot);
        if (site is null)
        {
            Visible = false;
            return;
        }
        _refresh -= delta;
        if (!Visible || _refresh <= 0)
        {
            _text.Text = SiteText.Tooltip(sim, site);
            _refresh = 0.25;
            ResetSize();
        }
        Visible = true;
        var at = _pinned is { } p ? _town.ScreenAt(p, 5f) ?? Vector2.Zero : GetViewport().GetMousePosition() + new Vector2(18, 18);
        var view = GetViewportRect().Size;
        Position = new Vector2(Mathf.Clamp(at.X, 8, view.X - Size.X - 8), Mathf.Clamp(at.Y, 8, view.Y - Size.Y - 8));
    }
}
