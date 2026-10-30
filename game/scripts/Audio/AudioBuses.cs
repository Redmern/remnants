using Godot;

namespace Remnants.Game.Audio;

/// <summary>
/// The bus layout, made at boot if the project has none: Master (light compression, then a hard limiter) →
/// Music, SFX (→ UI, World), Ambience. Music and Ambience start with an Amplify effect that <see cref="Duck"/> lowers
/// under stings, so ducking never touches the player's volume settings.
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master", Music = "Music", Sfx = "SFX", Ui = "UI", World = "World", Ambience = "Ambience";

    /// <summary>The Amplify slot on <see cref="Music"/> and <see cref="Ambience"/> used for ducking.</summary>
    private const int DuckEffect = 0;

    public static void Ensure()
    {
        Add(Music, Master, duck: true);
        Add(Sfx, Master);
        Add(Ui, Sfx);
        Add(World, Sfx);
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
        foreach (var name in new[] { Music, Ambience })
        {
            var bus = AudioServer.GetBusIndex(name);
            if (bus >= 0 && AudioServer.GetBusEffectCount(bus) > DuckEffect && AudioServer.GetBusEffect(bus, DuckEffect) is AudioEffectAmplify amp)
                amp.VolumeDb = db;
        }
    }

    /// <summary>The bus a manifest category plays on when the event map does not say.</summary>
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
