using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>The spacing scale (UX audit 21.1): 4 inside chips and rows, 8 between controls, 12 between sections, 16 for padding.</summary>
public static class Space
{
    public const int Xs = 4, Sm = 8, Md = 12, Lg = 16, Xl = 24;
}

/// <summary>Fit-to-content panels re-measure when the room their parent gives them changes (a window resize).</summary>
public static class Fit
{
    /// <summary>Calls <paramref name="fit"/> (deferred) whenever <paramref name="self"/>'s parent control resizes.</summary>
    public static void OnParentResized(Control self, Action fit)
    {
        Control? parent = null;
        void Changed() => Callable.From(fit).CallDeferred();
        self.TreeEntered += () =>
        {
            parent = self.GetParent() as Control;
            if (parent is not null) parent.Resized += Changed;
        };
        self.TreeExiting += () =>
        {
            if (parent is not null) parent.Resized -= Changed;
            parent = null;
        };
    }
}

/// <summary>Panel widths (UX audit 21.3): roster, survivor and building column.</summary>
public static class PanelWidth
{
    public const float Side = 360, Narrow = 300, Wide = 760, Modal = 520;
}

/// <summary>
/// Cost chips that update in place: one chip per resource (icon and amount, the name too when <c>named</c>), kept
/// across refreshes by resource id, so a stock tick changes a colour or a number instead of rebuilding the row. A cost
/// the stock cannot cover turns red and says "short N" beside it (colour is never the only signal); the tooltip names
/// the resource and what the colony has.
/// </summary>
public partial class CostRow : HFlowContainer
{
    private readonly bool _named;
    private readonly Dictionary<string, (PanelContainer Chip, Label Amount, Label Short, TextureRect Icon)> _chips = new();
    private readonly Label _free = Widgets.Label("free", "Muted");
    private readonly Unchanged _set = new();
    private string _ids = "";

    public CostRow() : this(false) { }

    public CostRow(bool named)
    {
        _named = named;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("h_separation", Space.Xs);
        AddThemeConstantOverride("v_separation", Space.Xs);
        AddChild(_free);
    }

    /// <summary>True when every cost is covered (the last <see cref="Set"/>).</summary>
    public bool Affordable { get; private set; } = true;

    /// <summary>Ignore the mouse on every chip (a row inside a button, whose tooltip says the costs).</summary>
    public bool PassMouse { get; set; }

    public void Set(IEnumerable<(string Id, string Name, double Need, double Have)> costs)
    {
        var list = costs.ToList();
        var ids = string.Join(",", list.Select(c => c.Id));
        if (ids != _ids)
        {
            // Which resources the cost names changed (another level, another building): re-make the chips once.
            _ids = ids;
            foreach (var (chip, _, _, _) in _chips.Values) chip.QueueFree();
            _chips.Clear();
            foreach (var c in list) _chips[c.Id] = Make(c.Id, c.Name);
        }
        _set.Visible(_free, list.Count == 0);
        Affordable = true;
        foreach (var (id, name, need, have) in list)
        {
            var (chip, amount, shortLabel, icon) = _chips[id];
            var missing = need - have;
            var isShort = missing > 1e-9;
            if (isShort) Affordable = false;
            _set.Text(amount, _named ? $"{Fmt.Amount(need)} {name}" : Fmt.Amount(need));
            _set.FontColor(amount, isShort ? Palette.Danger : Palette.Text);
            _set.Modulate(icon, isShort ? Palette.Danger : Palette.TextMuted);
            _set.Visible(shortLabel, isShort);
            if (isShort) _set.Text(shortLabel, $"short {Fmt.Amount(Math.Ceiling(missing))}");
            _set.Tooltip(chip, isShort ? $"{name}: need {Fmt.Amount(need)}, have {Fmt.Amount(have)}" : $"{name}: {Fmt.Amount(need)} (have {Fmt.Amount(have)})");
        }
    }

    private (PanelContainer, Label, Label, TextureRect) Make(string id, string name)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Chip", MouseFilter = PassMouse ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", Space.Xs);
        chip.AddChild(row);
        var icon = Icons.Rect(Icons.Resource(id, 16), 16, Palette.TextMuted);
        icon.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(icon);
        var amount = Widgets.Label("", "Number");
        amount.AddThemeFontSizeOverride("font_size", 15);
        amount.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(amount);
        var shortLabel = Widgets.Label("", "Small", Palette.Danger);
        shortLabel.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(shortLabel);
        AddChild(chip);
        return (chip, amount, shortLabel, icon);
    }
}

/// <summary>
/// One row of tab buttons (the HUD's one tab style): each tab a toggle in a group that acts on press. Tabs can hide
/// (a building without a queue has no Queue tab); hiding the selected one falls back to the first visible tab.
/// </summary>
public partial class TabStrip : HBoxContainer
{
    private readonly List<Button> _tabs = new();
    private readonly ButtonGroup _group = new();

    public int Selected { get; private set; }

    /// <summary>The selected tab changed (by click, keyboard or <see cref="Select"/>).</summary>
    public event Action<int>? Changed;

    public TabStrip()
    {
        AddThemeConstantOverride("separation", 2);
    }

    public int Add(string title, string? tooltip = null)
    {
        var index = _tabs.Count;
        var button = Motion.OnPress(new Button
        {
            Text = title,
            ThemeTypeVariation = "Tab",
            ToggleMode = true,
            ButtonGroup = _group,
            ButtonPressed = index == 0,
            FocusMode = FocusModeEnum.All,
            TooltipText = tooltip ?? "",
            CustomMinimumSize = new Vector2(0, 28),
            MouseDefaultCursorShape = CursorShape.PointingHand,
            Name = $"Tab{index}",
        });
        button.Pressed += () => Select(index);
        _tabs.Add(button);
        AddChild(button);
        return index;
    }

    public Button Tab(int index) => _tabs[index];

    public int Count => _tabs.Count;

    public void Select(int index)
    {
        if (index < 0 || index >= _tabs.Count || !_tabs[index].Visible) return;
        // Without a signal the group does not release the others: press exactly this one.
        for (var i = 0; i < _tabs.Count; i++) _tabs[i].SetPressedNoSignal(i == index);
        if (index == Selected) return;
        Selected = index;
        Changed?.Invoke(index);
    }

    /// <summary>Shows or hides a tab; the selection moves to the first visible tab when its tab hides.</summary>
    public void SetShown(int index, bool shown)
    {
        if (_tabs[index].Visible == shown) return;
        _tabs[index].Visible = shown;
        if (!shown && index == Selected && _tabs.FindIndex(t => t.Visible) is var first and >= 0) Select(first);
    }

    public void SetTitle(int index, string title)
    {
        if (_tabs[index].Text != title) _tabs[index].Text = title;
    }

    /// <summary>The next / previous visible tab, wrapping (keyboard).</summary>
    public void Step(int by)
    {
        for (var i = 1; i <= _tabs.Count; i++)
        {
            var at = ((Selected + by * i) % _tabs.Count + _tabs.Count) % _tabs.Count;
            if (!_tabs[at].Visible) continue;
            Select(at);
            return;
        }
    }
}

/// <summary>
/// A section that folds: a header row (chevron, caps title, an optional summary on the right, an optional (i)) and a
/// body revealed with <see cref="Motion"/>. Summary first, details on demand.
/// </summary>
public partial class Expander : VBoxContainer
{
    private readonly Button _header;
    private readonly Label _summary = Widgets.Label("", "Small");

    public Control Body { get; }

    public bool Open => Body.Visible;

    /// <summary>Opened (true) or folded (false) by the player or <see cref="SetOpen"/>.</summary>
    public event Action<bool>? Toggled;

    public Expander(string title, Control body, bool open = false, Control? extra = null)
    {
        Body = body;
        AddThemeConstantOverride("separation", Space.Xs);
        var row = Widgets.Row(Space.Xs);
        _header = new Button
        {
            Text = title.ToUpperInvariant(),
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            ThemeTypeVariation = "Flat",
            FocusMode = FocusModeEnum.All,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(0, 26),
        };
        _header.AddThemeFontOverride("font", Fonts.Display);
        _header.AddThemeFontSizeOverride("font_size", 13);
        _header.Pressed += Toggle;
        row.AddChild(_header);
        _summary.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(_summary);
        if (extra is not null) row.AddChild(extra);
        AddChild(row);
        body.Visible = open;
        AddChild(body);
        Restyle();
    }

    public void Toggle() => SetOpen(!Body.Visible);

    public void SetOpen(bool open)
    {
        if (open == Body.Visible) return;
        if (open) Motion.Reveal(Body);
        else Body.Visible = false; // closing at once: a fading body would hold its space while the rows below wait
        Restyle();
        Toggled?.Invoke(open);
    }

    /// <summary>The line on the right of the header ("2 hurt", "All healthy").</summary>
    public void SetSummary(string text, Color? color = null)
    {
        if (_summary.Text != text) _summary.Text = text;
        _summary.AddThemeColorOverride("font_color", color ?? Palette.TextMuted);
    }

    private void Restyle()
    {
        _header.Icon = Icons.Get(Body.Visible ? "ui_chevron_down" : "ui_chevron_right", 14);
        _header.TooltipText = Body.Visible ? "Fold" : "Show the details";
    }
}

/// <summary>
/// The hotkey letter in a button's corner (UX audit 4.1: icon-only buttons say their key without a hover). Drawn in
/// code; ignores the mouse. <see cref="Attach"/> adds it to a button.
/// </summary>
public partial class KeyBadge : Control
{
    private readonly string _key;

    public KeyBadge() : this("") { }

    public KeyBadge(string key)
    {
        _key = key;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Adds the badge for <paramref name="key"/> (a hotkey name; blank or long names add nothing).</summary>
    public static void Attach(Button button, string? key)
    {
        if (key is not { Length: > 0 and <= 3 }) return;
        button.AddChild(new KeyBadge(key));
    }

    public override void _Draw()
    {
        var font = Fonts.Numbers;
        const int size = 10;
        var text = font.GetStringSize(_key, HorizontalAlignment.Left, -1, size);
        var box = new Rect2(Size.X - text.X - 5, Size.Y - size - 3, text.X + 4, size + 2);
        DrawRect(box, Palette.Void with { A = 0.85f });
        DrawRect(box, Palette.EdgeLight, false, 1);
        DrawString(font, new Vector2(box.Position.X + 2, box.End.Y - 2.5f), _key, HorizontalAlignment.Left, -1, size, Palette.TextMuted);
    }
}
