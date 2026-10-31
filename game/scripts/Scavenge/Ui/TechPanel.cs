using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>What a tech button asks the screen to do with the next map click.</summary>
public enum TechAim { None, Deploy, Fly, Heal, Fire }

/// <summary>
/// Hacking and the scout drone for the acting unit, as <see cref="ActionButton"/>s with AP pips and hotkeys: Hack (H) each
/// device in the tool's reach with its chance and progress (or Override a Linked), Deploy (K, then click a tile next to
/// the unit), Fly (J, then click a lit tile), Recall (K), Mark (M) a hostile the drone sees, and drop a payload (P); the
/// drone variants' role (R): a medic drone's Heal and a combat drone's Fire arm a click on a highlighted unit, a cargo
/// drone loads from a cache next to it, and Ferry (I) flies its hold out from next to an exit.
/// Hidden when the unit has neither a hacking tool nor a drone. Core decides whether anything works.
/// </summary>
public partial class TechPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly VBoxContainer _content = Widgets.Column(4);
    private readonly List<ActionButton> _buttons = new();
    private readonly PanelRows _rows;

    public event Action<IScavengeCommand>? CommandRequested;
    public event Action<TechAim>? AimChosen;

    public TechPanel(ScavengePresenter presenter)
    {
        _rows = new PanelRows(_content);
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
        _rows.Begin();
        _buttons.Clear();
        Build(busy, aim);
        _rows.End();
    }

    private void Build(bool busy, TechAim aim)
    {
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
            _rows.Heading("HACK");
            var cost = Hacking.ApCost(data, unit!);
            var options = _presenter.HackOptions(unit!);
            if (options.Count == 0) _rows.Note($"Nothing in reach of the {Hacking.Tool(data, unit!)!.Name}.");
            var first = true;
            foreach (var o in options)
            {
                var progress = o.Needed > 1 ? $" · {o.Done}/{o.Needed} done" : "";
                var what = o.Device is { } d ? ScavengeText.HackEffect(d.Kind) : "it fights for you for a few rounds";
                var locked = o.Device is { } dev && Hacking.LockedOut(_presenter.State, dev, _presenter.Side);
                var button = Add("skill_hacking", $"Hack {o.Name}", locked ? "locked out after a failed hack" : $"{o.Chance * 100:0}% on the last step{progress}",
                        cost, first ? "scav_hack" : null,
                        $"Hack the {o.Name.ToLowerInvariant()}: {what}. Each try puts work in; the last rolls {o.Chance * 100:0}%. A failure sets off an alarm, shocks you and locks you out for a while.",
                    () => CommandRequested?.Invoke(new Hack(pid, unit!.Id, o.Device?.Id, o.Target?.Id)));
                button.SetBlocked(locked || cost > unit!.Ap, cost > unit!.Ap);
                first = false;
            }
        }

        if (drone is null && carried is null) return;
        var rules = data.Rules.Drone;
        var droneItem = drone?.DroneItemId ?? carried!;
        var spec = data.Items[droneItem].Drone!;
        _rows.Heading(ScavengeText.Item(data, droneItem).ToUpperInvariant());
        if (drone is null)
        {
            var battery = Drones.PackedBattery(data, unit!, carried!);
            var max = Drones.MaxBattery(data, unit!, carried!);
            var deploy = Add("skill_dronepiloting", "Deploy drone", $"battery {battery}/{max} rounds · then click a tile next to you",
                rules.DeployApCost, "scav_drone", "Launch the scout drone: it flies over anything, sees far, and shares its sight with your team.",
                () => AimChosen?.Invoke(aim == TechAim.Deploy ? TechAim.None : TechAim.Deploy));
            deploy.ToggleMode = true;
            deploy.ButtonPressed = aim == TechAim.Deploy;
            deploy.SetBlocked(battery < rules.MinBattery || rules.DeployApCost > unit!.Ap, rules.DeployApCost > unit!.Ap);
            return;
        }

        var range = Drones.ControlRange(data, unit!, drone.DroneItemId!);
        var fly = Add("ui_move", "Fly drone", $"{Drones.FlyRange(sim, drone)} tiles · range {range} · {drone.Battery} rounds left",
            rules.FlyApCost, "scav_fly", "Fly the drone over walls and water to a lit tile; you pay the AP.",
            () => AimChosen?.Invoke(aim == TechAim.Fly ? TechAim.None : TechAim.Fly));
        fly.ToggleMode = true;
        fly.ButtonPressed = aim == TechAim.Fly;
        fly.SetBlocked(drone.Battery <= 0 || rules.FlyApCost > unit!.Ap, rules.FlyApCost > unit!.Ap);

        var inRange = unit!.Pos.Chebyshev(drone.Pos) <= range;
        var recall = Add("ui_extract", "Recall drone", inRange ? "back into the pack to recharge" : "out of control range: fly it closer",
            rules.RecallApCost, "scav_drone", "Call the drone back; it recharges in the pack.",
            () => CommandRequested?.Invoke(new RecallDrone(pid, unit.Id)));
        recall.SetBlocked(!inRange || rules.RecallApCost > unit.Ap, rules.RecallApCost > unit.Ap);

        AddRole(unit, drone, spec, aim, inRange);

        if (unit.Hook(PerkHooks.DroneCanMark) > 0)
        {
            var first = true;
            foreach (var t in _presenter.State.Units.Where(t => t.IsActive && drone.HostileTo(t) && _presenter.CanSee(t)
                                                                && Detection.Spots(data, _presenter.State, drone, t)).OrderBy(t => t.Pos.Chebyshev(drone.Pos)))
            {
                var marked = Drones.IsMarked(_presenter.State, t.Id, _presenter.Side);
                var mark = Add("stance_scan", $"Mark {t.Name}", marked ? "marked" : $"seen through walls, +{rules.MarkHitBonus * 100:0}% to hit",
                    rules.MarkApCost, first ? "scav_mark" : null, "Spotter: your whole team sees the target and hits it more easily for a while.",
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
                var drop = Add($"item_{data.Items[id].Kind.ToString().ToLowerInvariant()}", $"Drop {ScavengeText.Item(data, id)}",
                    "where the drone hovers", rules.PayloadApCost, first ? "scav_payload" : null, "Payload: the drone drops it on its tile.",
                    () => CommandRequested?.Invoke(new DropPayload(pid, unit.Id, id)));
                drop.SetBlocked(rules.PayloadApCost > unit.Ap, rules.PayloadApCost > unit.Ap);
                first = false;
            }
        }
    }

    /// <summary>The variant's own orders (medic, cargo, combat); Core checks range, AP and targets.</summary>
    private void AddRole(Unit unit, Unit drone, DroneItemDef spec, TechAim aim, bool inRange)
    {
        var sim = _presenter.Sim;
        var data = sim.Data;
        var pid = _presenter.PlayerId;
        var cost = spec.ActionApCost;
        switch (spec.Role)
        {
            case DroneRole.Medic:
            {
                var patients = HealTargets(unit, drone);
                var kit = patients.Select(p => DroneRoles.HealItem(data, unit, p)).FirstOrDefault(i => i is not null)
                          ?? unit.Pack.FirstOrDefault(id => data.Items[id].Use is { TargetsTile: false, Heal: > 0 });
                var detail = !inRange ? "out of control range" : kit is null ? "no bandage or med-kit in your pack"
                    : patients.Count == 0 ? $"fly it next to someone hurt · {ScavengeText.Item(data, kit)}"
                    : $"{patients.Count} in reach · then click who · {ScavengeText.Item(data, kit)}";
                var heal = Add("ui_heal", "Drone: heal", detail, cost, "scav_drone_action",
                        "Medic drone: drops a bandage or med-kit from your pack on a friendly unit next to it (stabilizes the downed).",
                    () => AimChosen?.Invoke(aim == TechAim.Heal ? TechAim.None : TechAim.Heal));
                heal.ToggleMode = true;
                heal.ButtonPressed = aim == TechAim.Heal;
                heal.SetBlocked(!inRange || kit is null || patients.Count == 0 || cost > unit.Ap, cost > unit.Ap);
                break;
            }
            case DroneRole.Cargo:
            {
                var hold = drone.Pack.Count;
                var first = true;
                foreach (var cache in _presenter.State.Caches.Where(c => c.Pos.Chebyshev(drone.Pos) <= 1 && _presenter.IsExplored(c.Pos)).ToList())
                    foreach (var id in cache.Items.Distinct().ToList())
                    {
                        var cacheId = cache.Id;
                        var load = Add("stat_loot", $"Drone: load {ScavengeText.Item(data, id)}", $"hold {hold}/{spec.Cargo}", cost,
                                first ? "scav_drone_action" : null, "Cargo drone: winch an item from the cache into the drone's hold.",
                            () => CommandRequested?.Invoke(new DroneLoad(pid, unit.Id, cacheId, id)));
                        load.SetBlocked(!inRange || hold >= spec.Cargo || cost > unit.Ap, cost > unit.Ap);
                        first = false;
                    }
                var ferryPoint = DroneRoles.FerryPoint(sim, drone.Pos);
                var ferry = Add("ui_extract", "Drone: ferry out",
                        ferryPoint ? $"{hold} item{(hold == 1 ? "" : "s")} home, safe whatever happens" : "fly it next to an exit first", cost, "scav_ferry",
                        "Cargo drone: flies its hold (and itself) out of the zone. They come home even if the party falls.",
                    () => CommandRequested?.Invoke(new DroneFerry(pid, unit.Id)));
                ferry.SetBlocked(!inRange || !ferryPoint || cost > unit.Ap, cost > unit.Ap);
                if (ferryPoint && inRange) ferry.ThemeTypeVariation = "Primary";
                break;
            }
            case DroneRole.Combat:
            {
                var targets = DroneRoles.FireTargets(sim, unit).Where(_presenter.CanSee).ToList();
                var gun = sim.Profile(drone).Actions.FirstOrDefault();
                var ap = gun?.ApCost ?? cost;
                var detail = !inRange ? "out of control range" : targets.Count == 0 ? "no hostile in its sights"
                    : $"{targets.Count} target{(targets.Count == 1 ? "" : "s")} · {gun?.MinDamage:0}–{gun?.MaxDamage:0} dmg · then click one";
                var fire = Add("skill_firearms", "Drone: fire", detail, ap, "scav_drone_action",
                        "Combat drone: its gun at a hostile it sees, aimed with your Drone Piloting. Loud: it draws fire.",
                    () => AimChosen?.Invoke(aim == TechAim.Fire ? TechAim.None : TechAim.Fire));
                fire.ToggleMode = true;
                fire.ButtonPressed = aim == TechAim.Fire;
                fire.SetBlocked(!inRange || targets.Count == 0 || ap > unit.Ap, ap > unit.Ap);
                break;
            }
        }
    }

    /// <summary>Friendly people next to the drone the medic could treat (hurt or downed).</summary>
    public List<Unit> HealTargets(Unit operatorUnit, Unit drone) =>
        _presenter.State.Units.Where(u => u.OnMap && u.Side == operatorUnit.Side && u.Kind is UnitKind.Survivor or UnitKind.Npc
                                          && u.Status is UnitStatus.Active or UnitStatus.Downed && u.Pos.Chebyshev(drone.Pos) <= 1
                                          && (u.Status == UnitStatus.Downed || u.Hp < u.MaxHp)).OrderBy(u => u.Id).ToList();

    private ActionButton Add(string icon, string text, string detail, int apCost, string? hotkey, Action onPressed) =>
        Add(icon, text, detail, apCost, hotkey, "", onPressed);

    private ActionButton Add(string icon, string text, string detail, int apCost, string? hotkey, string tooltip, Action onPressed)
    {
        var button = _rows.Button(icon, text, detail, apCost, hotkey, tooltip, onPressed);
        _buttons.Add(button);
        return button;
    }
}
