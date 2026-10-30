using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game;

/// <summary>
/// The one HUD theme for every screen: worn-metal panels (<see cref="PanelArt"/>), bevelled buttons with hover /
/// pressed / disabled / keyboard-focus states, the bundled fonts (<see cref="Fonts"/>), tabs, lists, sliders, tooltips,
/// scrollbars and progress bars, all in <see cref="Palette"/> colours. Screens set it on their HUD root (and on
/// dialogs under a CanvasLayer, which does not pass themes on); panels pick a type variation and add content.
/// <para>Variations: PanelContainer <c>Card</c>, <c>CardSelected</c>, <c>Inset</c>, <c>Chip</c>, <c>Toast</c>;
/// Label <c>Title</c>, <c>Display</c>, <c>Heading</c>, <c>Muted</c>, <c>Small</c>, <c>Number</c>; Button
/// <c>Primary</c>, <c>Flat</c>, <c>Tab</c>.</para>
/// </summary>
public static class HudTheme
{
    public const int FontSize = 15;

    public static Color Text => Palette.Text;

    private static Theme? _shared;

    public static Theme Shared => _shared ??= Build();

    /// <summary>A flat box, for small things (chips, highlights, bar fills) that do not want the metal grain.</summary>
    public static StyleBoxFlat Box(Color bg, Color? border = null, int borderWidth = 1, int radius = 2, int padX = 10, int padY = 8)
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

    // Shared styleboxes the components swap in by hand (hover/selected cards).
    public static readonly StyleBox CardStyle = PanelArt.Box(new(Palette.Raised, Palette.EdgeDark, 0.05f), 8, 6);
    public static readonly StyleBox CardHoverStyle = PanelArt.Box(new(Palette.RaisedHover, Palette.EdgeLight, 0.06f), 8, 6);
    public static readonly StyleBox CardSelectedStyle = PanelArt.Box(new(Palette.RaisedHover, Palette.Amber, 0.06f), 8, 6);
    public static readonly StyleBox InsetStyle = PanelArt.Box(new(Palette.Inset, Palette.EdgeDark, 0.03f, Sunken: true), 8, 6);

    private static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = FontSize, DefaultFont = Fonts.Body };

        Panels(theme);
        Labels(theme);
        Buttons(theme);
        Lists(theme);
        Tabs(theme);
        Inputs(theme);
        Bars(theme);
        Windows(theme);
        return theme;
    }

    private static void Panels(Theme theme)
    {
        theme.SetStylebox("panel", "PanelContainer", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true), 12, 10));
        theme.SetStylebox("panel", "Panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true), 12, 10));
        Variation(theme, "Card", "PanelContainer", CardStyle);
        Variation(theme, "CardSelected", "PanelContainer", CardSelectedStyle);
        Variation(theme, "Inset", "PanelContainer", InsetStyle);
        Variation(theme, "Chip", "PanelContainer", Box(new Color(Palette.Inset, 0.85f), Palette.Edge, radius: 3, padX: 6, padY: 2));
        Variation(theme, "Toast", "PanelContainer",
            PanelArt.Box(new(Palette.Raised, Palette.EdgeDark, 0.06f, Strip: Palette.Rust, StripHeight: 3), 14, 10));

        var line = new StyleBoxLine { Color = Palette.Edge, Thickness = 1 };
        theme.SetStylebox("separator", "HSeparator", line);
        theme.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = Palette.Edge, Thickness = 1, Vertical = true });
        theme.SetConstant("separation", "HSeparator", 6);
        theme.SetConstant("separation", "VSeparator", 6);
        theme.SetConstant("separation", "VBoxContainer", 6);
        theme.SetConstant("separation", "HBoxContainer", 6);
    }

    private static void Labels(Theme theme)
    {
        theme.SetColor("font_color", "Label", Palette.Text);
        theme.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.0f));
        LabelVariation(theme, "Title", Fonts.DisplayBold, 34, Palette.Text);
        LabelVariation(theme, "Display", Fonts.Display, 20, Palette.Text);
        LabelVariation(theme, "Heading", Fonts.Display, 13, Palette.TextMuted);
        LabelVariation(theme, "Muted", Fonts.Body, 14, Palette.TextMuted);
        LabelVariation(theme, "Small", Fonts.Body, 12, Palette.TextMuted);
        LabelVariation(theme, "Number", Fonts.Numbers, 16, Palette.Text);

        theme.SetFont("normal_font", "RichTextLabel", Fonts.Body);
        theme.SetFont("bold_font", "RichTextLabel", Fonts.BodyBold);
        theme.SetFont("italics_font", "RichTextLabel", Fonts.BodyItalic);
        theme.SetFont("mono_font", "RichTextLabel", Fonts.Numbers);
        foreach (var size in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "mono_font_size" })
            theme.SetFontSize(size, "RichTextLabel", 14);
        theme.SetColor("default_color", "RichTextLabel", Palette.Text);
        theme.SetConstant("line_separation", "RichTextLabel", 2);
    }

    private static void Buttons(Theme theme)
    {
        StyleBox Look(Color fill, Color outline, bool sunken = false, float bevel = 0.05f) =>
            PanelArt.Box(new(fill, outline, bevel, Sunken: sunken, Grain: 0.012f), 10, 5);

        var normal = Look(Palette.Raised, Palette.EdgeDark);
        var hover = Look(Palette.RaisedHover, Palette.EdgeLight);
        var pressed = Look(Palette.RustDark, Palette.Amber, sunken: true);
        var disabled = Look(Palette.Panel.Darkened(0.15f), Palette.EdgeDark, bevel: 0.01f);
        var focus = Box(new Color(0, 0, 0, 0), Palette.Amber, 2, radius: 3, padX: 0, padY: 0);
        focus.DrawCenter = false;
        focus.SetExpandMarginAll(2);

        foreach (var type in new[] { "Button", "OptionButton", "MenuButton", "CheckBox", "CheckButton" })
        {
            var flat = type is "CheckBox" or "CheckButton";
            theme.SetStylebox("normal", type, flat ? Empty(4, 3) : normal);
            theme.SetStylebox("hover", type, flat ? Empty(4, 3) : hover);
            theme.SetStylebox("pressed", type, flat ? Empty(4, 3) : pressed);
            theme.SetStylebox("hover_pressed", type, flat ? Empty(4, 3) : pressed);
            theme.SetStylebox("disabled", type, flat ? Empty(4, 3) : disabled);
            theme.SetStylebox("focus", type, focus);
            theme.SetFont("font", type, Fonts.BodyBold);
            theme.SetFontSize("font_size", type, 14);
            theme.SetColor("font_color", type, Palette.Text);
            theme.SetColor("font_hover_color", type, Colors.White);
            theme.SetColor("font_pressed_color", type, Palette.Amber.Lightened(0.3f));
            theme.SetColor("font_hover_pressed_color", type, Palette.Amber.Lightened(0.3f));
            theme.SetColor("font_focus_color", type, Colors.White);
            theme.SetColor("font_disabled_color", type, Palette.TextDim);
            theme.SetColor("icon_normal_color", type, Palette.TextMuted);
            theme.SetColor("icon_hover_color", type, Colors.White);
            theme.SetColor("icon_pressed_color", type, Palette.Amber);
            theme.SetColor("icon_hover_pressed_color", type, Palette.Amber);
            theme.SetColor("icon_focus_color", type, Colors.White);
            theme.SetColor("icon_disabled_color", type, Palette.TextDim);
            theme.SetConstant("h_separation", type, 6);
            theme.SetConstant("icon_max_width", type, 20);
        }
        theme.SetIcon("arrow", "OptionButton", Icons.Get("ui_chevron_down", 14));

        theme.SetTypeVariation("Primary", "Button");
        theme.SetStylebox("normal", "Primary", Look(Palette.Rust.Darkened(0.25f), Palette.EdgeDark, bevel: 0.07f));
        theme.SetStylebox("hover", "Primary", Look(Palette.Rust.Darkened(0.05f), Palette.Amber, bevel: 0.07f));
        theme.SetColor("font_color", "Primary", Colors.White);

        theme.SetTypeVariation("Flat", "Button");
        theme.SetStylebox("normal", "Flat", Empty(6, 4));
        theme.SetStylebox("hover", "Flat", Box(new Color(1, 1, 1, 0.06f), radius: 3, padX: 6, padY: 4));
        theme.SetStylebox("pressed", "Flat", Box(new Color(Palette.Amber, 0.18f), radius: 3, padX: 6, padY: 4));
        theme.SetStylebox("hover_pressed", "Flat", Box(new Color(Palette.Amber, 0.24f), radius: 3, padX: 6, padY: 4));
        theme.SetStylebox("disabled", "Flat", Empty(6, 4));
        theme.SetColor("font_color", "Flat", Palette.TextMuted);

        // Segmented tab-like toggles (filters, speed).
        theme.SetTypeVariation("Tab", "Button");
        theme.SetStylebox("normal", "Tab", Box(Palette.Inset, Palette.EdgeDark, radius: 2, padX: 8, padY: 3));
        theme.SetStylebox("hover", "Tab", Box(Palette.Raised, Palette.EdgeLight, radius: 2, padX: 8, padY: 3));
        theme.SetStylebox("pressed", "Tab", Box(Palette.RustDark, Palette.Amber, radius: 2, padX: 8, padY: 3));
        theme.SetStylebox("hover_pressed", "Tab", Box(Palette.RustDark.Lightened(0.08f), Palette.Amber, radius: 2, padX: 8, padY: 3));
        theme.SetFontSize("font_size", "Tab", 13);

        theme.SetColor("font_color", "CheckBox", Palette.Text);
        theme.SetIcon("checked", "CheckBox", Icons.Get("ui_checkbox_on", 18));
        theme.SetIcon("unchecked", "CheckBox", Icons.Get("ui_checkbox_off", 18));
        theme.SetIcon("checked_disabled", "CheckBox", Icons.Get("ui_checkbox_on", 18));
        theme.SetIcon("unchecked_disabled", "CheckBox", Icons.Get("ui_checkbox_off", 18));
    }

    private static void Lists(Theme theme)
    {
        theme.SetStylebox("panel", "ItemList", InsetStyle);
        theme.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        theme.SetStylebox("selected", "ItemList", Box(Palette.RustDark, Palette.Amber, radius: 2, padX: 4, padY: 2));
        theme.SetStylebox("selected_focus", "ItemList", Box(Palette.RustDark, Palette.Amber, radius: 2, padX: 4, padY: 2));
        theme.SetStylebox("hovered", "ItemList", Box(new Color(1, 1, 1, 0.05f), radius: 2, padX: 4, padY: 2));
        theme.SetColor("font_color", "ItemList", Palette.Text);
        theme.SetColor("font_selected_color", "ItemList", Colors.White);
        theme.SetColor("guide_color", "ItemList", new Color(Palette.Edge, 0.4f));

        var menu = PanelArt.Box(new(Palette.Raised, Palette.EdgeLight, 0.03f), 4, 4);
        theme.SetStylebox("panel", "PopupMenu", menu);
        theme.SetStylebox("hover", "PopupMenu", Box(Palette.RustDark, Palette.Amber, radius: 2, padX: 6, padY: 3));
        theme.SetColor("font_color", "PopupMenu", Palette.Text);
        theme.SetColor("font_hover_color", "PopupMenu", Colors.White);
        theme.SetColor("font_disabled_color", "PopupMenu", Palette.TextDim);
        theme.SetColor("font_separator_color", "PopupMenu", Palette.TextMuted);
        theme.SetConstant("v_separation", "PopupMenu", 6);
        theme.SetFont("font", "PopupMenu", Fonts.Body);
        theme.SetFontSize("font_size", "PopupMenu", 14);

        var grabber = Box(Palette.EdgeLight, radius: 3, padX: 0, padY: 0);
        var track = Box(new Color(0, 0, 0, 0.3f), radius: 3, padX: 0, padY: 0);
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", bar, track);
            theme.SetStylebox("scroll_focus", bar, track);
            theme.SetStylebox("grabber", bar, grabber);
            theme.SetStylebox("grabber_highlight", bar, Box(Palette.TextDim, radius: 3, padX: 0, padY: 0));
            theme.SetStylebox("grabber_pressed", bar, Box(Palette.Amber, radius: 3, padX: 0, padY: 0));
        }
        theme.SetConstant("scrollbar_h_separation", "ScrollContainer", 4);
    }

    private static void Tabs(Theme theme)
    {
        var selected = PanelArt.Box(new(Palette.Raised, Palette.EdgeDark, 0.05f, Strip: Palette.Amber), 12, 5);
        var unselected = PanelArt.Box(new(Palette.Inset, Palette.EdgeDark, 0.02f), 12, 5);
        var hovered = PanelArt.Box(new(Palette.RaisedHover, Palette.EdgeLight, 0.04f), 12, 5);
        foreach (var type in new[] { "TabContainer", "TabBar" })
        {
            theme.SetStylebox("tab_selected", type, selected);
            theme.SetStylebox("tab_unselected", type, unselected);
            theme.SetStylebox("tab_hovered", type, hovered);
            theme.SetStylebox("tab_disabled", type, unselected);
            theme.SetStylebox("tab_focus", type, new StyleBoxEmpty());
            theme.SetFont("font", type, Fonts.Display);
            theme.SetFontSize("font_size", type, 15);
            theme.SetColor("font_selected_color", type, Palette.Text);
            theme.SetColor("font_unselected_color", type, Palette.TextMuted);
            theme.SetColor("font_hovered_color", type, Colors.White);
            theme.SetConstant("h_separation", type, 4);
            theme.SetConstant("icon_max_width", type, 16);
        }
        theme.SetStylebox("panel", "TabContainer", PanelArt.Box(new(Palette.Raised.Darkened(0.2f), Palette.EdgeDark, 0.03f), 10, 8));
        theme.SetStylebox("tabbar_background", "TabContainer", new StyleBoxEmpty());
    }

    private static void Inputs(Theme theme)
    {
        var field = Box(Palette.Inset, Palette.Edge, radius: 2, padX: 8, padY: 5);
        var fieldFocus = Box(Palette.Inset, Palette.Amber, radius: 2, padX: 8, padY: 5);
        foreach (var type in new[] { "LineEdit", "SpinBox", "TextEdit" })
        {
            theme.SetStylebox("normal", type, field);
            theme.SetStylebox("focus", type, fieldFocus);
            theme.SetStylebox("read_only", type, field);
            theme.SetColor("font_color", type, Palette.Text);
            theme.SetColor("font_placeholder_color", type, Palette.TextDim);
            theme.SetColor("caret_color", type, Palette.Amber);
            theme.SetColor("selection_color", type, new Color(Palette.Amber, 0.35f));
        }

        var slider = Box(Palette.Inset, Palette.EdgeDark, radius: 3, padX: 0, padY: 3);
        var area = Box(Palette.Rust, radius: 3, padX: 0, padY: 3);
        theme.SetStylebox("slider", "HSlider", slider);
        theme.SetStylebox("grabber_area", "HSlider", area);
        theme.SetStylebox("grabber_area_highlight", "HSlider", Box(Palette.Amber, radius: 3, padX: 0, padY: 3));
        theme.SetIcon("grabber", "HSlider", Icons.Get("ui_grabber", 18));
        theme.SetIcon("grabber_highlight", "HSlider", Icons.Get("ui_grabber_hot", 18));
        theme.SetStylebox("focus", "HSlider", Box(new Color(0, 0, 0, 0), Palette.Amber, 1, radius: 3, padX: 0, padY: 0));
    }

    private static void Bars(Theme theme)
    {
        theme.SetStylebox("background", "ProgressBar", Box(Palette.Void, Palette.EdgeDark, radius: 2, padX: 0, padY: 0));
        theme.SetStylebox("fill", "ProgressBar", Box(Palette.Good, radius: 2, padX: 0, padY: 0));
        theme.SetFont("font", "ProgressBar", Fonts.Numbers);
        theme.SetFontSize("font_size", "ProgressBar", 12);
        theme.SetColor("font_color", "ProgressBar", Palette.Text);
        theme.SetColor("font_outline_color", "ProgressBar", Palette.Void);
        theme.SetConstant("outline_size", "ProgressBar", 3);

        theme.SetStylebox("panel", "TooltipPanel",
            PanelArt.Box(new(new Color("15130f"), Palette.EdgeLight, 0.03f, Strip: Palette.Rust), 10, 7));
        theme.SetColor("font_color", "TooltipLabel", Palette.Text);
        theme.SetFont("font", "TooltipLabel", Fonts.Body);
        theme.SetFontSize("font_size", "TooltipLabel", 14);
    }

    private static void Windows(Theme theme)
    {
        var panel = PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.Rust, StripHeight: 3), 14, 12);
        theme.SetStylebox("panel", "AcceptDialog", panel);
        theme.SetStylebox("panel", "ConfirmationDialog", panel);
        var border = PanelArt.Box(new(Palette.Raised, Palette.EdgeDark, 0.05f), 4, 4);
        border.ExpandMarginTop = 30;
        border.ExpandMarginLeft = border.ExpandMarginRight = border.ExpandMarginBottom = 4;
        theme.SetStylebox("embedded_border", "Window", border);
        theme.SetStylebox("embedded_unfocused_border", "Window", border);
        theme.SetFont("title_font", "Window", Fonts.Display);
        theme.SetFontSize("title_font_size", "Window", 17);
        theme.SetColor("title_color", "Window", Palette.Text);
        theme.SetConstant("title_height", "Window", 30);
        theme.SetIcon("close", "Window", Icons.Get("ui_close", 16));
        theme.SetIcon("close_pressed", "Window", Icons.Get("ui_close", 16));
    }

    private static void Variation(Theme theme, string name, string baseType, StyleBox panel)
    {
        theme.SetTypeVariation(name, baseType);
        theme.SetStylebox("panel", name, panel);
    }

    private static void LabelVariation(Theme theme, string name, Font font, int size, Color color)
    {
        theme.SetTypeVariation(name, "Label");
        theme.SetFont("font", name, font);
        theme.SetFontSize("font_size", name, size);
        theme.SetColor("font_color", name, color);
    }

    private static StyleBoxEmpty Empty(int padX, int padY) => new()
    {
        ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY,
    };
}
