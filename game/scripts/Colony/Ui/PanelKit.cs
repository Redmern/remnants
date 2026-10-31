using System;
using System.Collections.Generic;
using Godot;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// One tab strip over a set of pages, the way the colony's dense screens split "what you do here" into one task per view
/// (Garage: Vehicles / Upgrades / Repairs / Build; Med Bay: Patients / Surgery / Dispensary; benches: Queue / Recipes /
/// Charging). Tabs act on press, take keyboard focus (Left / Right move between them) and the page fades in with
/// <see cref="Motion.Reveal"/>. Pages are built once and kept; only the visible one is refreshed by its owner.
/// </summary>
public partial class PanelTabs : VBoxContainer
{
    private readonly HBoxContainer _strip = Widgets.Row(2);
    private readonly ButtonGroup _group = new();
    private readonly Dictionary<string, (Button Tab, Control Page, string Label)> _pages = new();
    private readonly List<string> _order = new();

    public string? Selected { get; private set; }

    /// <summary>A tab was picked (by the player or <see cref="Select"/>).</summary>
    public event Action<string>? Changed;

    public PanelTabs()
    {
        AddThemeConstantOverride("separation", 8);
        _strip.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_strip);
    }

    /// <summary>The strip itself, e.g. to add a control after the tabs.</summary>
    public HBoxContainer Strip => _strip;

    public T Add<T>(string id, string label, T page, string tooltip = "") where T : Control
    {
        var tab = Motion.OnPress(new Button
        {
            Text = label,
            ThemeTypeVariation = "Tab",
            ToggleMode = true,
            ButtonGroup = _group,
            TooltipText = tooltip,
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
            MouseDefaultCursorShape = CursorShape.PointingHand,
            ClipText = true,
        });
        tab.Pressed += () => Select(id);
        tab.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true } key) return;
            var step = key.Keycode switch { Key.Right => 1, Key.Left => -1, _ => 0 };
            if (step == 0) return;
            var visible = _order.FindAll(o => _pages[o].Tab.Visible);
            var at = visible.IndexOf(id);
            if (at < 0) return;
            var next = visible[(at + step + visible.Count) % visible.Count];
            Select(next);
            _pages[next].Tab.GrabFocus();
            tab.AcceptEvent();
        };
        Motion.PressFeedback(tab);
        _strip.AddChild(tab);
        page.Visible = false;
        page.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(page);
        _pages[id] = (tab, page, label);
        _order.Add(id);
        if (Selected is null) Select(id, notify: false);
        return page;
    }

    public Control Page(string id) => _pages[id].Page;

    public Button Tab(string id) => _pages[id].Tab;

    /// <summary>Renames a tab ("Queue 3"), only when it changed.</summary>
    public void SetLabel(string id, string label)
    {
        if (_pages[id].Tab.Text != label) _pages[id].Tab.Text = label;
    }

    /// <summary>Hides or shows a tab (a bench without a charging bench); a hidden selected tab falls back to the first.</summary>
    public void SetTabVisible(string id, bool visible)
    {
        var tab = _pages[id].Tab;
        if (tab.Visible == visible) return;
        tab.Visible = visible;
        if (!visible && Selected == id) Select(_order.Find(o => _pages[o].Tab.Visible) ?? id);
    }

    public void Select(string id) => Select(id, notify: true);

    private void Select(string id, bool notify)
    {
        if (!_pages.ContainsKey(id)) return;
        var changed = Selected != id;
        Selected = id;
        foreach (var (key, (tab, page, _)) in _pages)
        {
            tab.SetPressedNoSignal(key == id);
            if (key == id) Motion.Reveal(page, new Vector2(-8, 0));
            else page.Visible = false;
        }
        if (changed && notify) Changed?.Invoke(id);
    }
}

/// <summary>
/// "▸ More" over a body that stays out of the way until asked for (details, all stats, locked recipes). The header is a
/// flat toggle with keyboard focus; the body reveals and conceals with <see cref="Motion"/>. The open state survives the
/// owner's refreshes because the body is kept, not rebuilt.
/// </summary>
public partial class Expander : VBoxContainer
{
    private readonly Button _head;
    private string _title;

    public VBoxContainer Body { get; } = Widgets.Column(4);
    public bool Expanded { get; private set; }

    public event Action<bool>? Toggled;

    public Expander(string title, bool expanded = false, Control? extra = null)
    {
        _title = title;
        AddThemeConstantOverride("separation", 4);
        var row = Widgets.Row(4);
        _head = Motion.OnPress(new Button
        {
            ThemeTypeVariation = "Flat",
            FocusMode = FocusModeEnum.All,
            Alignment = HorizontalAlignment.Left,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(0, 24),
        });
        _head.AddThemeFontSizeOverride("font_size", 13);
        _head.AddThemeColorOverride("font_color", Palette.TextMuted);
        _head.Pressed += () => SetExpanded(!Expanded);
        row.AddChild(_head);
        if (extra is not null) row.AddChild(extra);
        AddChild(row);
        Body.Visible = expanded;
        AddChild(Body);
        Expanded = expanded;
        Paint();
    }

    public void SetTitle(string title)
    {
        if (_title == title) return;
        _title = title;
        Paint();
    }

    public void SetExpanded(bool expanded)
    {
        if (Expanded == expanded) return;
        Expanded = expanded;
        if (expanded) Motion.Reveal(Body);
        else Motion.Conceal(Body);
        Paint();
        Toggled?.Invoke(expanded);
    }

    private void Paint() => _head.Text = (Expanded ? "▾ " : "▸ ") + _title;
}

/// <summary>"[−] 3 [+]": a small quantity stepper (batches to queue). Wheel and the arrow keys step it too.</summary>
public partial class Stepper : HBoxContainer
{
    private readonly Label _value = Widgets.Label("1", "Number");
    private readonly Button _minus, _plus;
    private int _amount;

    public int Min { get; }
    public int Max { get; }
    public int Value
    {
        get => _amount;
        set
        {
            var v = Math.Clamp(value, Min, Max);
            if (v == _amount && _value.Text == v.ToString()) return;
            _amount = v;
            _value.Text = v.ToString();
            _minus.Disabled = v <= Min;
            _plus.Disabled = v >= Max;
            Changed?.Invoke(v);
        }
    }

    public event Action<int>? Changed;

    public Stepper(int min = 1, int max = 99, int value = 1, string tooltip = "")
    {
        Min = min;
        Max = max;
        AddThemeConstantOverride("separation", 2);
        TooltipText = tooltip;
        _minus = Step("−", -1);
        AddChild(_minus);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        _value.CustomMinimumSize = new Vector2(26, 0);
        _value.MouseFilter = MouseFilterEnum.Pass;
        AddChild(_value);
        _plus = Step("+", 1);
        AddChild(_plus);
        _amount = min - 1;
        Value = value;
    }

    private Button Step(string text, int by)
    {
        var button = Motion.OnPress(new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.All,
            CustomMinimumSize = new Vector2(26, 28),
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = TooltipText,
        });
        button.Pressed += () => Value += by * (Input.IsKeyPressed(Key.Shift) ? 5 : 1);
        button.GuiInput += e =>
        {
            switch (e)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                    Value++;
                    button.AcceptEvent();
                    break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                    Value--;
                    button.AcceptEvent();
                    break;
            }
        };
        Motion.PressFeedback(button);
        return button;
    }
}

/// <summary>
/// The rows of a list kept by key across refreshes (orders by id, vehicles by id, patients by survivor): a row is built
/// once when its key appears (and enters with <see cref="Motion.Enter"/>), moved when the order changes, updated in
/// place by the owner every refresh, and leaves with <see cref="Motion.Exit"/> when its key goes. Nothing is cleared
/// while the player is using the list, so open pickers, hover and focus survive a tick.
/// </summary>
public sealed class KeyedRows<TKey> where TKey : notnull
{
    private readonly Container _box;
    private readonly Dictionary<TKey, Control> _rows = new();
    private readonly int _offset;

    /// <param name="offset">Children of <paramref name="box"/> before the rows (a header kept on top).</param>
    public KeyedRows(Container box, int offset = 0)
    {
        _box = box;
        _offset = offset;
    }

    public int Count => _rows.Count;

    public bool TryGet(TKey key, out Control row) => _rows.TryGetValue(key, out row!);

    /// <summary>
    /// Makes the rows match <paramref name="keys"/> in that order: new keys get <paramref name="make"/>, every row
    /// (new or kept) gets <paramref name="update"/>, rows of keys no longer there leave.
    /// </summary>
    public void Sync<TRow>(IReadOnlyList<TKey> keys, Func<TKey, TRow> make, Action<TKey, TRow>? update = null) where TRow : Control
    {
        var wanted = new HashSet<TKey>(keys);
        foreach (var key in new List<TKey>(_rows.Keys))
        {
            if (wanted.Contains(key)) continue;
            var gone = _rows[key];
            _rows.Remove(key);
            // Out of the order at once, so the rows below close up without waiting for the fade.
            if (gone.GetParent() == _box) _box.MoveChild(gone, _box.GetChildCount() - 1);
            Motion.Exit(gone);
        }
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var fresh = false;
            if (!_rows.TryGetValue(key, out var row))
            {
                row = make(key);
                _rows[key] = row;
                _box.AddChild(row);
                fresh = true;
            }
            if (row.GetIndex() != _offset + i) _box.MoveChild(row, _offset + i);
            update?.Invoke(key, (TRow)row);
            if (fresh) Motion.Enter(row, i);
        }
    }

    /// <summary>Drops every row at once (the list now shows something else).</summary>
    public void Reset()
    {
        foreach (var row in _rows.Values)
        {
            if (row.GetParent() == _box) _box.RemoveChild(row);
            row.QueueFree();
        }
        _rows.Clear();
    }
}

/// <summary>Small helpers shared by the colony panels' stage-2 layouts.</summary>
public static class PanelKit
{
    /// <summary>A button the keyboard can reach (the widget builders default to no focus for map-overlay use).</summary>
    public static T Focusable<T>(T control) where T : Control
    {
        control.FocusMode = Control.FocusModeEnum.All;
        return control;
    }

    /// <summary>A muted caption with an (i) to its help topic: "Quality (i)".</summary>
    public static HBoxContainer Tip(string text, string topic, string variation = "Small", Color? color = null, Func<Remnants.Core.Help.HelpContext>? context = null) =>
        InfoTip.Beside(Widgets.Label(text, variation, color ?? Palette.TextMuted), topic, context);

    /// <summary>A section header with an (i) at its end.</summary>
    public static HBoxContainer Header(string title, string? icon, string topic, Control? extra = null)
    {
        var header = Widgets.SectionHeader(title, icon, extra);
        header.AddChild(new InfoTip(topic));
        return header;
    }

    /// <summary>A label that keeps its row height when emptied, so the rows below never jump.</summary>
    public static Label Steady(string variation = "Small", Color? color = null)
    {
        var label = Widgets.Wrapped("", variation, color);
        label.CustomMinimumSize = new Vector2(40, 18);
        return label;
    }

    /// <summary>A state chip on a queue row ("Repeat", "Waiting for materials", "Not started").</summary>
    public static PanelContainer StateChip(string text, Color color, string tooltip = "")
    {
        var chip = Widgets.Chip(null, text, color, tooltip);
        chip.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        ChipText(chip).AddThemeFontSizeOverride("font_size", 13);
        return chip;
    }

    /// <summary>The text label inside a <see cref="Widgets.Chip"/>, to update it in place.</summary>
    public static Label ChipText(PanelContainer chip)
    {
        foreach (var child in chip.GetChild(0).GetChildren())
            if (child is Label label) return label;
        throw new InvalidOperationException("Not a chip");
    }

    /// <summary>
    /// Makes a card act like a button: a left click or Enter / Space picks it, it takes keyboard focus (Up / Down move to
    /// the card above / below in its list) and shows focus with a warm tint.
    /// </summary>
    public static T Clickable<T>(T card, Action onClick) where T : Control
    {
        card.FocusMode = Control.FocusModeEnum.All;
        card.MouseFilter = Control.MouseFilterEnum.Stop;
        card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        card.FocusEntered += () => card.SelfModulate = new Color(1.25f, 1.12f, 0.95f);
        card.FocusExited += () => card.SelfModulate = Colors.White;
        card.GuiInput += e =>
        {
            switch (e)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                    onClick();
                    break;
                case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter or Key.Space }:
                    card.AcceptEvent();
                    onClick();
                    break;
                case InputEventKey { Pressed: true, Keycode: Key.Up or Key.Down } key when card.GetParent() is { } list:
                    var step = key.Keycode == Key.Down ? 1 : -1;
                    for (var i = card.GetIndex() + step; i >= 0 && i < list.GetChildCount(); i += step)
                        if (list.GetChild(i) is Control { Visible: true, FocusMode: Control.FocusModeEnum.All } next)
                        {
                            card.AcceptEvent();
                            next.GrabFocus();
                            break;
                        }
                    break;
            }
        };
        return card;
    }
}
