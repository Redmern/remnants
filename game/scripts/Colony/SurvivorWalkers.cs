using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Survivors;
using Remnants.Game.Anim;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual: one figure per survivor going about the day. Workers circle their workplace (a spot on each side
/// of the building, a pause, the next spot) with the odd break at home; idle adults potter about near home or sit at
/// the campfire; children play by the Town Hall; patients sit outside the Med Bay; infants stay at home. Long walks
/// follow the streets between plots. Figures keep a little apart. At night (Core's schedule) the sleepers walk home —
/// or patients back to the Med Bay — and go inside; children's earlier bedtime sends them in first; night-shift workers
/// stay at their lit workplace and the rest spend the evening round the campfire. Nothing here feeds back into the sim.
/// Rigged figures (<see cref="CharacterRig"/>) walk (children run), work with their job's loop at the workplace
/// (<see cref="ClipMap.Work"/>), sit at the fire and outside the Med Bay, and play by the Town Hall; footfalls near the
/// camera sound (<c>colony.step</c>). Playback follows the colony's speed but caps at 2x like the walking, and figures
/// far from the camera or off screen update their pose less often (animation LOD).
/// </summary>
public partial class SurvivorWalkers : Node3D
{
    private const float WalkSpeed = 1.6f; // m per real second at 1x
    private const float Personal = 0.75f; // m figures keep between them
    private const float SitDrop = 0.78f;  // how far a seated figure sinks (hips to the ground); rigless figures only
    private const float WalkStride = 2f;  // m covered by one walk cycle at clip speed 1
    private const float RunStride = 2.4f;
    private const float NearLod = 28f, FarLod = 55f; // m from the camera: full rate, every 2nd frame, every 4th beyond

    private enum Role { Worker, Idle, Child, Patient, Infant, Asleep }

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
        public bool Inside;
        public Vector3 Bed;
        public Vector3 Push;
        public CharacterRig? Rig;
        public string? Job;
        public float Gait = 1f;
    }

    private readonly Dictionary<int, Walker> _walkers = new();
    private ColonySession _session = null!;
    private TownLayout _layout = null!;
    private ModelLibrary _models = null!;
    private MeshInstance3D _marker = null!;
    private Label3D _name = null!;
    private Label3D _hoverName = null!;
    private int? _hoverId;
    private Vector2? _mouse;
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
        _name = NameLabel(Hud.Palette.Amber);
        _hoverName = NameLabel(Hud.Palette.Text);
        AddChild(_hoverName);
        AddChild(_name);
        Sync();
    }

    /// <summary>A name tag that keeps one size on screen whatever the zoom, outlined so it reads over any ground.</summary>
    private static Label3D NameLabel(Color color) => new()
    {
        Font = Hud.Fonts.BodyBold, FontSize = 30, OutlineSize = 12, OutlineModulate = new Color(0.05f, 0.045f, 0.04f, 0.9f),
        FixedSize = true, PixelSize = 0.00038f, NoDepthTest = true, Visible = false, RenderPriority = 10, OutlineRenderPriority = 9,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = color,
    };

    /// <summary>Tracks the mouse to name the figure under it (the HUD gets first pick of the event).</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m) _mouse = m.Position;
    }

    /// <summary>The walker nearest the mouse on screen, if one is close enough to be meant.</summary>
    private int? Hovered()
    {
        if (_mouse is not { } mouse || GetViewport().GetCamera3D() is not { } camera) return null;
        int? best = null;
        var bestDistance = 28f;
        foreach (var (id, w) in _walkers)
        {
            if (w.Inside) continue;
            var chest = w.Node.GlobalPosition + new Vector3(0, 0.9f, 0);
            if (camera.IsPositionBehind(chest)) continue;
            var distance = camera.UnprojectPosition(chest).DistanceTo(mouse);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = id;
            }
        }
        return best;
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
                w.Rig = CharacterRig.Attach(_models, w.Node, ModelLibrary.SurvivorId(s.IsChild, s.Sex));
                if (w.Rig is { } rig)
                {
                    var node = w.Node;
                    rig.ClipEvent += (_, evt) =>
                    {
                        if (evt == "step" && rig.UpdateEvery == 1 && node.IsVisibleInTree()) Audio.AudioManager.Fire("colony.step", node.GlobalPosition);
                    };
                }
                if (s.IsInfant) w.Node.Scale = Vector3.One * 0.7f;
                AddChild(w.Node);
                _walkers[s.Id] = w;
                w.Node.Position = at is { } was ? was with { Y = 0 } : Vector3.Inf;
            }

            var homeBuilding = s.HomeBuildingId is { } hid ? sim.State.FindBuilding(hid) : null;
            w.Home = Doorstep(homeBuilding is null ? camp : _layout.Center(homeBuilding.Plot), s.Id);
            var role = sim.IsAsleep(s) ? Role.Asleep
                : s.PatientAtBuildingId is not null ? Role.Patient
                : s.IsInfant ? Role.Infant
                : s.AssignedBuildingId is not null && sim.OnShift(s) ? Role.Worker
                : s.AssignedBuildingId is not null && !s.IsChild ? Role.Idle
                : s.IsChild ? Role.Child
                : Role.Idle;
            var workplace = (s.PatientAtBuildingId ?? s.AssignedBuildingId) is { } bid ? sim.State.FindBuilding(bid) : null;
            w.Job = workplace is not null && s.PatientAtBuildingId is null ? sim.Data.Buildings.GetValueOrDefault(workplace.DefId)?.Job : null;
            var center = workplace is null ? w.Home : _layout.Center(workplace.Plot);
            if (role != w.Role || center != w.WorkCenter || w.Node.Position == Vector3.Inf)
            {
                w.Role = role;
                w.WorkCenter = center;
                w.Work = role == Role.Patient ? Doorstep(center, s.Id * 7 + 3) : center;
                w.Bed = s.PatientAtBuildingId is not null ? Doorstep(center, s.Id * 7 + 3) : w.Home;
                if (w.Inside && role != Role.Asleep) WakeUp(w);
                if (w.Node.Position == Vector3.Inf) w.Node.Position = role is Role.Worker or Role.Patient ? Spot(w) : role == Role.Asleep ? w.Bed : w.Home;
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
            .Select(kv => SurvivorDressing.LimbKey(kv.Value)));

    /// <summary>Puts sleepers inside at once instead of walking them home (after a jump in time, e.g. debug captures).</summary>
    public void Settle()
    {
        foreach (var w in _walkers.Values.Where(w => w.Role == Role.Asleep && !w.Inside))
        {
            w.Path.Clear();
            Stand(w);
            w.Node.Position = w.Bed;
            w.Inside = true;
            w.Node.Visible = false;
            w.Pause = float.MaxValue;
        }
    }

    /// <summary>Out of the door again where they went in.</summary>
    private static void WakeUp(Walker w)
    {
        w.Inside = false;
        w.Node.Visible = true;
        w.Node.Position = w.Bed;
    }

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
        var pace = Mathf.Min(_session.Speed, 2);
        var dt = (float)delta * pace;
        if (dt > 0)
        {
            Separate(dt);
            foreach (var w in _walkers.Values) Step(w, dt);
        }
        Animate(pace);

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

        _hoverId = Hovered();
        if (_hoverId is { } hover && hover != SelectedSurvivorId && _walkers.TryGetValue(hover, out var h))
        {
            _hoverName.Visible = true;
            _hoverName.Text = _session.Sim.State.FindSurvivor(hover)?.Name ?? "";
            _hoverName.Position = (h.Node.Position with { Y = 0 }) + new Vector3(0, 2.3f, 0);
        }
        else _hoverName.Visible = false;
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
        if (w.Rig is { } rig && rig.Pick(w.Role == Role.Child ? ["run", "walk"] : ["walk"]) is { } gait)
        {
            rig.Loop(gait);
            w.Gait = speed * (rig.Info(gait)?.Length ?? 1f) / (gait == "run" ? RunStride : WalkStride);
        }
    }

    /// <summary>
    /// Clip speed and LOD per figure: the gait's own pace while walking (so feet do not skate), times the colony pace
    /// (capped at 2x, paused at 0); figures beyond <see cref="NearLod"/> / <see cref="FarLod"/> or off screen update
    /// their pose less often.
    /// </summary>
    private void Animate(float pace)
    {
        var camera = GetViewport().GetCamera3D();
        foreach (var w in _walkers.Values)
        {
            if (w.Rig is not { } rig) continue;
            rig.SpeedScale = pace * (w.Path.Count > 0 && w.Pause <= 0 ? w.Gait : 1f);
            if (camera is null || w.Inside) continue;
            var at = w.Node.GlobalPosition + Vector3.Up;
            // The ortho camera sits far back; its zoom (Size) is what makes a figure big or small on screen.
            var distance = camera.Projection == Camera3D.ProjectionType.Orthogonal ? camera.Size : camera.GlobalPosition.DistanceTo(at);
            rig.UpdateEvery = !camera.IsPositionInFrustum(at) ? 8 : distance < NearLod ? 1 : distance < FarLod ? 2 : 4;
        }
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
            case Role.Asleep when w.Node.Position.DistanceTo(w.Bed) < 0.4f:
                // At the door: in they go, until the schedule wakes them (see Sync).
                w.Inside = true;
                w.Node.Visible = false;
                w.Pause = float.MaxValue;
                return;
            case Role.Asleep:
                goal = w.Bed;
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
            Role.Child => w.Rig is null ? w.Rng.RandfRange(0.3f, 1.5f) : w.Rng.RandfRange(2.5f, 5f),
            Role.Patient or Role.Infant => w.Rng.RandfRange(6f, 12f),
            Role.Asleep => 0.3f,
            _ => atFire ? w.Rng.RandfRange(10f, 18f) : w.Rng.RandfRange(2f, 5f),
        };
        if (atFire || w.Role == Role.Patient) Sit(w);
        else if (w.Rig is { } rig)
        {
            // At the workplace: the job's loop facing the building; children play; everyone else stands about.
            var clip = w.Role switch
            {
                Role.Worker when w.FaceTo is not null => ClipMap.Work(w.Job),
                Role.Child => "child_play",
                _ => "idle",
            };
            rig.Loop(rig.Pick(clip, "idle") ?? "idle");
        }
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
        var list = _walkers.Values.Where(w => !w.Inside).ToList();
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

    /// <summary>Seated: the sit clip; rigless figures get legs out in front, hips on the ground (prosthetic legs too).</summary>
    private static void Sit(Walker w)
    {
        if (w.Seated) return;
        w.Seated = true;
        if (w.Rig is { } rig && rig.Pick("sit") is { } sit)
        {
            rig.Loop(sit);
            return;
        }
        foreach (var leg in Legs(w.Node)) leg.RotationDegrees = leg.RotationDegrees with { X = -90 };
        var drop = Legs(w.Node).Select(l => l.Position.Y).DefaultIfEmpty(0).Max() * (SitDrop / 0.9f);
        w.Node.Position = w.Node.Position with { Y = -drop * w.Node.Scale.Y };
    }

    private static void Stand(Walker w)
    {
        if (!w.Seated) return;
        w.Seated = false;
        if (w.Rig is { } rig)
        {
            rig.Loop("idle");
            return;
        }
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
