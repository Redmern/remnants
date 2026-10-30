using System;
using System.Collections.Generic;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Survivors;
using Remnants.Game.Colony;

namespace Remnants.Game.Hud;

/// <summary>
/// Survivor portraits rendered at runtime from the 3D survivor models: a head-and-shoulders (waist-up for a hand
/// held weapon or prosthetic arm to show) shot in a private <see cref="SubViewport"/>, one model per frame, then copied
/// into a cached texture. Callers get the texture at once (blank until rendered; it fills in place) keyed by what the
/// figure looks like, so a lost limb, a new prosthetic or growing up gets a new portrait and nothing else re-renders.
/// Lives on the tree root, so the cache survives scene changes.
/// </summary>
public partial class PortraitStudio : Node
{
    public const int Pixels = 160;

    private static PortraitStudio? _instance;
    private readonly Dictionary<string, ImageTexture> _cache = new();
    private readonly Queue<(string Key, Func<ModelLibrary, Node3D> Build, ImageTexture Target, bool Whole)> _jobs = new();
    private ModelLibrary? _models;
    private SubViewport? _viewport;
    private Node3D? _stage;
    private Camera3D? _camera;
    private (ImageTexture Target, int Frames)? _pending;

    private static PortraitStudio Instance(SceneTree tree)
    {
        if (_instance is not null && IsInstanceValid(_instance)) return _instance;
        _instance = new PortraitStudio { Name = "PortraitStudio" };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
        return _instance;
    }

    /// <summary>A colony survivor (no gear in the colony; prosthetics and missing limbs show).</summary>
    public static Texture2D For(SceneTree tree, Survivor s) =>
        Instance(tree).Get($"s{s.Id}|{Look(s.LifeStage.ToString(), s.Body)}", lib => lib.Survivor(s));

    /// <summary>A field unit with its equipment hung on (weapon in hand, armor, tool).</summary>
    public static Texture2D For(SceneTree tree, int id, bool child, Sex sex, Body body, IReadOnlyDictionary<EquipSlot, string> equipment)
    {
        var gear = string.Join(",", equipment.Values);
        return Instance(tree).Get($"u{id}|{child}|{sex}|{Look("", body)}|{gear}", lib =>
        {
            var bodyId = ModelLibrary.SurvivorId(child, sex);
            var model = lib.TryInstance(bodyId) ?? Placeholders.Survivor(child);
            SurvivorDressing.FitLimbs(lib, model, body, child);
            SurvivorDressing.AttachGear(lib, model, bodyId, equipment);
            return model;
        });
    }

    /// <summary>Any model, cached under <paramref name="key"/> (which must change when the look does).</summary>
    public static Texture2D For(SceneTree tree, string key, Func<ModelLibrary, Node3D> build) => Instance(tree).Get(key, build);

    /// <summary>A small object (a gear item with its mods) framed whole, side-on, cached under <paramref name="key"/>.</summary>
    public static Texture2D Object(SceneTree tree, string key, Func<ModelLibrary, Node3D> build) => Instance(tree).Get(key, build, whole: true);

    /// <summary>A body's look for cache keys: life stage plus each part's condition and prosthetic kind.</summary>
    public static string LookOf(Body body) => Look("", body);

    /// <summary>A stable background tint per person so identical models still read as different people.</summary>
    public static Color Backdrop(int id)
    {
        var hue = (id * 0.6180339f) % 1f;
        return Color.FromHsv(hue, 0.28f, 0.26f);
    }

    private static string Look(string stage, Body body)
    {
        var parts = new List<string>();
        foreach (var part in Enum.GetValues<BodyPart>())
        {
            var p = body[part];
            parts.Add(Colony.SurvivorDressing.LimbKey(p));
        }
        return stage + ":" + string.Join("", parts);
    }

    private ImageTexture Get(string key, Func<ModelLibrary, Node3D> build, bool whole = false)
    {
        if (_cache.TryGetValue(key, out var texture)) return texture;
        texture = ImageTexture.CreateFromImage(Image.CreateEmpty(Pixels, Pixels, false, Image.Format.Rgba8));
        _cache[key] = texture;
        _jobs.Enqueue((key, build, texture, whole));
        return texture;
    }

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(Pixels, Pixels),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_viewport);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.75f, 0.72f, 0.68f),
            AmbientLightEnergy = 0.7f,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = env });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-30, 35, 0), LightEnergy = 1.1f, LightColor = new Color(1, 0.93f, 0.82f) });
        _viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-10, -140, 0), LightEnergy = 0.5f, LightColor = new Color(0.9f, 0.55f, 0.35f) });
        _stage = new Node3D();
        _viewport.AddChild(_stage);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Current = true };
        _viewport.AddChild(_camera);
    }

    public override void _Process(double delta)
    {
        if (_viewport is null || _stage is null || _camera is null) return;
        if (_pending is { } pending)
        {
            // Let the render land before reading it back.
            if (pending.Frames > 0)
            {
                _pending = (pending.Target, pending.Frames - 1);
                return;
            }
            pending.Target.SetImage(_viewport.GetTexture().GetImage());
            foreach (var child in _stage.GetChildren()) child.QueueFree();
            _pending = null;
        }
        if (_jobs.Count == 0) return;

        var (_, build, target, whole) = _jobs.Dequeue();
        _models ??= ModelLibrary.Load();
        var model = build(_models);
        // A three-quarter turn reads better than flat front-on; an object is turned side-on, a little toward the camera.
        model.RotationDegrees = whole ? new Vector3(12, -70, 0) : new Vector3(0, -18, 0);
        _stage.AddChild(model);
        // Rigged bodies stand in their idle pose (frame 0), so a portrait matches the figure on the map.
        if (!whole) Anim.CharacterRig.PoseStill(model, "idle");
        if (whole) FrameWhole(model);
        else Frame(model);
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _pending = (target, 2);
    }

    /// <summary>Points the camera at the whole model, sized to its larger side.</summary>
    private void FrameWhole(Node3D model)
    {
        Aabb? bounds = null;
        foreach (var mesh in model.FindChildren("*", "MeshInstance3D", true, false))
            if (mesh is MeshInstance3D m && m.IsVisibleInTree())
            {
                var box = m.GlobalTransform * m.GetAabb();
                bounds = bounds is { } b ? b.Merge(box) : box;
            }
        var aabb = bounds ?? new Aabb(new Vector3(-0.2f, 0, -0.2f), new Vector3(0.4f, 0.4f, 0.4f));
        var center = aabb.GetCenter();
        _camera!.Size = Mathf.Max(0.05f, Mathf.Max(aabb.Size.X, aabb.Size.Y) * 1.12f);
        _camera.Position = center + new Vector3(0, 0, 6);
        _camera.LookAt(center, Vector3.Up);
    }

    /// <summary>Points the camera at the top half of the model, sized to its height.</summary>
    private void Frame(Node3D model)
    {
        var top = 0f;
        foreach (var mesh in model.FindChildren("*", "MeshInstance3D", true, false))
            if (mesh is MeshInstance3D m && m.IsVisibleInTree())
            {
                var box = m.GlobalTransform * m.GetAabb();
                top = Mathf.Max(top, box.End.Y);
            }
        if (top <= 0) top = 1.8f;
        var size = top * 0.5f;
        var centerY = top - size * 0.47f;
        _camera!.Size = size;
        _camera.Position = new Vector3(0, centerY + 0.05f * top, 6);
        _camera.LookAt(new Vector3(0, centerY, 0), Vector3.Up);
    }
}
