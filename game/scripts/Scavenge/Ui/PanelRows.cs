using System;
using System.Collections.Generic;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The rows of an order panel, kept across refreshes. A panel's refresh runs between <see cref="Begin"/> and
/// <see cref="End"/> and asks for its rows in order (<see cref="Heading"/>, <see cref="Note"/>, <see cref="Button"/>); the
/// row at the same place and of the same kind as last time is re-used and updated in place, extra rows are hidden, and
/// only a row of a new kind is built. The scavenge HUD refreshes after every command and event, and building every
/// button again (theme lookups, text shaping, layout) was most of the 20–30 ms frame each command cost.
/// <see cref="Fresh"/> takes a one-off control, freed on the next refresh.
/// </summary>
public sealed class PanelRows(VBoxContainer box)
{
    private readonly List<Control> _rows = new();
    private readonly List<Control> _fresh = new();
    private int _next; // pooled rows taken this refresh
    private int _at;   // children placed this refresh (rows and one-offs), in the order asked

    public void Begin()
    {
        _next = _at = 0;
        foreach (var c in _fresh)
        {
            box.RemoveChild(c);
            c.QueueFree();
        }
        _fresh.Clear();
    }

    /// <summary>Hides the rows this refresh did not use.</summary>
    public void End()
    {
        for (var i = _next; i < _rows.Count; i++)
            if (_rows[i].Visible) _rows[i].Visible = false;
    }

    private void Place(Control c)
    {
        if (c.GetIndex() != _at) box.MoveChild(c, _at);
        _at++;
    }

    private T Take<T>(Func<T> make) where T : Control
    {
        if (_next < _rows.Count && _rows[_next] is T same)
        {
            _next++;
            if (!same.Visible)
            {
                // A row coming back (a salvage node in reach again, another unit's extra attack) eases in.
                same.Visible = true;
                Motion.Enter(same, _next);
            }
            Place(same);
            return same;
        }
        var made = make();
        if (_next < _rows.Count)
        {
            // A row of another kind sits here: the new one takes its place.
            var old = _rows[_next];
            box.RemoveChild(old);
            old.QueueFree();
            _rows[_next] = made;
        }
        else _rows.Add(made);
        box.AddChild(made);
        _next++;
        Place(made);
        return made;
    }

    /// <summary>A sub-heading ("SALVAGE"), with an (i) for the help <paramref name="topic"/> when given.</summary>
    public TipHeading Heading(string text, string? topic = null, string tooltip = "")
    {
        var heading = Take(() => new TipHeading());
        heading.Configure(text, topic, tooltip);
        return heading;
    }

    public Label Note(string text, string variation = "Small")
    {
        var label = Take(() => Widgets.Wrapped("", variation));
        label.ThemeTypeVariation = variation;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.Text = text;
        return label;
    }

    public ActionButton Button(string icon, string text, string detail, int apCost, string? hotkey, string tooltip, Action onPressed)
    {
        var made = false;
        var button = Take(() =>
        {
            made = true;
            return new ActionButton(icon, text, detail, apCost, hotkey, tooltip);
        });
        if (!made) button.Configure(icon, text, detail, apCost, hotkey, tooltip);
        button.OnPressed = onPressed;
        return button;
    }

    /// <summary>A one-off control (a chip, a row of small buttons) placed after the rows so far; freed at the next <see cref="Begin"/>.</summary>
    public T Fresh<T>(T control) where T : Control
    {
        box.AddChild(control);
        Place(control);
        _fresh.Add(control);
        return control;
    }

    /// <summary>A pooled row of the caller's own kind (the ping buttons, a status chip): made once, re-used in place.</summary>
    public T Row<T>(Func<T> make) where T : Control => Take(make);

    /// <summary>A status chip (icon, text, tint, tooltip), updated in place.</summary>
    public RowChip Chip(string icon, string text, Color tint, string tooltip)
    {
        var chip = Take(() => new RowChip());
        chip.Configure(icon, text, tint, tooltip);
        return chip;
    }
}

/// <summary>A pooled sub-heading of an order panel: caps label, and an <see cref="Hud.Help.InfoTip"/> when it has a help topic.</summary>
public partial class TipHeading : HBoxContainer
{
    private readonly Label _label = Widgets.Label("", "Heading");
    private readonly Hud.Help.InfoTip _tip = new("");

    public TipHeading()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);
        _label.AutowrapMode = TextServer.AutowrapMode.Off;
        _label.MouseFilter = MouseFilterEnum.Pass;
        AddChild(_label);
        AddChild(_tip);
    }

    public void Configure(string text, string? topic, string tooltip = "")
    {
        _label.Text = text;
        _label.TooltipText = tooltip;
        _tip.Topic = topic ?? "";
        _tip.Visible = topic is not null;
    }
}

/// <summary>A pooled status chip: icon and text in an inset, tinted.</summary>
public partial class RowChip : PanelContainer
{
    private readonly TextureRect _icon = Icons.Rect(Icons.Get("ui_info", 14), 16, Palette.TextMuted);
    private readonly Label _text = Widgets.Label("", "Number");
    private string _iconId = "ui_info";

    public RowChip()
    {
        ThemeTypeVariation = "Chip";
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var row = Widgets.Row(4);
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);
        row.AddChild(_icon);
        _text.ClipText = true;
        _text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _text.AddThemeFontSizeOverride("font_size", 15);
        row.AddChild(_text);
    }

    public void Configure(string icon, string text, Color tint, string tooltip)
    {
        if (icon != _iconId)
        {
            _iconId = icon;
            _icon.Texture = Icons.Get(icon, 14);
        }
        _text.Text = text;
        _text.AddThemeColorOverride("font_color", tint);
        TooltipText = tooltip;
    }
}

/// <summary>
/// Panels of the scavenge HUD arrive and leave with <see cref="Motion.Reveal"/> / <see cref="Motion.Conceal"/> when
/// what they show comes or goes, and are left alone otherwise (the HUD refreshes after every event).
/// </summary>
public static class Presence
{
    private static readonly StringName Shown = "presence_shown";

    public static void Set(Control control, bool shown)
    {
        var was = control.HasMeta(Shown) ? control.GetMeta(Shown).AsBool() : control.Visible;
        if (was == shown) return;
        control.SetMeta(Shown, shown);
        if (shown) Motion.Reveal(control);
        else Motion.Conceal(control);
    }
}
