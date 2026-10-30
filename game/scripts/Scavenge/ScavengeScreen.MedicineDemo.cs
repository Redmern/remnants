using System.Linq;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Debug: <c>--scavenge --medicine-demo</c>. The acting survivor's neighbour goes down (a broken leg, bleeding out), the
/// acting one carries a blood bag and revives them with it; the revived survivor is selected so the card shows the HP and
/// the fracture, and the log the revive. With <c>--screenshot=PATH</c> the moment is saved.
/// </summary>
public partial class ScavengeScreen
{
    public static bool MedicineDemoRequested => DebugCapture.HasFlag("--medicine-demo");

    private async Task MedicineDemoScript()
    {
        await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sim = _presenter.Sim;
        if (_presenter.ActingUnit is not { } medic) return;
        var hurt = sim.State.Units.FirstOrDefault(u => u.Id != medic.Id && u.PlayerId == medic.PlayerId && u.IsActive && u.Kind == UnitKind.Survivor);
        if (hurt is null) return;
        // Debug only: put them side by side, break a leg and drop the friend to 0 HP.
        var free = Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3).Select(dy => new Pos(medic.Pos.X + dx, medic.Pos.Y + dy)))
            .FirstOrDefault(p => p != medic.Pos && sim.State.Map.InBounds(p) && sim.Data.Terrain(sim.State.Map[p]).Passable && sim.State.UnitAt(p) is null);
        if (free != default) hurt.Pos = free;
        if (!medic.Pack.Contains("blood_bag")) medic.Pack.Add("blood_bag");
        if (sim.Data.Medicine is { } med)
        {
            hurt.Body.Injure(BodyPart.LeftLeg, 0.55);
            Treatment.Fracture(med, hurt.Body, BodyPart.LeftLeg);
        }
        Combat.ApplyDamage(sim, hurt, BodyPart.Torso, hurt.Hp + 5);
        _units.Sync();
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        await Run(new UseItem(_presenter.PlayerId, medic.Id, "blood_bag", hurt.Id));
        while (_busy) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Select(hurt.Id);
        _camera.GlideTo(TileGrid.ToWorld(hurt.Pos));
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        if (DebugCapture.Value("--screenshot") is { Length: > 0 } path)
        {
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved to {path}" : $"Screenshot failed: {error}");
            if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
        }
    }
}
