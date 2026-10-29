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
            }
        }
        catch (JsonException e)
        {
            GD.PushWarning($"Ignoring invalid model manifest {path}: {e.Message}");
        }
    }

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

    public Node3D Survivor(Survivor s)
    {
        var id = s.IsChild ? "survivor_child" : s.Sex == Sex.Female ? "survivor_adult_female" : "survivor_adult_male";
        return TryInstance(id) ?? Placeholders.Survivor(s.IsChild);
    }
}
