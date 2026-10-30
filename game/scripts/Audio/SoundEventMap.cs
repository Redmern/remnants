using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Remnants.Game.Audio;

/// <summary>Music and ambience dip under a sting: by <see cref="Db"/>, held <see cref="Hold"/> s, back over <see cref="Release"/> s.</summary>
public sealed record DuckSpec(float Db = -9, float Hold = 1.2f, float Release = 1.5f);

/// <summary>
/// How a sound plays, from <c>sound_events.json</c> "sounds" (anything left out falls back to the manifest's default
/// volume and the bus for its category). <see cref="Cooldown"/> and <see cref="MaxVoices"/> keep ten footsteps in one
/// frame from stacking; <see cref="UnitSize"/> / <see cref="MaxDistance"/> are the 3D attenuation in metres.
/// </summary>
public sealed record SoundTuning(
    string? Bus = null, float VolumeDb = 0, float PitchJitter = 0.04f, float VolumeJitter = 1.5f, float Cooldown = 0.04f,
    int MaxVoices = 4, DuckSpec? Duck = null, float UnitSize = 18, float MaxDistance = 90);

/// <summary>What a game event plays: one of <see cref="Sounds"/> at random, and the visual cue it raises (if any).</summary>
public sealed record SoundEvent(string Name, IReadOnlyList<string> Sounds, string? Cue = null);

/// <summary>
/// The data-driven event → sound map in <c>res://audio/sound_events.json</c>, so retuning is a data edit:
/// <list type="bullet">
/// <item><c>events</c>: event name → a sound id, a list of ids (one picked at random), or
/// <c>{ "sounds": [...], "cue": "ambush"|"contact" }</c>. Names are dotted (<c>colony.constructed</c>,
/// <c>scavenge.attack.shoot</c>) and look-ups fall back by dropping the last part (<see cref="Resolve"/>).</item>
/// <item><c>sounds</c>: per-id <see cref="SoundTuning"/>.</item>
/// <item><c>music</c>: music state → track id; <c>layers</c>: layer name → track id played on top.</item>
/// <item><c>ambience</c>: <c>colony_day</c>, <c>colony_night</c>, <c>campfire</c>, <c>work</c> (building def id → loop,
/// with <c>default</c>); <c>zones</c>: zone id → ambience tags; <c>zoneAmbience</c>: tag → loop id.</item>
/// </list>
/// </summary>
public sealed class SoundEventMap
{
    public const string Path = "res://audio/sound_events.json";

    private readonly Dictionary<string, SoundEvent> _events = new();
    private readonly Dictionary<string, SoundTuning> _tuning = new();

    public IReadOnlyDictionary<string, SoundEvent> Events => _events;
    public Dictionary<string, string> Music { get; } = new();
    public Dictionary<string, string> Layers { get; } = new();
    public Dictionary<string, string> Ambience { get; } = new();
    public Dictionary<string, string> WorkLoops { get; } = new();
    public Dictionary<string, string[]> ZoneTags { get; } = new();
    public Dictionary<string, string> ZoneAmbience { get; } = new();

    public static SoundEventMap Load(string path = Path)
    {
        var map = new SoundEventMap();
        if (!FileAccess.FileExists(path))
        {
            GD.PushWarning($"Audio: no {path}; every event is silent.");
            return map;
        }
        try
        {
            map.Read(FileAccess.GetFileAsString(path));
        }
        catch (JsonException e)
        {
            GD.PushError($"Audio: could not read {path}: {e.Message}");
        }
        return map;
    }

    public void Read(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.TryGetProperty("events", out var events))
            foreach (var e in events.EnumerateObject())
                if (!e.Name.StartsWith("//") && ParseEvent(e.Name, e.Value) is { } ev) _events[e.Name] = ev;
        if (root.TryGetProperty("sounds", out var sounds))
            foreach (var s in sounds.EnumerateObject()) _tuning[s.Name] = ParseTuning(s.Value);
        Strings(root, "music", Music);
        Strings(root, "layers", Layers);
        Strings(root, "zoneAmbience", ZoneAmbience);
        if (root.TryGetProperty("ambience", out var amb))
            foreach (var a in amb.EnumerateObject())
                if (a.Value.ValueKind == JsonValueKind.String) Ambience[a.Name] = a.Value.GetString()!;
                else if (a.Name == "work") Strings(amb, "work", WorkLoops);
        if (root.TryGetProperty("zones", out var zones))
            foreach (var z in zones.EnumerateObject())
                ZoneTags[z.Name] = z.Value.ValueKind == JsonValueKind.Array
                    ? z.Value.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToArray()
                    : [];
    }

    /// <summary>
    /// The event for <paramref name="name"/>, or the nearest mapped parent: <c>scavenge.attack.aimed_shot</c> →
    /// <c>scavenge.attack</c>. Null when nothing on the way is mapped.
    /// </summary>
    public SoundEvent? Resolve(string name)
    {
        for (var key = name; key.Length > 0; key = key.Contains('.') ? key[..key.LastIndexOf('.')] : "")
            if (_events.TryGetValue(key, out var e)) return e;
        return null;
    }

    public SoundTuning Tuning(string soundId) => _tuning.GetValueOrDefault(soundId) ?? new SoundTuning();

    public bool HasTuning(string soundId) => _tuning.ContainsKey(soundId);

    /// <summary>Every sound id the map can ask for, for checks (each should be in the manifest once it lands).</summary>
    public IEnumerable<string> AllSoundIds() =>
        _events.Values.SelectMany(e => e.Sounds).Concat(Music.Values).Concat(Layers.Values).Concat(Ambience.Values)
            .Concat(WorkLoops.Values).Concat(ZoneAmbience.Values).Where(id => id.Length > 0).Distinct();

    private static SoundEvent? ParseEvent(string name, JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.String: return new SoundEvent(name, [v.GetString()!]);
            case JsonValueKind.Array: return new SoundEvent(name, Ids(v));
            case JsonValueKind.Object:
                var sounds = v.TryGetProperty("sounds", out var list) ? Ids(list)
                    : v.TryGetProperty("sound", out var one) && one.ValueKind == JsonValueKind.String ? [one.GetString()!] : [];
                var cue = v.TryGetProperty("cue", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                return new SoundEvent(name, sounds, cue);
            default: return null;
        }
    }

    private static string[] Ids(JsonElement v) =>
        v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
        : v.ValueKind == JsonValueKind.String ? [v.GetString()!] : [];

    private static SoundTuning ParseTuning(JsonElement v)
    {
        var t = new SoundTuning();
        if (v.ValueKind != JsonValueKind.Object) return t;
        float F(string n, float d) => v.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetSingle() : d;
        DuckSpec? duck = null;
        if (v.TryGetProperty("duck", out var dk))
            duck = dk.ValueKind switch
            {
                JsonValueKind.True => new DuckSpec(),
                JsonValueKind.Object => new DuckSpec(
                    dk.TryGetProperty("db", out var db) ? db.GetSingle() : -9,
                    dk.TryGetProperty("hold", out var h) ? h.GetSingle() : 1.2f,
                    dk.TryGetProperty("release", out var r) ? r.GetSingle() : 1.5f),
                _ => null,
            };
        return t with
        {
            Bus = v.TryGetProperty("bus", out var bus) && bus.ValueKind == JsonValueKind.String ? bus.GetString() : null,
            VolumeDb = F("volumeDb", t.VolumeDb),
            PitchJitter = F("pitchJitter", t.PitchJitter),
            VolumeJitter = F("volumeJitter", t.VolumeJitter),
            Cooldown = F("cooldown", t.Cooldown),
            MaxVoices = (int)F("maxVoices", t.MaxVoices),
            UnitSize = F("unitSize", t.UnitSize),
            MaxDistance = F("maxDistance", t.MaxDistance),
            Duck = duck,
        };
    }

    private static void Strings(JsonElement root, string name, Dictionary<string, string> into)
    {
        if (!root.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return;
        // [] marks a key as deliberately silent (kept as ""), so it does not fall back to a default.
        foreach (var p in obj.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) into[p.Name] = p.Value.GetString()!;
            else if (p.Value.ValueKind == JsonValueKind.Array) into[p.Name] = "";
    }
}
