using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Anim;
using Remnants.Game.Colony;

namespace Remnants.Game.Scavenge;

/// <summary>
/// One node per unit (model, side ring). Names, HP bars and pop-ups are HUD chips (<see cref="Ui.WorldTags"/>, fed by
/// <see cref="Floated"/>) so they stack instead of overlapping. <see cref="Sync"/> snaps everything to Core state;
/// <see cref="Replay"/> plays a command's events step by step so the player can follow enemy turns. Rigged models
/// (<see cref="CharacterRig"/>) walk their tile path facing the way they go, attack with their weapon's clip and take the
/// hit, pop-up and sound on its <c>impact</c> / <c>fire</c> event, react, go down, die, hold their stance loop
/// (overwatch_aim, crouch_brace, downed_loop, hover), hack, throw flares, use items and carry the downed on their back.
/// Static models fall back to tweens. Models turn to the way Core says the unit faces. A tile walk takes
/// <see cref="Hud.GameSettings.WalkTileSeconds"/>; with <see cref="Hud.GameSettings.SkipAnimations"/> every step snaps to its
/// end in the same frame, and steps of others the side sees nothing of are always instant. Purely visual.
/// </summary>
public partial class UnitViews : Node3D
{
    private const float MoveSeconds = 0.2f;
    private const float StrikeSeconds = 0.16f;
    private const float TurnSeconds = 0.18f;    // a unit turning on the spot (a strike readied, Core's facing synced)
    private const float WalkStride = 2f;        // m per walk cycle at speed 1 (two footfalls)
    private const float RunStride = 2.4f;

    private static readonly Regex DamagePattern = new(@"for (\d+)", RegexOptions.Compiled);

    private static readonly Color BlockColor = new(0.45f, 0.75f, 1f);
    private static readonly Color PerfectColor = new(1f, 0.85f, 0.3f);
    private static readonly Color StunColor = new(1f, 0.9f, 0.2f);
    private static readonly Color LowStaminaColor = new(0.35f, 0.5f, 1f);

    private sealed class View
    {
        public int Id;
        public bool Low;
        public Node3D Root = null!;
        public Node3D Model = null!;
        public Node3D Pose = null!;
        public MeshInstance3D Ring = null!;
        public CharacterRig? Rig;
        public float Yaw = 45;
        public string BodyKey = "";
    }

    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;
    private readonly Dictionary<int, View> _views = new();
    private StandardMaterial3D _hitFlash = null!, _healFlash = null!, _networkFlash = null!, _whiffFlash = null!, _blockFlash = null!, _perfectFlash = null!, _stunFlash = null!, _lowStamina = null!;

    /// <summary>The screen's feel (juiced pops, hit-stop); null plays the plain replay.</summary>
    public ScavengeJuice? Juice { get; set; }

    /// <summary>A pop-up for the HUD: where (unit position), what, which colour, and whether it is a big one.</summary>
    public event System.Action<Vector3, string, Color, bool>? Floated;

    /// <summary>A step of a resolve's playback begins, in sight: the camera goes there.</summary>
    public event System.Action<ScavengeEvent>? StepStarted;

    /// <summary>An action's pattern tiles flash: green if it landed, else red.</summary>
    public event System.Action<List<Pos>, bool>? TilesFlashed;

    /// <summary>A unit's adrenaline went up (true) or down: its meter pulses.</summary>
    public event System.Action<int, bool>? AdrenalinePulsed;

    /// <summary>A footfall of a unit (from its walk clip, or one per tile without a rig), for the step sounds.</summary>
    public event System.Action<int, Vector3>? Footstep;

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _hitFlash = Placeholders.Material(new Color(1f, 0.15f, 0.1f, 0.65f), unshaded: true);
        _healFlash = Placeholders.Material(new Color(0.4f, 1f, 0.45f, 0.55f), unshaded: true);
        _whiffFlash = Placeholders.Material(new Color(0.55f, 0.55f, 0.55f, 0.6f), unshaded: true);
        _blockFlash = Placeholders.Material(new Color(BlockColor, 0.6f), unshaded: true);
        _perfectFlash = Placeholders.Material(new Color(PerfectColor, 0.8f), unshaded: true);
        _stunFlash = Placeholders.Material(new Color(StunColor, 0.65f), unshaded: true);
        _lowStamina = Placeholders.Material(new Color(LowStaminaColor, 0.14f), unshaded: true);
        _networkFlash = Placeholders.Material(new Color(0.3f, 0.9f, 1f, 0.6f), unshaded: true);
    }

    public Vector3? WorldOf(int unitId) => _views.TryGetValue(unitId, out var v) ? v.Root.Position : null;

    /// <summary>The rig of a unit's model, if it is rigged (for debug captures).</summary>
    public CharacterRig? RigOf(int unitId) => _views.GetValueOrDefault(unitId)?.Rig;

    /// <summary>Snap every unit to the current state: position, hold pose, fog, ring colour, label, limbs.</summary>
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
            ((StandardMaterial3D)view.Ring.MaterialOverride).AlbedoColor = SidePalette.For(_presenter, unit);
            view.Ring.Visible = unit.CarriedById is null;

            var bodyKey = BodyKey(unit);
            if (bodyKey != view.BodyKey && unit.Kind != UnitKind.Enemy)
            {
                view.Model.QueueFree();
                Build(view, unit);
                view.BodyKey = bodyKey;
                view.Low = false;
            }
            Hold(view, unit);
            FaceCore(view, unit);
            SyncStamina(view, unit);
            if (view.Low) ((StandardMaterial3D)view.Ring.MaterialOverride).AlbedoColor = LowStaminaColor;
        }
    }

    /// <summary>Turns the model to where Core says the unit faces, if it does not already (a strike readied).</summary>
    private void FaceCore(View view, Unit unit)
    {
        if (unit.Facing is not { } f || f == default || unit.CarriedById is not null) return;
        var toward = TileGrid.ToWorld(unit.Pos + f) - TileGrid.ToWorld(unit.Pos);
        var yaw = Mathf.RadToDeg(Mathf.Atan2(toward.X, toward.Z));
        if (Mathf.Abs(Mathf.Wrap(yaw - view.Yaw, -180f, 180f)) < 1f) return;
        Face(view, view.Root.Position + toward, Fast ? 0.01f : TurnSeconds);
    }

    /// <summary>
    /// Whether the side saw anything of a replayed event: it is about one of the side's own units (actor or target), or the
    /// event's tile, the actor's or the target's was in sight (<paramref name="visible"/>: before or after the command). An
    /// event about no unit counts as seen.
    /// </summary>
    public bool InSight(ScavengeEvent e, IReadOnlySet<int> visible) =>
        InSight(e, visible, e.UnitId is { } a ? _views.GetValueOrDefault(a) : null, e.TargetId is { } t ? _views.GetValueOrDefault(t) : null);

    private bool InSight(ScavengeEvent e, IReadOnlySet<int> visible, View? actor, View? target) =>
        ReplayPacing.InSight(_presenter.State, _presenter.Side, e, visible,
            actor is null ? null : TileGrid.FromWorld(actor.Root.Position), target is null ? null : TileGrid.FromWorld(target.Root.Position));

    /// <summary>Replays snap to their end (<see cref="Hud.GameSettings.SkipAnimations"/>): read at every step, so the hotkey works mid-replay.</summary>
    private static bool Fast => Hud.GameSettings.SkipAnimations;

    /// <summary>The pose a unit keeps between actions: its stance loop on a rig, else the old tipped-over capsule when downed.</summary>
    private void Hold(View view, Unit unit)
    {
        var downed = unit.Status == UnitStatus.Downed;
        if (view.Rig is { } rig)
        {
            view.Pose.RotationDegrees = Vector3.Zero;
            view.Pose.Position = Vector3.Zero;
            if (rig.Pick(ClipMap.Hold(unit)) is { } hold && (rig.Base != hold || rig.Current != "death")) rig.Loop(hold, interrupt: false);
        }
        else
        {
            view.Pose.RotationDegrees = downed ? new Vector3(0, 0, 80) : Vector3.Zero;
            view.Pose.Position = downed ? new Vector3(0, 0.3f, 0) : Vector3.Zero;
        }
    }

    private View Ensure(Unit unit)
    {
        if (_views.TryGetValue(unit.Id, out var existing)) return existing;
        var root = new Node3D { Name = $"Unit{unit.Id}" };
        var pose = new Node3D { Name = "Pose" };
        root.AddChild(pose);
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.62f, OuterRadius = 0.78f, Rings = 24, RingSegments = 6 },
            MaterialOverride = Placeholders.Material(SidePalette.For(_presenter, unit), unshaded: true),
            Scale = new Vector3(1, 0.15f, 1),
            Position = new Vector3(0, 0.12f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(ring);
        AddChild(root);
        var view = new View { Id = unit.Id, Root = root, Pose = pose, Ring = ring, BodyKey = BodyKey(unit) };
        Build(view, unit);
        _views[unit.Id] = view;
        return view;
    }

    /// <summary>(Re)builds the model and its rig; the facing carries over.</summary>
    private void Build(View view, Unit unit)
    {
        view.Model = _models.Unit(unit, unit.Side != Unit.EnemySide);
        view.Model.RotationDegrees = new Vector3(0, view.Yaw, 0);
        view.Pose.AddChild(view.Model);
        view.Rig = CharacterRig.Of(view.Model);
        if (view.Rig is { } rig)
        {
            var id = view.Id;
            rig.ClipEvent += (_, name) =>
            {
                if (name == "step" && _views.TryGetValue(id, out var v) && v.Root.Visible) Footstep?.Invoke(id, v.Root.GlobalPosition);
                // Linked deaths: the cyan Network collapse flickers on the clip's flicker beats.
                if (name == "flicker" && _views.TryGetValue(id, out var f)) Flash(f, _networkFlash, 0.08f);
            };
        }
    }

    /// <summary>A unit of the side low on stamina is tinted, with a word and a sound as it gets there. A rival's stamina is its secret.</summary>
    private void SyncStamina(View view, Unit unit)
    {
        var low = unit.IsActive && unit.Side == _presenter.Side && unit.Kind == UnitKind.Survivor && ReplayFeedback.LowStamina(_presenter.Sim, unit);
        if (low == view.Low) return;
        view.Low = low;
        foreach (var m in view.Model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()) m.MaterialOverlay = low ? _lowStamina : null;
        if (!low || !view.Root.Visible) return;
        Floated?.Invoke(view.Root.Position, "Winded", LowStaminaColor, false);
        Audio.AudioManager.Fire(ReplayFeedback.LowStaminaSound, view.Root.Position + Vector3.Up);
    }

    /// <summary>What the model shows: limb conditions (and prosthetic kind) plus worn gear; the model is rebuilt when it changes.</summary>
    private static string BodyKey(Unit unit) => unit.Side + "|" +
        string.Join(",", unit.Body.Parts.OrderBy(kv => kv.Key).Select(kv => SurvivorDressing.LimbKey(kv.Value)))
        + "|" + string.Join(",", unit.Equipment.OrderBy(kv => kv.Key).Select(kv => kv.Value));

    /// <summary>
    /// Plays events in order. Steps the local side cannot see (an enemy moving in the dark) are skipped
    /// without delay. <paramref name="visible"/> is what the side saw before or after the command.
    /// <paramref name="onStep"/> runs at each event's beat: at the start of most, at the impact of an attack; it is told
    /// whether the side saw the step (<see cref="InSight"/>). An event of another side's unit the side saw nothing of (not
    /// the actor, not the target, not the tile) plays at once: no walk, no wait.
    /// </summary>
    public async Task Replay(IReadOnlyList<ScavengeEvent> events, IReadOnlySet<int> visible, System.Action<ScavengeEvent, bool>? onStep = null)
    {
        var map = _presenter.State.Map;
        bool Seen(Pos? p) => p is { } at && map.InBounds(at) && visible.Contains(map.Index(at));
        bool Mine(int? id) => id is { } i && _presenter.State.FindUnit(i) is { } u && u.Side == _presenter.Side;

        var steps = ReplayPacing.StepStarts(events).ToHashSet();
        var index = -1;
        ScavengeEvent? previous = null;
        foreach (var e in events)
        {
            index++;
            var actor = e.UnitId is { } a && _views.TryGetValue(a, out var av) ? av : null;
            var target = e.TargetId is { } t && _views.TryGetValue(t, out var tv) ? tv : null;
            var unit = e.UnitId is { } uid ? _presenter.State.FindUnit(uid) : null;
            var seen = InSight(e, visible, actor, target);
            var stepped = false;
            void Beat()
            {
                if (stepped) return;
                stepped = true;
                onStep?.Invoke(e, seen);
            }

            if (!seen)
            {
                // Out of sight: put a moving model where it went (one footfall heard), and nothing else holds the replay.
                if (e.Type is ScavengeEventType.Moved or ScavengeEventType.DroneFlown && actor is not null && e.Pos is { } went)
                {
                    actor.Root.Position = TileGrid.ToWorld(went);
                    if (e.Type == ScavengeEventType.Moved) Footstep?.Invoke(actor.Id, actor.Root.Position);
                }
                if (e.Type == ScavengeEventType.Displaced && target is not null && e.Pos is { } thrown)
                    await Displace(actor, target, thrown, e.TargetId is { } moved ? _presenter.State.FindUnit(moved) : null, snap: true);
                Beat();
                previous = e;
                continue;
            }

            if (steps.Contains(index) && !Fast)
            {
                StepStarted?.Invoke(e);
                await Wait((float)ReplayPacing.StepPause);
            }

            var outcome = ReplayFeedback.Of(e);
            switch (e.Type)
            {
                case ScavengeEventType.Moved when actor is not null && e.Pos is { } dashed && outcome is { Kind: OutcomeKind.Dash }:
                    Beat();
                    actor.Root.Visible = true;
                    FloatText(actor.Root.Position, outcome.Text, Hud.Palette.Amber);
                    await Dash(actor, unit, dashed);
                    break;

                // The defended tile rings; a perfect block rings wider, brighter and holds longer.
                case ScavengeEventType.Blocked when outcome is not null && (target ?? actor) is { } guarded:
                    Beat();
                    var perfect = outcome.Kind == OutcomeKind.PerfectBlock;
                    Flash(guarded, perfect ? _perfectFlash : _blockFlash, perfect ? 0.45f : 0.25f);
                    Burst(guarded.Root.Position, perfect ? PerfectColor : BlockColor, perfect ? 2.6f : 1.5f, perfect ? 0.5f : 0.3f);
                    FloatText(guarded.Root.Position, outcome.Text, perfect ? PerfectColor : BlockColor);
                    await Wait(perfect ? 0.45f : 0.25f);
                    break;

                // The unit reels; WorldTags keeps the icon on it while the stun lasts.
                case ScavengeEventType.Stunned when outcome is not null && ReplayFeedback.VictimOf(e) is { } stunnedId && _views.TryGetValue(stunnedId, out var stunned):
                    Beat();
                    Flash(stunned, _stunFlash, 0.35f);
                    Burst(stunned.Root.Position + Vector3.Up * 1.9f, StunColor, 0.8f, 0.4f);
                    FloatText(stunned.Root.Position, outcome.Text, StunColor);
                    await Reel(stunned);
                    break;

                case ScavengeEventType.Collided when outcome is not null && target is not null:
                    Beat();
                    Flash(target, _hitFlash, 0.2f);
                    Burst(target.Root.Position, Hud.Palette.Warn, 1.6f, 0.25f);
                    FloatText(target.Root.Position, outcome.Text, Hud.Palette.Warn);
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.IntentFizzled when outcome is not null && actor is not null:
                    Beat();
                    Flash(actor, _whiffFlash, 0.3f);
                    FloatText(actor.Root.Position, outcome.Text, Hud.Palette.TextMuted);
                    await Wait(0.2f);
                    break;

                case ScavengeEventType.AdrenalineChanged when outcome is not null && actor is not null:
                    Beat();
                    AdrenalinePulsed?.Invoke(actor.Id, outcome.Kind == OutcomeKind.AdrenalineGain);
                    // Above the meter, which it would cover.
                    Floated?.Invoke(actor.Root.Position + Vector3.Up * 0.7f, outcome.Text, outcome.Kind == OutcomeKind.AdrenalineGain ? Hud.Palette.Amber : Hud.Palette.TextMuted, false);
                    break;

                case ScavengeEventType.PatternResolved when e.Tiles is { } flashed:
                    Beat();
                    if (!Fast) TilesFlashed?.Invoke(flashed, e.Landed);
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.Moved or ScavengeEventType.DroneFlown when actor is not null && e.Pos is { } to:
                    Beat();
                    var from = TileGrid.FromWorld(actor.Root.Position);
                    actor.Root.Visible = true;
                    await Walk(actor, unit, from, to);
                    if (!Mine(e.UnitId) && !Seen(to)) actor.Root.Visible = false;
                    break;

                // A card shoved, dragged, swapped or spun a unit: it slides to where it lands (snaps when skipping).
                case ScavengeEventType.Displaced when target is not null && e.Pos is { } landing:
                    Beat();
                    target.Root.Visible = true;
                    await Displace(actor, target, landing, e.TargetId is { } movedId ? _presenter.State.FindUnit(movedId) : null, snap: false);
                    if (outcome is not null) FloatText(target.Root.Position, outcome.Text, Hud.Palette.Warn);
                    if (Juice is { } shoveJuice) await shoveJuice.HitStop(e, seen: true);
                    if (!Mine(e.TargetId) && !Seen(landing)) target.Root.Visible = false;
                    break;

                case ScavengeEventType.Attacked or ScavengeEventType.Missed when actor is not null && target is not null:
                    // A splash or burst logs one event per target: the swing plays once, the others only land.
                    var followUp = previous is { Type: ScavengeEventType.Attacked or ScavengeEventType.Missed } p && p.UnitId == e.UnitId;
                    await Strike(actor, unit, target, e, followUp, Beat);
                    break;

                // From the fog: no one to animate, only the victim's hit.
                case ScavengeEventType.Attacked or ScavengeEventType.Missed when actor is null && target is not null && e.UnitId is null:
                    Beat();
                    Land(target, e);
                    if (Juice is { } fogJuice) await fogJuice.HitStop(e, seen: false);
                    await Wait(0.3f);
                    break;

                // A strike readied in sight: the unit turns to where it means to strike, so the telegraph reads in its stance.
                case ScavengeEventType.IntentDeclared when actor is not null && unit is not null && !Mine(e.UnitId):
                    Beat();
                    FaceCore(actor, unit);
                    await Wait(TurnSeconds);
                    break;

                case ScavengeEventType.LimbLost when actor is not null && Seen(e.Pos):
                    Beat();
                    FloatText(actor.Root.Position + Vector3.Up * 0.5f, "limb lost!", Hud.Palette.Danger);
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.Downed when actor is not null && (Seen(e.Pos) || Mine(e.UnitId)):
                    Beat();
                    if (actor.Rig is { } downRig && downRig.Once("downed", then: downRig.Pick("downed_loop", "idle")))
                        await Wait(downRig.EventDelay("downed", "ground") is > 0 and var ground ? ground : 0.5f);
                    else
                    {
                        var tip = CreateTween();
                        tip.TweenProperty(actor.Pose, "rotation_degrees", new Vector3(0, 0, 80), 0.25);
                        await Finish(tip);
                    }
                    FloatText(actor.Root.Position, "DOWN", Hud.Palette.Danger);
                    break;

                case ScavengeEventType.Died or ScavengeEventType.BledOut when actor is not null && (Seen(e.Pos) || Mine(e.UnitId)):
                    Beat();
                    if (Juice is { } killJuice) await killJuice.HitStop(e, seen: true);
                    FloatText(actor.Root.Position, "dead", Hud.Palette.TextMuted);
                    if (actor.Rig is { } deathRig && deathRig.Once("death", hold: true))
                    {
                        // The body falls, lies a moment, then sinks away.
                        await Wait((deathRig.Info("death")?.Length ?? 1f) + 0.5f);
                    }
                    var sink = CreateTween();
                    sink.TweenProperty(actor.Root, "scale", new Vector3(1, 0.05f, 1), 0.3);
                    await Finish(sink);
                    actor.Root.Visible = false;
                    actor.Root.Scale = Vector3.One;
                    break;

                case ScavengeEventType.Extracted or ScavengeEventType.Evacuated when actor is not null:
                    Beat();
                    FloatText(actor.Root.Position, e.Type == ScavengeEventType.Extracted ? "extracted" : "evacuated", Hud.Palette.Good);
                    await TweenTo(actor.Root, actor.Root.Position + Vector3.Up * 3, 0.35f);
                    actor.Root.Visible = false;
                    break;

                case ScavengeEventType.Lookout when actor is not null && Seen(e.Pos):
                    Beat();
                    if (actor.Rig?.Once("lookout") == true) await Wait(0.8f);
                    break;

                case ScavengeEventType.HackProgress or ScavengeEventType.Hacked or ScavengeEventType.HackFailed or ScavengeEventType.Overridden
                    when actor is not null && e.Side is null && (Seen(e.Pos) || Mine(e.UnitId)):
                    // Kneel and type at the device, then the result.
                    if (e.Pos is { } device) Face(actor, TileGrid.ToWorld(device));
                    if (actor.Rig is { } hackRig && hackRig.Has("hack") && previous?.UnitId != e.UnitId)
                    {
                        hackRig.Loop("hack");
                        await Wait(0.9f);
                        if (unit is not null && hackRig.Pick(ClipMap.Hold(unit)) is { } after) hackRig.Loop(after);
                    }
                    Beat();
                    if (e.Type is ScavengeEventType.Hacked or ScavengeEventType.Overridden && e.Pos is { } doneAt)
                        FloatText(TileGrid.ToWorld(doneAt), e.Type == ScavengeEventType.Hacked ? "HACKED" : "OVERRIDDEN", Hud.Palette.Amber);
                    else if (e.Type == ScavengeEventType.HackFailed && e.Pos is { } failedAt)
                        FloatText(TileGrid.ToWorld(failedAt), "ALARM!", Hud.Palette.Danger);
                    if (e.Type != ScavengeEventType.HackProgress) await Wait(0.3f);
                    break;

                case ScavengeEventType.Hacked or ScavengeEventType.Overridden when e.Side is null && e.Pos is { } hackedAt && (Seen(hackedAt) || Mine(e.UnitId)):
                    Beat();
                    FloatText(TileGrid.ToWorld(hackedAt), e.Type == ScavengeEventType.Hacked ? "HACKED" : "OVERRIDDEN", Hud.Palette.Amber);
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.HackFailed when e.Pos is { } alarmAt && (Seen(alarmAt) || Mine(e.UnitId)):
                    Beat();
                    FloatText(TileGrid.ToWorld(alarmAt), "ALARM!", Hud.Palette.Danger);
                    await Wait(0.3f);
                    break;

                // Thrown onto a tile (a flare) or set down (a sensor, the scout drone).
                case ScavengeEventType.FlareLit or ScavengeEventType.SensorPlaced or ScavengeEventType.DroneDeployed
                    when actor is not null && (Seen(e.Pos) || Mine(e.UnitId)):
                    if (e.Pos is { } placed) Face(actor, TileGrid.ToWorld(placed));
                    var (clip, beat) = e.Type == ScavengeEventType.FlareLit ? ("throw", "release") : ("use_item", "apply");
                    if (actor.Rig is { } useRig && useRig.Once(clip)) await Wait(useRig.EventDelay(clip, beat));
                    Beat();
                    break;

                case ScavengeEventType.Healed or ScavengeEventType.Stabilized or ScavengeEventType.Revived when target is not null:
                    if (actor is not null && actor != target)
                    {
                        Face(actor, target.Root.Position);
                        if (actor.Rig is { } medRig && medRig.Once("use_item")) await Wait(medRig.EventDelay("use_item", "apply"));
                    }
                    else if (actor?.Rig is { } selfRig && selfRig.Once("use_item")) await Wait(selfRig.EventDelay("use_item", "apply"));
                    Beat();
                    Flash(target, _healFlash);
                    if (Juice is { } healJuice) healJuice.Pop(target.Root.Position, e.Type.ToString().ToLowerInvariant(), Hud.Palette.Good, 0);
                    else FloatText(target.Root.Position, e.Type.ToString().ToLowerInvariant(), Hud.Palette.Good);
                    if (e.Type == ScavengeEventType.Revived && target.Rig is { } upRig && upRig.Pick("idle") is { } idle) upRig.Loop(idle);
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.PickedUp when actor is not null && Seen(e.Pos):
                    Beat();
                    if (actor.Rig?.Once("use_item") == true) await Wait(0.4f);
                    break;

                case ScavengeEventType.PickedUpBody or ScavengeEventType.PutDown when actor is not null:
                    Beat();
                    if (target is not null) Face(actor, target.Root.Position);
                    if (actor.Rig?.Once("use_item") == true) await Wait(0.5f);
                    break;

                // The first EmpBurst line is the blast (its follow-up names what it knocked out).
                case ScavengeEventType.EmpBurst when e.Pos is { } empAt && (Seen(empAt) || Mine(e.UnitId))
                                                     && previous is not { Type: ScavengeEventType.EmpBurst }:
                    if (actor is not null && actor.Root.Visible)
                    {
                        Face(actor, TileGrid.ToWorld(empAt));
                        if (actor.Rig is { } throwRig && throwRig.Once("throw")) await Wait(throwRig.EventDelay("throw", "release"));
                    }
                    Beat();
                    EmpRing(TileGrid.ToWorld(empAt), EmpRadius());
                    FloatText(TileGrid.ToWorld(empAt), "EMP", Hud.Palette.Network);
                    await Wait(0.6f);
                    break;

                case ScavengeEventType.DecoyDeployed when actor is not null && (Seen(e.Pos) || Mine(e.UnitId)):
                    Beat();
                    actor.Root.Visible = true;
                    Flash(actor, _networkFlash, 0.3f);
                    FloatText(actor.Root.Position, "decoy", Hud.Palette.Network);
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.DecoyGone when actor is not null:
                    Beat();
                    var fade = CreateTween();
                    fade.TweenProperty(actor.Root, "scale", new Vector3(0.05f, 1.4f, 0.05f), 0.3);
                    await Finish(fade);
                    actor.Root.Visible = false;
                    actor.Root.Scale = Vector3.One;
                    break;

                case ScavengeEventType.DroneHealed when target is not null:
                    if (actor is not null) Face(actor, target.Root.Position);
                    Beat();
                    Flash(target, _healFlash);
                    FloatText(target.Root.Position, "treated", Hud.Palette.Good);
                    await Wait(0.35f);
                    break;

                case ScavengeEventType.DroneLoaded when actor is not null:
                    Beat();
                    FloatText(actor.Root.Position + Vector3.Up, "loaded", Hud.Palette.Amber);
                    await Wait(0.3f);
                    break;

                case ScavengeEventType.DroneFerried when actor is not null:
                    Beat();
                    FloatText(actor.Root.Position + Vector3.Up, "ferried out", Hud.Palette.Good);
                    await TweenTo(actor.Root, actor.Root.Position + Vector3.Up * 4, 0.5f);
                    actor.Root.Visible = false;
                    break;

                default:
                    Beat();
                    break;
            }
            previous = e;
        }
    }

    /// <summary>
    /// Walks the unit tile by tile from <paramref name="from"/> to <paramref name="to"/> (a view-side path over the tiles the
    /// unit can cross), turning toward each step; the walk clip (carry_walk when carrying, run on long paths) plays at the
    /// pace the tween goes, and its footfalls make the step sounds. Rigless models glide.
    /// </summary>
    private async Task Walk(View view, Unit? unit, Pos from, Pos to)
    {
        var path = unit is null ? [to] : TilePath(unit, from, to);
        var rig = view.Rig;
        var gait = rig is null || unit is null ? null : rig.Pick(path.Count > 3 && unit.CarryingId is null ? ["run", "walk"] : ClipMap.Move(unit));
        var seconds = (float)ReplayPacing.WalkSeconds(Hud.GameSettings.WalkTileSeconds, path.Count, gait == "run", skip: false) / path.Count;
        if (Fast)
        {
            Face(view, TileGrid.ToWorld(to), 0.01f);
            view.Root.Position = TileGrid.ToWorld(to);
            return;
        }
        if (rig is null || gait is null || gait == "hover")
        {
            // Rigless (or flying): one glide, a footfall per tile.
            var glide = Mathf.Max(MoveSeconds, seconds * path.Count * 0.6f);
            Face(view, TileGrid.ToWorld(to));
            if (rig is null && unit?.Kind != UnitKind.Drone) Footstep?.Invoke(view.Id, TileGrid.ToWorld(to));
            await TweenTo(view.Root, TileGrid.ToWorld(to), glide);
            return;
        }

        var stride = gait == "run" ? RunStride : WalkStride;
        var length = rig.Info(gait)?.Length ?? 1f;
        var cell = TileGrid.ToWorld(new Pos(1, 0)).DistanceTo(TileGrid.ToWorld(new Pos(0, 0)));
        rig.Loop(gait);
        foreach (var step in path)
        {
            var next = TileGrid.ToWorld(step);
            var distance = view.Root.Position.DistanceTo(next);
            // The clip's cycle covers `stride` metres at speed 1: match it to the tween so feet do not skate.
            rig.SpeedScale = Mathf.Clamp(distance / Mathf.Max(0.01f, seconds) * length / stride, 0.6f, 3f);
            Face(view, next, turnSeconds: 0.08f);
            await TweenTo(view.Root, next, seconds * distance / Mathf.Max(0.01f, cell), linear: true);
        }
        rig.SpeedScale = 1f;
        if (unit is not null && rig.Pick(ClipMap.Hold(unit)) is { } hold) rig.Loop(hold);
    }

    /// <summary>A dash: much quicker than a walk, in a straight line, a streak behind it.</summary>
    private async Task Dash(View view, Unit? unit, Pos to)
    {
        var (from, end) = (view.Root.Position, TileGrid.ToWorld(to));
        Face(view, end, 0.05f);
        if (Fast)
        {
            view.Root.Position = end;
            return;
        }
        Streak(from, end);
        view.Rig?.Loop(view.Rig.Pick(["run", "walk"]) ?? "idle");
        await TweenTo(view.Root, end, 0.2f, linear: true);
        if (unit is not null && view.Rig?.Pick(ClipMap.Hold(unit)) is { } hold) view.Rig.Loop(hold);
    }

    /// <summary>A bright bar along the ground from one point to another, fading fast.</summary>
    private void Streak(Vector3 from, Vector3 to)
    {
        var length = from.DistanceTo(to);
        if (length < 0.01f) return;
        var material = Placeholders.Material(new Color(Hud.Palette.Amber, 0.85f), unshaded: true);
        var bar = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.06f, length) }, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(bar);
        bar.Position = (from + to) / 2 + Vector3.Up * 0.3f;
        bar.Rotation = new Vector3(0, Mathf.Atan2(to.X - from.X, to.Z - from.Z), 0);
        var tween = bar.CreateTween();
        tween.TweenProperty(material, "albedo_color:a", 0f, 0.45);
        tween.TweenCallback(Callable.From(bar.QueueFree));
    }

    /// <summary>A flat ring sweeping out from a point while it fades. Nothing while skipping animations.</summary>
    private void Burst(Vector3 at, Color color, float radius, float seconds)
    {
        if (Fast) return;
        var material = Placeholders.Material(new Color(color, 0.9f), unshaded: true);
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.86f, OuterRadius = 1f, Rings = 32, RingSegments = 4 }, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Scale = new Vector3(0.3f, 0.3f, 0.3f),
        };
        AddChild(ring);
        ring.Position = at + Vector3.Up * 0.25f;
        var tween = ring.CreateTween().SetParallel();
        if (!Hud.GameSettings.ReduceMotion) tween.TweenProperty(ring, "scale", new Vector3(radius, 0.3f, radius), seconds).SetEase(Tween.EaseType.Out);
        else ring.Scale = new Vector3(radius, 0.3f, radius);
        tween.TweenProperty(material, "albedo_color:a", 0f, seconds);
        tween.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    /// <summary>A stunned unit rocks on its feet (holds still with reduce motion).</summary>
    private async Task Reel(View view)
    {
        if (Fast) return;
        if (Hud.GameSettings.ReduceMotion)
        {
            await Wait(0.3f);
            return;
        }
        var rest = view.Pose.RotationDegrees;
        var tween = CreateTween();
        foreach (var lean in new[] { 9f, -7f, 4f })
            tween.TweenProperty(view.Pose, "rotation_degrees", rest + new Vector3(0, 0, lean), 0.1f).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(view.Pose, "rotation_degrees", rest, 0.1f);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>
    /// A unit put on another tile by a card (<see cref="ScavengeEventType.Displaced"/>): it slides there with no walk, a
    /// swap's player takes the tile it left (Core logs only the moved unit: the player is the one standing on the landing
    /// tile), and a spin on the spot turns it away from the player. It then faces the way Core says. With
    /// <paramref name="snap"/> (out of sight) or skipping animations everything lands in the same frame.
    /// </summary>
    private async Task Displace(View? actor, View target, Pos landing, Unit? moved, bool snap)
    {
        var from = TileGrid.FromWorld(target.Root.Position);
        var instant = snap || Fast;
        var settled = moved is not null && moved.Pos == landing;
        if (from == landing)
        {
            // A spin on the spot: its back turned to the one who spun it.
            if (instant)
            {
                if (settled) SnapFacing(target, moved!);
                return;
            }
            if (actor is not null) Face(target, target.Root.Position + (target.Root.Position - actor.Root.Position), TurnSeconds);
            await Wait(TurnSeconds);
            if (settled) FaceCore(target, moved!);
            return;
        }

        // A swap: the player stood on the landing tile and takes the one the other unit left.
        var swap = actor is not null && actor != target && TileGrid.FromWorld(actor.Root.Position) == landing;
        if (instant)
        {
            target.Root.Position = TileGrid.ToWorld(landing);
            if (swap) actor!.Root.Position = TileGrid.ToWorld(from);
            if (settled) SnapFacing(target, moved!);
            return;
        }
        if (target.Rig is { } rig && !swap && rig.Current is not ("death" or "downed" or "downed_loop")) rig.Once("hit_react");
        var slides = new List<Task> { TweenTo(target.Root, TileGrid.ToWorld(landing), MoveSeconds * 1.5f) };
        if (swap) slides.Add(TweenTo(actor!.Root, TileGrid.ToWorld(from), MoveSeconds * 1.5f));
        await Task.WhenAll(slides);
        if (settled) FaceCore(target, moved!);
    }

    /// <summary>Sets the model's facing to Core's at once (no turn tween), for snapped steps.</summary>
    private static void SnapFacing(View view, Unit unit)
    {
        if (unit.Facing is not { } f || f == default || unit.CarriedById is not null) return;
        var toward = TileGrid.ToWorld(unit.Pos + f) - TileGrid.ToWorld(unit.Pos);
        view.Yaw = Mathf.RadToDeg(Mathf.Atan2(toward.X, toward.Z));
        view.Model.RotationDegrees = new Vector3(0, view.Yaw, 0);
    }

    /// <summary>
    /// The tiles walked from one tile to another: the cheapest 8-way path over what this unit can cross (no corner
    /// cutting), which is what Core's walk allows; straight-line moves (and jumps) get a straight line. Purely visual.
    /// </summary>
    private List<Pos> TilePath(Unit unit, Pos from, Pos to)
    {
        var map = _presenter.State.Map;
        var movement = _presenter.Sim.Profile(unit).Movement;
        int? Cost(Pos p) => map.InBounds(p) ? movement.Cost(map[p]) : null;
        var best = new Dictionary<Pos, int> { [from] = 0 };
        var back = new Dictionary<Pos, Pos>();
        var queue = new PriorityQueue<Pos, int>();
        queue.Enqueue(from, 0);
        var limit = from.Chebyshev(to) * 4 + 8;
        while (queue.TryDequeue(out var p, out var cost))
        {
            if (p == to) break;
            if (cost > best[p] || cost > limit) continue;
            foreach (var d in Pos.Neighbours8)
            {
                var n = p + d;
                if (Cost(n) is not { } step) continue;
                if (d.X != 0 && d.Y != 0 && Cost(new Pos(p.X + d.X, p.Y)) is null && Cost(new Pos(p.X, p.Y + d.Y)) is null) continue;
                var c = cost + step * 10 + (d.X != 0 && d.Y != 0 ? 4 : 0);
                if (best.TryGetValue(n, out var b) && b <= c) continue;
                best[n] = c;
                back[n] = p;
                queue.Enqueue(n, c);
            }
        }
        if (!back.ContainsKey(to)) return Line(from, to);
        var path = new List<Pos>();
        for (var p = to; p != from; p = back[p]) path.Add(p);
        path.Reverse();
        return path.Count > from.Chebyshev(to) * 2 + 2 ? Line(from, to) : path;
    }

    private static List<Pos> Line(Pos from, Pos to)
    {
        var steps = Mathf.Max(1, from.Chebyshev(to));
        var dx = (to.X - from.X) / (float)steps;
        var dy = (to.Y - from.Y) / (float)steps;
        return Enumerable.Range(1, steps).Select(i => new Pos(from.X + Mathf.RoundToInt(dx * i), from.Y + Mathf.RoundToInt(dy * i))).ToList();
    }

    /// <summary>
    /// An attack: turn to the target, play the weapon's clip and, on its impact / fire event, land the hit (flash,
    /// pop-up, hit_react, sound via <paramref name="beat"/>). Rigless attackers lunge as before.
    /// </summary>
    private async Task Strike(View actor, Unit? unit, View target, ScavengeEvent e, bool followUp, System.Action beat)
    {
        Face(actor, target.Root.Position);
        // A shot from range lights up the shooter for a moment (it matters most at night), as the round leaves.
        var ranged = actor.Root.Position.DistanceTo(target.Root.Position) > TileGrid.TileSize * 1.5f;
        var inner = beat;
        beat = () =>
        {
            if (ranged && !followUp) MuzzleFlash(actor.Root.Position);
            inner();
        };
        if (Fast)
        {
            beat();
            Land(target, e);
            return;
        }
        var rig = actor.Rig;
        var actionId = unit is null ? null : Audio.ScavengeAudio.ActionId(_presenter.Sim, unit, e.Message);
        var action = unit is null || actionId is null ? null : _presenter.Sim.Profile(unit).Action(actionId);
        var clip = rig?.Pick(ClipMap.Attack(actionId, action?.Pattern));
        if (followUp && clip is not null)
        {
            beat();
            Land(target, e);
            if (Juice is { } splashJuice) await splashJuice.HitStop(e, seen: true);
            await Wait(0.12f);
            return;
        }
        if (rig is not null && clip is not null && rig.Once(clip))
        {
            var info = rig.Info(clip);
            var hit = new[] { ClipMap.Impact, ClipMap.Fire, ClipMap.Pulse }.Select(evt => rig.EventDelay(clip, evt)).FirstOrDefault(d => d > 0);
            if (hit <= 0) hit = (info?.Length ?? 0.6f) * 0.45f;
            await Wait(hit);
            beat();
            Land(target, e);
            if (Juice is { } clipJuice) await clipJuice.HitStop(e, seen: true);
            // Let the follow-through play, but do not hold the turn for the whole recovery.
            await Wait(Mathf.Clamp((info?.Length ?? 0.6f) - hit, 0.15f, 0.45f));
            return;
        }

        var home = actor.Root.Position;
        var lunge = home + (target.Root.Position - home).Normalized() * 0.6f;
        await TweenTo(actor.Root, lunge, StrikeSeconds);
        beat();
        Land(target, e);
        if (Juice is { } lungeJuice) await lungeJuice.HitStop(e, seen: true);
        await TweenTo(actor.Root, home, StrikeSeconds);
        await Wait(0.12f);
    }

    /// <summary>The blow lands: hit flash, damage pop-up and hit_react, or a "miss".</summary>
    private void Land(View target, ScavengeEvent e)
    {
        if (e.Type == ScavengeEventType.Attacked)
        {
            Flash(target, _hitFlash);
            var damage = DamagePattern.Match(e.Message);
            var text = damage.Success ? $"-{damage.Groups[1].Value}" : "hit";
            var squad = e.UnitId is { } by && _presenter.State.FindUnit(by)?.Side == _presenter.Side;
            if (Juice is { } juice) juice.Pop(target.Root.Position, text, Hud.Palette.Danger, e.Amount ?? 0, e.Tier, squad);
            else FloatText(target.Root.Position, text, Hud.Palette.Danger);
            if (target.Rig is { } rig && rig.Current is not ("death" or "downed" or "downed_loop")) rig.Once("hit_react");
        }
        else
        {
            Flash(target, _whiffFlash, 0.3f);
            FloatText(target.Root.Position, ReplayFeedback.Of(e)?.Text ?? "Whiff", Hud.Palette.TextMuted);
        }
    }

    /// <summary>Turns the model toward a world point (models face +Z).</summary>
    private void Face(View view, Vector3 at, float turnSeconds = 0.12f)
    {
        var dir = at - view.Root.Position;
        if (new Vector2(dir.X, dir.Z).LengthSquared() < 0.0001f) return;
        var yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
        // The short way round.
        var from = view.Model.RotationDegrees.Y;
        yaw = from + Mathf.Wrap(yaw - from, -180f, 180f);
        view.Yaw = yaw;
        var tween = CreateTween();
        tween.TweenProperty(view.Model, "rotation_degrees", new Vector3(0, yaw, 0), turnSeconds);
    }

    /// <summary>A carried unit rides on its carrier's back socket, lying across the shoulders.</summary>
    public override void _Process(double delta)
    {
        if (_presenter is null) return;
        FlickerDecoys();
        foreach (var view in _views.Values)
        {
            if (_presenter.State.FindUnit(view.Id) is not { CarriedById: { } carrierId } ||
                !_views.TryGetValue(carrierId, out var carrier) || carrier.Rig is null ||
                SurvivorDressing.Skeleton(carrier.Model) is not { } skeleton || skeleton.FindBone("socket_back") < 0) continue;
            var back = SurvivorDressing.SocketNode(skeleton, "socket_back");
            if (!back.IsInsideTree()) continue;
            // Turned onto its side across the shoulders: the hips (about 0.9 m up the body) on the socket.
            view.Pose.GlobalTransform = back.GlobalTransform * new Transform3D(new Basis(Vector3.Back, Mathf.Pi / 2), new Vector3(0.9f, 0.12f, -0.12f));
            view.Model.RotationDegrees = Vector3.Zero;
            view.Ring.Visible = false;
        }
    }

    private async Task TweenTo(Node3D node, Vector3 to, float seconds, bool linear = false)
    {
        if (Fast)
        {
            node.Position = to;
            return;
        }
        var tween = CreateTween().SetTrans(linear ? Tween.TransitionType.Linear : Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "position", to, seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private async Task Wait(float seconds)
    {
        if (ReplayPacing.Hold(seconds, seen: true, skip: Fast) <= 0) return;
        await ToSignal(GetTree().CreateTimer(Mathf.Max(0.01f, seconds)), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>Waits for a tween to finish; skipping animations, runs it to its end at once.</summary>
    private async Task Finish(Tween tween)
    {
        if (Fast)
        {
            tween.CustomStep(60);
            return;
        }
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>A short warm light at a shooter, fading out in a third of a second.</summary>
    /// <summary>The widest EMP burst in the data (the event does not say which device went off).</summary>
    private int EmpRadius() =>
        _presenter.Sim.Data.Items.Values.Select(i => i.Use?.Emp?.Radius ?? 0).DefaultIfEmpty(2).Max() is > 0 and var r ? r : 2;

    /// <summary>
    /// An EMP going off: a cyan flash and light at the tile, and a flat ring that sweeps out to the burst's edge
    /// (<paramref name="radius"/> tiles, Chebyshev, so a square reads truer than a circle) while it fades.
    /// </summary>
    public void EmpRing(Vector3 at, int radius)
    {
        var cyan = Hud.Palette.Network;
        var root = new Node3D { Name = "EmpBurst", Position = at };
        AddChild(root);
        var ringMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(cyan, 0.9f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = cyan, EmissionEnergyMultiplier = 2,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false,
        };
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.92f, OuterRadius = 1f, Rings = 48, RingSegments = 4 },
            MaterialOverride = ringMaterial, Position = new Vector3(0, 0.25f, 0), Scale = new Vector3(0.2f, 0.4f, 0.2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(ring);
        var edge = (radius + 0.5f) * TileGrid.TileSize;
        var areaMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(cyan, 0.0f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var area = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(edge * 2, edge * 2) }, MaterialOverride = areaMaterial,
            Position = new Vector3(0, 0.15f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(area);
        var flash = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.9f, Height = 1.8f, RadialSegments = 16, Rings = 8 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.85f, 1f, 1f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = cyan,
            },
            Position = new Vector3(0, 1f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(flash);
        var light = new OmniLight3D
        {
            Position = new Vector3(0, 2f, 0), LightColor = cyan, LightEnergy = 8f, OmniRange = edge * 1.6f, ShadowEnabled = false,
        };
        root.AddChild(light);

        // Kept (Hold) for a still: --tech-demo=emp freezes it at its widest.
        var tween = root.CreateTween().SetParallel();
        tween.TweenProperty(ring, "scale", new Vector3(edge, 1.2f, edge), 0.55).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(areaMaterial, "albedo_color", new Color(cyan, 0.22f), 0.35);
        tween.TweenProperty(flash, "scale", Vector3.One * 2.6f, 0.3);
        tween.TweenProperty((StandardMaterial3D)flash.MaterialOverride, "albedo_color", new Color(0.85f, 1f, 1f, 0f), 0.5);
        if (HoldEffects) return;
        tween.Chain().TweenProperty(ringMaterial, "albedo_color", new Color(cyan, 0f), 0.6);
        tween.TweenProperty(areaMaterial, "albedo_color", new Color(cyan, 0f), 0.6);
        tween.TweenProperty(light, "light_energy", 0f, 0.6);
        tween.Chain().TweenCallback(Callable.From(root.QueueFree));
    }

    /// <summary>Debug stills: effects stay at their peak instead of fading.</summary>
    public bool HoldEffects { get; set; }

    /// <summary>Holo-decoys flicker: their shared material's alpha jitters, with the odd drop-out.</summary>
    private void FlickerDecoys()
    {
        var t = (float)Time.GetTicksMsec() / 1000f;
        foreach (var view in _views.Values)
        {
            if (!view.Model.HasMeta(ScavengeModels.HologramMeta) || view.Model.GetMeta(ScavengeModels.HologramMeta).As<StandardMaterial3D>() is not { } holo) continue;
            var phase = t * 9f + view.Id * 1.7f;
            var dropout = Mathf.Sin(phase * 0.37f) > 0.93f;
            var alpha = dropout ? 0.12f : 0.34f + 0.1f * Mathf.Sin(phase) + 0.05f * Mathf.Sin(phase * 2.9f);
            holo.AlbedoColor = new Color(holo.AlbedoColor, alpha);
        }
    }

    private void MuzzleFlash(Vector3 at)
    {
        var light = new OmniLight3D
        {
            Position = at + Vector3.Up * 1.2f, LightColor = new Color(1f, 0.75f, 0.45f), LightEnergy = 5f,
            OmniRange = TileGrid.TileSize * 4f, ShadowEnabled = false,
        };
        AddChild(light);
        var tween = light.CreateTween();
        tween.TweenProperty(light, "light_energy", 0f, 0.35f);
        tween.TweenCallback(Callable.From(light.QueueFree));
    }

    /// <summary>Tints every mesh of the unit for a moment.</summary>
    private void Flash(View view, Material overlay, float seconds = 0.25f)
    {
        var meshes = view.Model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToList();
        foreach (var m in meshes) m.MaterialOverlay = overlay;
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            foreach (var m in meshes.Where(GodotObject.IsInstanceValid)) m.MaterialOverlay = view.Low ? _lowStamina : null;
        };
    }

    private void FloatText(Vector3 at, string text, Color color) => Floated?.Invoke(at, text, color, text.StartsWith('-'));
}
