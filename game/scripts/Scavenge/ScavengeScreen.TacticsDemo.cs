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
                case "discard":
                    // The hand's dead card goes for free, as its Discard button would: it sweeps into the used pile.
                    await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                    if (_presenter.Hand()?.Cards.FirstOrDefault(c => c.Dead) is { } dead)
                    {
                        var ok = await Run(new Core.Scavenge.DiscardDeadCard(_presenter.PlayerId, u.Id, dead.Id));
                        GD.Print($"Tactics demo: discarded {dead.Id}: {ok}");
                    }
                    else GD.Print("Tactics demo: no dead card in hand");
                    break;
                case "enemy" when husk is not null:
                    Select(husk.Id);
                    break;
                case "targeting" or "smoke" when husk is not null:
                    // The Husk right next to the acting unit, so Shove reaches it; the aim hovers it, tooltip and all.
                    u.Ap = _presenter.Sim.Profile(u).MaxAp;
                    if (Core.Scavenge.Pos.Neighbours8.Select(d => u.Pos + d)
                            .FirstOrDefault(p => _presenter.State.Map.InBounds(p) && _presenter.Sim.Data.Terrain(_presenter.State.Map[p]).Passable
                                                 && _presenter.State.UnitAt(p) is null) is var next && next != default)
                        husk.Pos = next;
                    SyncAll();
                    var card = TacticsDemo.Mode == "smoke" ? "smoke_bomb" : "shove";
                    _hud.Hand.Choose(card);
                    BeginCardAim(card, u.Id, false, false);
                    var aimTile = DemoAimTile(husk.Pos);
                    _hover = aimTile;
                    _map.SetHover(aimTile);
                    RefreshHud();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (_cardAim?.At(aimTile) is { } option)
                        _hud.TargetTip.Show(_cardAim.CardName, husk.Name, option, _camera.Camera.UnprojectPosition(TileGrid.ToWorld(aimTile)));
                    GD.Print($"Tactics demo: aiming {card}, {_cardAim?.Options.Count(o => o.Ok)} of {_cardAim?.Options.Count} targets work; " +
                             $"at {aimTile}: {(_cardAim?.At(aimTile) is { } at ? at.Ok ? at.Outcome : at.Reason : "none")}");
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
        // --play-aim: click the aimed target (the Husk) as the player would, replay, and check the models match Core.
        if (DebugCapture.HasFlag("--play-aim") && _cardAim is { } aimed
            && _presenter.State.Units.FirstOrDefault(t => t.EnemyDefId == "husk" && _presenter.CanSee(t)) is { } aimAt)
        {
            ClickCardAim(aimed, DemoAimTile(aimAt.Pos));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var off = _presenter.State.Units.Where(t => t.OnMap && _units.WorldOf(t.Id) is { } w && TileGrid.FromWorld(w) != t.Pos).Select(t => t.Name).ToList();
            GD.Print($"Tactics demo: played {aimed.CardId}; Husk at {aimAt.Pos}, {_presenter.State.FieldEffects.Count} field effects; " +
                     (off.Count == 0 ? "every model on its Core tile" : $"models off their Core tile: {string.Join(", ", off)}"));
        }
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }

    /// <summary>
    /// The demo's aim: the Husk's tile when a target there works (Shove), else the working target nearest it (a Smoke Bomb
    /// is a screen, never over the player next to it).
    /// </summary>
    private Core.Scavenge.Pos DemoAimTile(Core.Scavenge.Pos husk) =>
        _cardAim?.At(husk) is { Ok: true } || _cardAim is null ? husk
        : _cardAim.Options.Where(o => o.Ok).OrderBy(o => o.Tile.Chebyshev(husk)).ThenBy(o => o.Tile.Y).ThenBy(o => o.Tile.X)
            .Select(o => o.Tile).DefaultIfEmpty(husk).First();
}
