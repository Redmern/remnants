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
/// The acting unit's plan: its Move, Action and Card slots, each cleared with a click, then its orders as
/// <see cref="ActionButton"/>s with AP pips and hotkeys: lookout (more sight and detection this round), recover, push and dash
/// (pointed on the map). An order into a full slot changes it. Costs come from Core's rules; Core decides whether it works.
/// The buttons are built once and updated in place.
/// </summary>
public partial class StancePanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly List<ActionButton> _buttons = new();
    private readonly PanelRows _rows;

    public event Action<IScavengeCommand>? CommandRequested;

    /// <summary>Push or dash chosen: it is pointed on the map.</summary>
    public event Action<TechAim>? AimChosen;


    public StancePanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(ScavengeHud.ColumnWidth, 0);
        var box = UiKit.Panel(this, 4);
        box.AddChild(Widgets.SectionHeader("Plan", "stance_overwatch", new InfoTip("planning")));
        var content = Widgets.Column(4);
        box.AddChild(content);
        _rows = new PanelRows(content);
    }

    /// <summary>Presses the stance bound to the key in <paramref name="e"/>, if it is available.</summary>
    public bool PressHotkey(InputEvent e)
    {
        if (!Visible) return false;
        var button = _buttons.FirstOrDefault(b => b.Hotkey is { } k && !b.Disabled && Hotkeys.Pressed(e, k));
        button?.EmitSignal(BaseButton.SignalName.Pressed);
        return button is not null;
    }

    private string? _signature;

    public void Refresh(bool busy, TechAim aim = TechAim.None)
    {
        var unit = _presenter.ActingUnit;
        var pid = _presenter.PlayerId;
        // The buttons only change with the acting unit and its AP and stances; the HUD refreshes after every event.
        var signature = unit is null || busy ? "" : $"{pid}|{unit.Id}|{unit.Name}|{unit.Ap}|{unit.Lookout}|{aim}|{SlotSignature(unit)}";
        if (signature == _signature) return;
        _signature = signature;
        Presence.Set(this, unit is not null && !busy);
        if (unit is null || busy) return;

        _rows.Begin();
        _buttons.Clear();
        AddSlots(unit, pid);
        var fog = _presenter.Sim.Data.Rules.Fog;
        Add("stance_scan", unit.Lookout ? "On lookout" : "Lookout", $"+{fog.Lookout.SightBonus:0} sight this round", "scav_lookout",
            $"+{fog.Lookout.SightBonus:0} sight and sharper eyes until the next round starts (see Stealth in Help).",
            fog.Lookout.ApCost, () => CommandRequested?.Invoke(new Lookout(pid, unit.Id)), unit, unit.Lookout);
        var rules = _presenter.Sim.Data.Rules;
        Add("stance_brace", "Recover", $"+{rules.Turn.RecoverStamina} stamina", "scav_recover",
            $"{unit.Name} gets {rules.Turn.RecoverStamina} stamina back once the actions are done.\nLoses to: any blow that lands, which cancels it and does {Fmt.Percent(rules.Turn.RecoverHitDamage)} more damage.",
            rules.Turn.RecoverApCost, () => CommandRequested?.Invoke(new Recover(pid, unit.Id)), unit, false);
        Add("ui_attack", "Push", $"{rules.Combat.Push.Tiles} tile away", "scav_push",
            "Shove what stands on the pattern away from where you end up. It resolves by priority: a pushed unit acts from its new tile.\nBeats: Defend, whose guard it breaks. Loses to: Dash, which gets away; an Attack that lands first.",
            rules.Combat.Push.ApCost, () => AimChosen?.Invoke(TechAim.Push), unit, aim == TechAim.Push);
        Add("ui_move", "Dash", $"{rules.Combat.Dash.Tiles} tiles straight", "scav_dash",
            "Run straight on, resolved by priority with the actions. It stops short of anything in the way.\nBeats: Push, which finds you gone. Loses to: an Attack that lands first or covers where you land.",
            rules.Combat.Dash.ApCost, () => AimChosen?.Invoke(TechAim.Dash), unit, aim == TechAim.Dash);
        _rows.End();
    }

    private string SlotSignature(Unit unit) =>
        string.Join(",", TacticsView.Slots(_presenter.Sim, unit).Select(s => $"{s.Used}/{s.Limit}:{s.Text}"));

    /// <summary>One row a slot. What it holds shows on it; the undo key goes to the slot of the latest entry.</summary>
    private void AddSlots(Unit unit, int pid)
    {
        var latest = Plans.Of(_presenter.State, unit)?.Steps.LastOrDefault() is { } step ? Plans.SlotOf(step) : (PlanSlot?)null;
        foreach (var slot in TacticsView.Slots(_presenter.Sim, unit))
        {
            var count = slot.Limit > 1 ? $" {slot.Used}/{slot.Limit}" : "";
            var (icon, tip) = slot.Slot switch
            {
                PlanSlot.Move => ("ui_move", $"The walk {unit.Name} plans this round ({unit.Mp} MP left). Click a lit tile to set or change it."),
                PlanSlot.Action => ("ui_attack", $"The action {unit.Name} plans this round ({unit.Ap} AP left). Choose another to change it."),
                _ => ("ui_items", $"The card {unit.Name} plays this round, on top of the action. Clear it to play another."),
            };
            var detail = slot.Clearable ? "click to clear" : slot.Used > 0 ? "stays played" : "empty";
            var target = slot.Slot;
            var button = _rows.Button(icon, $"{slot.Slot}{count}: {slot.Text}", detail, 0, target == latest ? "scav_undo" : null,
                tip + (slot.Clearable ? "\nClick to clear it; what it cost comes back." : ""),
                () => CommandRequested?.Invoke(new UndoPlan(pid, unit.Id, Slot: target)));
            button.SetBlocked(!slot.Clearable, false);
            _buttons.Add(button);
        }
    }

    private void Add(string icon, string text, string detail, string hotkey, string tooltip, int cost, Action pressed, Unit unit, bool active)
    {
        var button = _rows.Button(icon, text, detail, cost, hotkey, tooltip, pressed);
        var dear = cost > Plans.ApToSpend(_presenter.State, unit);
        button.SetBlocked(dear, dear);
        if (active) button.ThemeTypeVariation = "Primary";
        _buttons.Add(button);
    }
}
