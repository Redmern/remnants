using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// A small card next to the cursor while an ability card is aimed and the cursor is over one of its targets: the card and
/// the target, and what comes of it (<see cref="CardTargetOption.Outcome"/>: the hit chance and damage of a strike, where
/// a shoved unit lands, a blow it lands in) or, greyed, Core's reason it cannot go there. Purely visual; the screen feeds it.
/// </summary>
public partial class CardTargetTip : PanelContainer
{
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly Label _state = Widgets.Label("", "Small");
    private readonly Label _text = Widgets.Wrapped("", "Small");

    public CardTargetTip()
    {
        ThemeTypeVariation = "Card";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(280, 0);
        _text.CustomMinimumSize = new Vector2(264, 0);
        Visible = false;
        var col = Widgets.Column(2);
        col.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(col);
        col.AddChild(_title);
        col.AddChild(_state);
        col.AddChild(_text);
    }

    /// <summary>Shows the option at the cursor: <paramref name="target"/> names the unit or tile.</summary>
    public void Show(string cardName, string target, CardTargetOption option, Vector2 screen)
    {
        _title.Text = $"{cardName.ToUpperInvariant()} → {target}";
        _title.AddThemeColorOverride("font_color", option.Ok ? Palette.Amber : Palette.TextDim);
        _state.Text = option.Ok ? "Click to play" + (option.Landing is { } land ? $" · lands on {land}" : "") : "Can't aim here";
        _state.AddThemeColorOverride("font_color", option.Ok ? Palette.Good : Palette.Warn);
        _text.Text = option.Ok ? (option.Outcome.Length > 0 ? Capital(option.Outcome) + "." : "") : option.Reason;
        _text.AddThemeColorOverride("font_color", option.Ok ? Palette.Text : Palette.TextMuted);
        _text.Visible = _text.Text.Length > 0;
        Position = screen + new Vector2(18, 18);
        Visible = true;
        Size = Vector2.Zero;
        CallDeferred(Control.MethodName.ResetSize);
    }

    public void HideTip() => Visible = false;

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
