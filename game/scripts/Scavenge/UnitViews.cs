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
/// One node per unit (model, side ring, name/HP label). <see cref="Sync"/> snaps everything to Core state;
/// <see cref="Replay"/> plays a command's events step by step (moves tween, hits flash) so the player can follow
/// enemy turns. Purely visual.
/// </summary>
public partial class UnitViews : Node3D
{
    private const float MoveSeconds = 0.28f;
    private const float StrikeSeconds = 0.16f;

    private static readonly Color MineColor = new(0.95f, 0.75f, 0.3f);
    private static readonly Color RecruitColor = new(0.55f, 0.9f, 0.5f);
    private static readonly Color EnemyColor = new(0.9f, 0.3f, 0.25f);
    private static readonly Color LinkedColor = new(0.3f, 0.85f, 0.95f);
    private static readonly Color NeutralColor = new(0.85f, 0.85f, 0.85f);

    private static readonly Regex DamagePattern = new(@"for (\d+)", RegexOptions.Compiled);

    private sealed class View
    {
        public Node3D Root = null!;
        public Node3D Model = null!;
        public Node3D Pose = null!;
        public MeshInstance3D Ring = null!;
        public Label3D Label = null!;
        public string BodyKey = "";
    }

    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;
    private readonly Dictionary<int, View> _views = new();
    private StandardMaterial3D _hitFlash = null!, _healFlash = null!;

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _hitFlash = Placeholders.Material(new Color(1f, 0.15f, 0.1f, 0.65f), unshaded: true);
        _healFlash = Placeholders.Material(new Color(0.4f, 1f, 0.45f, 0.55f), unshaded: true);
    }

    public Vector3? WorldOf(int unitId) => _views.TryGetValue(unitId, out var v) ? v.Root.Position : null;

    /// <summary>Snap every unit to the current state: position, pose, fog, ring colour, label, limbs.</summary>
    public void Sync()
    {
        foreach (var unit in _presenter.State.Units)
        {
            var view = Ensure(unit);
            view.Root.Position = TileGrid.ToWorld(unit.Pos);
            view.Root.Visible = _presenter.CanSee(unit);
            view.Pose.RotationDegrees = unit.Status == UnitStatus.Downed ? new Vector3(0, 0, 80) : Vector3.Zero;
            view.Pose.Position = unit.Status == UnitStatus.Downed ? new Vector3(0, 0.3f, 0) : Vector3.Zero;
            ((StandardMaterial3D)view.Ring.MaterialOverride).AlbedoColor = RingColor(unit);
            view.Label.Text = unit.Status == UnitStatus.Downed
                ? $"{unit.Name}\nDOWN {(unit.Stabilized ? "stable" : $"{unit.BleedOut}r")}"
                : $"{unit.Name}\n{unit.Hp:0}/{unit.MaxHp:0}";
            view.Label.Modulate = unit.Status == UnitStatus.Downed ? new Color(1f, 0.55f, 0.45f) : RingColor(unit).Lightened(0.3f);

            var bodyKey = BodyKey(unit.Body);
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
            MaterialOverride = Placeholders.Material(RingColor(unit), unshaded: true),
            Scale = new Vector3(1, 0.15f, 1),
            Position = new Vector3(0, 0.12f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(ring);
        var label = new Label3D
        {
            FontSize = 24, PixelSize = 0.01f, OutlineSize = 7, NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0, unit.EnemyDefId == "linked" ? 2.6f : 2.2f, 0),
        };
        root.AddChild(label);
        AddChild(root);
        var view = new View { Root = root, Model = model, Pose = pose, Ring = ring, Label = label, BodyKey = BodyKey(unit.Body) };
        _views[unit.Id] = view;
        return view;
    }

    /// <summary>Models face +Z; turn them toward the default iso camera (south-east) so faces read.</summary>
    private static Vector3 Facing(Unit unit) => new(0, 45, 0);

    private Color RingColor(Unit u) => u switch
    {
        { Kind: UnitKind.Enemy } => u.EnemyDefId == "linked" ? LinkedColor : EnemyColor,
        _ when _presenter.IsMine(u) => u.FollowingId is null ? MineColor : RecruitColor,
        { Side: Unit.NeutralSide } => NeutralColor,
        _ => EnemyColor,
    };

    private static string BodyKey(Body body) => string.Join(",", body.Parts.OrderBy(kv => kv.Key).Select(kv => (int)kv.Value.Condition));

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
