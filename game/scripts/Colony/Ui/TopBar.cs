using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Remnants.Game.Colony.Ui;

/// <summary>Stockpile, clock, beds, speed controls and the build / save / load / menu buttons.</summary>
public partial class TopBar : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Label _resources = Ui.Label(size: 15);
    private readonly Label _clock = Ui.Label(size: 15);
    private readonly Dictionary<int, Button> _speedButtons = new();

    public event Action? BuildPressed;
    public event Action? ExpeditionPressed;
    public event Action? MenuPressed;

    public TopBar(ColonySession session)
    {
        _session = session;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        Ui.Panel(this).AddChild(row);

        _resources.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_resources);
        row.AddChild(_clock);
        row.AddChild(new VSeparator());

        foreach (var speed in ColonySession.Speeds)
        {
            var button = Ui.Button(speed == 0 ? "II" : $"{speed}x", () => _session.SetSpeed(speed), 40);
            button.ToggleMode = true;
            _speedButtons[speed] = button;
            row.AddChild(button);
        }
        row.AddChild(new VSeparator());
        row.AddChild(Ui.Button("Build", () => BuildPressed?.Invoke(), 70));
        row.AddChild(Ui.Button("Expedition", () => ExpeditionPressed?.Invoke(), 90));
        row.AddChild(Ui.Button("Save", _session.Save, 60));
        row.AddChild(Ui.Button("Load", _session.Load, 60));
        row.AddChild(Ui.Button("Menu", () => MenuPressed?.Invoke(), 60));
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var stock = sim.Data.Resources.Values
            .Select(r => $"{r.Name} {sim.State.Stockpile.Get(r.Id):0}");
        var people = sim.State.Survivors.Count;
        var away = sim.State.Survivors.Count(s => s.Away);
        _resources.Text = string.Join("   ", stock) + $"     Pop {people}{(away > 0 ? $" ({away} away)" : "")}  Beds {sim.BedCapacity}";
        _clock.Text = ColonyText.Clock(sim);
        foreach (var (speed, button) in _speedButtons) button.SetPressedNoSignal(speed == _session.Speed);
    }
}
