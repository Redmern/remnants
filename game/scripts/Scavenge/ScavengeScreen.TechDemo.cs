using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Scavenge.Ui;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--tech-demo</c> scripts (see <see cref="TechDemo"/>).</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// <c>hack</c>: one hack action on the terminal (the progress ring shows 1/2) with its tooltip up. <c>turret</c>: turns end
    /// until the hacked turret fires at the Husk. <c>drone</c>: launch the drone and fly it toward the hidden Husk until it
    /// shows. Then (with <c>--screenshot=PATH</c>) the frame is saved.
    /// </summary>
    /// <summary>
    /// The electronics stagings: <c>medic</c> launches the medic drone next to the hurt teammate and treats them; <c>cargo</c>
    /// launches the cargo drone by the cache, loads two items and flies it next to the exit (Ferry ready); <c>combat</c> launches
    /// the combat drone and fires at the Husk; <c>emp</c> throws the EMP over the Linked and its drone and holds the burst.
    /// Debug only: the operator's AP is topped up between orders so one turn shows the whole moment.
    /// </summary>
    private async Task ElectronicsDemo(int pid, System.Func<Task> settle)
    {
        if (_presenter.ActingUnit is not { } op) return;
        var state = _presenter.State;
        void TopUp() => op.Ap = _presenter.Sim.Profile(op).MaxAp;
        async Task Order(IScavengeCommand c)
        {
            await settle();
            TopUp();
            await Run(c);
            await settle();
        }
        Pos DeployNear(Pos target) => _presenter.DeployTiles(op).OrderBy(p => p.Chebyshev(target)).ThenBy(p => p.DistanceSquared(target)).First();
        switch (TechDemo.Mode)
        {
            case "medic":
                if (state.Units.FirstOrDefault(u => u.Side == op.Side && u.Kind == UnitKind.Survivor && u.Id != op.Id && u.Hp < u.MaxHp) is not { } hurt) break;
                await Order(new DeployDrone(pid, op.Id, DeployNear(hurt.Pos)));
                if (op.DroneId is not { } medic) break;
                await Order(new DroneHeal(pid, op.Id, hurt.Id));
                TopUp();
                Select(op.Id);
                _aim = TechAim.Heal;
                RefreshHud();
                if (state.FindUnit(medic) is { } m) _camera.GlideTo((TileGrid.ToWorld(m.Pos) + TileGrid.ToWorld(hurt.Pos)) / 2);
                _camera.Camera.Size = 16;
                break;
            case "cargo":
                var cache = state.Caches.Last();
                await Order(new DeployDrone(pid, op.Id, DeployNear(cache.Pos)));
                if (op.DroneId is not { } cargoId || state.FindUnit(cargoId) is not { } cargo) break;
                if (cargo.Pos.Chebyshev(cache.Pos) > 1)
                    await Order(new FlyDrone(pid, op.Id, Drones.FlightTargets(_presenter.Sim, op).OrderBy(p => p.Chebyshev(cache.Pos)).First()));
                foreach (var item in cache.Items.Take(2).ToList()) await Order(new DroneLoad(pid, op.Id, cache.Id, item));
                var exit = state.Map.Exits.OrderBy(x => x.Chebyshev(cargo.Pos)).First();
                if (!DroneRoles.FerryPoint(_presenter.Sim, cargo.Pos))
                    await Order(new FlyDrone(pid, op.Id, Drones.FlightTargets(_presenter.Sim, op)
                        .Where(p => DroneRoles.FerryPoint(_presenter.Sim, p)).OrderBy(p => p.Chebyshev(exit)).DefaultIfEmpty(exit).First()));
                TopUp();
                Select(op.Id);
                _camera.GlideTo((TileGrid.ToWorld(cargo.Pos) + TileGrid.ToWorld(op.Pos)) / 2);
                _camera.Camera.Size = 18;
                break;
            case "combat":
                var husk = state.Units.Last(u => u.Kind == UnitKind.Enemy && u.OnMap);
                await Order(new DeployDrone(pid, op.Id, DeployNear(husk.Pos)));
                if (op.DroneId is not { } gunId) break;
                await settle();
                TopUp();
                if (DroneRoles.FireTargets(_presenter.Sim, op).FirstOrDefault() is { } target)
                {
                    _camera.GlideTo((TileGrid.ToWorld(state.FindUnit(gunId)!.Pos) + TileGrid.ToWorld(target.Pos)) / 2);
                    await Order(new DroneFire(pid, op.Id, target.Id));
                    // Replay the shot for the still, aimed on the next target highlight.
                    if (state.Events.LastOrDefault(e => e.UnitId == gunId && e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed) is { } shot)
                    {
                        TopUp();
                        Select(op.Id);
                        _aim = TechAim.Fire;
                        RefreshHud();
                        _ = _units.Replay([shot], _presenter.Visible);
                        await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
                        return;
                    }
                }
                Select(op.Id);
                break;
            case "emp":
                var linked = state.Units.First(u => u.Kind == UnitKind.Enemy && u.EnemyDefId == "linked" && u.OnMap);
                _units.HoldEffects = true;
                _camera.GlideTo((TileGrid.ToWorld(op.Pos) + TileGrid.ToWorld(linked.Pos)) / 2);
                await settle();
                TopUp();
                _usingItem = "emp_grenade";
                RefreshHud();
                await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
                await Order(new UseItem(pid, op.Id, "emp_grenade", At: linked.Pos));
                Select(linked.Id);
                await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
                break;
        }
    }

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
        await ElectronicsDemo(pid, Settle);
        await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }
}
