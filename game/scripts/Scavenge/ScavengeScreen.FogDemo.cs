using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--fog-demo</c> script (see <see cref="Fog.FogDemo"/>).</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// The party's last survivor in this round's order goes on overwatch (so it still holds when the next round starts),
    /// the others end their turns, until the hidden Linked ambushes someone;
    /// then (with <c>--screenshot=PATH</c>) the moment is saved while the incoming-fire streak is up.
    /// </summary>
    private async Task FogDemoScript()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        var watcher = Fog.FogDemo.WatcherId;
        for (var i = 0; i < 12 && !_presenter.Done; i++)
        {
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_presenter.State.Events.Any(e => e.Type == ScavengeEventType.Ambushed)) break;
            if (_presenter.ActingUnit is not { } u) break;
            var pid = _presenter.PlayerId;
            await Run(u.Id == watcher ? new SetStance(pid, u.Id, Stance.Overwatch) : new EndTurn(pid, u.Id));
        }
        while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var victim = _presenter.State.Events.LastOrDefault(e => e.Type == ScavengeEventType.Ambushed)?.TargetId;
        if (victim is { } id && _presenter.State.FindUnit(id) is { } v)
        {
            Select(id);
            _camera.GlideTo(TileGrid.ToWorld(v.Pos));
            // Replay the streak for the still frame (the live one may have faded during the replay).
            _fog.Incoming(_presenter.State.Events.Last(e => e.Type == ScavengeEventType.Ambushed));
        }
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }
}
