using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// A stockpile chip: icon, amount and the change over the last day (green "+", red "−", with the sign so colour is
/// not the only signal). The tooltip carries the name and the breakdown the owner passes in.
/// </summary>
public partial class ResourceChip : PanelContainer
{
    private readonly Label _amount = Widgets.Label("", "Number");
    private readonly Label _delta = Widgets.Label("", "Number");
    private double _last = double.NaN;

    public ResourceChip() : this("wood") { }

    public ResourceChip(string resourceId)
    {
        ThemeTypeVariation = "Chip";
        MouseFilter = MouseFilterEnum.Stop;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        AddChild(row);
        row.AddChild(Icons.Rect(Icons.Resource(resourceId, 18), 18, Palette.TextMuted));
        _amount.AddThemeFontSizeOverride("font_size", 17);
        row.AddChild(_amount);
        _delta.AddThemeFontSizeOverride("font_size", 12);
        _delta.VerticalAlignment = VerticalAlignment.Bottom;
        row.AddChild(_delta);
    }

    /// <summary>Shows <paramref name="amount"/> and, when known, the change per day.</summary>
    public void Set(double amount, double? perDay, string tooltip)
    {
        _amount.Text = Fmt.Amount(amount);
        if (!double.IsNaN(_last) && Fmt.Amount(_last) != _amount.Text) Motion.Flash(_amount, amount > _last ? Palette.Good : Palette.Danger);
        _last = amount;
        _delta.Visible = perDay is { } d && System.Math.Abs(d) >= 0.05;
        if (perDay is { } day)
        {
            _delta.Text = Fmt.Delta(day);
            _delta.AddThemeColorOverride("font_color", day > 0 ? Palette.Good : Palette.Danger);
        }
        TooltipText = tooltip;
    }
}
