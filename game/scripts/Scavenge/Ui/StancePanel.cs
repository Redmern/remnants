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
/// The acting unit's plan orders as <see cref="ActionButton"/>s with AP pips and hotkeys: lookout (more sight and
/// detection this round), recover, push and dash (pointed on the map) and taking back the last step. Costs come from
/// Core's rules; Core decides whether it works. The buttons are built once and updated in place.
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
        var signature = unit is null || busy ? "" : $"{pid}|{unit.Id}|{unit.Name}|{unit.Ap}|{unit.Lookout}|{aim}|{Plans.Of(_presenter.State, unit)?.Steps.Count}";
        if (signature == _signature) return;
        _signature = signature;
        Presence.Set(this, unit is not null && !busy);
        if (unit is null || busy) return;

        _rows.Begin();
        _buttons.Clear();
        var fog = _presenter.Sim.Data.Rules.Fog;
        Add("stance_scan", unit.Lookout ? "On lookout" : "Lookout", $"+{fog.Lookout.SightBonus:0} sight this round", "scav_lookout",
            $"+{fog.Lookout.SightBonus:0} sight and sharper eyes until {unit.Name}'s next turn (see Stealth in Help).",
            fog.Lookout.ApCost, () => CommandRequested?.Invoke(new Lookout(pid, unit.Id)), unit, unit.Lookout);
        var rules = _presenter.Sim.Data.Rules;
        Add("stance_brace", "Recover", $"+{rules.Turn.RecoverStamina} stamina", "scav_recover",
            $"Spend the round's breath: {unit.Name} gets {rules.Turn.RecoverStamina} stamina back when the round resolves.",
            rules.Turn.RecoverApCost, () => CommandRequested?.Invoke(new Recover(pid, unit.Id)), unit, false);
        Add("ui_attack", "Push", $"{rules.Combat.Push.Tiles} tile away", "scav_push",
            "Shove what stands on the pattern away from where you end up. It resolves by priority: a pushed unit acts from its new tile.",
            rules.Combat.Push.ApCost, () => AimChosen?.Invoke(TechAim.Push), unit, aim == TechAim.Push);
        Add("ui_move", "Dash", $"{rules.Combat.Dash.Tiles} tiles straight", "scav_dash",
            "Run straight on, resolved by priority with the actions. It stops short of anything in the way.",
            rules.Combat.Dash.ApCost, () => AimChosen?.Invoke(TechAim.Dash), unit, aim == TechAim.Dash);
        var steps = Plans.Of(_presenter.State, unit)?.Steps.Count ?? 0;
        var undo = _rows.Button("stat_time", "Take back", $"{steps} planned", 0, "scav_undo", "Take back the last planned step; what it cost comes back.",
            () => CommandRequested?.Invoke(new UndoPlan(pid, unit.Id)));
        undo.SetBlocked(steps == 0, false);
        _buttons.Add(undo);
        _rows.End();
    }

    private void Add(string icon, string text, string detail, string hotkey, string tooltip, int cost, Action pressed, Unit unit, bool active)
    {
        var button = _rows.Button(icon, text, detail, cost, hotkey, tooltip, pressed);
        button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
        if (active) button.ThemeTypeVariation = "Primary";
        _buttons.Add(button);
    }
}
