using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// A tactical order button: icon, label (with a muted detail line), its AP cost as pips (red when the unit cannot
/// pay) and its hotkey as a keycap. Toggle buttons (the chosen attack, an item being aimed) show pressed.
/// </summary>
public partial class ActionButton : Button
{
    private readonly TextureRect _icon;
    private readonly Label _title;
    private readonly Label _detail;
    private readonly Pips _cost;
    private readonly PanelContainer _cap;
    private readonly Label _key;
    private int _apCost;
    private string _iconId = "";

    /// <summary>The InputMap action that presses this button, if any.</summary>
    public string? Hotkey { get; private set; }

    /// <summary>What a press does; a pooled button (<see cref="PanelRows"/>) gets a new one on every refresh.</summary>
    public System.Action? OnPressed { get; set; }

    public ActionButton() : this("ui_attack", "", "", 0, null) { }

    public ActionButton(string icon, string text, string detail, int apCost, string? hotkey, string tooltip = "")
    {
        FocusMode = FocusModeEnum.None;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride($"margin_{side}", 8);
        AddChild(margin);
        var row = Widgets.Row(8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(row);

        _icon = Icons.Rect(Icons.Get(icon, 20), 20, Palette.Rust);
        _iconId = icon;
        _icon.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_icon);
        var text_ = Widgets.Column(-2);
        text_.MouseFilter = MouseFilterEnum.Ignore;
        text_.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text_.Alignment = BoxContainer.AlignmentMode.Center;
        _title = Widgets.Label("", "", size: 15);
        _title.AddThemeFontOverride("font", Fonts.BodyBold);
        _title.ClipText = true;
        text_.AddChild(_title);
        _detail = Widgets.Label("", "Small");
        _detail.ClipText = true;
        text_.AddChild(_detail);
        row.AddChild(text_);

        _cost = new Pips(0, 0, Palette.Amber) { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        row.AddChild(_cost);

        _cap = new PanelContainer { ThemeTypeVariation = "Chip", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _key = Widgets.Label("", "", Palette.TextMuted, 12);
        _key.AddThemeFontOverride("font", Fonts.BodyBold); // a condensed O reads as 0
        _key.HorizontalAlignment = HorizontalAlignment.Center;
        _key.CustomMinimumSize = new Vector2(12, 0);
        _cap.AddChild(_key);
        row.AddChild(_cap);
        Pressed += () => OnPressed?.Invoke();
        Motion.PressFeedback(this);
        Configure(icon, text, detail, apCost, hotkey, tooltip);
    }

    /// <summary>
    /// Sets everything the constructor takes, in place: a pooled button is re-used across refreshes instead of being
    /// rebuilt (Godot skips setters whose value did not change, so an unchanged button costs no relayout). Resets the
    /// toggle, pressed state, variation and blocked look; callers set those again after.
    /// </summary>
    public void Configure(string icon, string text, string detail, int apCost, string? hotkey, string tooltip = "")
    {
        Hotkey = hotkey;
        _apCost = apCost;
        CustomMinimumSize = new Vector2(0, detail.Length > 0 ? 44 : 36);
        var key = hotkey is null ? "" : Hotkeys.KeyName(hotkey);
        TooltipText = Widgets.Tip(tooltip.Length > 0 ? tooltip : text, key.Length > 0 ? key : null)
                      + (apCost > 0 ? $"\nCosts {apCost} AP" : "");
        if (icon != _iconId)
        {
            _iconId = icon;
            _icon.Texture = Icons.Get(icon, 20);
        }
        _title.Text = text;
        _title.Visible = text.Length > 0;
        _detail.Text = detail;
        _detail.Visible = detail.Length > 0;
        _cost.Visible = apCost > 0;
        _key.Text = key;
        _cap.Visible = key.Length > 0;
        ToggleMode = false;
        ButtonPressed = false;
        ThemeTypeVariation = "";
        SetBlocked(false);
    }

    /// <summary>Greys the button out; the cost pips turn red when <paramref name="tooExpensive"/>.</summary>
    public void SetBlocked(bool blocked, bool tooExpensive = false)
    {
        Disabled = blocked;
        _icon.Modulate = blocked ? Palette.TextDim : Palette.Rust;
        _cost.Set(_apCost, _apCost, tooExpensive ? Palette.Danger : Palette.Amber);
    }
}
