using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The pickup panel, docked under the unit card while the acting unit stands at a pile, on an exit with something in
/// the pack, or loaded enough to be slowed. Titled by what it shows ("Pile", "Your pack"). One load bar against the
/// encumbrance steps (a warning before it bites); each pile in reach as item chips to take one by one (greyed when too
/// heavy) and Take all (F: what fits, lightest first, one pick-up's AP; amber with what it costs in moves when it would
/// slow you); and the pack with Drop 1 and, on an exit, Stash (it waits there for the next run). Rows are kept and
/// updated in place. Every button sends a Core command.
/// </summary>
public partial class LootPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly List<ActionButton> _buttons = new();
    private readonly PanelRows _rows;
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly StatBar _load = new("stat_load");
    private readonly Label _note = Widgets.Wrapped("", "Small");
    private readonly Unchanged _set = new();

    public event Action<IScavengeCommand>? CommandRequested;

    public LootPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(320, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var box = UiKit.Panel(this, 5);
        var header = Widgets.Row(6);
        header.AddChild(Icons.Rect(Icons.Get("stat_loot", 14), 14, Palette.Rust));
        header.AddChild(_title);
        header.AddChild(new HSeparator { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter });
        header.AddChild(new InfoTip("loot"));
        box.AddChild(header);
        _load.TooltipText = "Load / carry capacity. Past the encumbrance steps a unit moves fewer tiles a turn and is louder.";
        box.AddChild(_load);
        box.AddChild(_note);
        var content = Widgets.Column(5);
        box.AddChild(content);
        _rows = new PanelRows(content);
        Visible = false;
    }

    public bool PressHotkey(InputEvent e)
    {
        if (!Visible) return false;
        var button = _buttons.FirstOrDefault(b => b.Hotkey is { } k && !b.Disabled && Hotkeys.Pressed(e, k));
        if (button is null) return false;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private string? _signature;

    public void Refresh(bool busy)
    {
        // While a turn replays the panel keeps what it shows; the next refresh catches up.
        if (busy) return;
        var unit = _presenter.ActingUnit;
        // Update only when what it shows changed: the acting unit, its AP, pack and load, the piles in reach.
        var signature = unit is null || _presenter.Done ? ""
            : $"{unit.Id}|{unit.Ap}|{unit.Pos}|{string.Join(",", unit.Pack)}|"
              + string.Join(";", _presenter.CachesInReach(unit).Select(c => $"{c.Id}:{_presenter.IsExplored(c.Pos)}:{string.Join(",", c.Items)}"));
        if (signature == _signature) return;
        _signature = signature;
        _rows.Begin();
        _buttons.Clear();
        Presence.Set(this, unit is not null && !_presenter.Done && Build(unit));
        _rows.End();
    }

    private bool Build(Unit unit)
    {
        var sim = _presenter.Sim;
        var data = sim.Data;
        var profile = sim.Profile(unit);
        var piles = _presenter.CachesInReach(unit).Where(c => _presenter.IsExplored(c.Pos)).ToList();
        var onExit = _presenter.State.Map.IsExit(unit.Pos);
        var (color, note) = LootText.Carry(data, profile.Load, profile.CarryCapacity);
        var slowed = color != Palette.Rust;
        var packShown = unit.Pack.Count > 0 && (slowed || onExit);
        if (piles.Count == 0 && !packShown) return false;

        _set.Text(_title, piles.Count > 0 ? (piles.Count == 1 ? "PILE" : $"{piles.Count} PILES") : "YOUR PACK");
        _load.Set(profile.CarryCapacity > 0 ? profile.Load / profile.CarryCapacity : 0, $"{profile.Load:0.#}/{profile.CarryCapacity:0} kg", color);
        _set.Text(_note, note);
        _set.Visible(_note, note.Length > 0);
        _set.FontColor(_note, color == Palette.Rust ? Palette.TextMuted : color);

        var cost = data.Rules.Turn.PickUpApCost;
        var room = profile.CarryCapacity - profile.Load;
        var first = true;
        foreach (var pile in piles)
        {
            _rows.Heading(LootText.PileName(_presenter, pile).ToUpperInvariant());
            var chips = _rows.Row(() =>
            {
                var flow = new HFlowContainer();
                flow.AddThemeConstantOverride("h_separation", 4);
                flow.AddThemeConstantOverride("v_separation", 4);
                return flow;
            });
            var n = 0;
            foreach (var group in pile.Items.GroupBy(id => id))
            {
                var id = group.Key;
                var weight = data.Items.TryGetValue(id, out var def) ? def.Weight : 0;
                var fits = weight <= room + 1e-9;
                var pileId = pile.Id;
                var chip = ChipAt(chips, n++);
                chip.Text = $"{ScavengeText.Item(data, id)}{(group.Count() > 1 ? $" ×{group.Count()}" : "")} · {weight:0.#} kg";
                chip.Icon = LootText.Icon(data, id);
                chip.Disabled = !fits || cost > unit.Ap;
                chip.TooltipText = fits ? $"Take one ({cost} AP)." : $"Too heavy: {weight:0.#} kg, room for {Math.Max(0, room):0.#} kg.";
                if (fits) chip.RemoveThemeColorOverride("font_disabled_color");
                else chip.AddThemeColorOverride("font_disabled_color", Palette.Danger);
                _chipActions[chip] = () => CommandRequested?.Invoke(new PickUp(_presenter.PlayerId, unit.Id, pileId, id));
            }
            for (var i = n; i < chips.GetChildCount(); i++) chips.GetChild<Control>(i).Visible = false;

            var fitting = TakeAllFits(data, pile.Items, room);
            var kg = fitting.Sum(i => data.Items[i].Weight);
            // What taking it all does to the unit's move range, before it is too late.
            var (after, slowNote) = LootText.Carry(data, profile.Load + kg, profile.CarryCapacity);
            var slows = after != color && after != Palette.Rust;
            var all = _rows.Button("stat_loot", slows ? "Take all (slows you)" : "Take all",
                fitting.Count == pile.Items.Count ? $"{fitting.Count} items · {kg:0.#} kg" : $"{fitting.Count} of {pile.Items.Count} fit · the rest is too heavy",
                cost, first ? "scav_loot" : null,
                "Take everything that fits from this pile, lightest first, for one pick-up." + (slows ? $"\nAfter: {slowNote}" : ""),
                () => CommandRequested?.Invoke(new TakeAll(_presenter.PlayerId, unit.Id, pile.Id)));
            all.SetBlocked(fitting.Count == 0 || cost > unit.Ap, cost > unit.Ap);
            all.SetWarning(slows);
            _buttons.Add(all);
            first = false;
        }

        // The pack, to lighten it or stash things: only when that matters (slowed, or on an exit).
        if (!packShown) return true;
        if (piles.Count > 0) _rows.Heading("YOUR PACK"); // otherwise the panel's title says it
        var stashCost = data.Salvage.StashApCost;
        foreach (var group in unit.Pack.GroupBy(id => id).OrderByDescending(g => data.Items[g.Key].Weight * g.Count()))
        {
            var id = group.Key;
            var row = _rows.Row(() => new PackRow());
            row.Set($"{ScavengeText.Item(data, id)}{(group.Count() > 1 ? $" ×{group.Count()}" : "")} · {data.Items[id].Weight * group.Count():0.#} kg",
                LootText.Icon(data, id), group.Count(), onExit, stashCost, unit.Ap,
                () => CommandRequested?.Invoke(new DropItem(_presenter.PlayerId, unit.Id, id)),
                () => CommandRequested?.Invoke(new StashItem(_presenter.PlayerId, unit.Id, id)));
        }
        return true;
    }

    private readonly Dictionary<Button, Action> _chipActions = new();

    /// <summary>The chip button at <paramref name="index"/> in a pile's flow, made the first time it is needed.</summary>
    private Button ChipAt(HFlowContainer flow, int index)
    {
        if (index < flow.GetChildCount())
        {
            var kept = flow.GetChild<Button>(index);
            kept.Visible = true;
            return kept;
        }
        var chip = new Button
        {
            ThemeTypeVariation = "Chip", FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        chip.Pressed += () =>
        {
            if (_chipActions.TryGetValue(chip, out var take)) take();
        };
        Motion.PressFeedback(chip);
        flow.AddChild(chip);
        return chip;
    }

    /// <summary>What Take all would carry off: lightest first while it fits (as Core does).</summary>
    private static List<string> TakeAllFits(ScavengeData data, IEnumerable<string> items, double room)
    {
        var taken = new List<string>();
        foreach (var id in items.OrderBy(id => data.Items[id].Weight).ThenBy(id => id, StringComparer.Ordinal))
        {
            if (data.Items[id].Weight > room + 1e-9) continue;
            room -= data.Items[id].Weight;
            taken.Add(id);
        }
        return taken;
    }
}

/// <summary>One kind of item in the pack: a chip with its weight, Drop 1, and Stash on an exit. Kept and updated in place.</summary>
public partial class PackRow : HBoxContainer
{
    private readonly PanelContainer _chip;
    private readonly Label _text;
    private readonly TextureRect _icon;
    private readonly Button _drop;
    private readonly Button _stash;
    private Action? _onDrop;
    private Action? _onStash;

    public PackRow()
    {
        AddThemeConstantOverride("separation", 4);
        _chip = Widgets.Chip(Icons.Get("item_generic", 14), "");
        _chip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var inner = _chip.GetChild(0);
        _icon = inner.GetChild<TextureRect>(0);
        _text = inner.GetChild<Label>(1);
        _text.ClipText = true;
        _text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(_chip);
        _drop = Widgets.TextButton("Drop 1", () => _onDrop?.Invoke(), "Drop one here; anyone can pick it up.");
        AddChild(_drop);
        _stash = Widgets.TextButton("Stash", () => _onStash?.Invoke());
        AddChild(_stash);
    }

    public void Set(string text, Texture2D icon, int count, bool onExit, int stashCost, int ap, Action drop, Action stash)
    {
        _text.Text = text;
        _icon.Texture = icon;
        _onDrop = drop;
        _onStash = stash;
        _drop.Text = count > 1 ? "Drop 1" : "Drop";
        _stash.Visible = onExit;
        _stash.Disabled = stashCost > ap;
        _stash.TooltipText = $"Leave one at this exit ({stashCost} AP): it is still here on your next run to this zone.";
    }
}
