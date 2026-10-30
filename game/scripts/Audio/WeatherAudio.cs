using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Remnants.Game.Audio;

/// <summary>
/// The weather slot in the ambience layer, ready for a weather system to drive; nothing here decides the weather.
/// <see cref="Set"/> crossfades to a kind's loops (<c>sound_events.json</c> "weather": kind → loop ids) at an
/// intensity; <see cref="Lightning"/> cracks now and rolls thunder after a delay that grows with distance;
/// <see cref="Gust"/> rattles debris. Debug: <c>--weather=KIND[:INTENSITY]</c> (e.g. <c>--weather=heavy_rain:0.8</c>).
/// </summary>
public partial class WeatherAudio : Node
{
    private const float FadeSeconds = 3f;

    private readonly Dictionary<string, AmbientLoop> _loops = new();

    public static WeatherAudio? Instance { get; private set; }

    public string Kind { get; private set; } = "none";
    public float Intensity { get; private set; }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // A bed id here (heavy_rain, wind); Core's weather ids are the colony's and the mission's to set (WeatherFx).
        if (DebugCapture.Value("--weather") is { Length: > 0 } arg && AudioManager.Instance?.Map.Weather.ContainsKey(arg.Split(':')[0]) == true)
        {
            var parts = arg.Split(':');
            Set(parts[0], parts.Length > 1 && float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : 1);
        }
    }

    /// <summary>Crossfades to <paramref name="kind"/>'s loops at <paramref name="intensity"/> (0..1); "none" or 0 fades out.</summary>
    public static void Set(string kind, float intensity) => Instance?.Apply(kind, intensity);

    /// <summary>A lightning crack, then thunder after a delay: <paramref name="distance"/> 0 (overhead) .. 1 (far, ~4 s).</summary>
    public static void Lightning(float distance = 0.5f)
    {
        AudioManager.Fire("weather.lightning");
        if (Instance is not { } weather) return;
        var timer = weather.GetTree().CreateTimer(0.2 + Mathf.Clamp(distance, 0, 1) * 3.8, processAlways: true);
        timer.Timeout += () => AudioManager.Fire("weather.thunder");
    }

    /// <summary>A gust rattling loose debris (dust storms, strong wind).</summary>
    public static void Gust() => AudioManager.Fire("weather.debris");

    private void Apply(string kind, float intensity)
    {
        Kind = kind;
        Intensity = Mathf.Clamp(intensity, 0, 1);
        if (AudioManager.Instance?.Map.Weather is not { } table) return;
        var wanted = table.GetValueOrDefault(kind) ?? [];
        if (kind != "none" && wanted.Length == 0) AudioManager.Instance.WarnOnce($"weather:{kind}", $"Audio: weather '{kind}' has no loops in sound_events.json.");
        foreach (var id in table.Values.SelectMany(ids => ids).Distinct())
        {
            if (!_loops.TryGetValue(id, out var loop))
            {
                if (!wanted.Contains(id)) continue;
                _loops[id] = loop = new AmbientLoop(this, id, fadeSeconds: FadeSeconds);
            }
            loop.Level = wanted.Contains(id) ? Intensity : 0;
        }
        if (DebugCapture.HasFlag("--audio-trace")) GD.Print($"[audio] weather {kind} {Intensity:0.##}");
    }

    public override void _Process(double delta)
    {
        foreach (var loop in _loops.Values) loop.Update((float)delta);
    }
}
