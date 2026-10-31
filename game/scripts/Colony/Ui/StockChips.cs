using System;
using System.Collections.Generic;
using Godot;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// A stockpile chip for the top bar: icon, the amount (counting to a new value) and a small ▲ / ▼ for the trend, so
/// the bar keeps one row however many resources there are. The tooltip names the resource and gives the change per
/// day ("+1.2/day") and what makes it. The amount keeps a minimum width so a count never nudges the chips beside it.
/// </summary>
public partial class StockChip : PanelContainer
{
    private readonly Label _amount = Widgets.Label("", "Number");
    private readonly Label _trend = Widgets.Label("", "Small");
    private readonly Unchanged _set = new();
    private double _shown = double.NaN;

    public string ResourceId { get; }

    public StockChip() : this("wood") { }

    public StockChip(string resourceId, bool wide = false)
    {
        ResourceId = resourceId;
        ThemeTypeVariation = "Chip";
        MouseFilter = MouseFilterEnum.Stop;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Space.Xs);
        AddChild(row);
        row.AddChild(Icons.Rect(Icons.Resource(resourceId, 18), 18, Palette.TextMuted));
        _amount.AddThemeFontSizeOverride("font_size", 17);
        _amount.CustomMinimumSize = new Vector2(wide ? 44 : 30, 0);
        _amount.HorizontalAlignment = HorizontalAlignment.Right;
        _amount.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_amount);
        _trend.CustomMinimumSize = new Vector2(wide ? 52 : 10, 0);
        _trend.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_trend);
    }

    /// <summary>Shows <paramref name="amount"/>, the trend from <paramref name="perDay"/> and the tooltip.</summary>
    public void Set(double amount, double? perDay, string tooltip, bool showRate = false)
    {
        if (double.IsNaN(_shown)) _amount.Text = Fmt.Amount(amount);
        else if (Fmt.Amount(_shown) != Fmt.Amount(amount))
        {
            Motion.CountUp(_amount, _shown, amount, Fmt.Amount);
            Motion.Flash(_amount, amount > _shown ? Palette.Good : Palette.Danger);
        }
        _shown = amount;
        var trend = perDay is { } d && Math.Abs(d) >= 0.05 ? Math.Sign(d) : 0;
        var text = trend == 0 ? "" : showRate ? $"{Fmt.Delta(perDay!.Value)}/d" : trend > 0 ? "▲" : "▼";
        _set.Text(_trend, text);
        _set.FontColor(_trend, trend > 0 ? Palette.Good : Palette.Danger);
        _set.Tooltip(this, tooltip);
    }
}

/// <summary>
/// The "+N" chip at the end of the top bar's resources: the resources past the core four, in a drawer that drops down
/// from it (each with its amount, change per day and tooltip). Its header has the (i) for where resources come from.
/// </summary>
public partial class ResourceDrawer : Button
{
    private readonly PopupPanel _popup = new();
    private readonly VBoxContainer _rows = Widgets.Column(Space.Xs);
    private readonly Dictionary<string, StockChip> _chips = new();
    private readonly Unchanged _set = new();

    public ResourceDrawer()
    {
        ThemeTypeVariation = "";
        FocusMode = FocusModeEnum.All;
        CustomMinimumSize = new Vector2(40, 30);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        AddThemeFontOverride("font", Fonts.Numbers);
        AddThemeFontSizeOverride("font_size", 16);
        _popup.Theme = HudTheme.Shared;
        _popup.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Strip: Palette.Rust, StripHeight: 3), 12, 10));
        var box = Widgets.Column(Space.Sm);
        box.CustomMinimumSize = new Vector2(220, 0);
        box.AddChild(Widgets.SectionHeader("Other resources", "item_material", new InfoTip("production")));
        box.AddChild(_rows);
        _popup.AddChild(box);
        AddChild(_popup, false, InternalMode.Back);
        Pressed += Open;
    }

    public void Open()
    {
        var scale = GetViewport().GetFinalTransform().Scale;
        _popup.Position = (Vector2I)(GetScreenPosition() + new Vector2(0, Size.Y + 4) * scale);
        _popup.ResetSize();
        _popup.Popup();
        var screen = GetViewport().GetVisibleRect().Size * scale;
        _popup.Position = new Vector2I(Math.Clamp(_popup.Position.X, 0, Math.Max(0, (int)screen.X - _popup.Size.X)), _popup.Position.Y);
    }

    /// <summary>One row per folded resource, kept by id; <paramref name="shown"/> false hides one with nothing in stock.</summary>
    public void Set(string id, string name, double amount, double? perDay, string tooltip, bool shown)
    {
        if (!_chips.TryGetValue(id, out var chip))
        {
            var row = Widgets.Row(Space.Sm);
            chip = new StockChip(id, wide: true);
            row.AddChild(chip);
            row.AddChild(Widgets.Label(name, "Muted"));
            _chips[id] = chip;
            _rows.AddChild(row);
        }
        _set.Visible(chip.GetParent<Control>(), shown);
        if (shown) chip.Set(amount, perDay, tooltip, showRate: true);
    }

    /// <summary>"+7" with the folded names in the tooltip; hidden when nothing is folded.</summary>
    public void SetCount(int count, string names)
    {
        Visible = count > 0;
        var text = $"+{count}";
        if (Text != text) Text = text;
        _set.Tooltip(this, $"{count} more resource{(count == 1 ? "" : "s")}: {names}\nClick for the list.");
        if (_popup.Visible) _popup.ResetSize();
    }
}
