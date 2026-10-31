using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The pickup panel, up while the acting unit stands at a pile, on an exit with something in the pack, or loaded enough
/// to be slowed: its carry weight against the encumbrance steps (a warning before it bites), each pile in reach as item
/// chips to take one by one (greyed when too heavy) and Take all (F: what fits, lightest first, one pick-up's AP), and
/// the pack with Drop and, on an exit, Stash (it waits there for the next run) while slowed or on an exit. Every button
/// sends a Core command.
/// </summary>
public partial class LootPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly VBoxContainer _content = Widgets.Column(5);
    private readonly List<ActionButton> _buttons = new();

    public event Action<IScavengeCommand>? CommandRequested;

    public LootPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(380, 0);
        var box = UiKit.Panel(this);
        box.AddChild(Widgets.SectionHeader("Loot", "stat_loot"));
        box.AddChild(_content);
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
        var unit = _presenter.ActingUnit;
        // Rebuild only when what it shows changed: the acting unit, its AP, pack and load, the piles in reach.
        var signature = unit is null || busy || _presenter.Done ? ""
            : $"{unit.Id}|{unit.Ap}|{unit.Pos}|{string.Join(",", unit.Pack)}|"
              + string.Join(";", _presenter.CachesInReach(unit).Select(c => $"{c.Id}:{_presenter.IsExplored(c.Pos)}:{string.Join(",", c.Items)}"));
        if (signature == _signature) return;
        _signature = signature;
        Widgets.Clear(_content);
        _buttons.Clear();
        if (unit is null || busy || _presenter.Done)
        {
            Visible = false;
            return;
        }
        var sim = _presenter.Sim;
        var data = sim.Data;
        var profile = sim.Profile(unit);
        var piles = _presenter.CachesInReach(unit).Where(c => _presenter.IsExplored(c.Pos)).ToList();
        var onExit = _presenter.State.Map.IsExit(unit.Pos);
        var (color, note) = LootText.Carry(data, profile.Load, profile.CarryCapacity);
        var slowed = color != Palette.Rust;
        Visible = piles.Count > 0 || (onExit && unit.Pack.Count > 0) || slowed;
        if (!Visible) return;

        var load = new StatBar("stat_load");
        load.Set(profile.CarryCapacity > 0 ? profile.Load / profile.CarryCapacity : 0, $"{profile.Load:0.#}/{profile.CarryCapacity:0} kg", color);
        load.TooltipText = "Load / carry capacity. Past the encumbrance steps a unit moves fewer tiles a turn.";
        _content.AddChild(load);
        if (note.Length > 0) _content.AddChild(Widgets.Label(note, "Small", color == Palette.Rust ? Palette.TextMuted : color));

        var cost = data.Rules.Turn.PickUpApCost;
        var room = profile.CarryCapacity - profile.Load;
        var first = true;
        foreach (var pile in piles)
        {
            _content.AddChild(Widgets.Label(LootText.PileName(_presenter, pile).ToUpperInvariant(), "Heading"));
            var chips = new HFlowContainer();
            chips.AddThemeConstantOverride("h_separation", 4);
            chips.AddThemeConstantOverride("v_separation", 4);
            foreach (var group in pile.Items.GroupBy(id => id))
            {
                var id = group.Key;
                var weight = data.Items.TryGetValue(id, out var def) ? def.Weight : 0;
                var fits = weight <= room + 1e-9;
                var pileId = pile.Id;
                var chip = new Button
                {
                    Text = $"{ScavengeText.Item(data, id)}{(group.Count() > 1 ? $" ×{group.Count()}" : "")} · {weight:0.#} kg",
                    Icon = LootText.Icon(data, id), ThemeTypeVariation = "Chip", FocusMode = FocusModeEnum.None,
                    Disabled = !fits || cost > unit.Ap, MouseDefaultCursorShape = CursorShape.PointingHand,
                    TooltipText = fits ? $"Take one ({cost} AP)." : $"Too heavy: {weight:0.#} kg, room for {Math.Max(0, room):0.#} kg.",
                };
                if (!fits) chip.AddThemeColorOverride("font_disabled_color", Palette.Danger);
                chip.Pressed += () => CommandRequested?.Invoke(new PickUp(_presenter.PlayerId, unit.Id, pileId, id));
                chips.AddChild(chip);
            }
            _content.AddChild(chips);
            var fitting = TakeAllFits(data, pile.Items, room);
            var all = Add(new ActionButton("stat_loot", "Take all",
                fitting.Count == pile.Items.Count ? $"{fitting.Count} items · {fitting.Sum(i => data.Items[i].Weight):0.#} kg"
                    : $"{fitting.Count} of {pile.Items.Count} fit · the rest is too heavy", cost, first ? "scav_loot" : null,
                "Take everything that fits from this pile, lightest first, for one pick-up."),
                () => CommandRequested?.Invoke(new TakeAll(_presenter.PlayerId, unit.Id, pile.Id)));
            all.SetBlocked(fitting.Count == 0 || cost > unit.Ap, cost > unit.Ap);
            first = false;
        }

        // The pack, to lighten it or stash things: only when that matters (slowed, or on an exit).
        if (unit.Pack.Count == 0 || !(slowed || onExit)) return;
        _content.AddChild(Widgets.Label("YOUR PACK", "Heading"));
        var stashCost = data.Salvage.StashApCost;
        foreach (var group in unit.Pack.GroupBy(id => id).OrderByDescending(g => data.Items[g.Key].Weight * g.Count()))
        {
            var id = group.Key;
            var row = Widgets.Row(4);
            var chip = Widgets.Chip(LootText.Icon(data, id), $"{ScavengeText.Item(data, id)}{(group.Count() > 1 ? $" ×{group.Count()}" : "")} · {data.Items[id].Weight * group.Count():0.#} kg");
            chip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(chip);
            row.AddChild(Widgets.TextButton("Drop", () => CommandRequested?.Invoke(new DropItem(_presenter.PlayerId, unit.Id, id)),
                "Drop one here; anyone can pick it up."));
            if (onExit)
            {
                var stash = Widgets.TextButton("Stash", () => CommandRequested?.Invoke(new StashItem(_presenter.PlayerId, unit.Id, id)),
                    $"Leave one at this exit ({stashCost} AP): it is still here on your next run to this zone.");
                stash.Disabled = stashCost > unit.Ap;
                row.AddChild(stash);
            }
            _content.AddChild(row);
        }
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

    private ActionButton Add(ActionButton button, Action onPressed)
    {
        button.Pressed += onPressed;
        _buttons.Add(button);
        _content.AddChild(button);
        return button;
    }
}
