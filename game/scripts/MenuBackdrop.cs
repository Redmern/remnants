using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Colony;

namespace Remnants.Game;

/// <summary>
/// The main menu's background: a small grown colony (its own throwaway sim, never <see cref="ColonySession.Current"/>)
/// with survivors walking about, seen by a camera that circles it slowly. Purely visual; it takes no input.
/// </summary>
public partial class MenuBackdrop : Node3D
{
    private const float DegreesPerSecond = 2.2f;

    private Camera3D _camera = null!;
    private ColonySession _session = null!;
    private Vector3 _focus;
    private float _yaw = 30;

    public override void _Ready()
    {
        var data = GameDataPaths.Load();
        _session = new ColonySession(NewGame.Create(data, 7, GameDataPaths.LoadScavenge()));
        Grow(_session);
        var layout = new TownLayout(data.Rules.Layout);
        var models = ModelLibrary.Load();

        _camera = new Camera3D { Fov = 34, Far = 400, Current = true };
        AddChild(_camera);
        var town = new TownView { Name = "Town" };
        AddChild(town);
        town.Setup(_session, layout, models, _camera);
        town.SetProcessUnhandledInput(false);
        town.SetProcessInput(false);
        var walkers = new SurvivorWalkers { Name = "Survivors" };
        AddChild(walkers);
        walkers.Setup(_session, layout, models);
        town.Sync();
        walkers.Sync();

        if (_session.Sim.State.Buildings.FirstOrDefault(b => b.DefId == data.Rules.TownHallBuildingId) is { } hall)
            _focus = layout.Center(hall.Plot) + new Vector3(TownLayout.PlotSize * 1.5f, 0, TownLayout.PlotSize);
        Place();
    }

    /// <summary>A few buildings and people at work, so the town looks lived in.</summary>
    private static void Grow(ColonySession session)
    {
        var sim = session.Sim;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 1000);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        sim.Execute(new Upgrade(hall));
        foreach (var (defId, plot) in new[] { ("farm", 8), ("woodcutter", 9), ("workshop", 14), ("med_bay", 15), ("quarry", 2), ("house", 16) })
            sim.Execute(new Construct(defId, plot));
        var jobs = sim.State.Buildings.Where(b => sim.Data.Buildings[b.DefId].Job is not null).ToList();
        var i = 0;
        foreach (var s in sim.State.Survivors.Where(s => !s.IsInfant))
            sim.Execute(new AssignJob(s.Id, jobs[i++ % jobs.Count].Id));
    }

    public override void _Process(double delta)
    {
        _yaw += (float)delta * DegreesPerSecond;
        _session.Advance(delta * 0.5); // life goes on, slowly
        Place();
    }

    private void Place()
    {
        var yaw = Mathf.DegToRad(_yaw);
        var pitch = Mathf.DegToRad(32);
        const float distance = 58;
        var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * distance;
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(-offset, Vector3.Up), _focus + offset);
    }
}
