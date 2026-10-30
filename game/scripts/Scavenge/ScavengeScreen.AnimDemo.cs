using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Anim;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--anim-demo</c> scripts (see <see cref="AnimDemo"/>), recorded by <see cref="FrameRecorder"/>.</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// <c>survivor</c>: walk up to the Husk, butt strike, aimed shot, then the survivor's death (replayed, not ruled).
    /// <c>enemy</c>: end the party's turns until the hunting Husk and Linked have walked up and attacked.
    /// </summary>
    private async Task AnimDemoScript()
    {
        try
        {
            await AnimDemoSteps();
        }
        catch (System.Exception e)
        {
            GD.PushError($"[anim-demo] {e}");
        }
        FrameRecorder.Finish();
    }

    private async Task AnimDemoSteps()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        var pid = _presenter.PlayerId;
        async Task Settle()
        {
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async Task Pause(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

        switch (AnimDemo.Mode)
        {
            case "survivor" when _presenter.ActingUnit is { } u:
                var husk = _presenter.State.Units.First(x => x.Kind == UnitKind.Enemy && x.OnMap);
                u.Ap = 30;
                // The furthest tile the survivor can walk to in one move, with the Husk waiting just beyond it.
                var map = _presenter.State.Map;
                bool Open(Pos p) => map.InBounds(p) && map[p] == Terrain.Floor && _presenter.State.UnitAt(p) is null;
                var beside = _presenter.Reachable(u).Keys.Where(p => Pos.Neighbours8.Any(d => Open(p + d) && (p + d).Chebyshev(u.Pos) > p.Chebyshev(u.Pos)))
                    .OrderByDescending(p => p.Chebyshev(u.Pos)).ThenBy(map.Index).First();
                husk.Pos = Pos.Neighbours8.Select(d => beside + d).Where(p => Open(p) && p.Chebyshev(u.Pos) > beside.Chebyshev(u.Pos)).OrderBy(map.Index).First();
                Visibility.UpdateExplored(_presenter.Sim.Data, _presenter.State);
                SyncAll();
                Select(u.Id);
                _camera.GlideTo((TileGrid.ToWorld(beside) + TileGrid.ToWorld(husk.Pos)) / 2);
                await Pause(0.6);
                FrameRecorder.Begin();
                await Pause(0.4);
                await Run(new Move(pid, u.Id, beside));
                await Settle();
                await Run(new Attack(pid, u.Id, husk.Id, "butt_strike"));
                await Settle();
                await Pause(0.3);
                await Run(new Attack(pid, u.Id, husk.Id, "aimed_shot"));
                await Settle();
                await Pause(0.4);
                await _units.Replay([new ScavengeEvent(_presenter.State.Round, ScavengeEventType.Died, $"{u.Name} died.", u.Id, Pos: u.Pos)],
                    _presenter.Visible);
                break;
            case "enemy" when _presenter.ActingUnit is not null:
                FrameRecorder.Begin();
                bool Acted() => _presenter.State.Events.Any(e => e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed
                    && _presenter.State.FindUnit(e.UnitId ?? -1)?.Kind == UnitKind.Enemy);
                for (var i = 0; i < 8 && !Acted() && _presenter.ActingUnit is { } a; i++)
                {
                    await Settle();
                    await Run(new EndTurn(pid, a.Id));
                }
                await Settle();
                break;
        }
        await Pause(0.6);
    }
}
