using System.Linq;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony;

/// <summary>
/// Debug scenes for the review fixes, for the smoke run and screenshots: `--worker-demo=open|assign|multi` empties the
/// Woodcutter, selects it and clicks a free "+" slot (open: the picker stays up; assign: then clicks a candidate who
/// already works elsewhere, else the best, and prints <c>[worker-demo]</c> with the result, failing if they lost their
/// other job; multi: as assign, then opens their panel to show both jobs); `--placing-demo` enters build mode for a Farm
/// with a free plot hovered, so the footprints show.
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
        foreach (var w in sim.State.WorkersAt(cutter.Id).ToList()) _session.Execute(JobPicks.Leave(w.Id, cutter.Id));
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
            if (mode is not ("assign" or "multi")) return;
            GetTree().CreateTimer(0.3).Timeout += () =>
            {
                // Someone who already holds another job shows that a pick adds rather than moves.
                var busy = sim.State.Survivors.FirstOrDefault(s => s.Jobs.Count > 0 && !s.Jobs.Contains(cutter.Id) && !JobPicks.AtCap(sim.State, sim.Data, s));
                var row = (busy is null ? null : picker.Rows.FirstOrDefault(r => r.Name == $"Pick{busy.Id}")) ?? picker.Rows.FirstOrDefault();
                if (row is null)
                {
                    GD.PrintErr("[worker-demo] picker is empty");
                    return;
                }
                var before = sim.State.Survivors.FirstOrDefault(s => row.Name == $"Pick{s.Id}")?.Jobs.ToList() ?? new();
                row.EmitSignal(BaseButton.SignalName.Pressed);
                var workers = _session.Sim.State.WorkersAt(cutter.Id).ToList();
                if (workers.Count != 1)
                {
                    GD.PrintErr($"[worker-demo] assign failed: {workers.Count} workers at {sim.Data.Buildings[cutter.DefId].Name}");
                    return;
                }
                var who = workers[0];
                if (!before.All(who.Jobs.Contains))
                {
                    GD.PrintErr($"[worker-demo] {who.Name} lost a job: had {before.Count}, now {ColonyText.Job(sim, who)}");
                    return;
                }
                GD.Print($"[worker-demo] assigned {who.Name} to {sim.Data.Buildings[cutter.DefId].Name}: {ColonyText.Job(sim, who)}");
                if (mode == "multi")
                {
                    picker.Hide();
                    _town.Select(null);
                    _hud.Roster.Select(who.Id);
                }
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
