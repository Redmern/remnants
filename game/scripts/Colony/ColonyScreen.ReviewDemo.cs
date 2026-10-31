using System.Linq;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony;

/// <summary>
/// Debug scenes for the review fixes, for the smoke run and screenshots: `--worker-demo=open|assign` empties the
/// Woodcutter, selects it and clicks a free "+" slot (open: the picker stays up; assign: then clicks the best
/// candidate and prints <c>[worker-demo]</c> with the result); `--placing-demo` enters build mode for a Farm with a free
/// plot hovered, so the footprints show.
/// </summary>
public partial class ColonyScreen
{
    private void RunWorkerDemo(string mode)
    {
        var sim = _session.Sim;
        // A fresh colony has no Woodcutter yet: build one (resources may be short; Core decides), else use any workplace.
        if (!sim.State.Buildings.Any(b => b.DefId == "woodcutter")) _session.Execute(new Construct("woodcutter"));
        if ((sim.State.Buildings.FirstOrDefault(b => b.DefId == "woodcutter")
             ?? sim.State.Buildings.FirstOrDefault(b => sim.Data.Buildings[b.DefId].Job is not null && sim.Data.Buildings[b.DefId].Level(b.Level).WorkerSlots > 0)) is not { } cutter)
        {
            GD.PrintErr("[worker-demo] no workplace in this colony");
            return;
        }
        foreach (var w in sim.State.WorkersAt(cutter.Id).ToList()) _session.Execute(new Unassign(w.Id));
        _session.SetSpeed(0);
        _hud.Roster.Select(null);
        _town.Select(cutter.Id);
        // Wait for the panel to lay out (the picker opens under the slot), then click "+" as a player would.
        GetTree().CreateTimer(0.4).Timeout += () =>
        {
            var slots = _hud.Building.Workers;
            if (!slots.OpenFirstFree() || slots.Picker is not { } picker)
            {
                GD.PrintErr("[worker-demo] no free slot to click");
                return;
            }
            GD.Print($"[worker-demo] picker open: {picker.Rows.Count()} candidates");
            if (mode != "assign") return;
            GetTree().CreateTimer(0.3).Timeout += () =>
            {
                if (picker.Rows.FirstOrDefault() is not { } row)
                {
                    GD.PrintErr("[worker-demo] picker is empty");
                    return;
                }
                row.EmitSignal(BaseButton.SignalName.Pressed);
                var workers = _session.Sim.State.WorkersAt(cutter.Id).Select(w => w.Name).ToList();
                if (workers.Count == 1) GD.Print($"[worker-demo] assigned {workers[0]} to {sim.Data.Buildings[cutter.DefId].Name}");
                else GD.PrintErr($"[worker-demo] assign failed: {workers.Count} workers at {sim.Data.Buildings[cutter.DefId].Name}");
            };
        };
    }

    private void RunPlacingDemo()
    {
        _session.SetSpeed(0);
        _hud.Roster.Select(null);
        _town.Select(null);
        _town.BeginPlacing("farm");
        var sim = _session.Sim;
        var hall = sim.State.Buildings.FirstOrDefault(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
        var free = Enumerable.Range(0, sim.Data.Rules.Layout.PlotCount).Where(sim.IsPlotFree)
            .OrderBy(p => hall is null ? p : System.Math.Abs(p - hall.Plot)).FirstOrDefault(-1);
        if (free >= 0) _town.HoverPlot(free);
    }
}
