using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// What the acting unit can do besides moving, as <see cref="ActionButton"/>s with icons, AP-cost pips and hotkeys.
/// Two panels in the right column: this one (Attack, Use: pick the attack for map clicks, use a consumable then click a
/// target, numbered 1–9 in that order so items start right after the attacks), and <see cref="Context"/> below the
/// stances for what is in reach here (salvage a node, I; recruit a stranger, T; the party's vehicle as a status chip with
/// Load, R, and Take at a workbench; extract on an exit or beside the vehicle, X). Piles are taken, and loot dropped or
/// stashed, from the <see cref="LootPanel"/> (F). Every button sends a Core command through the screen; Core decides
/// whether it works. The fixed order keeps the buttons above a newly reachable one where they were.
/// </summary>
public partial class ActionPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly List<ActionButton> _buttons = new();
    private readonly PanelRows _rows;
    private readonly PanelRows _context;

    /// <summary>The number keys, in order: attacks take the first, items the ones after.</summary>
    private static readonly string[] Slots =
        ["scav_attack_1", "scav_attack_2", "scav_attack_3", "scav_item_1", "scav_item_2", "scav_item_3", "scav_item_4", "scav_item_5", "scav_item_6"];

    /// <summary>What is in reach where the acting unit stands (salvage, recruit, vehicle, extract); placed by the HUD under the stances.</summary>
    public PanelContainer Context { get; } = new() { Name = "Context", Visible = false };

    /// <summary>The attack map clicks on enemies will use.</summary>
    public string? ActionId { get; private set; }

    public event Action<string>? ActionChosen;
    public event Action<string>? UseItemChosen;
    public event Action<IScavengeCommand>? CommandRequested;

    public ActionPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(ScavengeHud.ColumnWidth, 0);
        var box = UiKit.Panel(this, 4);
        var tip = new InfoTip("hit_chance");
        var header = Widgets.SectionHeader("Actions", "ui_attack", tip);
        header.MouseFilter = MouseFilterEnum.Pass;
        header.TooltipText = "Orders for the unit whose turn it is. Each pip (■) on a button is 1 AP.\nHover a red target on the map for the hit chance.";
        box.AddChild(header);
        var content = Widgets.Column(4);
        box.AddChild(content);
        _rows = new PanelRows(content);

        Context.CustomMinimumSize = new Vector2(ScavengeHud.ColumnWidth, 0);
        Context.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var contextBox = UiKit.Panel(Context, 4);
        contextBox.AddChild(Widgets.SectionHeader("Here", "stat_loot"));
        var contextRows = Widgets.Column(4);
        contextBox.AddChild(contextRows);
        _context = new PanelRows(contextRows);
    }

    public void SetAction(string? actionId)
    {
        ActionId = actionId;
        ActionChosen?.Invoke(actionId ?? "");
    }

    /// <summary>Presses the button bound to the key in <paramref name="e"/>, if any is enabled.</summary>
    public bool PressHotkey(InputEvent e)
    {
        var button = _buttons.FirstOrDefault(b => b.Hotkey is { } k && !b.Disabled && b.IsVisibleInTree() && Hotkeys.Pressed(e, k));
        if (button is null) return false;
        if (button.ToggleMode) button.ButtonPressed = !button.ButtonPressed;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    public void Refresh(bool busy, string? usingItem)
    {
        _rows.Begin();
        _context.Begin();
        _buttons.Clear();
        var shown = Build(busy, usingItem);
        _rows.End();
        _context.End();
        Presence.Set(this, shown);
        Presence.Set(Context, shown && _contextUsed);
    }

    /// <summary>What a click does while <paramref name="item"/> is being aimed: the header line of the right column.</summary>
    public string ItemHint(string item)
    {
        var data = _presenter.Sim.Data;
        return data.Items[item].Use is { TargetsTile: true } onTile
            ? $"Click a tile within {onTile.Range} for the {ScavengeText.Item(data, item)}"
              + (onTile.Emp is { } emp ? $": it bursts {emp.Radius} tiles around" : "") + ". Right-click or Esc cancels."
            : $"Click who gets the {ScavengeText.Item(data, item)} (yourself or someone next to you). Right-click or Esc cancels.";
    }

    private bool _contextUsed;

    private bool Build(bool busy, string? usingItem)
    {
        _contextUsed = false;
        var unit = _presenter.ActingUnit;
        if (unit is null || busy) return false;

        var sim = _presenter.Sim;
        var data = sim.Data;
        var profile = sim.Profile(unit);
        if (ActionId is null || profile.Action(ActionId) is null) ActionId = profile.Actions.FirstOrDefault()?.Id;

        var slot = 0;
        _rows.Heading("ATTACK WITH");
        foreach (var a in profile.Actions)
        {
            var icon = a.Pattern == AttackPattern.Melee ? "skill_melee" : "skill_firearms";
            var hotkey = slot < 3 ? Slots[slot++] : null;
            var button = Add(_rows, icon, a.Name, $"{a.Pattern} {a.Reach} · {a.MinDamage:0}–{a.MaxDamage:0} dmg · {Fmt.Percent(a.Accuracy)}",
                a.ApCost, hotkey, $"{a.Name}: choose it for clicks on targets. Hover a target for its hit chance."
                    + (a.CloseApPenalty > 0 ? $"\n+{a.CloseApPenalty} AP within {a.CloseRange} tiles (scope)." : "")
                    + (a.ArmorPierce > 0 ? $"\nPierces {a.ArmorPierce:0.#} armor." : ""), () => SetAction(a.Id));
            button.Toggle(a.Id == ActionId);
            button.SetBlocked(a.ApCost > unit.Ap, a.ApCost > unit.Ap);
        }

        var usable = unit.Pack.Distinct().Where(id => data.Items[id].Use is not null).ToList();
        if (usable.Count > 0)
        {
            _rows.Heading("USE");
            foreach (var id in usable)
            {
                var def = data.Items[id];
                var use = def.Use!;
                var count = unit.Pack.Count(p => p == id);
                var hotkey = slot < Slots.Length ? Slots[slot++] : null;
                var ap = FieldMedicine.ApCost(unit, use);
                var charges = ElectronicWarfare.ChargesLeft(data, unit, id);
                var title = charges is { } c ? $"{ScavengeText.Item(data, id)} · {c} charge{(c == 1 ? "" : "s")}" : $"{ScavengeText.Item(data, id)} ×{count}";
                var button = Add(_rows, ScavengeText.UseIcon(def), title, ScavengeText.UseDetail(use),
                    ap, hotkey, ScavengeText.UseTip(data, def, charges), () => UseItemChosen?.Invoke(id));
                button.Toggle(id == usingItem);
                button.SetBlocked(ap > unit.Ap || charges is 0, ap > unit.Ap);
            }
        }

        var nodes = _presenter.NodesInReach(unit).ToList();
        if (nodes.Count > 0)
        {
            _contextUsed = true;
            _context.Heading("SALVAGE", "salvage");
            var first = true;
            foreach (var node in nodes)
            {
                if (data.Salvage.Node(node.Kind) is not { } def) continue;
                var nodeId = node.Id;
                var actions = FieldSalvage.ActionsLeft(def, node, unit);
                var yield = FieldSalvage.Estimate(sim, node, unit).OrderByDescending(kv => kv.Value).FirstOrDefault();
                var detail = $"{(yield.Key is null ? "" : $"~{yield.Value:0.#} {ScavengeText.Item(data, yield.Key)} · ")}{actions} action{(actions == 1 ? "" : "s")} left · noise {def.Noise}";
                var button = Add(_context, "stat_loot", $"Salvage {def.Name}", detail, def.ApCost, first ? "scav_salvage" : null,
                    $"Salvage the {def.Name.ToLowerInvariant()}: a lot comes off every few actions into your pack (what does not fit stays on the ground). Loud: enemies within {def.Noise} tiles hear it.",
                    () => CommandRequested?.Invoke(new Salvage(_presenter.PlayerId, unit.Id, nodeId)));
                button.SetBlocked(def.ApCost > unit.Ap, def.ApCost > unit.Ap);
                first = false;
            }
        }

        foreach (var npc in _presenter.RecruitableNear(unit))
        {
            _contextUsed = true;
            var npcId = npc.Id;
            var cost = data.Rules.Turn.RecruitApCost;
            var button = Add(_context, "stat_recruit", $"Recruit {npc.Name}", "they follow you and join the colony", cost, "scav_recruit", "",
                () => CommandRequested?.Invoke(new Recruit(_presenter.PlayerId, unit.Id, npcId)));
            button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
        }

        VehicleActions(unit);

        var beside = ParkedVehicles.Beside(_presenter.State, unit);
        if (_presenter.State.Map.IsExit(unit.Pos) || beside is not null)
        {
            _contextUsed = true;
            _context.Heading("EXTRACT", "extraction");
            Add(_context, "ui_extract", beside is not null ? $"Extract by the {beside.Name}" : "Extract here", "leave with what you carry", 0, "scav_extract",
                    "Leave the zone with what you carry" + (beside is not null ? "; the vehicle takes its stash home." : ". Anyone left behind does not come home."),
                () => CommandRequested?.Invoke(new Extract(_presenter.PlayerId, unit.Id))).ThemeTypeVariation = "Primary";
        }
        return true;
    }

    /// <summary>The party's vehicle: a status chip, and beside it Load (heaviest loot first) and, with a workbench, Take.</summary>
    private void VehicleActions(Unit unit)
    {
        var state = _presenter.State;
        var data = _presenter.Sim.Data;
        if (ParkedVehicles.Of(state, _presenter.PlayerId) is not { } vehicle) return;
        _contextUsed = true;
        _context.Heading("VEHICLE", "vehicles");
        var load = ParkedVehicles.Load(data, vehicle);
        var status = vehicle.Wrecked ? $"{vehicle.Name}: wrecked, its cargo lost"
            : $"{vehicle.Name}  {vehicle.Hp:0}/{vehicle.MaxHp:0} HP · stash {load:0.#}/{vehicle.CargoKg:0} kg";
        _context.Chip("ui_vehicle", status, vehicle.Wrecked ? Palette.Danger : Palette.Gauge(vehicle.Hp / vehicle.MaxHp),
            "Parked at the zone's edge: extract beside it, load loot into it (it goes home with the vehicle). Enemies next to it damage it.");
        if (vehicle.Wrecked || vehicle.Pos.Chebyshev(unit.Pos) > 1) return;
        var cost = data.Rules.Turn.PickUpApCost;
        var haul = unit.Pack.Where(id => data.Items[id].Weight <= vehicle.CargoKg - load + 1e-9)
            .OrderByDescending(id => data.Items[id].Kind is ItemKind.Material or ItemKind.Schematic)
            .ThenByDescending(id => data.Items[id].Weight).ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (haul is not null)
        {
            var button = Add(_context, "ui_stash", $"Load {ScavengeText.Item(data, haul)}", $"{data.Items[haul].Weight:0.#} kg into the {vehicle.Name}", cost, "scav_stash", "",
                () => CommandRequested?.Invoke(new StashItem(_presenter.PlayerId, unit.Id, haul)));
            button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
        }
        if (vehicle.Workbench)
            foreach (var id in vehicle.Stash.Distinct().Take(3))
            {
                var item = id;
                var button = Add(_context, "stat_loot", $"Take {ScavengeText.Item(data, id)}", "out of the stash (workbench)", cost, null, "",
                    () => CommandRequested?.Invoke(new TakeFromStash(_presenter.PlayerId, unit.Id, item)));
                button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
            }
    }

    private ActionButton Add(PanelRows rows, string icon, string text, string detail, int apCost, string? hotkey, string tooltip, Action onPressed)
    {
        var button = rows.Button(icon, text, detail, apCost, hotkey, tooltip, onPressed);
        _buttons.Add(button);
        return button;
    }
}
