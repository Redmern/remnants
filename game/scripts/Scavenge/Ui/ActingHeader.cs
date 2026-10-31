using System;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The top of the right column: whose orders the column shows (the acting unit's portrait and "Silas's turn"), and one
/// reserved two-line hint under it: what a click does while aiming, that the selected unit is not the one acting, the
/// enemy's turn, autoplay. The hint keeps its height whatever it says, so the buttons below never move.
/// </summary>
public partial class ActingHeader : PanelContainer
{
    private readonly Portrait _portrait = new(40);
    private readonly Label _name = Widgets.Label("", "Display");
    private readonly Label _hint = Widgets.Wrapped("", "Small");
    private readonly Unchanged _set = new();
    private int? _actingId;
    private string _portraitKey = "";

    /// <summary>The portrait was clicked: select the acting unit again.</summary>
    public event Action? ActingClicked;

    public ActingHeader()
    {
        CustomMinimumSize = new Vector2(ScavengeHud.ColumnWidth, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var box = UiKit.Panel(this, 4);
        var row = Widgets.Row(8);
        box.AddChild(row);
        _portrait.MouseFilter = MouseFilterEnum.Stop;
        _portrait.MouseDefaultCursorShape = CursorShape.PointingHand;
        _portrait.TooltipText = Widgets.Tip("Select the unit whose turn it is and centre on it", Hotkeys.KeyName("scav_center"));
        _portrait.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) ActingClicked?.Invoke();
        };
        row.AddChild(_portrait);
        var text = Widgets.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.AddThemeFontSizeOverride("font_size", 18);
        _name.ClipText = true;
        text.AddChild(_name);
        // Two lines reserved: aiming hints come and go without moving the orders below.
        _hint.CustomMinimumSize = new Vector2(40, 34);
        _hint.VerticalAlignment = VerticalAlignment.Top;
        _hint.MaxLinesVisible = 2;
        text.AddChild(_hint);
        row.AddChild(text);
    }

    /// <param name="acting">The unit whose turn it is (null: enemy turn, another player's, or the run is over).</param>
    /// <param name="hint">The line under the name; never empty, so the header keeps its height.</param>
    public void Refresh(ScavengePresenter presenter, Unit? acting, string title, string hint, Color hintColor)
    {
        _set.Text(_name, title);
        _set.FontColor(_name, acting is null ? Palette.TextMuted : SidePalette.For(presenter, acting).Lightened(0.2f));
        _set.Text(_hint, hint);
        _set.FontColor(_hint, hintColor);
        _set.Tooltip(_hint, hint);
        _set.Visible(_portrait, acting is not null);
        if (acting is null) return;
        // The portrait renders the model with its gear: only again when the unit or its gear changed.
        var key = $"{acting.Id}|{string.Join(",", acting.Equipment.Values)}";
        if (key == _portraitKey && _actingId == acting.Id) return;
        _portraitKey = key;
        _actingId = acting.Id;
        _portrait.Set(UnitPortrait.For(GetTree(), acting), SidePalette.For(presenter, acting).Darkened(0.55f));
    }
}
