using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony;

/// <summary>
/// Construction sites in the town (no rules; see <see cref="ConstructionSystem"/>). A new building's site shows its
/// level-1 model rising from the ground with progress (<see cref="BuildReveal"/>), a wooden scaffold that climbs ahead of
/// it, the delivered materials in piles at the front of the plot (logs, rubble for stone, crates for the rest), dust
/// while builders work, and a progress ring over it. An upgrade site wraps the standing model (drawn by
/// <see cref="TownView"/>) in a full scaffold; <see cref="Finish"/> plays the hand-over when a level is reached.
/// Everything is made in code over the existing models and props.
/// </summary>
public partial class ConstructionSites : Node3D
{
    private static readonly Color Timber = new(0.55f, 0.4f, 0.26f);
    private static readonly Color Pole = new(0.42f, 0.31f, 0.21f);
    private static readonly Color Bark = new(0.36f, 0.25f, 0.16f);
    private static readonly Color Cut = new(0.78f, 0.62f, 0.42f);

    private sealed class Entry
    {
        public Node3D Root = null!;
        public string Key = "";
        public List<ShaderMaterial> Reveal = new();
        public float Height;
        public Aabb Bounds;
        public MultiMeshInstance3D? Scaffold;
        public Node3D Piles = null!;
        public string PileKey = "";
        public Sprite3D Ring = null!;
        public Label3D Percent = null!;
        public int RingStep = -1;
        public GpuParticles3D Dust = null!;
        public float Shown;
        public float Target;
        public bool Upgrade;
    }

    private readonly Dictionary<int, Entry> _sites = new();
    private ColonySession _session = null!;
    private TownLayout _layout = null!;
    private ModelLibrary _models = null!;
    private Func<int, Node3D?> _standing = null!;

    /// <summary>Whether a site for this building id is shown (it was under construction at the last sync).</summary>
    public bool Has(int buildingId) => _sites.ContainsKey(buildingId);

    public void Setup(ColonySession session, TownLayout layout, ModelLibrary models, Func<int, Node3D?> standing)
    {
        _session = session;
        _layout = layout;
        _models = models;
        _standing = standing;
    }

    public void Sync()
    {
        var sim = _session.Sim;
        var alive = new HashSet<int>();
        foreach (var site in sim.State.Construction.Sites)
        {
            alive.Add(site.BuildingId);
            var key = $"{site.DefId}:{site.Level}:{site.Plot}";
            if (!_sites.TryGetValue(site.BuildingId, out var e) || e.Key != key)
            {
                if (e is not null) e.Root.QueueFree();
                e = Make(site, key);
                _sites[site.BuildingId] = e;
            }
            e.Target = (float)site.Progress;
            SyncPiles(e, site);
            SyncRing(e, site);
            e.Dust.Emitting = Working(sim, site);
        }
        foreach (var gone in _sites.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _sites[gone].Root.QueueFree();
            _sites.Remove(gone);
        }
    }

    /// <summary>Someone on the crew is at work there this hour (dust, sound, walkers' tools).</summary>
    public static bool Working(ColonySim sim, ConstructionSite site) =>
        !site.Paused && ConstructionSystem.Crew(sim.State, sim.Data, site).Any(c => sim.OnShift(c.Survivor));

    private Entry Make(ConstructionSite site, string key)
    {
        var sim = _session.Sim;
        var def = sim.Data.Buildings[site.DefId];
        var e = new Entry { Key = key, Upgrade = site.IsUpgrade };
        e.Root = new Node3D { Name = $"Site{site.BuildingId}", Position = _layout.Center(site.Plot) };
        AddChild(e.Root);

        if (site.IsUpgrade)
        {
            // Scaffold around the bigger of the standing model and the next level's.
            var next = _models.Building(def, site.Level);
            var bounds = Bounds(next);
            if (_standing(site.BuildingId) is { } old) bounds = bounds.Merge(Bounds(old));
            next.Free();
            e.Bounds = bounds;
            e.Height = bounds.End.Y;
            e.Scaffold = Scaffold(bounds, e.Height + 0.6f);
            e.Root.AddChild(e.Scaffold);
            e.Shown = e.Target = (float)site.Progress;
        }
        else
        {
            var model = _models.Building(def, 1);
            e.Root.AddChild(model);
            e.Bounds = Bounds(model);
            e.Height = Mathf.Max(0.5f, e.Bounds.End.Y);
            e.Reveal = BuildReveal.Apply(model);
            e.Shown = e.Target = (float)site.Progress;
            BuildReveal.Set(e.Reveal, CutAt(e, e.Shown));
            e.Scaffold = Scaffold(e.Bounds, e.Height + 0.6f);
            e.Root.AddChild(e.Scaffold);
            ScaffoldTo(e, e.Shown);
        }

        e.Piles = new Node3D { Name = "Piles" };
        e.Root.AddChild(e.Piles);
        e.Ring = new Sprite3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.012f, NoDepthTest = true,
            Position = new Vector3(0, e.Height + (e.Upgrade ? 2.4f : 1.6f), 0), Modulate = Colors.White, RenderPriority = 2,
        };
        e.Root.AddChild(e.Ring);
        e.Percent = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = e.Ring.Position, FontSize = 30, PixelSize = 0.01f,
            OutlineSize = 8, NoDepthTest = true, RenderPriority = 3,
        };
        e.Root.AddChild(e.Percent);
        e.Dust = Dust();
        e.Root.AddChild(e.Dust);
        return e;
    }

    public override void _Process(double delta)
    {
        foreach (var e in _sites.Values)
        {
            if (Mathf.IsEqualApprox(e.Shown, e.Target)) continue;
            // Glide to the new share over about a second; a raid's knock-back drops at once.
            e.Shown = e.Target < e.Shown ? e.Target : Mathf.MoveToward(e.Shown, e.Target, (float)delta * 0.6f);
            if (!e.Upgrade)
            {
                BuildReveal.Set(e.Reveal, CutAt(e, e.Shown));
                ScaffoldTo(e, e.Shown);
            }
        }
    }

    /// <summary>World height of the cut: the ground plus the share of the model's height (a little below 0 at 0%).</summary>
    private float CutAt(Entry e, float progress) => e.Root.Position.Y - 0.05f + (e.Height + 0.1f) * progress;

    /// <summary>A new building's scaffold stands a storey above what is built (all of it once the frame is up).</summary>
    private static void ScaffoldTo(Entry e, float progress)
    {
        if (e.Scaffold?.Multimesh is not { } mm) return;
        var top = Mathf.Min(e.Height + 0.6f, e.Height * progress + 1.4f);
        var bars = e.Scaffold.GetMeta("tops").AsInt32Array();
        var visible = 0;
        for (var i = 0; i < bars.Length; i++)
            if (bars[i] / 100f <= top) visible = i + 1;
        mm.VisibleInstanceCount = progress >= 0.999f ? 0 : visible;
    }

    // ---- scaffold ----

    /// <summary>
    /// Poles at the corners and every ~1.6 m along each side, ledgers every 1.2 m, a diagonal brace on each face; one
    /// multimesh of boxes, ordered bottom to top so a prefix of it is a scaffold that far up.
    /// </summary>
    public static MultiMeshInstance3D Scaffold(Aabb bounds, float height)
    {
        var pieces = new List<(Transform3D Xf, Vector3 Size, float Top)>();
        const float gap = 0.35f, poleW = 0.09f, boardH = 0.06f, boardD = 0.22f;
        float x0 = bounds.Position.X - gap, x1 = bounds.End.X + gap, z0 = bounds.Position.Z - gap, z1 = bounds.End.Z + gap;
        int Steps(float len) => Math.Max(1, (int)Math.Ceiling(len / 1.6f));
        var posts = new List<Vector2>();
        void Side(Vector2 a, Vector2 b)
        {
            var n = Steps(a.DistanceTo(b));
            for (var i = 0; i < n; i++) posts.Add(a.Lerp(b, i / (float)n));
        }
        Side(new(x0, z0), new(x1, z0));
        Side(new(x1, z0), new(x1, z1));
        Side(new(x1, z1), new(x0, z1));
        Side(new(x0, z1), new(x0, z0));

        // Poles go up in lifts, so a low scaffold is short poles.
        for (var y = 0f; y < height; y += 1.2f)
        {
            var lift = Mathf.Min(1.2f, height - y);
            foreach (var p in posts)
                pieces.Add((new Transform3D(Basis.Identity, new Vector3(p.X, y + lift / 2, p.Y)), new Vector3(poleW, lift, poleW), y + lift));
            var ledger = y + lift;
            pieces.Add((new Transform3D(Basis.Identity, new Vector3((x0 + x1) / 2, ledger, z0)), new Vector3(x1 - x0, boardH, poleW), ledger));
            pieces.Add((new Transform3D(Basis.Identity, new Vector3((x0 + x1) / 2, ledger, z1)), new Vector3(x1 - x0, boardH, poleW), ledger));
            pieces.Add((new Transform3D(Basis.Identity, new Vector3(x0, ledger, (z0 + z1) / 2)), new Vector3(poleW, boardH, z1 - z0), ledger));
            pieces.Add((new Transform3D(Basis.Identity, new Vector3(x1, ledger, (z0 + z1) / 2)), new Vector3(poleW, boardH, z1 - z0), ledger));
            // A walkway board at the front, and a brace across the front and one side of the lift.
            pieces.Add((new Transform3D(Basis.Identity, new Vector3((x0 + x1) / 2, ledger + 0.02f, z1 + boardD / 2)), new Vector3(x1 - x0, boardH, boardD), ledger));
            var w = x1 - x0;
            var diag = Mathf.Sqrt(w * w + lift * lift);
            pieces.Add((new Transform3D(new Basis(Vector3.Forward, Mathf.Atan2(lift, w)), new Vector3((x0 + x1) / 2, y + lift / 2, z1 + 0.06f)),
                new Vector3(diag, 0.05f, 0.05f), ledger));
            var d = z1 - z0;
            var side = Mathf.Sqrt(d * d + lift * lift);
            pieces.Add((new Transform3D(new Basis(Vector3.Right, -Mathf.Atan2(lift, d)), new Vector3(x1 + 0.06f, y + lift / 2, (z0 + z1) / 2)),
                new Vector3(0.05f, 0.05f, side), ledger));
        }
        pieces.Sort((a, b) => a.Top.CompareTo(b.Top));

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One }, InstanceCount = pieces.Count,
        };
        for (var i = 0; i < pieces.Count; i++)
        {
            var (xf, size, _) = pieces[i];
            mm.SetInstanceTransform(i, xf.ScaledLocal(size));
            mm.SetInstanceColor(i, size.Y > 0.5f ? Pole : Timber.Lerp(Cut, (i * 37 % 10) / 20f));
        }
        var material = Placeholders.Material(Colors.White);
        material.VertexColorUseAsAlbedo = true;
        material.Roughness = 0.95f;
        var node = new MultiMeshInstance3D { Name = "Scaffold", Multimesh = mm, MaterialOverride = material };
        node.SetMeta("tops", pieces.Select(p => (int)(p.Top * 100)).ToArray());
        return node;
    }

    // ---- materials on site ----

    /// <summary>Delivered materials as piles along the front of the plot: a pile per ~15 units, at most four per material.</summary>
    private void SyncPiles(Entry e, ConstructionSite site)
    {
        var counts = site.Delivered.Where(kv => kv.Value >= 1)
            .Select(kv => (Res: kv.Key, Piles: Math.Clamp((int)Math.Ceiling(kv.Value / 15), 1, 4)))
            .ToList();
        // Piles go as the work uses them up (none left once it is done).
        var left = 1 - (float)site.Progress * 0.85f;
        counts = counts.Select(c => (c.Res, Math.Max(c.Piles > 0 ? 1 : 0, (int)Math.Round(c.Piles * left)))).ToList();
        var key = string.Join(",", counts.Select(c => $"{c.Res}{c.Item2}"));
        if (key == e.PileKey) return;
        e.PileKey = key;
        foreach (var child in e.Piles.GetChildren()) child.QueueFree();
        var x = -TownLayout.PlotSize * 0.4f;
        var z = TownLayout.PlotSize * 0.42f;
        var i = 0;
        foreach (var (res, n) in counts)
            for (var k = 0; k < n; k++, i++)
            {
                var pile = Pile(res, i);
                pile.Position = new Vector3(x + i * 0.9f, 0, z + (i % 2) * 0.25f);
                pile.RotateY(i * 0.7f);
                e.Piles.AddChild(pile);
            }
    }

    private Node3D Pile(string res, int seed)
    {
        if (res == "wood") return Logs(seed);
        var id = res == "stone" ? "rubble" : "loot_crate";
        if (_models.TryInstance(id) is { } prop)
        {
            prop.Scale = Vector3.One * (res == "stone" ? 0.55f : 0.6f);
            return prop;
        }
        var box = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.45f, 0.5f) }, MaterialOverride = Placeholders.Material(new Color(0.45f, 0.42f, 0.38f)) };
        box.Position = new Vector3(0, 0.22f, 0);
        var n = new Node3D();
        n.AddChild(box);
        return n;
    }

    /// <summary>A small stack of logs: three on the ground, two on them, one on top.</summary>
    private static Node3D Logs(int seed)
    {
        var node = new Node3D { Name = "Logs" };
        var log = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.12f, Height = 1.2f, RadialSegments = 8 };
        var bark = Placeholders.Material(Bark.Lerp(Pole, seed % 3 * 0.3f));
        var rows = new[] { 3, 2, 1 };
        for (var r = 0; r < rows.Length; r++)
            for (var i = 0; i < rows[r]; i++)
                node.AddChild(new MeshInstance3D
                {
                    Mesh = log, MaterialOverride = bark,
                    Rotation = new Vector3(0, 0, Mathf.Pi / 2),
                    Position = new Vector3(0, 0.12f + r * 0.2f, (i - (rows[r] - 1) / 2f) * 0.24f),
                });
        return node;
    }

    // ---- progress ring ----

    private static void SyncRing(Entry e, ConstructionSite site)
    {
        var step = (int)Math.Round(site.Progress * 50);
        var stalled = site.Paused || site.Builders.Count + site.Helpers.Count == 0;
        if (stalled) step += 1000;
        if (step == e.RingStep) return;
        e.RingStep = step;
        e.Ring.Texture = Ring((float)site.Progress, stalled);
        e.Percent.Text = $"{site.Progress * 100:0}%";
        e.Percent.Modulate = stalled ? new Color(0.95f, 0.6f, 0.4f) : new Color(1f, 0.92f, 0.78f);
    }

    /// <summary>A ring: a dark track with an amber arc for the share done (rust when nobody works it).</summary>
    private static ImageTexture Ring(float share, bool stalled)
    {
        const int size = 72;
        const float outer = 33f, inner = 26f;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var fill = stalled ? new Color(0.85f, 0.45f, 0.3f) : new Color(0.95f, 0.72f, 0.3f);
        var track = new Color(0.08f, 0.07f, 0.06f, 0.75f);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = x + 0.5f - size / 2f;
            var dy = y + 0.5f - size / 2f;
            var r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > outer + 1 || r < inner - 1)
            {
                image.SetPixel(x, y, r < inner - 1 ? new Color(0, 0, 0, 0.35f) : new Color(0, 0, 0, 0));
                continue;
            }
            var edge = Mathf.Clamp(Mathf.Min(outer + 1 - r, r - inner + 1), 0, 1);
            // Clockwise from 12 o'clock.
            var angle = (Mathf.Atan2(dx, -dy) + Mathf.Tau) % Mathf.Tau / Mathf.Tau;
            var c = angle <= share ? fill : track;
            image.SetPixel(x, y, new Color(c.R, c.G, c.B, c.A * edge));
        }
        return ImageTexture.CreateFromImage(image);
    }

    // ---- dust ----

    private static GpuParticles3D Dust()
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(2.6f, 0.1f, 2.6f),
            Direction = Vector3.Up, Spread = 35, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.6f,
            Gravity = new Vector3(0, 0.05f, 0), ScaleMin = 0.4f, ScaleMax = 1.1f,
            Color = new Color(0.62f, 0.55f, 0.45f, 0.35f),
        };
        var quad = new QuadMesh { Size = new Vector2(0.5f, 0.5f) };
        var material = Placeholders.Material(new Color(0.62f, 0.55f, 0.45f, 0.3f), unshaded: true);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        material.VertexColorUseAsAlbedo = true;
        quad.Material = material;
        return new GpuParticles3D
        {
            Name = "Dust", Amount = 14, Lifetime = 2.2f, ProcessMaterial = process, DrawPass1 = quad,
            Emitting = false, Position = new Vector3(0, 0.1f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>
    /// A level reached: the old model sinks away while the new one settles in from a little lower, under a puff of dust.
    /// <paramref name="old"/> is freed when done.
    /// </summary>
    public void Finish(Node3D buildingNode, Node3D? old, Node3D model)
    {
        if (old is not null)
        {
            var parked = old.GlobalTransform;
            old.GetParent()?.RemoveChild(old);
            AddChild(old);
            old.GlobalTransform = parked;
            var fade = old.CreateTween();
            fade.TweenProperty(old, "scale", new Vector3(old.Scale.X * 1.03f, 0.02f, old.Scale.Z * 1.03f), 0.7).SetEase(Tween.EaseType.In);
            fade.TweenCallback(Callable.From(old.QueueFree));
        }
        var target = model.Scale;
        model.Scale = new Vector3(target.X, target.Y * 0.4f, target.Z);
        var grow = model.CreateTween();
        grow.TweenInterval(0.35);
        grow.TweenProperty(model, "scale", target, 0.6).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        var puff = Dust();
        puff.Amount = 40;
        puff.OneShot = true;
        puff.Explosiveness = 0.85f;
        if (puff.ProcessMaterial is ParticleProcessMaterial p)
        {
            p.InitialVelocityMin = 1.2f;
            p.InitialVelocityMax = 2.6f;
            p.Spread = 70;
            p.EmissionBoxExtents = new Vector3(3, 0.3f, 3);
        }
        buildingNode.AddChild(puff);
        puff.Emitting = true;
        var cleanup = puff.CreateTween();
        cleanup.TweenInterval(3.5);
        cleanup.TweenCallback(Callable.From(puff.QueueFree));
    }

    /// <summary>The AABB of a model's meshes in its parent's space (before it enters the tree).</summary>
    public static Aabb Bounds(Node3D model)
    {
        Aabb? box = null;
        void Visit(Node node, Transform3D parent)
        {
            var xf = node is Node3D n3 ? parent * n3.Transform : parent;
            if (node is MeshInstance3D { Mesh: { } mesh })
            {
                var b = xf * mesh.GetAabb();
                box = box is { } a ? a.Merge(b) : b;
            }
            foreach (var child in node.GetChildren()) Visit(child, xf);
        }
        Visit(model, Transform3D.Identity);
        return box ?? new Aabb(new Vector3(-2, 0, -2), new Vector3(4, 3, 4));
    }
}
