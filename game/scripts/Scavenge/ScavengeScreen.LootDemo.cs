using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--loot-demo</c> scripts (see <see cref="LootDemo"/>).</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// <c>pile</c>: shoot the Husk until it drops, step next to its pile, then frame it with the pickup panel up and the pile's tooltip.
    /// <c>salvage</c>: one Salvage action on the tree (the ring shows 1/2), its tooltip up, the carry bar warning. Then (with
    /// <c>--screenshot=PATH</c>) the frame is saved.
    /// </summary>
    private async Task LootDemoScript()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        var pid = _presenter.PlayerId;
        async Task Settle()
        {
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        Pos? focus = null;
        switch (LootDemo.Mode)
        {
            case "pile" when _presenter.ActingUnit is { } u:
                var husk = _presenter.State.Units.Last(x => x.Kind == UnitKind.Enemy && x.OnMap);
                var action = _presenter.AttackOptions(u, _presenter.Sim.Profile(u).Actions.First()).Count > 0
                    ? _presenter.Sim.Profile(u).Actions.First() : _presenter.Sim.Profile(u).Actions.Last();
                for (var i = 0; i < 3 && husk.OnMap && husk.Status != UnitStatus.Dead && u.Ap >= action.ApCost && _presenter.ActingUnit?.Id == u.Id; i++)
                    await StrikeAndLand(u, husk, action.Id);
                focus = husk.Pos;
                // Step next to the pile so the pickup panel opens.
                if (_presenter.ActingUnit?.Id == u.Id
                    && _presenter.Sim.Reachable(u).Where(kv => kv.Value < u.Ap && kv.Key.Chebyshev(husk.Pos) == 1).OrderBy(kv => kv.Value)
                        .Select(kv => (Pos?)kv.Key).FirstOrDefault() is { } next)
                {
                    await Run(new Move(pid, u.Id, next));
                    await Settle();
                }
                Select(u.Id);
                break;
            case "salvage" when _presenter.ActingUnit is { } w:
                var node = _presenter.NodesInReach(w).First();
                await Run(new Salvage(pid, w.Id, node.Id));
                await Settle();
                focus = node.Pos;
                Select(w.Id);
                break;
        }
        if (focus is { } at)
        {
            // Frame the spot above the pickup panel (it sits low in the middle of the screen).
            _camera.GlideTo(TileGrid.ToWorld(at) + new Vector3(3, 0, 3));
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            // The card beside the spot, not over it, so the pile or node itself shows in the still.
            var screen = _camera.Camera.UnprojectPosition(TileGrid.ToWorld(at)) / GetViewport().GetVisibleRect().Size * _hud.LootTip.GetViewportRect().Size
                         + new Vector2(120, -260);
            if (_presenter.KnownPiles.FirstOrDefault(c => c.Pos == at) is { } pile) _hud.LootTip.ShowPile(_presenter, pile, screen);
            else if (_presenter.KnownNodes.FirstOrDefault(n => n.Pos == at) is { } n) _hud.LootTip.ShowNode(_presenter, n, screen);
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
