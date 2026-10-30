using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Game.Colony;

namespace Remnants.Game.Anim;

/// <summary>
/// Plays a rigged model's clips (manifest v2: survivors, enemies, drones). Finds the glTF's <c>AnimationPlayer</c>, sets
/// each clip's loop mode from the manifest, and drives it through an <see cref="AnimationTree"/> state machine with a
/// crossfade between every pair of clips. A <b>base</b> loop (idle, walk, overwatch_aim, downed_loop, work_chop, sit, ...)
/// plays until a <b>one-shot</b> (an attack, hit_react, throw, death) takes over; the one-shot returns to the base at its
/// end, or holds its last frame (<c>death</c>), or hands over to a named loop (<c>downed</c> → <c>downed_loop</c>).
/// Clip events (manifest <c>events</c>, seconds from the clip start: <c>impact</c>, <c>fire</c>, <c>release</c>, <c>apply</c>,
/// <c>ground</c>, ...) come out as <see cref="ClipEvent"/> signals; walk-type loops without footfall events get a
/// <c>step</c> at each half cycle. The tree is advanced by hand: <see cref="SpeedScale"/> follows the game's time scale
/// and <see cref="UpdateEvery"/> thins the updates of distant figures (animation LOD). Purely visual.
/// </summary>
public partial class CharacterRig : Node
{
    private const float Crossfade = 0.18f;
    private const string StepEvent = "step";

    /// <summary>Walk-type loops: they get footfall <c>step</c> events if the clip carries none.</summary>
    private static readonly HashSet<string> Gaits = ["walk", "run", "carry_walk"];

    /// <summary>One state machine per distinct clip set (every survivor shares one); the playback state is per tree.</summary>
    private static readonly Dictionary<string, AnimationNodeStateMachine> Machines = new();

    [Signal] public delegate void ClipEventEventHandler(StringName clip, StringName name);
    [Signal] public delegate void ClipFinishedEventHandler(StringName clip);

    private AnimationTree _tree = null!;
    private AnimationNodeStateMachinePlayback _playback = null!;
    private Dictionary<string, ClipInfo> _clips = null!;
    private string _base = "";
    private string _current = "";
    private string? _then;
    private bool _oneShot, _finished;
    private float _time;
    private float _pending;
    private int _frame;

    /// <summary>Playback speed (1 = authored); the colony sets it from its time scale, capped so 4x stays readable.</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>Animation LOD: advance the pose every Nth frame (the clock and events still run every frame).</summary>
    public int UpdateEvery { get; set; } = 1;

    /// <summary>The clip playing now (or crossfading in).</summary>
    public string Current => _current;

    /// <summary>Seconds into the current clip (authored time, wrapped for a loop).</summary>
    public float ClipTime => Mathf.Max(0f, _time);

    /// <summary>The loop the rig returns to after a one-shot.</summary>
    public string Base => _base;

    public IReadOnlyCollection<string> ClipNames => _clips.Keys;

    /// <summary>
    /// Adds a rig to a model instanced from <paramref name="id"/> and starts its first loop (<c>idle</c>, else
    /// <c>hover</c>, else the first looping clip). Null when the model has no animations (a static v1 model or a
    /// placeholder), so callers keep their tween-only fallback.
    /// </summary>
    public static CharacterRig? Attach(ModelLibrary library, Node3D model, string id)
    {
        if (FindPlayer(model) is not { } player) return null;
        var names = player.GetAnimationList().Where(n => n != "RESET").ToList();
        if (names.Count == 0) return null;

        var manifest = library.Clips(id);
        var clips = new Dictionary<string, ClipInfo>();
        foreach (var name in names)
        {
            var animation = player.GetAnimation(name);
            var info = manifest.GetValueOrDefault(name) ?? new ClipInfo(name, (float)animation.Length, animation.LoopMode != Animation.LoopModeEnum.None,
                new Dictionary<string, float[]>());
            animation.LoopMode = info.Loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            if (info.Length <= 0) info = info with { Length = (float)animation.Length };
            if (info.Loop && Gaits.Contains(name) && !info.Events.ContainsKey(StepEvent))
                info = info with { Events = new Dictionary<string, float[]>(info.Events) { [StepEvent] = [0f, info.Length / 2] } };
            clips[name] = info;
        }

        var tree = new AnimationTree
        {
            Name = "AnimationTree",
            TreeRoot = Machine(clips.Values),
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
        };
        model.AddChild(tree);
        tree.AnimPlayer = tree.GetPathTo(player);
        tree.Active = true;

        var rig = new CharacterRig { Name = "Rig", _tree = tree, _clips = clips };
        rig._playback = (AnimationNodeStateMachinePlayback)tree.Get("parameters/playback");
        model.AddChild(rig);
        var first = new[] { "idle", "hover" }.FirstOrDefault(clips.ContainsKey) ?? clips.Values.FirstOrDefault(c => c.Loop)?.Name ?? clips.Keys.First();
        rig.Loop(first);
        return rig;
    }

    /// <summary>The rig on a model (or a model wrapped in a node, like the lifted scout drone), if it has one.</summary>
    public static CharacterRig? Of(Node3D? model) => model?.GetNodeOrNull<CharacterRig>("Rig") ?? model?.FindChild("Rig", true, false) as CharacterRig;

    private static AnimationPlayer? FindPlayer(Node3D model) =>
        model.GetNodeOrNull<AnimationPlayer>("AnimationPlayer") ?? model.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();

    /// <summary>Every clip a state, and a crossfading transition between every pair, so any clip can follow any other.</summary>
    private static AnimationNodeStateMachine Machine(IEnumerable<ClipInfo> clips)
    {
        var names = clips.Select(c => c.Name).Order(System.StringComparer.Ordinal).ToList();
        var key = string.Join(",", names);
        if (Machines.TryGetValue(key, out var cached)) return cached;
        var machine = new AnimationNodeStateMachine();
        for (var i = 0; i < names.Count; i++)
            machine.AddNode(names[i], new AnimationNodeAnimation { Animation = names[i] }, new Vector2(i % 6 * 200, i / 6 * 120));
        foreach (var from in names)
            foreach (var to in names.Where(n => n != from))
                machine.AddTransition(from, to, new AnimationNodeStateMachineTransition
                {
                    XfadeTime = to == "death" ? 0.1f : Crossfade,
                    SwitchMode = AnimationNodeStateMachineTransition.SwitchModeEnum.Immediate,
                    AdvanceMode = AnimationNodeStateMachineTransition.AdvanceModeEnum.Enabled,
                });
        Machines[key] = machine;
        return machine;
    }

    public bool Has(string clip) => _clips.ContainsKey(clip);

    /// <summary>The first of these clips the body has (clip names are shared across bodies, but not every body has all).</summary>
    public string? Pick(params string[] candidates) => candidates.FirstOrDefault(_clips.ContainsKey);

    public ClipInfo? Info(string clip) => _clips.GetValueOrDefault(clip);

    /// <summary>Seconds into the clip at which its first <paramref name="evt"/> lands at the current speed (0 when it has none).</summary>
    public float EventDelay(string clip, string evt) =>
        _clips.GetValueOrDefault(clip)?.Events.GetValueOrDefault(evt) is [var at, ..] ? at / Mathf.Max(0.01f, SpeedScale) : 0f;

    /// <summary>Makes <paramref name="clip"/> the base loop and plays it now (unless a one-shot is still running).</summary>
    public void Loop(string clip, bool interrupt = true)
    {
        if (!_clips.ContainsKey(clip)) return;
        _base = clip;
        if (_oneShot && !_finished && !interrupt) return;
        if (_current == clip && !_oneShot) return;
        Enter(clip, oneShot: false);
    }

    /// <summary>
    /// Plays a one-shot clip. At its end the rig goes back to the base loop, or to <paramref name="then"/> (which becomes
    /// the base), or holds the last frame when <paramref name="hold"/> (death). Returns false if the body lacks the clip.
    /// </summary>
    public bool Once(string clip, string? then = null, bool hold = false)
    {
        if (!_clips.ContainsKey(clip)) return false;
        _then = hold ? null : then ?? "";
        Enter(clip, oneShot: true);
        return true;
    }

    private void Enter(string clip, bool oneShot)
    {
        if (_current == clip) _playback.Start(clip);
        else if (_current.Length == 0) _playback.Start(clip);
        else _playback.Travel(clip);
        _current = clip;
        _oneShot = oneShot;
        _finished = false;
        _time = -0.0001f;
    }

    /// <summary>Applies the current pose now (e.g. right after building a model off screen), without waiting a frame.</summary>
    public void Snap() => _tree.Advance(0);

    public override void _Process(double delta)
    {
        var dt = (float)delta * SpeedScale;
        if (dt <= 0) return;
        Tick(dt);
        _pending += dt;
        _frame++;
        var parent = GetParent() as Node3D;
        if (_frame % Mathf.Max(1, UpdateEvery) != 0 || parent is { Visible: false }) return;
        _tree.Advance(_pending);
        _pending = 0;
    }

    /// <summary>The event clock: fires the clip's events crossed in this step, and ends a one-shot.</summary>
    private void Tick(float dt)
    {
        if (_finished || !_clips.TryGetValue(_current, out var info)) return;
        var from = _time;
        var to = _time + dt;
        var length = Mathf.Max(0.001f, info.Length);
        foreach (var (name, times) in info.Events)
            foreach (var at in times)
            {
                if (info.Loop)
                {
                    // Every crossing of `at` within (from, to], over as many cycles as the step spans.
                    var k = Mathf.Ceil((from - at) / length);
                    for (var t = at + k * length; t <= to; t += length)
                        if (t > from) EmitSignal(SignalName.ClipEvent, info.Name, name);
                }
                else if (at > from && at <= to) EmitSignal(SignalName.ClipEvent, info.Name, name);
            }
        _time = info.Loop ? to % length : to;
        if (info.Loop || to < length || !_oneShot) return;

        _finished = true;
        var clip = _current;
        if (_then is { } next)
        {
            if (next.Length > 0) _base = next;
            Enter(_base, oneShot: false);
        }
        EmitSignal(SignalName.ClipFinished, clip);
    }

    /// <summary>
    /// Poses a model still at a clip's frame (portraits use idle at 0), without a tree. The model must be in the
    /// scene tree. No-op for static models.
    /// </summary>
    public static void PoseStill(Node3D model, string clip, float time = 0f)
    {
        if (FindPlayer(model) is not { } player || !player.HasAnimation(clip)) return;
        player.Play(clip);
        player.Seek(time, update: true);
        player.Pause();
    }
}
