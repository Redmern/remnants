using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--gear-demo</c> script (see <see cref="GearDemo"/>).</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// Selects the modded survivor, aims its first attack (so the Linked's attack preview shows the mod tags), frames
    /// both, then (with <c>--screenshot=PATH</c>) saves the frame.
    /// </summary>
    private async Task GearDemoScript()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_presenter.ActingUnit is { } u)
        {
            Select(u.Id);
            var action = _presenter.Sim.Profile(u).Actions.FirstOrDefault();
            if (action is not null) _actionPanel.SetAction(action.Id);
            var target = _presenter.State.Units.FirstOrDefault(t => t.OnMap && u.HostileTo(t) && _presenter.CanSee(t));
            _camera.GlideTo(target is null ? TileGrid.ToWorld(u.Pos) : (TileGrid.ToWorld(u.Pos) + TileGrid.ToWorld(target.Pos)) / 2);
            Select(u.Id);
        }
        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }
}
