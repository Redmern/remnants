using System;
using System.Collections.Generic;
using Godot;
using Remnants.Core.Data;

namespace Remnants.Game.Colony.Ui;

/// <summary>Lists building types with their construction cost. Picking one starts plot placement in the town view.</summary>
public partial class BuildMenu : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Label _hint = Ui.Label("", 13, Ui.Muted);
    private readonly Dictionary<string, Button> _buttons = new();

    public event Action<string>? TypeChosen;

    public BuildMenu(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        var box = Ui.Panel(this, 4);
        box.AddChild(Ui.Heading("Construct"));
        foreach (var def in session.Sim.Data.Buildings.Values)
        {
            var button = Ui.Button("", () => TypeChosen?.Invoke(def.Id));
            button.Alignment = HorizontalAlignment.Left;
            _buttons[def.Id] = button;
            box.AddChild(button);
        }
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_hint);
        Visible = false;
    }

    public void SetPlacing(BuildingDef? def) =>
        _hint.Text = def is null
            ? "Pick a building, then click a free plot."
            : $"Placing {def.Name}: click a free plot. Right-click or Esc cancels.";

    public void Refresh()
    {
        var sim = _session.Sim;
        foreach (var (id, button) in _buttons)
        {
            var def = sim.Data.Buildings[id];
            var cost = def.Level(1).Cost;
            button.Text = $"{def.Name}  —  {ColonyText.Cost(cost, sim.Data)}";
            button.Modulate = sim.State.Stockpile.CanAfford(cost) ? Colors.White : new Color(1, 0.6f, 0.55f);
        }
    }
}
