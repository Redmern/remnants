using System.Linq;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// A tactical order button: icon, label (with a muted detail line), its AP cost as pips (red when the unit cannot
/// pay) and its hotkey as a keycap. Toggle buttons (the chosen attack, an item being aimed) show pressed.
/// A compact one (the Actions row) is a square icon tile with its AP cost as a number badge; name, detail, cost and
/// hotkey are in its tooltip.
/// </summary>
public partial class ActionButton : Button
{
    private readonly TextureRect _icon;
    private readonly Label _title;
    private readonly Label _detail;
    private readonly Pips _cost;
    private readonly PanelContainer _cap;
    private readonly Label _key;
    private readonly PanelContainer? _badge;
    private readonly Label? _badgeNumber;
    private readonly bool _compact;
    private readonly int _iconSize;
    private int _apCost;
    private string _iconId = "";

    /// <summary>What the button reads.</summary>
    public string Title => _title.Text;

    /// <summary>The InputMap action that presses this button, if any.</summary>
    public string? Hotkey { get; private set; }

    /// <summary>What a press does; a pooled button (<see cref="PanelRows"/>) gets a new one on every refresh.</summary>
    public System.Action? OnPressed { get; set; }

    public ActionButton() : this("ui_attack", "", "", 0, null) { }

    public ActionButton(string icon, string text, string detail, int apCost, string? hotkey, string tooltip = "", bool compact = false)
    {
        _compact = compact;
        _iconSize = compact ? CompactIcon : 20;
        FocusMode = FocusModeEnum.None;
        SizeFlagsHorizontal = compact ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill;
        MouseDefaultCursorShape = CursorShape.PointingHand;

        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride($"margin_{side}", compact ? 0 : 8);
        AddChild(margin);
        var row = Widgets.Row(8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        if (compact) row.Alignment = BoxContainer.AlignmentMode.Center;
        margin.AddChild(row);

        _icon = Icons.Rect(Icons.Get(icon, _iconSize), _iconSize, Palette.Rust);
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

        if (compact)
        {
            // The AP cost, as a number in the tile's corner.
            _badge = new PanelContainer { ThemeTypeVariation = "Chip", MouseFilter = MouseFilterEnum.Ignore };
            _badge.SetAnchorsPreset(LayoutPreset.BottomRight);
            _badge.GrowHorizontal = GrowDirection.Begin;
            _badge.GrowVertical = GrowDirection.Begin;
            _badgeNumber = Widgets.Label("", "", Palette.Amber, 11);
            _badgeNumber.AddThemeFontOverride("font", Fonts.BodyBold);
            _badgeNumber.HorizontalAlignment = HorizontalAlignment.Center;
            _badgeNumber.CustomMinimumSize = new Vector2(8, 0);
            _badge.AddChild(_badgeNumber);
            AddChild(_badge);
        }
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
        CustomMinimumSize = _compact ? new Vector2(CompactSize, CompactSize) : new Vector2(0, detail.Length > 0 ? 44 : 36);
        var key = hotkey is null ? "" : Hotkeys.KeyName(hotkey);
        // A long detail line is clipped on the button, and a compact one shows neither: the tooltip carries them whole.
        var body = _compact
            ? string.Join("\n", new[] { text, detail, tooltip }.Where(t => t.Length > 0).Distinct())
            : (tooltip.Length > 0 ? tooltip : text) + (detail.Length > 34 && !tooltip.Contains(detail) ? $"\n{detail}" : "");
        TooltipText = Widgets.Tip(body, key.Length > 0 ? key : null)
                      + (apCost > 0 ? $"\nCosts {apCost} AP{(_compact ? "" : $" ({new string('■', apCost)}: one pip per AP)")}" : "");
        if (icon != _iconId)
        {
            _iconId = icon;
            _icon.Texture = Icons.Get(icon, _iconSize);
        }
        _title.Text = text;
        _title.Visible = text.Length > 0 && !_compact;
        _detail.Text = detail;
        _detail.Visible = detail.Length > 0 && !_compact;
        _cost.Visible = apCost > 0 && !_compact;
        _key.Text = key;
        _cap.Visible = key.Length > 0 && !_compact;
        if (_badge is not null)
        {
            _badgeNumber!.Text = apCost.ToString();
            _badge.Visible = apCost > 0;
        }
        ToggleMode = false;
        ButtonPressed = false;
        ActionMode = ActionModeEnum.Release;
        SetWarning(false);
        ThemeTypeVariation = "";
        SetBlocked(false);
    }

    /// <summary>
    /// Makes it a toggle (the chosen attack, an item or drone order being aimed), shown pressed when
    /// <paramref name="pressed"/>. Toggles act on press (<see cref="Motion.OnPress"/>): nothing a slip of the mouse would regret.
    /// </summary>
    public ActionButton Toggle(bool pressed)
    {
        ToggleMode = true;
        ButtonPressed = pressed;
        Motion.OnPress(this);
        return this;
    }

    private bool _warning;

    /// <summary>An amber title: the order works but costs something (Take all that slows you).</summary>
    public void SetWarning(bool warning)
    {
        if (warning == _warning) return;
        _warning = warning;
        if (warning) _title.AddThemeColorOverride("font_color", Palette.Warn);
        else _title.RemoveThemeColorOverride("font_color");
    }

    /// <summary>Greys the button out; the cost pips turn red when <paramref name="tooExpensive"/>.</summary>
    public void SetBlocked(bool blocked, bool tooExpensive = false)
    {
        Disabled = blocked;
        _icon.Modulate = blocked ? Palette.TextDim : Palette.Rust;
        _cost.Set(_apCost, _apCost, tooExpensive ? Palette.Danger : Palette.Amber);
        _badgeNumber?.AddThemeColorOverride("font_color", tooExpensive ? Palette.Danger : Palette.Amber);
    }

    private const int CompactSize = 44, CompactIcon = 28;
}
