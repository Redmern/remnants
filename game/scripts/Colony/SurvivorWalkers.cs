using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual: one figure per survivor going about the day. Workers circle their workplace (a spot on each side
/// of the building, a pause, the next spot) with the odd break at home; idle adults potter about near home or sit at
/// the campfire; children play by the Town Hall; patients sit outside the Med Bay; infants stay at home. Long walks
/// follow the streets between plots. Figures keep a little apart. Nothing here feeds back into the sim.
/// </summary>
public partial class SurvivorWalkers : Node3D
{
    private const float WalkSpeed = 1.6f; // m per real second at 1x
    private const float Personal = 0.75f; // m figures keep between them
    private const float SitDrop = 0.78f;  // how far a seated figure sinks (hips to the ground)

    private enum Role { Worker, Idle, Child, Patient, Infant }

    private sealed class Walker
    {
        public required Node3D Node;
        public required string Look;
        public required RandomNumberGenerator Rng;
        public Role Role;
        public Vector3 Home, Work, WorkCenter;
        public readonly Queue<Vector3> Path = new();
        public Vector3? FaceTo;
        public float Pause;
        public int SpotsUntilBreak;
        public bool Seated;
        public Vector3 Push;
    }

    private readonly Dictionary<int, Walker> _walkers = new();
    private ColonySession _session = null!;
    private TownLayout _layout = null!;
    private ModelLibrary _models = null!;
    private MeshInstance3D _marker = null!;
    private Label3D _name = null!;
    private Vector3 _campfire, _playground;

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
        _name = new Label3D
        {
            FontSize = 32, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true, Visible = false,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color(1f, 0.9f, 0.65f),
        };
        AddChild(_name);
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
        var hall = sim.State.Buildings.FirstOrDefault(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
        var camp = hall is null ? Vector3.Zero : _layout.Center(hall.Plot);
        (_campfire, _playground) = hall is null ? (camp, camp) : _layout.Commons(hall.Plot);

        var alive = new HashSet<int>();
        foreach (var s in sim.State.Present)
        {
            alive.Add(s.Id);
            var look = Look(s);
            if (!_walkers.TryGetValue(s.Id, out var w) || w.Look != look)
            {
                var at = w?.Node.Position;
                w?.Node.QueueFree();
                var rng = new RandomNumberGenerator { Seed = (ulong)s.Id * 7919 + 17 };
                w = new Walker { Node = _models.Survivor(s), Look = look, Rng = rng };
                if (s.IsInfant) w.Node.Scale = Vector3.One * 0.7f;
                AddChild(w.Node);
                _walkers[s.Id] = w;
                w.Node.Position = at is { } was ? was with { Y = 0 } : Vector3.Inf;
            }

            var homeBuilding = s.HomeBuildingId is { } hid ? sim.State.FindBuilding(hid) : null;
            w.Home = Doorstep(homeBuilding is null ? camp : _layout.Center(homeBuilding.Plot), s.Id);
            var role = s.PatientAtBuildingId is not null ? Role.Patient
                : s.IsInfant ? Role.Infant
                : s.AssignedBuildingId is not null ? Role.Worker
                : s.IsChild ? Role.Child
                : Role.Idle;
            var workplace = (s.PatientAtBuildingId ?? s.AssignedBuildingId) is { } bid ? sim.State.FindBuilding(bid) : null;
            var center = workplace is null ? w.Home : _layout.Center(workplace.Plot);
            if (role != w.Role || center != w.WorkCenter || w.Node.Position == Vector3.Inf)
            {
                w.Role = role;
                w.WorkCenter = center;
                w.Work = role == Role.Patient ? Doorstep(center, s.Id * 7 + 3) : center;
                if (w.Node.Position == Vector3.Inf) w.Node.Position = role is Role.Worker or Role.Patient ? Spot(w) : w.Home;
                Restart(w);
            }
        }

        foreach (var gone in _walkers.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _walkers[gone].Node.QueueFree();
            _walkers.Remove(gone);
        }
    }

    /// <summary>What the figure looks like: a new model is built when it changes (grew up, lost or got a limb).</summary>
    private static string Look(Survivor s) =>
        $"{s.LifeStage}|" + string.Join(",", s.Body.Parts.OrderBy(kv => kv.Key)
            .Select(kv => $"{(int)kv.Value.Condition}{(kv.Value.ProstheticQuality < 1 ? "" : "+")}"));

    /// <summary>Drop the current errand and pick the next one right away.</summary>
    private void Restart(Walker w)
    {
        w.Path.Clear();
        w.Pause = 0;
        w.SpotsUntilBreak = w.Rng.RandiRange(6, 10);
        Stand(w);
    }

    public override void _Process(double delta)
    {
        if (_session is null) return;
        // At 4x the town would scurry; figures go at most double speed (they are not tied to ticks anyway).
        var dt = (float)delta * Mathf.Min(_session.Speed, 2);
        if (dt > 0)
        {
            Separate(dt);
            foreach (var w in _walkers.Values) Step(w, dt);
        }

        if (SelectedSurvivorId is { } id && _walkers.TryGetValue(id, out var sel))
        {
            var ground = sel.Node.Position with { Y = 0 };
            _marker.Visible = true;
            _marker.Position = ground + new Vector3(0, 0.05f, 0);
            _name.Visible = true;
            _name.Text = _session.Sim.State.FindSurvivor(id)?.Name ?? "";
            _name.Position = ground + new Vector3(0, 2.3f, 0);
        }
        else _marker.Visible = _name.Visible = false;
    }

    private void Step(Walker w, float dt)
    {
        if (w.Pause > 0)
        {
            w.Pause -= dt;
            if (w.FaceTo is { } face) Face(w, face - w.Node.Position);
            return;
        }
        if (w.Path.Count == 0)
        {
            NextErrand(w);
            return;
        }

        var target = w.Path.Peek();
        var to = target - w.Node.Position;
        to.Y = 0;
        var speed = WalkSpeed * (w.Role == Role.Child ? 1.5f : 1f);
        var step = speed * dt;
        if (to.Length() <= step)
        {
            w.Node.Position = target;
            w.Path.Dequeue();
            if (w.Path.Count == 0) Arrive(w);
            return;
        }
        var dir = to.Normalized();
        w.Node.Position += dir * step;
        Face(w, dir);
    }

    /// <summary>Plans the walk to the next spot for this figure's role.</summary>
    private void NextErrand(Walker w)
    {
        Stand(w);
        w.FaceTo = null;
        Vector3 goal;
        switch (w.Role)
        {
            case Role.Worker when w.SpotsUntilBreak <= 0:
                w.SpotsUntilBreak = w.Rng.RandiRange(6, 10);
                goal = w.Home;
                break;
            case Role.Worker:
                w.SpotsUntilBreak--;
                goal = Spot(w);
                w.FaceTo = w.WorkCenter;
                break;
            case Role.Patient:
                goal = w.Work + Scatter(w, 0.6f);
                break;
            case Role.Infant:
                goal = w.Home + Scatter(w, 0.6f);
                break;
            case Role.Child:
                goal = _playground + Scatter(w, 3f);
                break;
            default:
                if (w.Rng.Randf() < 0.45f)
                {
                    // A free place round the fire, facing it.
                    var angle = w.Rng.Randf() * Mathf.Tau;
                    goal = _campfire + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.6f;
                    w.FaceTo = _campfire;
                }
                else goal = w.Home + Scatter(w, 2.2f);
                break;
        }
        Route(w, goal);
    }

    /// <summary>At the end of a walk: how long to stay, and whether to sit.</summary>
    private void Arrive(Walker w)
    {
        var atFire = w.FaceTo == _campfire && w.Role == Role.Idle;
        w.Pause = w.Role switch
        {
            Role.Worker => w.FaceTo is null ? w.Rng.RandfRange(4f, 7f) : w.Rng.RandfRange(4f, 8f),
            Role.Child => w.Rng.RandfRange(0.3f, 1.5f),
            Role.Patient or Role.Infant => w.Rng.RandfRange(6f, 12f),
            _ => atFire ? w.Rng.RandfRange(10f, 18f) : w.Rng.RandfRange(2f, 5f),
        };
        if (atFire || w.Role == Role.Patient) Sit(w);
    }

    /// <summary>A spot on one side of the workplace building, just outside its footprint (up to 6 m of the 8 m plot).</summary>
    private Vector3 Spot(Walker w)
    {
        const float edge = TownLayout.PlotSize * 0.43f;
        var along = w.Rng.RandfRange(-edge, edge);
        var offset = w.Rng.RandiRange(0, 3) switch
        {
            0 or 1 => new Vector3(along, 0, edge), // the front is where most of the work shows
            2 => new Vector3(edge, 0, along),
            _ => new Vector3(-edge, 0, along),
        };
        return w.WorkCenter + offset;
    }

    /// <summary>
    /// Walks along the streets (the gutters between plots) when the goal is far: out to the nearest street, along it,
    /// across, and in. Short hops go straight.
    /// </summary>
    private void Route(Walker w, Vector3 goal)
    {
        w.Path.Clear();
        var from = w.Node.Position with { Y = 0 };
        if (from.DistanceTo(goal) > TownLayout.PlotSize * 0.8f && (_layout.InsideGrid(from) || _layout.InsideGrid(goal)))
        {
            var streetZ = _layout.GutterZ(from.Z);
            var streetX = _layout.GutterX(goal.X);
            var goalStreetZ = _layout.GutterZ(goal.Z);
            foreach (var waypoint in new[]
                     {
                         new Vector3(from.X, 0, streetZ), new Vector3(streetX, 0, streetZ),
                         new Vector3(streetX, 0, goalStreetZ), new Vector3(goal.X, 0, goalStreetZ),
                     })
                if (waypoint.DistanceTo(w.Path.Count > 0 ? w.Path.Last() : from) > 0.2f) w.Path.Enqueue(waypoint);
        }
        w.Path.Enqueue(goal);
    }

    /// <summary>Figures closer than <see cref="Personal"/> drift apart (paused ones too, so groups spread into rings).</summary>
    private void Separate(float dt)
    {
        var list = _walkers.Values.ToList();
        foreach (var w in list) w.Push = Vector3.Zero;
        for (var i = 0; i < list.Count; i++)
        for (var j = i + 1; j < list.Count; j++)
        {
            var d = list[j].Node.Position - list[i].Node.Position;
            d.Y = 0;
            var dist = d.Length();
            if (dist >= Personal) continue;
            var dir = dist < 0.01f ? new Vector3(Mathf.Cos(i + j), 0, Mathf.Sin(i + j)) : d / dist;
            var push = dir * (Personal - dist) * 0.5f;
            list[i].Push -= push;
            list[j].Push += push;
        }
        foreach (var w in list) w.Node.Position += w.Push.LimitLength(Mathf.Min(1f, 2f * dt));
    }

    /// <summary>Seated: legs out in front, hips on the ground. Works on prosthetic legs too (they share the node names).</summary>
    private static void Sit(Walker w)
    {
        if (w.Seated) return;
        w.Seated = true;
        foreach (var leg in Legs(w.Node)) leg.RotationDegrees = leg.RotationDegrees with { X = -90 };
        var drop = Legs(w.Node).Select(l => l.Position.Y).DefaultIfEmpty(0).Max() * (SitDrop / 0.9f);
        w.Node.Position = w.Node.Position with { Y = -drop * w.Node.Scale.Y };
    }

    private static void Stand(Walker w)
    {
        if (!w.Seated) return;
        w.Seated = false;
        foreach (var leg in Legs(w.Node)) leg.RotationDegrees = leg.RotationDegrees with { X = 0 };
        w.Node.Position = w.Node.Position with { Y = 0 };
    }

    private static IEnumerable<Node3D> Legs(Node3D model) =>
        model.FindChildren("leg_?", nameof(Node3D), true, false).OfType<Node3D>();

    private static void Face(Walker w, Vector3 dir)
    {
        if (dir.LengthSquared() < 0.0001f) return;
        w.Node.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z), 0); // models face +Z
    }

    private static Vector3 Scatter(Walker w, float radius)
    {
        var angle = w.Rng.Randf() * Mathf.Tau;
        var r = radius * Mathf.Sqrt(w.Rng.Randf());
        return new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r);
    }

    /// <summary>A stable spot per survivor on the open strip in front of a plot's building (footprints are up to 6 m of the 8 m plot).</summary>
    private static Vector3 Doorstep(Vector3 plotCenter, int seed)
    {
        var h = (uint)seed * 2654435761u;
        return plotCenter + new Vector3((h % 1000 / 1000f * 2 - 1) * 2.5f, 0, TownLayout.PlotSize * 0.42f + ((h >> 12) % 1000 / 1000f - 0.5f) * 0.5f);
    }
}
