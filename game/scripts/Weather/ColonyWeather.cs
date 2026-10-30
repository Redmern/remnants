using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Audio;
using Remnants.Game.Colony;

namespace Remnants.Game.WeatherFx;

/// <summary>
/// The colony's weather on screen and in the ears: follows Core's weather at home (<see cref="Core.Colony.Weather.Current"/>)
/// into the <see cref="WeatherView"/> and <see cref="WeatherAudio"/>. Debug: <c>--weather=KIND[:INTENSITY]</c> sets the
/// weather at home from now on (the chain carries on after it), for screenshots.
/// </summary>
public partial class ColonyWeather : Node
{
    private ColonySession _session = null!;
    private WeatherView _view = null!;
    private WeatherSpell? _shown;

    public void Setup(ColonySession session, WeatherView view)
    {
        _session = session;
        _view = view;
        if (DebugCapture.Value("--weather") is { Length: > 0 } arg) Force(arg);
        Refresh(instant: true);
    }

    public override void _Process(double delta) => Refresh(instant: false);

    private void Refresh(bool instant)
    {
        var sim = _session.Sim;
        var spell = Core.Colony.Weather.Current(sim.State, sim.Data);
        if (spell == _shown) return;
        _shown = spell;
        _view.Set(spell.Kind, spell.Intensity, instant);
        WeatherSounds.Set(spell.Kind, spell.Intensity);
    }

    /// <summary>Debug: a spell of the given kind over the colony from this tick for a long while.</summary>
    private void Force(string arg)
    {
        var sim = _session.Sim;
        var parts = arg.Split(':');
        var intensity = parts.Length > 1 && double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : 0.9;
        var tick = sim.State.Tick;
        var region = sim.Data.Weather.ColonyRegion;
        var spells = sim.State.Weather.Spells.TryGetValue(region, out var list) ? list : sim.State.Weather.Spells[region] = new();
        var now = Core.Colony.Weather.Current(sim.State, sim.Data);
        spells.RemoveAll(s => s.Start >= now.Start);
        spells.Add(new WeatherSpell(now.Index, parts[0], tick, tick + 24 * 30, intensity));
    }
}

/// <summary>Core's weather ids to the weather beds in <c>sound_events.json</c> (rain turns heavy from 0.75 up).</summary>
public static class WeatherSounds
{
    public static string AudioKind(string? kind, double intensity) => kind switch
    {
        "rain" => intensity >= 0.75 ? "heavy_rain" : "rain",
        "thunderstorm" => "heavy_rain",
        "dust_storm" => "dust_storm",
        "overcast" => "wind",
        _ => "none",
    };

    public static void Set(string? kind, double intensity) =>
        WeatherAudio.Set(AudioKind(kind, intensity), kind == "overcast" ? 0.3f : (float)Mathf.Clamp(0.45 + 0.55 * intensity, 0, 1));
}
