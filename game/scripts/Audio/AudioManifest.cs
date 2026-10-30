using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Remnants.Game.Audio;

/// <summary>One stem of a layered music track: a file and the intensity it joins at (0 = always on).</summary>
public sealed record SoundStem(string File, string Layer, float Intensity);

/// <summary>
/// A sound in the audio manifest: its files (the main one first, then variants), loop points, default volume, and
/// for music the tempo and stems. Paths are resolved against the manifest's folder.
/// </summary>
public sealed record SoundAsset(
    string Id, string Category, IReadOnlyList<string> Files, double Duration, bool Loop, double LoopStart, double LoopEnd,
    float VolumeDb, IReadOnlyList<string> Tags, double Bpm, int BeatsPerBar, IReadOnlyList<SoundStem> Stems)
{
    /// <summary>The pipeline's mix bus (music, layer, ambience, sfx, sfx_stereo, ui, sting); see <see cref="AudioBuses.ForManifest"/>.</summary>
    public string Bus { get; init; } = "";

    /// <summary>Music that shares a sync group has the same tempo and length: switch between them at the same position.</summary>
    public string? SyncGroup { get; init; }

    /// <summary>Seconds per bar, for crossfades on bar-ish boundaries (4/4 at 90 bpm when the manifest says nothing).</summary>
    public double BarSeconds => 60.0 / (Bpm > 0 ? Bpm : 90) * (BeatsPerBar > 0 ? BeatsPerBar : 4);
}

/// <summary>
/// Reads <c>res://assets/generated/audio/manifest.json</c>, synced from the remnants-assets <c>export/audio/</c> by
/// <c>tools/sync-assets.sh</c>. Accepts <c>{ "sounds"|"assets": [...] }</c> or a bare array of
/// <c>{id, category, bus?, file, variants?, duration_s?, loop?: null|true|{start_s, end_s}, default_volume_db?|
/// default_volume?, tags?, music?: {bpm, bars, sync_group}, stems?}</c> (camelCase spellings too, and the older
/// <c>duration</c> / <c>loop_start</c> / <c>volume_db</c>); malformed entries are skipped. A missing manifest is an
/// empty one: the game stays silent apart from <see cref="PlaceholderSounds"/>.
/// </summary>
public sealed class AudioManifest
{
    public const string Root = "res://assets/generated/audio";

    private readonly Dictionary<string, SoundAsset> _sounds = new();

    public int Count => _sounds.Count;
    public string? Source { get; private set; }

    public bool TryGet(string id, out SoundAsset sound) => _sounds.TryGetValue(id, out sound!);

    public IEnumerable<SoundAsset> All => _sounds.Values;

    public static AudioManifest Load(string path = $"{Root}/manifest.json")
    {
        var manifest = new AudioManifest();
        if (!FileAccess.FileExists(path)) return manifest;
        try
        {
            manifest.Read(FileAccess.GetFileAsString(path), path.GetBaseDir());
            manifest.Source = path;
        }
        catch (JsonException e)
        {
            GD.PushWarning($"Audio: could not read {path}: {e.Message}");
        }
        return manifest;
    }

    /// <summary>Parses manifest JSON; <paramref name="dir"/> is what relative file paths are resolved against.</summary>
    public void Read(string json, string dir)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var list = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("sounds", out var s) ? s
            : root.TryGetProperty("assets", out var a) ? a : default;
        if (list.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in list.EnumerateArray())
            if (Parse(entry, dir) is { } sound) _sounds[sound.Id] = sound;
    }

    private static SoundAsset? Parse(JsonElement e, string dir)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (Str(e, "id") is not { Length: > 0 } id || Str(e, "file") is not { Length: > 0 } file) return null;
        string Path(string rel) => rel.StartsWith("res://") || rel.StartsWith("user://") || rel.StartsWith('/') ? rel : $"{dir}/{rel}";

        var files = new List<string> { Path(file) };
        if (Prop(e, "variants") is { ValueKind: JsonValueKind.Array } variants)
            foreach (var v in variants.EnumerateArray())
                if ((v.ValueKind == JsonValueKind.String ? v.GetString() : Str(v, "file")) is { Length: > 0 } rel && Path(rel) is var p && !files.Contains(p))
                    files.Add(p);

        var loopStart = Num(e, "loop_start") ?? 0;
        var loopEnd = Num(e, "loop_end") ?? 0;
        var loop = false;
        switch (Prop(e, "loop"))
        {
            case { ValueKind: JsonValueKind.True }: loop = true; break;
            case { ValueKind: JsonValueKind.Object } points:
                loop = true;
                loopStart = Num(points, "start_s") ?? Num(points, "start") ?? loopStart;
                loopEnd = Num(points, "end_s") ?? Num(points, "end") ?? loopEnd;
                break;
        }
        loop |= Num(e, "loop_start") is not null || Num(e, "loop_end") is not null;

        // "volume" is linear 0..1 (the pipeline's "default volume"); "volume_db" wins when both are given.
        var linear = Num(e, "volume") ?? Num(e, "default_volume");
        var volumeDb = (Num(e, "default_volume_db") ?? Num(e, "volume_db")) is { } db ? (float)db
            : linear is { } lin ? Mathf.LinearToDb((float)Math.Max(lin, 0.0001)) : 0f;

        var stems = new List<SoundStem>();
        if (Prop(e, "stems") is { ValueKind: JsonValueKind.Array } stemList)
            foreach (var st in stemList.EnumerateArray())
                if (Str(st, "file") is { Length: > 0 } rel)
                    stems.Add(new SoundStem(Path(rel), Str(st, "layer") ?? Str(st, "id") ?? "", (float)(Num(st, "intensity") ?? 0)));

        var tags = Prop(e, "tags") is { ValueKind: JsonValueKind.Array } t
            ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : [];
        // Music metadata sits in a "music" object in the pipeline's manifest, or flat.
        var music = Prop(e, "music") is { ValueKind: JsonValueKind.Object } m ? m : e;
        return new SoundAsset(id, Str(e, "category") ?? "", files, Num(e, "duration_s") ?? Num(e, "duration") ?? 0, loop, loopStart,
            loopEnd, volumeDb, tags, Num(music, "bpm") ?? 0, (int)(Num(music, "beats_per_bar") ?? 0), stems)
        {
            Bus = Str(e, "bus") ?? "",
            SyncGroup = Str(music, "sync_group"),
        };
    }

    /// <summary>A property by its snake_case name, or the camelCase spelling of it.</summary>
    private static JsonElement? Prop(JsonElement e, string snake)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        if (e.TryGetProperty(snake, out var v)) return v;
        var parts = snake.Split('_');
        var camel = parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return e.TryGetProperty(camel, out v) ? v : null;
    }

    private static string? Str(JsonElement e, string name) => Prop(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static double? Num(JsonElement e, string name) => Prop(e, name) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;
}
