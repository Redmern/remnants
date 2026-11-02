using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Remnants.Game.Scavenge;

/// <summary>Debug: the <c>--tactics-demo</c> script (see <see cref="TacticsDemo"/>).</summary>
public partial class ScavengeScreen
{
    /// <summary>
    /// Frames the staged telegraphs; with <c>picker</c> opens the character picker on a card, with <c>reaction</c> on the
    /// reaction card; with <c>enemy</c> selects the Husk (its card as the bestiary allows); with <c>preview</c> aims the first attack at the Husk so the move tiles show hit chances; then
    /// (with <c>--screenshot=PATH</c>) saves the frame.
    /// </summary>
    private async Task TacticsDemoScript()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_presenter.ActingUnit is { } u)
        {
            Select(u.Id);
            var husk = _presenter.State.Units.FirstOrDefault(t => t.EnemyDefId == "husk" && _presenter.CanSee(t));
            _camera.GlideTo(husk is null ? TileGrid.ToWorld(u.Pos) : (TileGrid.ToWorld(u.Pos) + TileGrid.ToWorld(husk.Pos)) / 2);
            switch (TacticsDemo.Mode)
            {
                case "picker":
                    _hud.Hand.Choose("steady_aim");
                    break;
                case "reaction":
                    _hud.Hand.Choose("sidestep");
                    break;
                case "enemy" when husk is not null:
                    Select(husk.Id);
                    break;
                case "preview" when husk is not null:
                    // Fresh AP and the Husk two steps off, so the move tiles in reach of a blow show their chances.
                    u.Ap = _presenter.Sim.Profile(u).MaxAp;
                    if (Core.Scavenge.Pos.Neighbours8.Select(d => new Core.Scavenge.Pos(u.Pos.X + 2 * d.X, u.Pos.Y + 2 * d.Y))
                            .FirstOrDefault(p => _presenter.State.Map.InBounds(p) && _presenter.Sim.Data.Terrain(_presenter.State.Map[p]).Passable
                                                 && _presenter.State.UnitAt(p) is null) is var near && near != default)
                        husk.Pos = near;
                    SyncAll();
                    if (_presenter.Sim.Profile(u).Actions.FirstOrDefault() is { } action) _actionPanel.SetAction(action.Id);
                    _hover = husk.Pos;
                    break;
            }
            RefreshHud();
        }
        await ToSignal(GetTree().CreateTimer(1.4), SceneTreeTimer.SignalName.Timeout);
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }
}
