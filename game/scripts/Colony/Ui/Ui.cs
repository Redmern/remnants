using Godot;

namespace Remnants.Game.Colony.Ui;

/// <summary>Small builders so the HUD panels can be laid out in code without scene boilerplate.</summary>
public static class Ui
{
    public static readonly Color Muted = new(0.7f, 0.68f, 0.62f);
    public static readonly Color Bad = new(0.9f, 0.45f, 0.35f);
    public static readonly Color Good = new(0.6f, 0.85f, 0.55f);

    public static Label Label(string text = "", int size = 14, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        return label;
    }

    public static Label Heading(string text) => Label(text.ToUpperInvariant(), 13, Muted);

    public static Button Button(string text, System.Action onPressed, int minWidth = 0)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 30), FocusMode = Control.FocusModeEnum.None };
        button.Pressed += onPressed;
        return button;
    }

    public static RichTextLabel Rich(bool scroll = false)
    {
        var rich = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = !scroll,
            ScrollActive = scroll,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectionEnabled = false,
        };
        rich.AddThemeFontSizeOverride("normal_font_size", 14);
        rich.AddThemeFontSizeOverride("bold_font_size", 14);
        return rich;
    }

    /// <summary>A dark translucent panel with a VBox inside; returns the VBox to fill.</summary>
    public static VBoxContainer Panel(PanelContainer panel, int separation = 6)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.09f, 0.08f, 0.88f),
            BorderColor = new Color(0.35f, 0.3f, 0.24f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        panel.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        panel.AddChild(box);
        return box;
    }

    public static string Hex(Color c) => c.ToHtml(false);
}
