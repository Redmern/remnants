using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual: one figure per survivor shuttling between home (a housing building with a free bed,
/// or the town hall when homeless) and their workplace. Idle survivors loiter near home.
/// Nothing here feeds back into the sim.
/// </summary>
public partial class SurvivorWalkers : Node3D
{
    private const float WalkSpeed = 1.6f; // m per real second at 1x
    private const float PauseSeconds = 2.5f;

    private sealed class Walker
    {
        public required Node3D Node;
        public required bool Child;
        public Vector3 Home, Work;
        public bool HeadingToWork = true;
        public float Pause;
    }

    private readonly Dictionary<int, Walker> _walkers = new();
    private ColonySession _session = null!;
    private TownLayout _layout = null!;
    private ModelLibrary _models = null!;
    private MeshInstance3D _marker = null!;

    public int? SelectedSurvivorId { get; set; }

    public void Setup(ColonySession session, TownLayout layout, ModelLibrary models)
    {
        _session = session;
        _layout = layout;
        _models = models;
        _marker = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.45f, OuterRadius = 0.6f, Rings = 24 },
            MaterialOverride = Placeholders.Material(new Color(0.95f, 0.75f, 0.3f), unshaded: true),
            Visible = false,
        };
        AddChild(_marker);
        Sync();
    }

    public void Reset()
    {
        foreach (var w in _walkers.Values) w.Node.QueueFree();
        _walkers.Clear();
        Sync();
    }

    public void Sync()
    {
        var sim = _session.Sim;
        var homes = AssignHomes();
        var alive = new HashSet<int>();
        foreach (var s in sim.State.Survivors)
        {
            alive.Add(s.Id);
            if (!_walkers.TryGetValue(s.Id, out var w) || w.Child != s.IsChild)
            {
                w?.Node.QueueFree();
                var home0 = homes.GetValueOrDefault(s.Id);
                w = new Walker { Node = _models.Survivor(s), Child = s.IsChild, Home = home0, Work = home0 };
                w.Node.Position = Doorstep(home0, s.Id);
                AddChild(w.Node);
                _walkers[s.Id] = w;
            }

            w.Home = Doorstep(homes.GetValueOrDefault(s.Id), s.Id);
            w.Work = s.AssignedBuildingId is { } bid && sim.State.FindBuilding(bid) is { } work
                ? Doorstep(_layout.Center(work.Plot), s.Id * 7 + 3)
                : w.Home + Jitter(s.Id * 13 + 5, 2.5f);
        }

        foreach (var gone in _walkers.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _walkers[gone].Node.QueueFree();
            _walkers.Remove(gone);
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null) return;
        var dt = (float)delta * _session.Speed;
        foreach (var w in _walkers.Values)
        {
            if (dt <= 0) break;
            if (w.Pause > 0)
            {
                w.Pause -= dt;
                continue;
            }
            var target = w.HeadingToWork ? w.Work : w.Home;
            var to = target - w.Node.Position;
            to.Y = 0;
            var step = WalkSpeed * (w.Child ? 1.2f : 1f) * dt;
            if (to.Length() <= step)
            {
                w.Node.Position = target;
                w.HeadingToWork = !w.HeadingToWork;
                w.Pause = PauseSeconds;
                continue;
            }
            var dir = to.Normalized();
            w.Node.Position += dir * step;
            w.Node.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z), 0); // models face +Z
        }

        if (SelectedSurvivorId is { } id && _walkers.TryGetValue(id, out var sel))
        {
            _marker.Visible = true;
            _marker.Position = sel.Node.Position + new Vector3(0, 0.05f, 0);
        }
        else _marker.Visible = false;
    }

    /// <summary>Spread housed survivors over housing buildings by bed count; the homeless camp at the town hall.</summary>
    private Dictionary<int, Vector3> AssignHomes()
    {
        var sim = _session.Sim;
        var result = new Dictionary<int, Vector3>();
        var townHall = sim.State.Buildings.FirstOrDefault(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
        var camp = townHall is null ? Vector3.Zero : _layout.Center(townHall.Plot);

        var beds = sim.State.Buildings
            .Where(b => sim.Data.Buildings[b.DefId].Category == BuildingCategory.Housing)
            .OrderBy(b => b.Id)
            .SelectMany(b => Enumerable.Repeat(_layout.Center(b.Plot), sim.Data.Buildings[b.DefId].Level(b.Level).Beds))
            .GetEnumerator();
        foreach (var s in sim.State.Survivors.OrderBy(s => s.Id))
            result[s.Id] = s.HasBed && beds.MoveNext() ? beds.Current : camp;
        return result;
    }

    /// <summary>A spot on the open strip in front of a plot's building (footprints are up to 6 m of the 8 m plot).</summary>
    private static Vector3 Doorstep(Vector3 plotCenter, int seed)
    {
        var spread = Jitter(seed, 1f);
        return plotCenter + new Vector3(spread.X * 2.5f, 0, TownLayout.PlotSize * 0.42f + spread.Z * 0.25f);
    }

    /// <summary>Stable pseudo-random ground offset per survivor so figures do not stack.</summary>
    private static Vector3 Jitter(int seed, float radius)
    {
        var h = (uint)seed * 2654435761u;
        var angle = (h % 3600) / 3600f * Mathf.Tau;
        var r = radius * (0.4f + (h >> 12) % 1000 / 1000f * 0.6f);
        return new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r);
    }
}
