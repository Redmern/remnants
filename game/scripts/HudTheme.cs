using Godot;

namespace Remnants.Game;

/// <summary>
/// The one HUD theme shared by the colony and scavenge screens: near-opaque dark panels with a warm rust border,
/// flat buttons, slim scrollbars, one font size scale. Each screen sets it on its HUD root
/// (and on dialogs under a CanvasLayer, which does not pass themes on); panels then only add their content.
/// </summary>
public static class HudTheme
{
    public const int FontSize = 14;

    public static readonly Color PanelBg = new(0.075f, 0.072f, 0.066f, 0.96f);
    public static readonly Color PanelBorder = new(0.32f, 0.27f, 0.21f);
    public static readonly Color Text = new(0.88f, 0.86f, 0.8f);
    public static readonly Color TextDisabled = new(0.5f, 0.48f, 0.44f);
    public static readonly Color Accent = new(0.95f, 0.72f, 0.32f);

    private static Theme? _shared;

    public static Theme Shared => _shared ??= Build();

    /// <summary>A flat box: the base for panels, buttons and chips.</summary>
    public static StyleBoxFlat Box(Color bg, Color? border = null, int borderWidth = 1, int radius = 3, int padX = 10, int padY = 8)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border ?? bg,
            ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY,
        };
        box.SetBorderWidthAll(border is null ? 0 : borderWidth);
        box.SetCornerRadiusAll(radius);
        return box;
    }

    private static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = FontSize };

        theme.SetStylebox("panel", "PanelContainer", Box(PanelBg, PanelBorder));
        theme.SetStylebox("panel", "TooltipPanel", Box(new Color(0.05f, 0.05f, 0.045f, 0.98f), PanelBorder, padX: 8, padY: 5));
        theme.SetColor("font_color", "TooltipLabel", Text);

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("default_color", "RichTextLabel", Text);

        var button = new Color(0.16f, 0.15f, 0.13f);
        theme.SetStylebox("normal", "Button", Box(button, new Color(0.26f, 0.23f, 0.19f), padX: 10, padY: 4));
        theme.SetStylebox("hover", "Button", Box(new Color(0.22f, 0.2f, 0.17f), new Color(0.45f, 0.38f, 0.28f), padX: 10, padY: 4));
        theme.SetStylebox("pressed", "Button", Box(new Color(0.3f, 0.24f, 0.14f), Accent, padX: 10, padY: 4));
        theme.SetStylebox("hover_pressed", "Button", Box(new Color(0.34f, 0.27f, 0.16f), Accent, padX: 10, padY: 4));
        theme.SetStylebox("disabled", "Button", Box(new Color(0.11f, 0.105f, 0.095f), padX: 10, padY: 4));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_hover_pressed_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", TextDisabled);

        var grabber = Box(new Color(0.35f, 0.31f, 0.26f), radius: 3, padX: 0, padY: 0);
        var track = Box(new Color(0, 0, 0, 0.25f), radius: 3, padX: 0, padY: 0);
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", bar, track);
            theme.SetStylebox("grabber", bar, grabber);
            theme.SetStylebox("grabber_highlight", bar, Box(new Color(0.48f, 0.42f, 0.33f), radius: 3, padX: 0, padY: 0));
            theme.SetStylebox("grabber_pressed", bar, Box(Accent, radius: 3, padX: 0, padY: 0));
        }

        theme.SetStylebox("background", "ProgressBar", Box(new Color(0.03f, 0.03f, 0.03f, 0.9f), radius: 2, padX: 0, padY: 0));
        theme.SetStylebox("fill", "ProgressBar", Box(new Color(0.55f, 0.8f, 0.45f), radius: 2, padX: 0, padY: 0));

        var line = new StyleBoxLine { Color = PanelBorder, Thickness = 1 };
        theme.SetStylebox("separator", "HSeparator", line);
        theme.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = PanelBorder, Thickness = 1, Vertical = true });
        return theme;
    }
}
