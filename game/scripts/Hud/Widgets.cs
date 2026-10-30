using System;
using System.Collections.Generic;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Small stateless builders for the HUD component library: labels by theme variation, section headers, icon buttons
/// (with their hotkey in the tooltip), level / AP pips, chips and cost chips. Stateful components have their own
/// classes (<see cref="StatBar"/>, <see cref="ResourceChip"/>, <see cref="Toasts"/>, <see cref="ConfirmDialog"/>).
/// </summary>
public static class Widgets
{
    public static Label Label(string text = "", string variation = "", Color? color = null, int? size = null)
    {
        var label = new Label { Text = text, ThemeTypeVariation = variation, VerticalAlignment = VerticalAlignment.Center };
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        if (size is { } s) label.AddThemeFontSizeOverride("font_size", s);
        return label;
    }

    public static Label Wrapped(string text = "", string variation = "", Color? color = null)
    {
        var label = Label(text, variation, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.CustomMinimumSize = new Vector2(40, 0);
        return label;
    }

    /// <summary>"ICON  TITLE ─────── [extra]": the header every panel section starts with.</summary>
    public static HBoxContainer SectionHeader(string title, string? icon = null, Control? extra = null)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        if (icon is not null) row.AddChild(Icons.Rect(Icons.Get(icon, 14), 14, Palette.Rust));
        row.AddChild(Label(title.ToUpperInvariant(), "Heading"));
        var rule = new HSeparator { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddChild(rule);
        if (extra is not null) row.AddChild(extra);
        return row;
    }

    /// <summary>A button with an icon and optional text; the tooltip names the hotkey when there is one.</summary>
    public static Button IconButton(string icon, string text, string tooltip, Action onPressed, string? hotkey = null, string variation = "")
    {
        var button = new Button
        {
            Text = text,
            Icon = Icons.Get(icon, 18),
            ThemeTypeVariation = variation,
            TooltipText = Tip(tooltip, hotkey),
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(text.Length == 0 ? 34 : 0, 32),
            IconAlignment = HorizontalAlignment.Left,
            ExpandIcon = false,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.Pressed += onPressed;
        Motion.PressFeedback(button);
        return button;
    }

    public static Button TextButton(string text, Action onPressed, string tooltip = "", string variation = "", int minWidth = 0)
    {
        var button = new Button
        {
            Text = text,
            ThemeTypeVariation = variation,
            TooltipText = tooltip,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(minWidth, 30),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        button.Pressed += onPressed;
        Motion.PressFeedback(button);
        return button;
    }

    /// <summary>"Build a structure\n[B]": tooltip text with its hotkey on a second line.</summary>
    public static string Tip(string text, string? hotkey) => hotkey is null ? text : $"{text}\nHotkey: {hotkey}";

    /// <summary>A row of square pips (<paramref name="filled"/> of <paramref name="total"/>), e.g. building level or AP.</summary>
    public static Pips Pips(int filled, int total, Color? on = null) => new(filled, total, on ?? Palette.Amber);

    /// <summary>Icon + text in a small inset chip.</summary>
    public static PanelContainer Chip(Texture2D? icon, string text, Color? textColor = null, string tooltip = "", Color? iconTint = null)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Chip", TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Stop };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        chip.AddChild(row);
        if (icon is not null) row.AddChild(Icons.Rect(icon, 16, iconTint ?? Palette.TextMuted));
        var label = Label(text, "Number", textColor);
        label.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(label);
        return chip;
    }

    /// <summary>
    /// Cost chips: one per resource, normal when the stock covers it, red with a warning mark (so colour is not the
    /// only signal) and "have N" in the tooltip when it does not.
    /// </summary>
    public static HFlowContainer CostChips(IEnumerable<(string Id, string Name, double Need, double Have)> costs)
    {
        var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        var any = false;
        foreach (var (id, name, need, have) in costs)
        {
            any = true;
            var short_ = have + 1e-9 < need;
            var text = short_ ? $"{Fmt.Amount(need)} !" : Fmt.Amount(need);
            var tip = short_ ? $"{name}: need {Fmt.Amount(need)}, have {Fmt.Amount(have)}" : $"{name}: {Fmt.Amount(need)} (have {Fmt.Amount(have)})";
            flow.AddChild(Chip(Icons.Resource(id, 16), text, short_ ? Palette.Danger : Palette.Text, tip, short_ ? Palette.Danger : Palette.TextMuted));
        }
        if (!any) flow.AddChild(Label("free", "Muted"));
        return flow;
    }

    public static Control Spacer(bool expand = true) =>
        new() { SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static VBoxContainer Column(int separation = 6)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    public static HBoxContainer Row(int separation = 6)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>Frees every child of <paramref name="node"/> (for panels that rebuild a list).</summary>
    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}

/// <summary>A row of small square pips: building levels, AP, party slots.</summary>
public partial class Pips : Control
{
    private int _filled;
    private int _total;
    private Color _on;
    private const float Pip = 9, Gap = 3;

    public Pips(int filled, int total, Color on)
    {
        _on = on;
        MouseFilter = MouseFilterEnum.Pass;
        Set(filled, total);
    }

    public Pips() : this(0, 0, Palette.Amber) { }

    public void Set(int filled, int total, Color? on = null)
    {
        _filled = filled;
        _total = total;
        if (on is { } c) _on = c;
        CustomMinimumSize = new Vector2(Math.Max(0, total * (Pip + Gap) - Gap), Pip + 2);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var y = (Size.Y - Pip) / 2;
        for (var i = 0; i < _total; i++)
        {
            var rect = new Rect2(i * (Pip + Gap), y, Pip, Pip);
            DrawRect(rect, i < _filled ? _on : Palette.Inset);
            DrawRect(rect, i < _filled ? _on.Darkened(0.4f) : Palette.EdgeLight, false, 1);
        }
    }
}
