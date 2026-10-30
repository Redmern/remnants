using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// What the acting unit can do besides moving, as <see cref="ActionButton"/>s with icons, AP-cost pips and hotkeys:
/// pick the attack for map clicks (1–3), use a consumable (4–6, then click a target), loot a cache in reach (F),
/// recruit a stranger (T), strip a wreck for parts (I), load loot into the party's vehicle (R) or take it back out at a
/// workbench, extract on an exit or beside the vehicle (X). The party's vehicle shows as a chip (HP, stash load). Every
/// button sends a Core command through the screen; Core decides whether it works.
/// </summary>
public partial class ActionPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Label _hint = Widgets.Wrapped("", "Small");
    private readonly VBoxContainer _content = Widgets.Column(4);
    private readonly List<ActionButton> _buttons = new();

    /// <summary>The attack map clicks on enemies will use.</summary>
    public string? ActionId { get; private set; }

    public event Action<string>? ActionChosen;
    public event Action<string>? UseItemChosen;
    public event Action<IScavengeCommand>? CommandRequested;

    public ActionPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(300, 0);
        var box = UiKit.Panel(this);
        box.AddChild(Widgets.SectionHeader("Actions", "ui_attack"));
        box.AddChild(_hint);
        box.AddChild(_content);
    }

    public void SetAction(string? actionId)
    {
        ActionId = actionId;
        ActionChosen?.Invoke(actionId ?? "");
    }

    /// <summary>Presses the button bound to the key in <paramref name="e"/>, if any is enabled.</summary>
    public bool PressHotkey(InputEvent e)
    {
        var button = _buttons.FirstOrDefault(b => b.Hotkey is { } k && !b.Disabled && Hotkeys.Pressed(e, k));
        if (button is null) return false;
        if (button.ToggleMode) button.ButtonPressed = !button.ButtonPressed;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    public void Refresh(bool busy, string? usingItem)
    {
        Widgets.Clear(_content);
        _buttons.Clear();
        var unit = _presenter.ActingUnit;
        if (unit is null || busy)
        {
            _hint.Text = _presenter.Done ? "The run is over." : "Waiting for the other units to act…";
            return;
        }

        var sim = _presenter.Sim;
        var data = sim.Data;
        var profile = sim.Profile(unit);
        if (ActionId is null || profile.Action(ActionId) is null) ActionId = profile.Actions.FirstOrDefault()?.Id;
        _hint.Text = usingItem is { } item
            ? data.Items[item].Use is { TargetsTile: true } onTile
                ? $"Click a tile within {onTile.Range} for the {ScavengeText.Item(data, item)}"
                  + (onTile.Emp is { } emp ? $": it bursts {emp.Radius} tiles around" : "") + ". Right-click or Esc cancels."
                : $"Click who gets the {ScavengeText.Item(data, item)} (yourself or someone next to you). Right-click or Esc cancels."
            : "Click a lit tile to move, a red target to attack with the chosen action.";

        _content.AddChild(Widgets.Label("ATTACK WITH", "Heading"));
        var n = 0;
        foreach (var a in profile.Actions)
        {
            var icon = a.Pattern == AttackPattern.Melee ? "skill_melee" : "skill_firearms";
            var hotkey = n < 3 ? $"scav_attack_{++n}" : null;
            var button = Add(new ActionButton(icon, a.Name, $"{a.Pattern} {a.Reach} · {a.MinDamage:0}–{a.MaxDamage:0} dmg · {Fmt.Percent(a.Accuracy)}",
                a.ApCost, hotkey, $"{a.Name}: choose it for clicks on targets. Hover a target for its hit chance."
                    + (a.CloseApPenalty > 0 ? $"\n+{a.CloseApPenalty} AP within {a.CloseRange} tiles (scope)." : "")
                    + (a.ArmorPierce > 0 ? $"\nPierces {a.ArmorPierce:0.#} armor." : "")), () => SetAction(a.Id));
            button.ToggleMode = true;
            button.ButtonPressed = a.Id == ActionId;
            button.SetBlocked(a.ApCost > unit.Ap, a.ApCost > unit.Ap);
        }

        var usable = unit.Pack.Distinct().Where(id => data.Items[id].Use is not null).ToList();
        if (usable.Count > 0)
        {
            _content.AddChild(Widgets.Label("USE", "Heading"));
            var i = 0;
            foreach (var id in usable)
            {
                var def = data.Items[id];
                var use = def.Use!;
                var count = unit.Pack.Count(p => p == id);
                var hotkey = i < 6 ? $"scav_item_{++i}" : null;
                var ap = FieldMedicine.ApCost(unit, use);
                var charges = ElectronicWarfare.ChargesLeft(data, unit, id);
                var title = charges is { } c ? $"{ScavengeText.Item(data, id)} · {c} charge{(c == 1 ? "" : "s")}" : $"{ScavengeText.Item(data, id)} ×{count}";
                var button = Add(new ActionButton(ScavengeText.UseIcon(def), title, ScavengeText.UseDetail(use),
                    ap, hotkey, ScavengeText.UseTip(data, def, charges)), () => UseItemChosen?.Invoke(id));
                button.ToggleMode = true;
                button.ButtonPressed = id == usingItem;
                button.SetBlocked(ap > unit.Ap || charges is 0, ap > unit.Ap);
            }
        }

        var caches = _presenter.CachesInReach(unit).ToList();
        if (caches.Count > 0)
        {
            _content.AddChild(Widgets.Label("LOOT", "Heading"));
            var first = true;
            var cost = data.Rules.Turn.PickUpApCost;
            foreach (var cache in caches)
                foreach (var id in cache.Items.Distinct())
                {
                    var weight = data.Items[id].Weight;
                    var cacheId = cache.Id;
                    var button = Add(new ActionButton("stat_loot", $"Take {ScavengeText.Item(data, id)}", $"{weight:0.#} kg", cost, first ? "scav_loot" : null),
                        () => CommandRequested?.Invoke(new PickUp(_presenter.PlayerId, unit.Id, cacheId, id)));
                    button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
                    first = false;
                }
        }

        foreach (var npc in _presenter.RecruitableNear(unit))
        {
            var npcId = npc.Id;
            var cost = data.Rules.Turn.RecruitApCost;
            var button = Add(new ActionButton("stat_recruit", $"Recruit {npc.Name}", "they follow you and join the colony", cost, "scav_recruit"),
                () => CommandRequested?.Invoke(new Recruit(_presenter.PlayerId, unit.Id, npcId)));
            button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
        }

        foreach (var wreck in Salvaging.Beside(_presenter.State, unit).Take(1))
        {
            var cost = data.Rules.Salvage.ApCost;
            var at = wreck.A;
            var button = Add(new ActionButton("ui_salvage", "Salvage the wreck", "strip it for vehicle parts · loud", cost, "scav_salvage",
                    "Strip the wrecked car for parts. Better wrecks in harder zones; engines only with your vehicle at the edge to haul them; a winch finds more. The noise carries."),
                () => CommandRequested?.Invoke(new Salvage(_presenter.PlayerId, unit.Id, at)));
            button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
        }

        VehicleActions(unit);

        var beside = ParkedVehicles.Beside(_presenter.State, unit);
        if (_presenter.State.Map.IsExit(unit.Pos) || beside is not null)
            Add(new ActionButton("ui_extract", beside is not null ? $"Extract by the {beside.Name}" : "Extract here",
                    "leave the zone with what you carry" + (beside is not null ? "; the vehicle takes its stash home" : ""), 0, "scav_extract"),
                () => CommandRequested?.Invoke(new Extract(_presenter.PlayerId, unit.Id))).ThemeTypeVariation = "Primary";
    }

    /// <summary>The party's vehicle: a status chip, and beside it Stash (heaviest loot first) and, with a workbench, Take.</summary>
    private void VehicleActions(Unit unit)
    {
        var state = _presenter.State;
        var data = _presenter.Sim.Data;
        if (ParkedVehicles.Of(state, _presenter.PlayerId) is not { } vehicle) return;
        _content.AddChild(Widgets.Label("VEHICLE", "Heading"));
        var load = ParkedVehicles.Load(data, vehicle);
        var status = vehicle.Wrecked ? $"{vehicle.Name}: wrecked, its cargo lost"
            : $"{vehicle.Name}  {vehicle.Hp:0}/{vehicle.MaxHp:0} HP · stash {load:0.#}/{vehicle.CargoKg:0} kg";
        _content.AddChild(Widgets.Chip(Icons.Get("ui_vehicle", 14), status, vehicle.Wrecked ? Palette.Danger : Palette.Gauge(vehicle.Hp / vehicle.MaxHp),
            "Parked at the zone's edge: extract beside it, load loot into it (it goes home with the vehicle). Enemies next to it damage it."));
        if (vehicle.Wrecked || vehicle.Pos.Chebyshev(unit.Pos) > 1) return;
        var cost = data.Rules.Turn.PickUpApCost;
        var haul = unit.Pack.Where(id => data.Items[id].Weight <= vehicle.CargoKg - load + 1e-9)
            .OrderByDescending(id => data.Items[id].Kind is ItemKind.Material or ItemKind.Schematic)
            .ThenByDescending(id => data.Items[id].Weight).ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (haul is not null)
        {
            var button = Add(new ActionButton("ui_stash", $"Load {ScavengeText.Item(data, haul)}", $"{data.Items[haul].Weight:0.#} kg into the {vehicle.Name}", cost, "scav_stash"),
                () => CommandRequested?.Invoke(new StashItem(_presenter.PlayerId, unit.Id, haul)));
            button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
        }
        if (vehicle.Workbench)
            foreach (var id in vehicle.Stash.Distinct().Take(3))
            {
                var item = id;
                var button = Add(new ActionButton("stat_loot", $"Take {ScavengeText.Item(data, id)}", "out of the stash (workbench)", cost, null),
                    () => CommandRequested?.Invoke(new TakeFromStash(_presenter.PlayerId, unit.Id, item)));
                button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
            }
    }

    private ActionButton Add(ActionButton button, Action onPressed)
    {
        button.Pressed += onPressed;
        _buttons.Add(button);
        _content.AddChild(button);
        return button;
    }
}
