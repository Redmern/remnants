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
    private readonly Label _detail;
    private readonly Pips _cost;
    private readonly Label _key;
    private readonly int _apCost;

    /// <summary>The InputMap action that presses this button, if any.</summary>
    public string? Hotkey { get; }

    public ActionButton() : this("ui_attack", "", "", 0, null) { }

    public ActionButton(string icon, string text, string detail, int apCost, string? hotkey, string tooltip = "")
    {
        Hotkey = hotkey;
        _apCost = apCost;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(0, detail.Length > 0 ? 44 : 36);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        var key = hotkey is null ? "" : Hotkeys.KeyName(hotkey);
        TooltipText = Widgets.Tip(tooltip.Length > 0 ? tooltip : text, key.Length > 0 ? key : null)
                      + (apCost > 0 ? $"\nCosts {apCost} AP" : "");

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride($"margin_{side}", 8);
        AddChild(margin);
        var row = Widgets.Row(8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        margin.AddChild(row);

        _icon = Icons.Rect(Icons.Get(icon, 20), 20, Palette.Rust);
        _icon.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_icon);
        var text_ = Widgets.Column(-2);
        text_.MouseFilter = MouseFilterEnum.Ignore;
        text_.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text_.Alignment = BoxContainer.AlignmentMode.Center;
        var title = Widgets.Label(text, "", size: 15);
        title.AddThemeFontOverride("font", Fonts.BodyBold);
        title.ClipText = true;
        text_.AddChild(title);
        _detail = Widgets.Label(detail, "Small");
        _detail.ClipText = true;
        _detail.Visible = detail.Length > 0;
        text_.AddChild(_detail);
        row.AddChild(text_);

        _cost = new Pips(apCost, apCost, Palette.Amber) { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _cost.Visible = apCost > 0;
        row.AddChild(_cost);

        var cap = new PanelContainer { ThemeTypeVariation = "Chip", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _key = Widgets.Label(key, "", Palette.TextMuted, 12);
        _key.AddThemeFontOverride("font", Fonts.BodyBold); // a condensed O reads as 0
        _key.HorizontalAlignment = HorizontalAlignment.Center;
        _key.CustomMinimumSize = new Vector2(12, 0);
        cap.AddChild(_key);
        cap.Visible = key.Length > 0;
        row.AddChild(cap);
        Motion.PressFeedback(this);
    }

    /// <summary>Greys the button out; the cost pips turn red when <paramref name="tooExpensive"/>.</summary>
    public void SetBlocked(bool blocked, bool tooExpensive = false)
    {
        Disabled = blocked;
        _icon.Modulate = blocked ? Palette.TextDim : Palette.Rust;
        _cost.Set(_apCost, _apCost, tooExpensive ? Palette.Danger : Palette.Amber);
    }
}
