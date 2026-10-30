using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>What a tech button asks the screen to do with the next map click.</summary>
public enum TechAim { None, Deploy, Fly }

/// <summary>
/// Hacking and the scout drone for the acting unit, as <see cref="ActionButton"/>s with AP pips and hotkeys: Hack (H) each
/// device in the tool's reach with its chance and progress (or Override a Linked), Deploy (K, then click a tile next to
/// the unit), Fly (J, then click a lit tile), Recall (K), Mark (M) a hostile the drone sees, and drop a payload (P).
/// Hidden when the unit has neither a hacking tool nor a drone. Core decides whether anything works.
/// </summary>
public partial class TechPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly VBoxContainer _content = Widgets.Column(4);
    private readonly List<ActionButton> _buttons = new();

    public event Action<IScavengeCommand>? CommandRequested;
    public event Action<TechAim>? AimChosen;

    public TechPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(300, 0);
        var box = UiKit.Panel(this);
        box.AddChild(Widgets.SectionHeader("Tech", "skill_hacking"));
        box.AddChild(_content);
    }

    public bool PressHotkey(InputEvent e)
    {
        var button = _buttons.FirstOrDefault(b => b.Hotkey is { } k && !b.Disabled && Hotkeys.Pressed(e, k));
        if (button is null) return false;
        if (button.ToggleMode) button.ButtonPressed = !button.ButtonPressed;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    public void Refresh(bool busy, TechAim aim)
    {
        Widgets.Clear(_content);
        _buttons.Clear();
        var unit = _presenter.ActingUnit;
        var sim = _presenter.Sim;
        var data = sim.Data;
        var hasKit = unit is not null && Hacking.Tool(data, unit) is not null;
        var drone = unit is null ? null : Drones.DroneOf(_presenter.State, unit);
        var carried = unit is null ? null : Drones.CarriedDrone(data, unit);
        Visible = unit is not null && !busy && (hasKit || drone is not null || carried is not null);
        if (!Visible) return;
        var pid = _presenter.PlayerId;

        if (hasKit)
        {
            _content.AddChild(Widgets.Label("HACK", "Heading"));
            var cost = Hacking.ApCost(data, unit!);
            var options = _presenter.HackOptions(unit!);
            if (options.Count == 0) _content.AddChild(Widgets.Wrapped($"Nothing in reach of the {Hacking.Tool(data, unit!)!.Name}.", "Small"));
            var first = true;
            foreach (var o in options)
            {
                var progress = o.Needed > 1 ? $" · {o.Done}/{o.Needed} done" : "";
                var what = o.Device is { } d ? ScavengeText.HackEffect(d.Kind) : "it fights for you for a few rounds";
                var locked = o.Device is { } dev && Hacking.LockedOut(_presenter.State, dev, _presenter.Side);
                var button = Add(new ActionButton("skill_hacking", $"Hack {o.Name}", locked ? "locked out after a failed hack" : $"{o.Chance * 100:0}% on the last step{progress}",
                        cost, first ? "scav_hack" : null,
                        $"Hack the {o.Name.ToLowerInvariant()}: {what}. Each try puts work in; the last rolls {o.Chance * 100:0}%. A failure sets off an alarm, shocks you and locks you out for a while."),
                    () => CommandRequested?.Invoke(new Hack(pid, unit!.Id, o.Device?.Id, o.Target?.Id)));
                button.SetBlocked(locked || cost > unit!.Ap, cost > unit!.Ap);
                first = false;
            }
        }

        if (drone is null && carried is null) return;
        var rules = data.Rules.Drone;
        _content.AddChild(Widgets.Label("SCOUT DRONE", "Heading"));
        if (drone is null)
        {
            var battery = Drones.PackedBattery(data, unit!, carried!);
            var max = Drones.MaxBattery(data, unit!, carried!);
            var deploy = Add(new ActionButton("skill_dronepiloting", "Deploy drone", $"battery {battery}/{max} rounds · then click a tile next to you",
                rules.DeployApCost, "scav_drone", "Launch the scout drone: it flies over anything, sees far, and shares its sight with your team."),
                () => AimChosen?.Invoke(aim == TechAim.Deploy ? TechAim.None : TechAim.Deploy));
            deploy.ToggleMode = true;
            deploy.ButtonPressed = aim == TechAim.Deploy;
            deploy.SetBlocked(battery < rules.MinBattery || rules.DeployApCost > unit!.Ap, rules.DeployApCost > unit!.Ap);
            return;
        }

        var range = Drones.ControlRange(data, unit!, drone.DroneItemId!);
        var fly = Add(new ActionButton("ui_move", "Fly drone", $"{Drones.FlyRange(sim, drone)} tiles · range {range} · {drone.Battery} rounds left",
            rules.FlyApCost, "scav_fly", "Fly the drone over walls and water to a lit tile; you pay the AP."),
            () => AimChosen?.Invoke(aim == TechAim.Fly ? TechAim.None : TechAim.Fly));
        fly.ToggleMode = true;
        fly.ButtonPressed = aim == TechAim.Fly;
        fly.SetBlocked(drone.Battery <= 0 || rules.FlyApCost > unit!.Ap, rules.FlyApCost > unit!.Ap);

        var inRange = unit!.Pos.Chebyshev(drone.Pos) <= range;
        var recall = Add(new ActionButton("ui_extract", "Recall drone", inRange ? "back into the pack to recharge" : "out of control range: fly it closer",
            rules.RecallApCost, "scav_drone", "Call the drone back; it recharges in the pack."),
            () => CommandRequested?.Invoke(new RecallDrone(pid, unit.Id)));
        recall.SetBlocked(!inRange || rules.RecallApCost > unit.Ap, rules.RecallApCost > unit.Ap);

        if (unit.Hook(PerkHooks.DroneCanMark) > 0)
        {
            var first = true;
            foreach (var t in _presenter.State.Units.Where(t => t.IsActive && drone.HostileTo(t) && _presenter.CanSee(t)
                                                                && Detection.Spots(data, _presenter.State, drone, t)).OrderBy(t => t.Pos.Chebyshev(drone.Pos)))
            {
                var marked = Drones.IsMarked(_presenter.State, t.Id, _presenter.Side);
                var mark = Add(new ActionButton("stance_scan", $"Mark {t.Name}", marked ? "marked" : $"seen through walls, +{rules.MarkHitBonus * 100:0}% to hit",
                    rules.MarkApCost, first ? "scav_mark" : null, "Spotter: your whole team sees the target and hits it more easily for a while."),
                    () => CommandRequested?.Invoke(new MarkTarget(pid, unit.Id, t.Id)));
                mark.SetBlocked(marked || rules.MarkApCost > unit.Ap, rules.MarkApCost > unit.Ap);
                first = false;
            }
        }
        if (unit.Hook(PerkHooks.DronePayload) > 0)
        {
            var first = true;
            foreach (var id in unit.Pack.Distinct().Where(id => data.Items[id].Use is { TargetsTile: true }))
            {
                var drop = Add(new ActionButton($"item_{data.Items[id].Kind.ToString().ToLowerInvariant()}", $"Drop {ScavengeText.Item(data, id)}",
                    "where the drone hovers", rules.PayloadApCost, first ? "scav_payload" : null, "Payload: the drone drops it on its tile."),
                    () => CommandRequested?.Invoke(new DropPayload(pid, unit.Id, id)));
                drop.SetBlocked(rules.PayloadApCost > unit.Ap, rules.PayloadApCost > unit.Ap);
                first = false;
            }
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
