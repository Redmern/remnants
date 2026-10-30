using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;
using Remnants.Core.Data;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony;

/// <summary>
/// Model lookup layer. Reads the generated-assets manifest (synced from the remnants-assets repo's
/// <c>export/</c> into <c>res://assets/generated/</c>) and instances models by id. Building ids are
/// <c>{defId}_l{level}</c>; a missing level falls back to the closest lower one, and a missing model
/// to a placeholder primitive, so the game runs with no generated assets at all.
/// </summary>
public sealed class ModelLibrary
{
    private const string Root = "res://assets/generated";
    private static readonly string[] ManifestPaths = [$"{Root}/manifest.json", $"{Root}/export/manifest.json"];

    private readonly Dictionary<string, string> _files = new();
    private readonly Dictionary<string, string[]> _tags = new();
    private readonly Dictionary<string, Dictionary<string, string>> _params = new();
    private readonly Dictionary<string, Dictionary<string, AttachPoint>> _sockets = new();
    private readonly Dictionary<string, Dictionary<string, AttachPoint>> _attach = new();
    private readonly Dictionary<string, Dictionary<string, ClipInfo>> _clips = new();
    private readonly Dictionary<string, Dictionary<string, string[]>> _limbBones = new();
    private readonly Dictionary<string, Vector3> _dimensions = new();
    private readonly Dictionary<string, Dictionary<string, UpgradeSlot>> _upgradeSlots = new();
    private readonly Dictionary<string, PackedScene?> _scenes = new();
    private readonly Dictionary<string, GltfState?> _gltf = new();

    public int ModelCount => _files.Count;

    public static ModelLibrary Load()
    {
        var library = new ModelLibrary();
        foreach (var path in ManifestPaths)
        {
            if (!FileAccess.FileExists(path)) continue;
            library.ReadManifest(path);
            break;
        }
        return library;
    }

    /// <summary>
    /// Accepts <c>{ "assets": [ {id, category, file, tags?, params?}, ... ] }</c> or a bare array; skips malformed entries.
    /// Version 2 (rigs) adds <c>clips</c>, <c>limb_bones</c> and <c>sockets[slot].bone</c>; a v1 manifest has none of
    /// them and its models stay static.
    /// </summary>
    private void ReadManifest(string path)
    {
        var dir = path.GetBaseDir();
        try
        {
            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
            var assets = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("assets", out var a) ? a : default;
            if (assets.ValueKind != JsonValueKind.Array) return;
            foreach (var entry in assets.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!entry.TryGetProperty("id", out var id) || !entry.TryGetProperty("file", out var file)) continue;
                if (id.GetString() is not { Length: > 0 } key || file.GetString() is not { Length: > 0 } rel) continue;
                _files[key] = $"{dir}/{rel}";
                if (entry.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                    _tags[key] = tags.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToArray();
                if (entry.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Object)
                    _params[key] = ps.EnumerateObject().Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                        .ToDictionary(kv => kv.Name, kv => kv.Value.GetString()!);
                if (entry.TryGetProperty("dimensions_m", out var dims) && dims.ValueKind == JsonValueKind.Object
                    && dims.TryGetProperty("x", out var dx) && dims.TryGetProperty("y", out var dy) && dims.TryGetProperty("z", out var dz)
                    && dx.ValueKind == JsonValueKind.Number && dy.ValueKind == JsonValueKind.Number && dz.ValueKind == JsonValueKind.Number)
                    _dimensions[key] = new Vector3(dx.GetSingle(), dy.GetSingle(), dz.GetSingle());
                if (Points(entry, "sockets") is { } sockets) _sockets[key] = sockets;
                if (UpgradeSlots(entry) is { } slots) _upgradeSlots[key] = slots;
                if (Points(entry, "attach") is { } attach) _attach[key] = attach;
                if (ReadClips(entry) is { Count: > 0 } clips) _clips[key] = clips;
                if (entry.TryGetProperty("limb_bones", out var limbs) && limbs.ValueKind == JsonValueKind.Object)
                    _limbBones[key] = limbs.EnumerateObject().Where(kv => kv.Value.ValueKind == JsonValueKind.Array)
                        .ToDictionary(kv => kv.Name, kv => kv.Value.EnumerateArray().Where(b => b.ValueKind == JsonValueKind.String).Select(b => b.GetString()!).ToArray());
            }
        }
        catch (JsonException e)
        {
            GD.PushWarning($"Ignoring invalid model manifest {path}: {e.Message}");
        }
    }

    /// <summary><c>{ slot: { point: [x,y,z], part?, rot_deg? } }</c> under <paramref name="property"/>; malformed slots are skipped.</summary>
    private static Dictionary<string, AttachPoint>? Points(JsonElement entry, string property)
    {
        if (!entry.TryGetProperty(property, out var slots) || slots.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, AttachPoint>();
        foreach (var slot in slots.EnumerateObject())
        {
            if (slot.Value.ValueKind != JsonValueKind.Object || Vector(slot.Value, "point") is not { } point) continue;
            var part = slot.Value.TryGetProperty("part", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var bone = slot.Value.TryGetProperty("bone", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            result[slot.Name] = new AttachPoint(point, Vector(slot.Value, "rot_deg") ?? Vector3.Zero, part, bone);
        }
        return result;
    }

    /// <summary><c>clips: [ { name, length_s, loop, events: { impact: [0.38] } } ]</c>; malformed clips are skipped.</summary>
    private static Dictionary<string, ClipInfo>? ReadClips(JsonElement entry)
    {
        if (!entry.TryGetProperty("clips", out var clips) || clips.ValueKind != JsonValueKind.Array) return null;
        var result = new Dictionary<string, ClipInfo>();
        foreach (var clip in clips.EnumerateArray())
        {
            if (clip.ValueKind != JsonValueKind.Object || !clip.TryGetProperty("name", out var n) || n.GetString() is not { Length: > 0 } name) continue;
            var length = clip.TryGetProperty("length_s", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetSingle() : 0f;
            var loop = clip.TryGetProperty("loop", out var lp) && lp.ValueKind == JsonValueKind.True;
            var events = new Dictionary<string, float[]>();
            if (clip.TryGetProperty("events", out var ev) && ev.ValueKind == JsonValueKind.Object)
                foreach (var e in ev.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Array))
                    events[e.Name] = e.Value.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Number).Select(t => t.GetSingle()).Order().ToArray();
            result[name] = new ClipInfo(name, length, loop, events);
        }
        return result;
    }

    private static Vector3? Vector(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3) return null;
        var xyz = v.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.Number ? n.GetSingle() : float.NaN).ToArray();
        return xyz.Any(float.IsNaN) ? null : new Vector3(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>
    /// A vehicle's upgrade slot nodes (<c>upg_engine</c>, <c>upg_armor</c>, <c>upg_utility_N</c>, <c>tow_hitch</c>; the manifest's
    /// <c>upgrade_slots</c>): where the node is, how much to scale an upgrade hung on it, and for <c>upg_tyres</c> each wheel's
    /// scale for a swapped tyre.
    /// </summary>
    public IReadOnlyDictionary<string, UpgradeSlot> VehicleSlots(string id) =>
        _upgradeSlots.GetValueOrDefault(id) ?? new Dictionary<string, UpgradeSlot>();

    private static Dictionary<string, UpgradeSlot>? UpgradeSlots(JsonElement entry)
    {
        if (!entry.TryGetProperty("upgrade_slots", out var slots) || slots.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, UpgradeSlot>();
        foreach (var slot in slots.EnumerateObject())
        {
            if (slot.Value.ValueKind != JsonValueKind.Object) continue;
            var scale = slot.Value.TryGetProperty("scale", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetSingle() : 1f;
            var wheels = new Dictionary<string, Vector3>();
            if (slot.Value.TryGetProperty("wheels", out var w) && w.ValueKind == JsonValueKind.Object)
                foreach (var wheel in w.EnumerateObject())
                    if (wheel.Value.ValueKind == JsonValueKind.Object && Vector(wheel.Value, "scale") is { } ws) wheels[wheel.Name] = ws;
            result[slot.Name] = new UpgradeSlot(Vector(slot.Value, "point") ?? Vector3.Zero, scale, wheels);
        }
        return result;
    }

    /// <summary>The model's size in metres from the manifest (<c>dimensions_m</c>), if it says.</summary>
    public Vector3? Dimensions(string id) => _dimensions.TryGetValue(id, out var d) ? d : null;

    /// <summary>A body's gear sockets (<c>hand_r</c>, <c>back</c>, <c>torso</c>, <c>head</c>), in model space, with the part each rides on.</summary>
    public IReadOnlyDictionary<string, AttachPoint> Sockets(string id) =>
        _sockets.GetValueOrDefault(id) ?? new Dictionary<string, AttachPoint>();

    /// <summary>A gear item's attach points by socket slot, in the item's own space.</summary>
    public IReadOnlyDictionary<string, AttachPoint> AttachPoints(string id) =>
        _attach.GetValueOrDefault(id) ?? new Dictionary<string, AttachPoint>();

    /// <summary>A rigged model's animation clips by name (manifest v2); empty for a static model.</summary>
    public IReadOnlyDictionary<string, ClipInfo> Clips(string id) =>
        _clips.GetValueOrDefault(id) ?? new Dictionary<string, ClipInfo>();

    /// <summary>The bones each body part (or the prosthetic replacing it) is skinned to (manifest v2 <c>limb_bones</c>).</summary>
    public IReadOnlyDictionary<string, string[]> LimbBones(string id) =>
        _limbBones.GetValueOrDefault(id) ?? new Dictionary<string, string[]>();

    /// <summary>Whether the model has a manifest entry (so a variant id can be probed before instancing it).</summary>
    public bool Has(string id) => _files.ContainsKey(id);

    /// <summary>Ids of the models carrying every one of these manifest tags, sorted so picks are stable.</summary>
    public IReadOnlyList<string> Tagged(params string[] tags) =>
        _tags.Where(kv => tags.All(kv.Value.Contains)).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();

    /// <summary>A string entry of a model's manifest <c>params</c> (e.g. a ground tile's <c>variant</c>).</summary>
    public string? Param(string id, string key) => _params.GetValueOrDefault(id)?.GetValueOrDefault(key);

    /// <summary>Instances the generated model with this id, or null if it is unknown or fails to load.</summary>
    public Node3D? TryInstance(string id)
    {
        if (!_files.TryGetValue(id, out var path)) return null;

        if (!_scenes.TryGetValue(path, out var scene))
            _scenes[path] = scene = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
        if (scene?.Instantiate() is Node3D imported) return imported;

        // Not imported by the editor yet: read the .glb at runtime.
        if (!_gltf.TryGetValue(path, out var state))
        {
            state = new GltfState();
            var error = new GltfDocument().AppendFromFile(ProjectSettings.GlobalizePath(path), state);
            if (error != Error.Ok)
            {
                GD.PushWarning($"Could not load model '{id}' from {path}: {error}");
                state = null;
            }
            _gltf[path] = state;
        }
        return state is null ? null : new GltfDocument().GenerateScene(state) as Node3D;
    }

    public Node3D Building(BuildingDef def, int level)
    {
        for (var l = level; l >= 1; l--)
        {
            if (TryInstance($"{def.Id}_l{l}") is not { } model) continue;
            // Only a lower level's model exists: grow it a little so upgrades still read.
            model.Scale = Vector3.One * (1 + 0.12f * (level - l));
            return model;
        }
        return Placeholders.Building(def.Category, level);
    }

    public static string SurvivorId(bool child, Sex sex) =>
        child ? "survivor_child" : sex == Sex.Female ? "survivor_adult_female" : "survivor_adult_male";

    /// <summary>A survivor's body with missing limbs hidden and prosthetics swapped in (see <see cref="SurvivorDressing"/>).</summary>
    public Node3D Survivor(Survivor s)
    {
        var id = SurvivorId(s.IsChild, s.Sex);
        if (TryInstance(id) is not { } model) return Placeholders.Survivor(s.IsChild);
        SurvivorDressing.FitLimbs(this, model, s.Body, s.IsChild);
        return model;
    }
}

/// <summary>
/// A socket on a body or an attach point on an item (manifest <c>sockets</c> / <c>attach</c>). <see cref="Bone"/> is the
/// socket bone of a rigged body (v2), identity at rest at <see cref="Point"/>.
/// </summary>
public sealed record UpgradeSlot(Vector3 Point, float Scale, IReadOnlyDictionary<string, Vector3> Wheels);

public readonly record struct AttachPoint(Vector3 Point, Vector3 RotationDegrees, string? Part, string? Bone = null);

/// <summary>An animation clip of a rigged model: length, whether it loops, and its events in seconds from the start.</summary>
public sealed record ClipInfo(string Name, float Length, bool Loop, IReadOnlyDictionary<string, float[]> Events);
