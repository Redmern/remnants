using System;
using System.Collections.Generic;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual: the colony's sun, moon, sky colour, ambient light and fog follow Core's clock, and the night lights
/// registered here (building lamps, floodlights, campfire, torches) come up at dusk and go out at dawn. The shown time
/// eases toward the sim's tick plus the session's progress into the next one, so the light moves smoothly at any speed,
/// holds still when paused, and a skip of whole days changes nothing on screen; a jump within the day eases across
/// in about a second instead of flashing.
/// </summary>
public partial class DayNightLighting : Node3D
{
    /// <summary>One look of the scene at an hour; the hours between are blended.</summary>
    private readonly record struct Key(
        float Hour, Color Sun, float SunEnergy, Color Moon, float MoonEnergy, Color Ambient, float AmbientEnergy,
        Color Sky, float Fog, float Lamps);

    // Dawn is warm and misty, the day as before this cycle, dusk amber, the night a dark blue-grey that keeps the town
    // readable (moonlight plus a cool ambient).
    private static readonly Key[] Keys =
    [
        new(0f, Colors.Black, 0f, new(0.62f, 0.72f, 1f), 0.42f, new(0.36f, 0.43f, 0.58f), 0.62f, new(0.05f, 0.065f, 0.1f), 0.010f, 1f),
        new(4.5f, Colors.Black, 0f, new(0.62f, 0.72f, 1f), 0.36f, new(0.38f, 0.42f, 0.55f), 0.6f, new(0.07f, 0.075f, 0.11f), 0.008f, 1f),
        new(5.5f, new(1f, 0.5f, 0.35f), 0.25f, new(0.62f, 0.72f, 1f), 0.2f, new(0.5f, 0.45f, 0.55f), 0.55f, new(0.24f, 0.17f, 0.2f), 0.007f, 0.8f),
        new(6.5f, new(1f, 0.66f, 0.45f), 0.75f, new(0.62f, 0.72f, 1f), 0f, new(0.62f, 0.54f, 0.5f), 0.58f, new(0.38f, 0.28f, 0.24f), 0.005f, 0.25f),
        new(8f, new(1f, 0.95f, 0.85f), 1.1f, Colors.Black, 0f, new(0.55f, 0.6f, 0.65f), 0.6f, new(0.15f, 0.16f, 0.17f), 0.003f, 0f),
        new(16.5f, new(1f, 0.93f, 0.8f), 1.1f, Colors.Black, 0f, new(0.55f, 0.6f, 0.65f), 0.6f, new(0.15f, 0.16f, 0.17f), 0.003f, 0f),
        new(18f, new(1f, 0.62f, 0.32f), 0.85f, Colors.Black, 0f, new(0.62f, 0.5f, 0.45f), 0.58f, new(0.34f, 0.2f, 0.15f), 0.006f, 0.35f),
        new(19.2f, new(0.95f, 0.4f, 0.25f), 0.3f, new(0.62f, 0.72f, 1f), 0.15f, new(0.45f, 0.4f, 0.5f), 0.56f, new(0.18f, 0.11f, 0.13f), 0.008f, 0.85f),
        new(20.5f, Colors.Black, 0f, new(0.62f, 0.72f, 1f), 0.4f, new(0.36f, 0.43f, 0.58f), 0.62f, new(0.06f, 0.07f, 0.11f), 0.010f, 1f),
        new(24f, Colors.Black, 0f, new(0.62f, 0.72f, 1f), 0.42f, new(0.36f, 0.43f, 0.58f), 0.62f, new(0.05f, 0.065f, 0.1f), 0.010f, 1f),
    ];

    private sealed record NightLight(OmniLight3D Light, float DayEnergy, float NightEnergy, bool Flicker, Func<bool>? On, float Seed);

    private sealed record Glow(StandardMaterial3D Material, Node Owner, float DayEnergy, float NightEnergy, Func<bool>? On);

    private readonly List<NightLight> _lights = new();
    private readonly List<Glow> _glows = new();
    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private Godot.Environment _env = null!;
    private ColonySession _session = null!;
    private double _shown = double.NaN;
    private long _progressTick = -1;
    private double _progress;
    private float _time;

    /// <summary>Hold the scene at this hour instead of following the clock (the menu backdrop shows dusk).</summary>
    public double? FixedHour { get; set; }

    /// <summary>0 by day to 1 at night: how far up the night lights are.</summary>
    public float Night { get; private set; }

    /// <summary>The hour on screen (eased), for views that want to agree with the light.</summary>
    public double ShownHour => DayNight.HourOf(_shown, _session.Sim.Data.Rules);

    public void Setup(ColonySession session)
    {
        _session = session;
        _sun = new DirectionalLight3D { Name = "Sun", ShadowEnabled = true };
        _moon = new DirectionalLight3D { Name = "Moon", RotationDegrees = new Vector3(-48, 150, 0) };
        AddChild(_sun);
        AddChild(_moon);
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightEnergy = 1f,
            FogSkyAffect = 0f,
        };
        AddChild(new WorldEnvironment { Environment = _env });
        Apply(Target());
    }

    /// <summary>A light that comes up at night (<paramref name="dayEnergy"/> by day), optionally flickering like a flame, while <paramref name="on"/> holds.</summary>
    public void AddLight(OmniLight3D light, float nightEnergy, float dayEnergy = 0, bool flicker = false, Func<bool>? on = null) =>
        _lights.Add(new NightLight(light, dayEnergy, nightEnergy, flicker, on, (float)GD.RandRange(0, 100)));

    /// <summary>An emissive material (a window, a lamp head) on <paramref name="owner"/> that glows at night; dropped when the owner goes.</summary>
    public void AddGlow(StandardMaterial3D material, Node owner, float nightEnergy, float dayEnergy = 0, Func<bool>? on = null) =>
        _glows.Add(new Glow(material, owner, dayEnergy, nightEnergy, on));

    public override void _Process(double delta)
    {
        if (_session is null) return;
        _time += (float)delta;
        var target = Target();
        var diff = target - _shown;
        // Whole days skipped (fast-forward, load) look the same: drop them, and go the short way round the clock (a load
        // 10 h earlier eases back, not forward through the night). What is left eases in, quickly but never in one frame.
        var days = Math.Round(diff / 24.0);
        _shown += days * 24;
        diff -= days * 24;
        _shown += diff * (1 - Math.Exp(-delta * 6));
        if (Math.Abs(target - _shown) < 1e-4) _shown = target;
        Apply(_shown);
    }

    private double Target()
    {
        if (FixedHour is { } h) return h;
        // Pausing or changing speed resets the session's progress into the tick; never step back within a tick for it.
        var tick = _session.Sim.State.Tick;
        if (tick != _progressTick)
        {
            _progressTick = tick;
            _progress = 0;
        }
        _progress = Math.Max(_progress, _session.Speed == 0 ? 0 : _session.TickProgress);
        return tick + _progress;
    }

    private void Apply(double time)
    {
        if (double.IsNaN(_shown)) _shown = time;
        var hour = (float)DayNight.HourOf(time, _session.Sim.Data.Rules);
        var k = Sample(hour);

        // The sun rises in the east, climbs to ~62° at midday and sets in the west; it never dips below 6° so its
        // shadows stay sane while it fades.
        var t = Mathf.Clamp((hour - 5f) / 15f, 0f, 1f);
        var elevation = Mathf.Max(6f, Mathf.Sin(Mathf.Pi * t) * 62f);
        _sun.RotationDegrees = new Vector3(-elevation, Mathf.Lerp(-110f, 70f, t), 0);
        _sun.LightColor = k.Sun;
        _sun.LightEnergy = k.SunEnergy;
        _sun.Visible = k.SunEnergy > 0.01f;
        _moon.LightColor = k.Moon;
        _moon.LightEnergy = k.MoonEnergy;
        _moon.Visible = k.MoonEnergy > 0.01f;

        _env.BackgroundColor = k.Sky;
        _env.AmbientLightColor = k.Ambient;
        _env.AmbientLightEnergy = k.AmbientEnergy;
        _env.FogLightColor = k.Sky.Lerp(k.Ambient, 0.35f);
        _env.FogDensity = k.Fog;
        Night = k.Lamps;

        _lights.RemoveAll(l => !IsInstanceValid(l.Light) || l.Light.IsQueuedForDeletion());
        foreach (var l in _lights)
        {
            var on = l.On?.Invoke() ?? true;
            var energy = on ? Mathf.Lerp(l.DayEnergy, l.NightEnergy, Night) : 0;
            if (l.Flicker) energy *= 0.82f + 0.1f * Mathf.Sin(_time * 9.1f + l.Seed) + 0.08f * Mathf.Sin(_time * 23.7f + l.Seed * 1.7f);
            l.Light.LightEnergy = energy;
            l.Light.Visible = energy > 0.01f;
        }
        _glows.RemoveAll(g => !IsInstanceValid(g.Owner) || g.Owner.IsQueuedForDeletion());
        foreach (var g in _glows)
            g.Material.EmissionEnergyMultiplier = (g.On?.Invoke() ?? true) ? Mathf.Lerp(g.DayEnergy, g.NightEnergy, Night) : 0;
    }

    private static Key Sample(float hour)
    {
        for (var i = 0; i < Keys.Length - 1; i++)
        {
            var (a, b) = (Keys[i], Keys[i + 1]);
            if (hour > b.Hour) continue;
            var f = Mathf.SmoothStep(0f, 1f, (hour - a.Hour) / (b.Hour - a.Hour));
            return new Key(hour, a.Sun.Lerp(b.Sun, f), Mathf.Lerp(a.SunEnergy, b.SunEnergy, f), a.Moon.Lerp(b.Moon, f),
                Mathf.Lerp(a.MoonEnergy, b.MoonEnergy, f), a.Ambient.Lerp(b.Ambient, f), Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, f),
                a.Sky.Lerp(b.Sky, f), Mathf.Lerp(a.Fog, b.Fog, f), Mathf.Lerp(a.Lamps, b.Lamps, f));
        }
        return Keys[^1];
    }
}
