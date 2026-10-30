using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>Small builders so the HUD panels can be laid out in code without scene boilerplate. Colours and looks come from <see cref="Palette"/> and <see cref="HudTheme"/>.</summary>
public static class Ui
{
    public static readonly Color Muted = Palette.TextMuted;
    public static readonly Color Bad = Palette.Danger;
    public static readonly Color Good = Palette.Good;

    public static Label Label(string text = "", int size = 14, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        return label;
    }

    /// <summary>A section header: condensed caps and a rule, optionally with an icon.</summary>
    public static Control Heading(string text, string? icon = null) => Widgets.SectionHeader(text, icon);

    public static Button Button(string text, System.Action onPressed, int minWidth = 0) =>
        Widgets.TextButton(text, onPressed, minWidth: minWidth);

    public static RichTextLabel Rich(bool scroll = false) => new()
    {
        BbcodeEnabled = true,
        FitContent = !scroll,
        ScrollActive = scroll,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        SelectionEnabled = false,
        MouseFilter = Control.MouseFilterEnum.Pass,
    };

    /// <summary>A HUD panel (styled by <see cref="HudTheme"/>) with a VBox inside; returns the VBox to fill.</summary>
    public static VBoxContainer Panel(PanelContainer panel, int separation = 8)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        panel.AddChild(box);
        return box;
    }

    /// <summary>A panel title row: icon, display-font title, filler, an optional close button.</summary>
    public static HBoxContainer TitleRow(string title, string icon, System.Action? onClose = null, string? closeHotkey = null)
    {
        var row = Widgets.Row(8);
        row.AddChild(Icons.Rect(Icons.Get(icon, 20), 20, Palette.Rust));
        var label = Widgets.Label(title, "Display");
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.ClipText = true;
        row.AddChild(label);
        if (onClose is not null)
            row.AddChild(Widgets.IconButton("ui_close", "", "Close", onClose, closeHotkey, "Flat"));
        return row;
    }

    public static string Hex(Color c) => c.ToHtml(false);
}
