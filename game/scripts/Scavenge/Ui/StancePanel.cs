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
/// Defenses against the fog for the acting unit, as <see cref="ActionButton"/>s with AP pips and hotkeys: Overwatch
/// (O; react to the first hostile that shows itself), Brace (B; no ambush bonus against it, extra cover) and Lookout
/// (L; more sight and detection this round). Costs come from Core's rules; Core decides whether it works. The three
/// buttons are built once and updated in place.
/// </summary>
public partial class StancePanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly List<ActionButton> _buttons = new();
    private readonly PanelRows _rows;

    public event Action<IScavengeCommand>? CommandRequested;

    public StancePanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(ScavengeHud.ColumnWidth, 0);
        var box = UiKit.Panel(this, 4);
        box.AddChild(Widgets.SectionHeader("Stance", "stance_overwatch", new InfoTip("overwatch")));
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

    public void Refresh(bool busy)
    {
        var unit = _presenter.ActingUnit;
        var pid = _presenter.PlayerId;
        // The buttons only change with the acting unit and its AP and stances; the HUD refreshes after every event.
        var signature = unit is null || busy ? "" : $"{pid}|{unit.Id}|{unit.Name}|{unit.Ap}|{unit.Stance}|{unit.Lookout}";
        if (signature == _signature) return;
        _signature = signature;
        Presence.Set(this, unit is not null && !busy);
        if (unit is null || busy) return;

        _rows.Begin();
        _buttons.Clear();
        var fog = _presenter.Sim.Data.Rules.Fog;
        Add("stance_overwatch", unit.Stance == Stance.Overwatch ? "On overwatch" : "Overwatch", "ends the turn", "scav_overwatch",
            "Fire at the first hostile that moves or reveals itself in sight and reach (once).",
            fog.Overwatch.ApCost, new SetStance(pid, unit.Id, Stance.Overwatch), unit, unit.Stance == Stance.Overwatch);
        Add("stance_brace", unit.Stance == Stance.Brace ? "Braced" : "Brace", $"+{fog.Brace.CoverBonus * 100:0}% cover · ends the turn", "scav_brace",
            $"No ambush bonus against {unit.Name}, +{fog.Brace.CoverBonus * 100:0}% cover; {fog.Brace.NextTurnApPenalty} AP less next turn.",
            fog.Brace.ApCost, new SetStance(pid, unit.Id, Stance.Brace), unit, unit.Stance == Stance.Brace);
        Add("stance_scan", unit.Lookout ? "On lookout" : "Lookout", $"+{fog.Lookout.SightBonus:0} sight this round", "scav_lookout",
            $"+{fog.Lookout.SightBonus:0} sight and sharper eyes until {unit.Name}'s next turn (see Stealth in Help).",
            fog.Lookout.ApCost, new Lookout(pid, unit.Id), unit, unit.Lookout);
        _rows.End();
    }

    private void Add(string icon, string text, string detail, string hotkey, string tooltip, int cost, IScavengeCommand command, Unit unit, bool active)
    {
        var button = _rows.Button(icon, text, detail, cost, hotkey, tooltip, () => CommandRequested?.Invoke(command));
        button.SetBlocked(active || cost > unit.Ap, cost > unit.Ap);
        if (active) button.ThemeTypeVariation = "Primary";
        _buttons.Add(button);
    }
}
