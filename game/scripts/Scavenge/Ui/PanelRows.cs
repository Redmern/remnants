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
            if (!same.Visible) same.Visible = true;
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

    public Label Heading(string text)
    {
        var label = Take(() => Widgets.Label("", "Heading"));
        label.ThemeTypeVariation = "Heading";
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        label.Text = text;
        return label;
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
}
