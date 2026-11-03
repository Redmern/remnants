using System;
using Godot;
using Remnants.Game.Audio;
using Remnants.Game.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game.WeatherFx;

/// <summary>
/// Purely visual weather over a scene, colony or tactical map alike: GPU particles for rain, dust and blown debris kept
/// around what the camera looks at, a grade on the time of day's light (<see cref="Look"/>, plugged into
/// <see cref="DayNightLighting.Weather"/>: darker and wetter under rain, a brown cast in a dust storm, ground mist in
/// fog), and lightning flashes. <see cref="Set"/> crossfades to a weather id from Core over a few seconds.
/// <see cref="GameSettings.WeatherEffects"/> scales it: Reduced draws a third of the particles and never flashes
/// (thunder still sounds), Off keeps only the tint. Nothing here decides the weather. Debug: <c>--lightning</c> holds a
/// flash in a thunderstorm, for a still.
/// </summary>
public partial class WeatherView : Node3D
{
    private const float FadeSeconds = 3f;

    private Camera3D? _camera;
    private float _area = 24f;
    private GpuParticles3D _rain = null!;
    private GpuParticles3D _dust = null!;
    private GpuParticles3D _debris = null!;
    private string _from = "clear";
    private float _fromIntensity;
    private float _blend = 1f;
    private float _flash;
    private float _nextBolt = 6f;
    private float _nextGust = 9f;

    /// <summary>The weather id shown (Core's: clear, overcast, rain, thunderstorm, fog, dust_storm).</summary>
    public string Kind { get; private set; } = "clear";
    public float Intensity { get; private set; }

    /// <summary>Strike lightning on its own now and then in a thunderstorm (the colony); the field flashes on Core's events.</summary>
    public bool AmbientLightning { get; set; } = true;

    /// <summary>
    /// Follow <paramref name="camera"/> (particles fill a box <paramref name="area"/> wide around where it looks), and grade
    /// <paramref name="lighting"/>'s sky.
    /// </summary>
    public void Setup(Camera3D camera, DayNightLighting? lighting, float area = 24f)
    {
        _camera = camera;
        _area = area;
        if (lighting is not null) lighting.Weather = Look;
        _rain = Particles("Rain", 2600, Streak(), RainProcess(), 1.1f);
        _dust = Particles("Dust", 900, Puff(), DustProcess(), 3.2f);
        _debris = Particles("Debris", 90, Chip(), DebrisProcess(), 2.2f);
    }

    /// <summary>
    /// Crossfades to <paramref name="kind"/> at <paramref name="intensity"/> (0..1), or switches at once with
    /// <paramref name="instant"/> (entering a scene). Null or unknown ids are clear.
    /// </summary>
    public void Set(string? kind, double intensity, bool instant = false)
    {
        kind = string.IsNullOrEmpty(kind) ? "clear" : kind;
        var i = Mathf.Clamp((float)intensity, 0, 1);
        if (kind == Kind && Mathf.IsEqualApprox(Intensity, i)) return;
        _from = Kind;
        _fromIntensity = Intensity;
        _blend = instant ? 1 : 0;
        Kind = kind;
        Intensity = i;
        if (instant) _from = Kind;
    }

    /// <summary>A lightning flash (unless the settings say no flashes) and its crack and thunder; <paramref name="distance"/> 0..1.</summary>
    /// <param name="flash">False holds the sky's flash back (the scavenge screen's strobing cap); the thunder still rolls.</param>
    public void Strike(float distance = 0.4f, bool flash = true)
    {
        if (flash && GameSettings.WeatherEffects == WeatherEffects.Full) _flash = Mathf.Lerp(1f, 0.45f, distance);
        WeatherAudio.Lightning(distance);
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _blend = Mathf.Min(1, _blend + dt / FadeSeconds);
        _flash = Mathf.Max(DebugCapture.HasFlag("--lightning") && Kind == "thunderstorm" ? 0.55f : 0, _flash - dt * 4f);
        Follow();

        var scale = GameSettings.WeatherEffects switch { WeatherEffects.Full => 1f, WeatherEffects.Reduced => 0.35f, _ => 0f };
        Show(_rain, scale * (Amount("rain") * 0.75f + Amount("thunderstorm")));
        Show(_dust, scale * Amount("dust_storm"));
        Show(_debris, scale * Amount("dust_storm"));

        if (AmbientLightning && Kind == "thunderstorm" && (_nextBolt -= dt) <= 0)
        {
            _nextBolt = (float)GD.RandRange(5, 16) / Mathf.Max(0.4f, Intensity);
            Strike((float)GD.RandRange(0.1, 1));
        }
        if (Kind == "dust_storm" && (_nextGust -= dt) <= 0)
        {
            _nextGust = (float)GD.RandRange(7, 18);
            WeatherAudio.Gust();
        }
    }

    /// <summary>How much of <paramref name="kind"/> is on screen now (its intensity, faded in or out).</summary>
    private float Amount(string kind) =>
        (Kind == kind ? Intensity * _blend : 0) + (_from == kind && _from != Kind ? _fromIntensity * (1 - _blend) : 0);

    /// <summary>The time of day's look under the weather: the outgoing kind's grade fading into the new one's, plus any flash.</summary>
    public DayNightLighting.SkyLook Look(DayNightLighting.SkyLook k)
    {
        var to = Grade(k, Kind, Intensity);
        var look = _blend >= 1 ? to : Lerp(Grade(k, _from, _fromIntensity), to, _blend);
        if (_flash <= 0) return look;
        return look with
        {
            Ambient = look.Ambient.Lerp(new Color(0.85f, 0.9f, 1f), _flash),
            AmbientEnergy = look.AmbientEnergy + 1.8f * _flash,
            Sky = look.Sky.Lerp(new Color(0.75f, 0.8f, 0.95f), _flash * 0.5f),
        };
    }

    private static DayNightLighting.SkyLook Grade(DayNightLighting.SkyLook k, string kind, float i)
    {
        if (i <= 0) return k;
        Color Grey(Color c, float amount) => c.Lerp(new Color(c.V, c.V, c.V), amount);
        switch (kind)
        {
            case "overcast":
                return k with { SunEnergy = k.SunEnergy * (1 - 0.45f * i), Sun = Grey(k.Sun, 0.5f * i), Sky = Grey(k.Sky, 0.5f * i), Ambient = Grey(k.Ambient, 0.3f * i) };
            case "rain":
            case "thunderstorm":
                var storm = kind == "thunderstorm" ? 1f : 0.6f;
                // Wet: darker, cooler, the light flattened; the ground reads as darker under the lower ambient.
                return k with
                {
                    SunEnergy = k.SunEnergy * (1 - 0.8f * storm * i), MoonEnergy = k.MoonEnergy * (1 - 0.6f * i),
                    Sun = Grey(k.Sun, 0.7f * i), Sky = Grey(k.Sky, 0.6f * i).Darkened(0.35f * storm * i).Lerp(new Color(0.16f, 0.19f, 0.24f), 0.3f * i),
                    Ambient = Grey(k.Ambient, 0.4f * i).Lerp(new Color(0.45f, 0.52f, 0.6f), 0.25f * i),
                    AmbientEnergy = k.AmbientEnergy * (1 - 0.25f * storm * i), Fog = k.Fog + 0.006f * i, GroundFog = k.GroundFog + 0.015f * i,
                };
            case "fog":
                return k with
                {
                    SunEnergy = k.SunEnergy * (1 - 0.55f * i), Sky = k.Sky.Lerp(new Color(0.55f, 0.58f, 0.6f) * Mathf.Max(0.25f, k.AmbientEnergy), 0.7f * i),
                    Ambient = Grey(k.Ambient, 0.5f * i), Fog = k.Fog + 0.012f * i, GroundFog = k.GroundFog + 0.12f * i,
                };
            case "dust_storm":
                var brown = new Color(0.72f, 0.5f, 0.3f);
                return k with
                {
                    Sun = k.Sun.Lerp(new Color(1f, 0.62f, 0.32f), 0.7f * i), SunEnergy = k.SunEnergy * (1 - 0.55f * i),
                    Sky = k.Sky.Lerp(brown * Mathf.Max(0.3f, k.AmbientEnergy), 0.8f * i),
                    Ambient = k.Ambient.Lerp(brown, 0.5f * i), Fog = k.Fog + 0.007f * i, GroundFog = k.GroundFog + 0.035f * i,
                };
            default:
                return k;
        }
    }

    private static DayNightLighting.SkyLook Lerp(DayNightLighting.SkyLook a, DayNightLighting.SkyLook b, float t) => b with
    {
        Sun = a.Sun.Lerp(b.Sun, t), SunEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, t), Moon = a.Moon.Lerp(b.Moon, t),
        MoonEnergy = Mathf.Lerp(a.MoonEnergy, b.MoonEnergy, t), Ambient = a.Ambient.Lerp(b.Ambient, t),
        AmbientEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t), Sky = a.Sky.Lerp(b.Sky, t), Fog = Mathf.Lerp(a.Fog, b.Fog, t),
        GroundFog = Mathf.Lerp(a.GroundFog, b.GroundFog, t),
    };

    /// <summary>Keeps the emitters over the ground point the camera looks at.</summary>
    private void Follow()
    {
        if (_camera is null || !IsInstanceValid(_camera)) return;
        var from = _camera.GlobalPosition;
        var dir = -_camera.GlobalBasis.Z;
        var t = Mathf.Abs(dir.Y) > 0.05f ? -from.Y / dir.Y : 20f;
        var focus = from + dir * Mathf.Clamp(t, 0, 200);
        GlobalPosition = new Vector3(focus.X, 0, focus.Z);
    }

    private static void Show(GpuParticles3D p, float amount)
    {
        var on = amount > 0.01f;
        if (p.Emitting != on) p.Emitting = on;
        if (on) p.AmountRatio = Mathf.Clamp(amount, 0, 1);
    }

    private GpuParticles3D Particles(string name, int amount, Mesh mesh, ParticleProcessMaterial process, float lifetime)
    {
        var p = new GpuParticles3D
        {
            Name = name, Amount = amount, Lifetime = lifetime, DrawPass1 = mesh, ProcessMaterial = process, Emitting = false,
            LocalCoords = false, VisibilityAabb = new Aabb(new Vector3(-_area, -2, -_area), new Vector3(2 * _area, 24, 2 * _area)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        return p;
    }

    private ParticleProcessMaterial RainProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(_area, 0.5f, _area),
        Direction = new Vector3(0.12f, -1, 0.05f), Spread = 2, InitialVelocityMin = 15, InitialVelocityMax = 19, Gravity = new Vector3(0, -6, 0),
    };

    private ParticleProcessMaterial DustProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(_area, 2.5f, _area),
        Direction = new Vector3(1, 0.05f, 0.35f), Spread = 12, InitialVelocityMin = 6, InitialVelocityMax = 11, Gravity = Vector3.Zero,
        ScaleMin = 0.6f, ScaleMax = 2.2f, TurbulenceEnabled = true, TurbulenceNoiseStrength = 1.4f, TurbulenceInfluenceMin = 0.05f, TurbulenceInfluenceMax = 0.15f,
    };

    private ParticleProcessMaterial DebrisProcess() => new()
    {
        EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(_area, 1.2f, _area),
        Direction = new Vector3(1, 0.2f, 0.3f), Spread = 20, InitialVelocityMin = 9, InitialVelocityMax = 14, Gravity = new Vector3(0, -2, 0),
        AngularVelocityMin = -540, AngularVelocityMax = 540, ScaleMin = 0.5f, ScaleMax = 1.4f,
    };

    private static QuadMesh Streak() => new()
    {
        Size = new Vector2(0.07f, 1.5f),
        Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.8f, 0.86f, 0.96f, 0.55f), BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            BillboardKeepScale = true,
        },
    };

    private static QuadMesh Puff() => new()
    {
        Size = new Vector2(1.6f, 1.6f),
        Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.66f, 0.48f, 0.3f, 0.12f), AlbedoTexture = SoftDot(),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, BillboardKeepScale = true,
        },
    };

    private static BoxMesh Chip() => new()
    {
        Size = new Vector3(0.12f, 0.02f, 0.08f),
        Material = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.2f, 0.16f), Roughness = 1 },
    };

    private static GradientTexture2D SoftDot()
    {
        var g = new Gradient();
        g.SetColor(0, Colors.White);
        g.SetColor(1, new Color(1, 1, 1, 0));
        return new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 64, Height = 64 };
    }
}
