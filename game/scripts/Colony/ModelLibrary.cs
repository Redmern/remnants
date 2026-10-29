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

    /// <summary>Accepts <c>{ "assets": [ {id, category, file, tags?, params?}, ... ] }</c> or a bare array; skips malformed entries.</summary>
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
                if (Points(entry, "sockets") is { } sockets) _sockets[key] = sockets;
                if (Points(entry, "attach") is { } attach) _attach[key] = attach;
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
            result[slot.Name] = new AttachPoint(point, Vector(slot.Value, "rot_deg") ?? Vector3.Zero, part);
        }
        return result;
    }

    private static Vector3? Vector(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3) return null;
        var xyz = v.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.Number ? n.GetSingle() : float.NaN).ToArray();
        return xyz.Any(float.IsNaN) ? null : new Vector3(xyz[0], xyz[1], xyz[2]);
    }

    /// <summary>A body's gear sockets (<c>hand_r</c>, <c>back</c>, <c>torso</c>, <c>head</c>), in model space, with the part each rides on.</summary>
    public IReadOnlyDictionary<string, AttachPoint> Sockets(string id) =>
        _sockets.GetValueOrDefault(id) ?? new Dictionary<string, AttachPoint>();

    /// <summary>A gear item's attach points by socket slot, in the item's own space.</summary>
    public IReadOnlyDictionary<string, AttachPoint> AttachPoints(string id) =>
        _attach.GetValueOrDefault(id) ?? new Dictionary<string, AttachPoint>();

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

/// <summary>A socket on a body or an attach point on an item (manifest <c>sockets</c> / <c>attach</c>).</summary>
public readonly record struct AttachPoint(Vector3 Point, Vector3 RotationDegrees, string? Part);
