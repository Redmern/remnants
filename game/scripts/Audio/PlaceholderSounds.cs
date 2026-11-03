using System;
using System.Collections.Generic;
using Godot;

namespace Remnants.Game.Audio;

/// <summary>
/// A handful of tiny synthesised sounds (UI blips, two stings) that stand in until the remnants-assets audio export
/// lands. Used only for ids the manifest does not have; everything else without a file stays silent.
/// </summary>
public static class PlaceholderSounds
{
    private const int Rate = 22050;

    private sealed record Recipe(float FreqFrom, float FreqTo, float Seconds, float Noise = 0, float Volume = 0.5f, float Attack = 0.004f);

    private static readonly Dictionary<string, Recipe> Recipes = new()
    {
        ["ui_click"] = new(1400, 900, 0.035f, Volume: 0.35f),
        ["ui_hover"] = new(2200, 2100, 0.018f, Volume: 0.12f),
        ["ui_confirm"] = new(660, 990, 0.12f, Volume: 0.4f),
        ["ui_error"] = new(220, 160, 0.18f, Noise: 0.15f, Volume: 0.45f),
        ["ui_panel_open"] = new(500, 800, 0.08f, Noise: 0.25f, Volume: 0.25f),
        ["ui_panel_close"] = new(800, 480, 0.08f, Noise: 0.25f, Volume: 0.22f),
        ["ui_toast"] = new(880, 1320, 0.1f, Volume: 0.25f),
        ["sting_death"] = new(140, 70, 1.4f, Noise: 0.1f, Volume: 0.6f, Attack: 0.05f),
        ["sting_ambush"] = new(90, 420, 0.5f, Noise: 0.6f, Volume: 0.6f, Attack: 0.002f),
        // Tactics placeholders until real recordings: a card snapped down, a riffle, a low warning, an air swish.
        ["card_play"] = new(1800, 600, 0.07f, Noise: 0.5f, Volume: 0.3f, Attack: 0.002f),
        ["card_shuffle"] = new(2400, 1600, 0.28f, Noise: 0.85f, Volume: 0.22f),
        ["telegraph_warn"] = new(320, 260, 0.22f, Noise: 0.1f, Volume: 0.28f),
        ["dodge_whoosh"] = new(900, 200, 0.25f, Noise: 0.9f, Volume: 0.3f),
        // Juice placeholders (docs/MISSING_ASSETS.md): a card flicked off the deck, a card slammed onto a target, the
        // hand swept into the used pile, the squad's chain tick (pitched up a semitone a link), a heavy thump on a kill.
        ["card_draw"] = new(2600, 1900, 0.05f, Noise: 0.7f, Volume: 0.18f, Attack: 0.002f),
        ["card_slam"] = new(260, 90, 0.16f, Noise: 0.45f, Volume: 0.5f, Attack: 0.001f),
        ["card_sweep"] = new(1400, 500, 0.22f, Noise: 0.9f, Volume: 0.2f),
        ["chain_tick"] = new(880, 900, 0.09f, Noise: 0.05f, Volume: 0.28f, Attack: 0.002f),
        ["kill_thump"] = new(120, 45, 0.35f, Noise: 0.3f, Volume: 0.6f, Attack: 0.001f),
    };

    private static readonly Dictionary<string, AudioStreamWav> Cache = new();

    public static bool Has(string id) => Recipes.ContainsKey(id);

    public static IEnumerable<string> Ids => Recipes.Keys;

    public static AudioStreamWav? Get(string id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;
        if (!Recipes.TryGetValue(id, out var r)) return null;
        var frames = (int)(r.Seconds * Rate);
        var data = new byte[frames * 2];
        var phase = 0.0;
        var noise = new System.Random(id.GetHashCode());
        for (var i = 0; i < frames; i++)
        {
            var t = i / (float)frames;
            var freq = r.FreqFrom + (r.FreqTo - r.FreqFrom) * t;
            phase += 2 * Math.PI * freq / Rate;
            var envelope = Math.Min(1, i / (r.Attack * Rate)) * Math.Pow(1 - t, 2);
            var sample = ((1 - r.Noise) * Math.Sin(phase) + r.Noise * (noise.NextDouble() * 2 - 1)) * envelope * r.Volume;
            var s = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            data[i * 2] = (byte)(s & 0xff);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        var wav = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
        Cache[id] = wav;
        return wav;
    }
}
