using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Colony;

namespace Remnants.Game.Scavenge;

/// <summary>
/// One node per unit (model, side ring, name label, HP bar). Names show only for the units in focus (selected,
/// hovered, acting) or downed; the HP bar for those and for anyone hurt. <see cref="Sync"/> snaps everything to Core state;
/// <see cref="Replay"/> plays a command's events step by step (moves tween, hits flash) so the player can follow
/// enemy turns. Purely visual.
/// </summary>
public partial class UnitViews : Node3D
{
    private const float MoveSeconds = 0.28f;
    private const float StrikeSeconds = 0.16f;

    private const float BarWidth = 0.9f, BarHeight = 0.09f;

    private static readonly Regex DamagePattern = new(@"for (\d+)", RegexOptions.Compiled);

    private sealed class View
    {
        public Node3D Root = null!;
        public Node3D Model = null!;
        public Node3D Pose = null!;
        public MeshInstance3D Ring = null!;
        public Label3D Label = null!;
        public Node3D Bar = null!;
        public MeshInstance3D BarFill = null!;
        public string BodyKey = "";
    }

    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;
    private readonly Dictionary<int, View> _views = new();
    private StandardMaterial3D _hitFlash = null!, _healFlash = null!;
    private readonly HashSet<int> _focus = new();

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _hitFlash = Placeholders.Material(new Color(1f, 0.15f, 0.1f, 0.65f), unshaded: true);
        _healFlash = Placeholders.Material(new Color(0.4f, 1f, 0.45f, 0.55f), unshaded: true);
    }

    public Vector3? WorldOf(int unitId) => _views.TryGetValue(unitId, out var v) ? v.Root.Position : null;

    /// <summary>The units whose name and HP show: the selected, the hovered and the acting one.</summary>
    public void SetFocus(params int?[] unitIds)
    {
        _focus.Clear();
        foreach (var id in unitIds)
            if (id is { } i) _focus.Add(i);
        foreach (var unit in _presenter.State.Units)
            if (_views.TryGetValue(unit.Id, out var view)) UpdateLabel(view, unit);
    }

    private void UpdateLabel(View view, Unit unit)
    {
        var downed = unit.Status == UnitStatus.Downed;
        var focused = _focus.Contains(unit.Id);
        view.Label.Visible = focused || downed;
        view.Label.Text = downed ? $"{unit.Name}\nDOWN {(unit.Stabilized ? "stable" : $"{unit.BleedOut}r")}" : unit.Name;
        view.Label.Modulate = downed ? new Color(1f, 0.55f, 0.45f) : SidePalette.For(_presenter, unit).Lightened(0.3f);

        var health = unit.MaxHp > 0 ? Mathf.Clamp((float)(unit.Hp / unit.MaxHp), 0, 1) : 0;
        view.Bar.Visible = !downed && (focused || health < 1);
        var width = BarWidth * health;
        var fill = (QuadMesh)view.BarFill.Mesh;
        fill.Size = new Vector2(Mathf.Max(width, 0.001f), BarHeight);
        fill.CenterOffset = new Vector3((width - BarWidth) / 2, 0, 0);
        ((StandardMaterial3D)view.BarFill.MaterialOverride).AlbedoColor =
            health < 0.35f ? new Color(0.95f, 0.35f, 0.25f) : health < 0.7f ? new Color(0.95f, 0.8f, 0.3f) : new Color(0.5f, 0.85f, 0.4f);
    }

    /// <summary>Snap every unit to the current state: position, pose, fog, ring colour, label, limbs.</summary>
    public void Sync()
    {
        // A remote view drops units the side no longer sees; their models stay hidden until they show up again.
        var present = _presenter.State.Units.Select(u => u.Id).ToHashSet();
        foreach (var (id, stale) in _views)
            if (!present.Contains(id)) stale.Root.Visible = false;
        foreach (var unit in _presenter.State.Units)
        {
            var view = Ensure(unit);
            view.Root.Position = TileGrid.ToWorld(unit.Pos);
            view.Root.Visible = _presenter.CanSee(unit);
            view.Pose.RotationDegrees = unit.Status == UnitStatus.Downed ? new Vector3(0, 0, 80) : Vector3.Zero;
            view.Pose.Position = unit.Status == UnitStatus.Downed ? new Vector3(0, 0.3f, 0) : Vector3.Zero;
            ((StandardMaterial3D)view.Ring.MaterialOverride).AlbedoColor = SidePalette.For(_presenter, unit);
            UpdateLabel(view, unit);

            var bodyKey = BodyKey(unit);
            if (bodyKey != view.BodyKey && unit.Kind != UnitKind.Enemy)
            {
                view.Model.QueueFree();
                view.Model = _models.Unit(unit);
                view.Model.RotationDegrees = Facing(unit);
                view.Pose.AddChild(view.Model);
                view.BodyKey = bodyKey;
            }
        }
    }

    private View Ensure(Unit unit)
    {
        if (_views.TryGetValue(unit.Id, out var existing)) return existing;
        var root = new Node3D { Name = $"Unit{unit.Id}" };
        var pose = new Node3D { Name = "Pose" };
        root.AddChild(pose);
        var model = _models.Unit(unit);
        model.RotationDegrees = Facing(unit);
        pose.AddChild(model);
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.62f, OuterRadius = 0.78f, Rings = 24, RingSegments = 6 },
            MaterialOverride = Placeholders.Material(SidePalette.For(_presenter, unit), unshaded: true),
            Scale = new Vector3(1, 0.15f, 1),
            Position = new Vector3(0, 0.12f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(ring);
        var top = unit.EnemyDefId?.StartsWith("linked") == true ? 2.6f : 2.2f;
        var label = new Label3D
        {
            FontSize = 22, PixelSize = 0.01f, OutlineSize = 7, NoDepthTest = true, RenderPriority = 2, OutlineRenderPriority = 1,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, VerticalAlignment = VerticalAlignment.Bottom,
            Position = new Vector3(0, top + 0.12f, 0),
        };
        root.AddChild(label);
        var bar = new Node3D { Name = "HpBar", Position = new Vector3(0, top, 0) };
        bar.AddChild(BarQuad(new Vector2(BarWidth + 0.06f, BarHeight + 0.06f), new Color(0.05f, 0.05f, 0.05f, 0.85f), 0));
        var barFill = BarQuad(new Vector2(BarWidth, BarHeight), Colors.White, 1);
        bar.AddChild(barFill);
        root.AddChild(bar);
        AddChild(root);
        var view = new View
        {
            Root = root, Model = model, Pose = pose, Ring = ring, Label = label, Bar = bar, BarFill = barFill, BodyKey = BodyKey(unit),
        };
        _views[unit.Id] = view;
        return view;
    }

    /// <summary>Models face +Z; turn them toward the default iso camera (south-east) so faces read.</summary>
    private static Vector3 Facing(Unit unit) => new(0, 45, 0);

    /// <summary>A camera-facing quad drawn over the scene; the fill's own mesh is resized as HP changes.</summary>
    private static MeshInstance3D BarQuad(Vector2 size, Color color, int priority)
    {
        var material = Placeholders.Material(color, unshaded: true);
        material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        material.NoDepthTest = true;
        material.RenderPriority = priority;
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = size }, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>What the model shows: limb conditions (and prosthetic kind) plus worn gear; the model is rebuilt when it changes.</summary>
    private static string BodyKey(Unit unit) =>
        string.Join(",", unit.Body.Parts.OrderBy(kv => kv.Key).Select(kv => $"{(int)kv.Value.Condition}{(kv.Value.ProstheticQuality < 1 ? "" : "+")}"))
        + "|" + string.Join(",", unit.Equipment.OrderBy(kv => kv.Key).Select(kv => kv.Value));

    /// <summary>
    /// Plays events in order. Steps the local side cannot see (an enemy moving in the dark) are skipped
    /// without delay. <paramref name="visible"/> is what the side saw before or after the command.
    /// </summary>
    public async Task Replay(IReadOnlyList<ScavengeEvent> events, IReadOnlySet<int> visible, System.Action<ScavengeEvent>? onStep = null)
    {
        var map = _presenter.State.Map;
        bool Seen(Pos? p) => p is { } at && map.InBounds(at) && visible.Contains(map.Index(at));
        bool Mine(int? id) => id is { } i && _presenter.State.FindUnit(i) is { } u && u.Side == _presenter.Side;

        foreach (var e in events)
        {
            onStep?.Invoke(e);
            var actor = e.UnitId is { } a && _views.TryGetValue(a, out var av) ? av : null;
            var target = e.TargetId is { } t && _views.TryGetValue(t, out var tv) ? tv : null;
            switch (e.Type)
            {
                case ScavengeEventType.Moved when actor is not null && e.Pos is { } to:
                    var from = TileGrid.FromWorld(actor.Root.Position);
                    if (!Mine(e.UnitId) && !Seen(from) && !Seen(to))
                    {
                        actor.Root.Position = TileGrid.ToWorld(to);
                        break;
                    }
                    actor.Root.Visible = true;
                    await TweenTo(actor.Root, TileGrid.ToWorld(to), MoveSeconds);
                    if (!Mine(e.UnitId) && !Seen(to)) actor.Root.Visible = false;
                    break;

                case ScavengeEventType.Attacked or ScavengeEventType.Missed when actor is not null && target is not null:
                    if (!Seen(TileGrid.FromWorld(actor.Root.Position)) && !Seen(e.Pos) && !Mine(e.TargetId)) break;
                    var home = actor.Root.Position;
                    var lunge = home + (target.Root.Position - home).Normalized() * 0.6f;
                    await TweenTo(actor.Root, lunge, StrikeSeconds);
                    if (e.Type == ScavengeEventType.Attacked)
                    {
                        Flash(target, _hitFlash);
                        var damage = DamagePattern.Match(e.Message);
                        FloatText(target.Root.Position, damage.Success ? $"-{damage.Groups[1].Value}" : "hit", new Color(1f, 0.4f, 0.3f));
                    }
                    else FloatText(target.Root.Position, "miss", new Color(0.85f, 0.85f, 0.85f));
                    await TweenTo(actor.Root, home, StrikeSeconds);
                    await Wait(0.12f);
                    break;

                // From the fog: no one to animate, only the victim's hit.
                case ScavengeEventType.Attacked or ScavengeEventType.Missed when actor is null && target is not null && e.UnitId is null:
                    if (e.Type == ScavengeEventType.Attacked)
                    {
                        Flash(target, _hitFlash);
                        var hidden = DamagePattern.Match(e.Message);
                        FloatText(target.Root.Position, hidden.Success ? $"-{hidden.Groups[1].Value}" : "hit", new Color(1f, 0.4f, 0.3f));
                    }
                    else FloatText(target.Root.Position, "miss", new Color(0.85f, 0.85f, 0.85f));
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.LimbLost when actor is not null && Seen(e.Pos):
                    FloatText(actor.Root.Position + Vector3.Up * 0.5f, "limb lost!", new Color(1f, 0.25f, 0.2f));
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.Downed when actor is not null && (Seen(e.Pos) || Mine(e.UnitId)):
                    var tip = CreateTween();
                    tip.TweenProperty(actor.Pose, "rotation_degrees", new Vector3(0, 0, 80), 0.25);
                    await ToSignal(tip, Tween.SignalName.Finished);
                    FloatText(actor.Root.Position, "DOWN", new Color(1f, 0.5f, 0.3f));
                    break;

                case ScavengeEventType.Died or ScavengeEventType.BledOut when actor is not null && (Seen(e.Pos) || Mine(e.UnitId)):
                    FloatText(actor.Root.Position, "dead", new Color(0.7f, 0.7f, 0.7f));
                    var sink = CreateTween();
                    sink.TweenProperty(actor.Root, "scale", new Vector3(1, 0.05f, 1), 0.3);
                    await ToSignal(sink, Tween.SignalName.Finished);
                    actor.Root.Visible = false;
                    actor.Root.Scale = Vector3.One;
                    break;

                case ScavengeEventType.Extracted or ScavengeEventType.Evacuated when actor is not null:
                    FloatText(actor.Root.Position, e.Type == ScavengeEventType.Extracted ? "extracted" : "evacuated", new Color(0.6f, 1f, 0.6f));
                    await TweenTo(actor.Root, actor.Root.Position + Vector3.Up * 3, 0.35f);
                    actor.Root.Visible = false;
                    break;

                case ScavengeEventType.Healed or ScavengeEventType.Stabilized or ScavengeEventType.Revived when target is not null:
                    Flash(target, _healFlash);
                    FloatText(target.Root.Position, e.Type.ToString().ToLowerInvariant(), new Color(0.5f, 1f, 0.5f));
                    await Wait(0.3f);
                    break;
            }
        }
    }

    private async Task TweenTo(Node3D node, Vector3 to, float seconds)
    {
        var tween = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "position", to, seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task Wait(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    /// <summary>Tints every mesh of the unit for a moment.</summary>
    private void Flash(View view, Material overlay)
    {
        var meshes = view.Model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList();
        foreach (var m in meshes) m.MaterialOverlay = overlay;
        GetTree().CreateTimer(0.25).Timeout += () =>
        {
            foreach (var m in meshes.Where(GodotObject.IsInstanceValid)) m.MaterialOverlay = null;
        };
    }

    private void FloatText(Vector3 at, string text, Color color)
    {
        var label = new Label3D
        {
            Text = text, FontSize = 48, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true, Modulate = color,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = at + new Vector3(0, 2.4f, 0),
        };
        AddChild(label);
        var tween = CreateTween().SetParallel();
        tween.TweenProperty(label, "position", label.Position + Vector3.Up * 1.2f, 0.9);
        tween.TweenProperty(label, "modulate:a", 0f, 0.9).SetDelay(0.3);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}
