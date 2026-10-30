using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--tech-demo</c> scripts (see <see cref="TechDemo"/>).</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// <c>hack</c>: one hack action on the terminal (the progress ring shows 1/2) with its tooltip up. <c>turret</c>: turns end
    /// until the hacked turret fires at the Husk. <c>drone</c>: launch the drone and fly it toward the hidden Husk until it
    /// shows. Then (with <c>--screenshot=PATH</c>) the frame is saved.
    /// </summary>
    private async Task TechDemoScript()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        var pid = _presenter.PlayerId;
        async Task Settle()
        {
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        switch (TechDemo.Mode)
        {
            case "hack" when _presenter.ActingUnit is { } u:
                var terminal = _presenter.State.Devices.First(d => d.Kind == DeviceKind.Terminal);
                await Run(new Hack(pid, u.Id, terminal.Id));
                await Settle();
                Select(u.Id);
                _camera.GlideTo(TileGrid.ToWorld(terminal.Pos));
                await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                var screen = _camera.Camera.UnprojectPosition(TileGrid.ToWorld(terminal.Pos));
                _hud.DeviceTip.Show(_presenter, terminal, screen / GetViewport().GetVisibleRect().Size * _hud.DeviceTip.GetViewportRect().Size);
                break;
            case "turret":
                var gun = _presenter.State.Devices.First(d => d.Kind == DeviceKind.Turret).UnitId!.Value;
                bool Fired() => _presenter.State.Events.Any(e => e.UnitId == gun && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed);
                for (var i = 0; i < 12 && !Fired() && _presenter.ActingUnit is { } a; i++)
                {
                    await Settle();
                    await Run(new EndTurn(pid, a.Id));
                }
                await Settle();
                Select(gun);
                if (_presenter.State.FindUnit(gun) is { } turret) _camera.GlideTo(TileGrid.ToWorld(turret.Pos));
                // Replay the shot for the still (the live one played during the turns).
                if (_presenter.State.Events.LastOrDefault(e => e.UnitId == gun && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed) is { } shot)
                    await _units.Replay([shot], _presenter.Visible);
                break;
            case "drone" when _presenter.ActingUnit is { } op:
                var lurker = _presenter.State.Units.Last(x => x.Kind == UnitKind.Enemy && x.OnMap);
                var deploy = _presenter.DeployTiles(op).OrderBy(p => p.DistanceSquared(lurker.Pos)).First();
                await Run(new DeployDrone(pid, op.Id, deploy));
                for (var i = 0; i < 2 && !_presenter.CanSee(lurker) && _presenter.ActingUnit?.Id == op.Id; i++)
                {
                    await Settle();
                    var to = Drones.FlightTargets(_presenter.Sim, op).OrderBy(p => p.DistanceSquared(lurker.Pos)).FirstOrDefault();
                    await Run(new FlyDrone(pid, op.Id, to));
                }
                await Settle();
                if (op.DroneId is { } droneId)
                {
                    Select(droneId);
                    // Frame the drone and what it found behind the wall.
                    if (_presenter.State.FindUnit(droneId) is { } drone) _camera.GlideTo((TileGrid.ToWorld(drone.Pos) + TileGrid.ToWorld(lurker.Pos)) / 2);
                }
                break;
        }
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }
}
