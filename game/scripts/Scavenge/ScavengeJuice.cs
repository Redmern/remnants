using System.Threading.Tasks;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Audio;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge;

/// <summary>
/// The scavenge screen's feel (phase 3a): the squad's hit chain (<see cref="HitChain"/>, shown by
/// <see cref="Ui.ChainMeter"/>) and its rising tick, damage pops that grow and heat with the value and the chain, screen
/// shake, hit-stop and a capped colour flash scaled by hit tier, arc, exposure, kills and shoves
/// (<see cref="ReplayJuice.Feel"/>), the music's chain layer, and the CRT look over the map. All of it is presentation:
/// it reads replayed events and <see cref="GameSettings"/>, never the sim, and skipping animations leaves only the pops
/// and the chain count (no shake, no flash, no hit-stop). Hit-stop slows the engine for a few hundredths of a second,
/// capped per round (<see cref="HitStopBudget"/>).
/// </summary>
public partial class ScavengeJuice : Node
{
    private ScavengePresenter _presenter = null!;
    private TacticalCamera _camera = null!;
    private Ui.WorldTags _tags = null!;
    private Ui.ChainMeter _meter = null!;
    private ColorRect _flash = null!;
    private ShaderMaterial _flashMaterial = null!;
    private CanvasLayer _crtLayer = null!;
    private ShaderMaterial _crtMaterial = null!;
    private Tween? _flashTween;
    private readonly HitChain _chain = new();
    private readonly HitStopBudget _budget = new();
    private readonly FlashGate _gate = new();
    private bool _stopping;
    private bool? _crtStill;

    /// <summary>Hit-stop seconds added in the round so far (the pacing check; <c>--juice-log</c> prints each round's).</summary>
    public double StoppedThisRound => _budget.Spent;

    private static bool Skip => GameSettings.SkipAnimations;
    private static JuiceSettings Settings => GameSettings.Juice;
    private static double Intensity => ReplayJuice.Intensity(GameSettings.EffectsLevel);
    private static readonly bool Log = DebugCapture.HasFlag("--juice-log");

    public void Setup(ScavengePresenter presenter, TacticalCamera camera, Ui.ScavengeHud hud)
    {
        _presenter = presenter;
        _camera = camera;
        _tags = hud.Tags;
        _meter = new Ui.ChainMeter();
        hud.AddChild(_meter);

        _flashMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hit_flash.gdshader") };
        _flash = new ColorRect { Name = "HitFlash", MouseFilter = Control.MouseFilterEnum.Ignore, Material = _flashMaterial, Visible = false };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(_flash);
        hud.MoveChild(_flash, 0);

        // Under the HUD (layer 1): the map bends and scans, the panels stay sharp.
        _crtMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crt.gdshader") };
        _crtLayer = new CanvasLayer { Name = "Crt", Layer = 0 };
        var crt = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _crtMaterial };
        crt.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _crtLayer.AddChild(crt);
        AddChild(_crtLayer);
        SyncCrt();
    }

    public override void _Process(double delta) => SyncCrt();

    public override void _ExitTree()
    {
        // Never leave the engine slowed (a screen closed during a hit-stop).
        Engine.TimeScale = 1;
        AudioManager.Instance?.Music.SetLayerLevel("chain_layer", 0);
    }

    /// <summary>The CRT layer follows the settings live: off with the toggle or effects Off; no curve or wobble with reduce motion.</summary>
    private void SyncCrt()
    {
        if (_crtLayer is null) return;
        var on = GameSettings.CrtShader && GameSettings.EffectsLevel != EffectsLevel.Off;
        _crtLayer.Visible = on;
        if (!on) return;
        var still = GameSettings.ReduceMotion;
        if (_crtStill == still) return;
        _crtStill = still;
        _crtMaterial.SetShaderParameter("curve", still ? 0f : 0.035f);
        _crtMaterial.SetShaderParameter("wobble", still ? 0f : 0.0012f);
    }

    /// <summary>
    /// One replayed event at its beat (the same moment its sound plays): grows, breaks or resets the chain, and gives
    /// what the side saw its shake, flash and sounds.
    /// </summary>
    public void Step(ScavengeEvent e, bool seen)
    {
        var link = _chain.Step(_presenter.State, _presenter.Side, e);
        if (link.Reset)
        {
            if (Log && _budget.Spent > 0) GD.Print($"[juice] round {e.Round - 1}: hit-stop {_budget.Spent:0.000} s");
            _budget.NewRound();
            _meter.Reset();
            Music(0);
            return;
        }
        var on = GameSettings.EffectsLevel != EffectsLevel.Off;
        if (link.Grew)
        {
            _meter.Grow(link.Count, link.Total, animate: on && !Skip && !GameSettings.ReduceMotion);
            if (on && link.Count >= 2) AudioManager.Fire("juice.chain", pitch: (float)ReplayJuice.ChainPitch(link.Count));
            Music(link.Count);
        }
        else if (link.Broke)
        {
            _meter.Break(link.Total);
            Music(0);
        }
        if (!seen || !on || Skip) return;
        switch (e.Type)
        {
            case ScavengeEventType.Attacked when e.TargetId != e.UnitId:
                Jolt(ReplayJuice.ImpactOf(e));
                break;
            case ScavengeEventType.Died:
                Jolt(Impact.Kill);
                AudioManager.Fire("juice.kill");
                break;
            case ScavengeEventType.Downed:
                Jolt(Impact.Kill with { Killed = false, Tier = HitTier.Solid, Amount = 0 });
                break;
            case ScavengeEventType.Displaced:
                Jolt(Impact.Push);
                break;
        }
    }

    /// <summary>
    /// The pitch of an event's hit sound: a squad blow climbs with the chain (half the chain tick's climb), the rest
    /// plays as is. Effects Off: 1.
    /// </summary>
    public float HitPitch(ScavengeEvent e)
    {
        if (GameSettings.EffectsLevel == EffectsLevel.Off || e.Type != ScavengeEventType.Attacked || _chain.Count < 2
            || e.UnitId is not { } by || _presenter.State.FindUnit(by)?.Side != _presenter.Side) return 1;
        return 1 + ((float)ReplayJuice.ChainPitch(_chain.Count) - 1) * 0.5f;
    }

    /// <summary>The music's chain layer swells with the chain (off with "Music follows the fight" off or effects Off).</summary>
    private void Music(int chain)
    {
        var level = GameSettings.MusicReactive ? ReplayJuice.MusicHeat(chain) * Mathf.Min(1, (float)Intensity) : 0;
        AudioManager.Instance?.Music.SetLayerLevel("chain_layer", (float)level);
    }

    private void Jolt(Impact impact)
    {
        var feel = ReplayJuice.Feel(impact, Settings);
        if (feel.Shake > 0) _camera.AddTrauma((float)feel.Shake);
        if (feel.Flash > 0 && AllowFlash()) Flash((float)feel.Flash, impact.Killed);
    }

    /// <summary>
    /// Whether a screen flash may show now (a hit's vignette, the sky's lightning): never with reduce motion, and never two
    /// within <see cref="FlashGate.MinGap"/> (one cap for every flash on this screen). Counts the flash when it may.
    /// </summary>
    public bool AllowFlash() => !GameSettings.ReduceMotion && _gate.Allow(Time.GetTicksMsec() / 1000.0);

    private void Flash(float strength, bool kill)
    {
        _flashTween?.Kill();
        _flash.Visible = true;
        _flashMaterial.SetShaderParameter("tint", kill ? new Color(1f, 0.9f, 0.75f, 0.55f) : new Color(1f, 0.22f, 0.12f, 0.5f));
        _flashMaterial.SetShaderParameter("strength", strength);
        _flashTween = _flash.CreateTween();
        _flashTween.TweenMethod(Callable.From<float>(v => _flashMaterial.SetShaderParameter("strength", v)), strength, 0f, 0.28)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _flashTween.TweenCallback(Callable.From(() => _flash.Visible = false));
    }

    /// <summary>
    /// A damage or heal pop over a unit: sized and heated by its value and the chain (<see cref="ReplayJuice.PopScale"/>),
    /// harder hits shake it (<paramref name="squad"/>: the side's own blow, the only kind the chain grows). Effects Off
    /// gives the plain pop.
    /// </summary>
    public void Pop(Vector3 at, string text, Color color, double amount, HitTier? tier = null, bool squad = true)
    {
        var k = (float)Intensity;
        if (k <= 0)
        {
            _tags.Pop(at, text, color, text.StartsWith('-'));
            return;
        }
        // Only the squad's own blows ride its chain; a hit taken grows with its value alone.
        var chain = squad ? _chain.Count : 0;
        var scale = 1 + ((float)ReplayJuice.PopScale(amount, chain) - 1) * Mathf.Min(k, 1.2f);
        var heat = (float)ReplayJuice.PopHeat(amount, chain) * Mathf.Min(k, 1f);
        if (tier == HitTier.Graze) (scale, heat) = (Mathf.Max(1, scale * 0.85f), heat * 0.5f);
        var hot = color.Lerp(new Color(1f, 0.93f, 0.62f), heat * 0.75f);
        var jitter = GameSettings.ReduceMotion || Skip ? 0 : (tier == HitTier.Solid ? 5f : 2f) * (0.4f + heat) * k;
        if (tier == HitTier.Solid) text += "!";
        _tags.Pop(at, text, hot, text.StartsWith('-'), scale, jitter);
    }

    /// <summary>
    /// Freezes the replay a moment on a blow (its <see cref="Feel.HitStop"/>, within the round's budget): the engine runs
    /// at a twentieth for that real time. Nothing while skipping animations, for a blow the side did not see (an unseen
    /// attacker: a hit from the fog), with reduce motion or with hit-stop off.
    /// </summary>
    public async Task HitStop(ScavengeEvent e, bool seen)
    {
        if (_stopping || GameSettings.EffectsLevel == EffectsLevel.Off) return;
        var impact = e.Type switch
        {
            ScavengeEventType.Attacked when e.TargetId != e.UnitId => ReplayJuice.ImpactOf(e),
            ScavengeEventType.Died => Remnants.Core.Scavenge.Impact.Kill,
            ScavengeEventType.Displaced => Remnants.Core.Scavenge.Impact.Push,
            _ => (Remnants.Core.Scavenge.Impact?)null,
        };
        if (impact is not { } hit) return;
        var seconds = _budget.Take(ReplayJuice.Feel(hit, Settings).HitStop, Skip, seen);
        if (seconds <= 0) return;
        _stopping = true;
        Engine.TimeScale = 0.05;
        await ToSignal(GetTree().CreateTimer(seconds, processAlways: true, processInPhysics: false, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
        Engine.TimeScale = 1;
        _stopping = false;
    }

    /// <summary>A card hit its target (the hand's slam): a small jolt.</summary>
    public void Slam()
    {
        if (GameSettings.EffectsLevel == EffectsLevel.Off || Skip) return;
        var feel = ReplayJuice.Feel(Remnants.Core.Scavenge.Impact.Push, Settings);
        if (feel.Shake > 0) _camera.AddTrauma((float)feel.Shake * 0.6f);
    }
}
