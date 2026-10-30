using Godot;

namespace Remnants.Game.Audio;

/// <summary>
/// The bus layout, made at boot if the project has none: Master (light compression, then a hard limiter) →
/// Music, SFX (→ UI, World, SFXStereo for non-positional stereo one-shots like thunder), Ambience. Music and
/// Ambience start with an Amplify effect that holds their mix trim and that <see cref="Duck"/> lowers under stings;
/// the SFX sub-buses carry their trim as bus volume. Trims come from <c>sound_events.json</c> "mix" (<see cref="SetTrims"/>),
/// so neither ducking nor the mix touches the player's volume settings.
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master", Music = "Music", Sfx = "SFX", Ui = "UI", World = "World", SfxStereo = "SFXStereo", Ambience = "Ambience";

    /// <summary>The Amplify slot on <see cref="Music"/> and <see cref="Ambience"/> used for ducking.</summary>
    private const int DuckEffect = 0;

    private static readonly System.Collections.Generic.Dictionary<string, float> Trims = new();
    private static float _duck;

    /// <summary>The mix: dB per bus on top of the player's volumes (Music and Ambience through their Amplify, sub-buses as volume).</summary>
    public static void SetTrims(System.Collections.Generic.IReadOnlyDictionary<string, float> trims)
    {
        Trims.Clear();
        foreach (var (bus, db) in trims) Trims[bus] = db;
        foreach (var name in new[] { Ui, World, SfxStereo })
            if (AudioServer.GetBusIndex(name) is var i and >= 0) AudioServer.SetBusVolumeDb(i, Trims.GetValueOrDefault(name));
        Duck(_duck);
    }

    public static void Ensure()
    {
        Add(Music, Master, duck: true);
        Add(Sfx, Master);
        Add(Ui, Sfx);
        Add(World, Sfx);
        Add(SfxStereo, Sfx);
        Add(Ambience, Master, duck: true);

        var master = AudioServer.GetBusIndex(Master);
        if (AudioServer.GetBusEffectCount(master) == 0)
        {
            AudioServer.AddBusEffect(master, new AudioEffectCompressor { Threshold = -14, Ratio = 3, AttackUs = 15000, ReleaseMs = 180, Gain = 0 });
            AudioServer.AddBusEffect(master, new AudioEffectHardLimiter { CeilingDb = -0.8f, PreGainDb = 0 });
        }
    }

    /// <summary>Sets the duck level (dB ≤ 0) on Music and Ambience.</summary>
    public static void Duck(float db)
    {
        _duck = db;
        foreach (var name in new[] { Music, Ambience })
        {
            var bus = AudioServer.GetBusIndex(name);
            if (bus >= 0 && AudioServer.GetBusEffectCount(bus) > DuckEffect && AudioServer.GetBusEffect(bus, DuckEffect) is AudioEffectAmplify amp)
                amp.VolumeDb = Trims.GetValueOrDefault(name) + db;
        }
    }

    /// <summary>The game bus for the pipeline's mix bus name, or null when it names none.</summary>
    public static string? ForManifest(string bus) => bus.ToLowerInvariant() switch
    {
        "music" or "layer" => Music,
        "ambience" => Ambience,
        "ui" => Ui,
        "sting" => Sfx,
        "sfx_stereo" => SfxStereo,
        "sfx" => World,
        _ => null,
    };

    /// <summary>The bus a manifest category plays on when neither the event map nor the manifest's bus says.</summary>
    public static string ForCategory(string category) => category.ToLowerInvariant() switch
    {
        "music" or "sting" or "music_sting" => Music,
        "ambience" or "ambient" or "loop" => Ambience,
        "ui" => Ui,
        "voice" or "bark" => World,
        _ => World,
    };

    private static void Add(string name, string send, bool duck = false)
    {
        var index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
        }
        AudioServer.SetBusSend(index, send);
        if (duck && AudioServer.GetBusEffectCount(index) == 0) AudioServer.AddBusEffect(index, new AudioEffectAmplify { VolumeDb = 0 });
    }
}
