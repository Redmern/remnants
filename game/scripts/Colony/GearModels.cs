using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Remnants.Core.Items;
using FileAccess = Godot.FileAccess;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual: the mods fitted on a gear item, shown on its model. A modded item id (<c>base[@q][+mkN][+mod]...</c>)
/// resolves to the base model <c>item_{definedId}</c>; each mod's model (code-side map, else the data's <c>model</c>)
/// is parented under the gear's <c>mod_{slot}</c> node (the glb node, else the manifest <c>mod_slots</c> point),
/// offset by minus the mod's <c>attach.mount</c> point. Mods with <c>params.apply = "recolor"</c> (camo, waterproofing)
/// are not hung: they recolour the garment's cloth materials to the <c>style.json</c> palette. Anything missing (no
/// model, no node, an old manifest) leaves the gear as it is.
/// </summary>
public static class GearModels
{
    private const string StylePath = "res://assets/generated/style.json";

    /// <summary>Mod item id → model id where the data's <c>model</c> differs from the asset's id.</summary>
    private static readonly Dictionary<string, string> ModModels = new()
    {
        ["mod_iron_sight"] = "mod_sight_iron",
        ["mod_reflex_sight"] = "mod_sight_reflex",
        ["mod_scope"] = "mod_scope",
        ["mod_suppressor"] = "mod_suppressor",
        ["mod_compensator"] = "mod_compensator",
        ["mod_long_barrel"] = "mod_long_barrel",
        ["mod_incendiary"] = "mod_incendiary_kit",
        ["mod_stock"] = "mod_stock",
        ["mod_flashlight"] = "mod_flashlight",
        ["mod_laser"] = "mod_laser",
        ["mod_emp_coil"] = "mod_emp_coil",
        ["mod_sharpened_edge"] = "mod_melee_edge",
        ["mod_serrated_edge"] = "mod_melee_edge",
        ["mod_weighted_head"] = "mod_melee_weight",
        ["mod_spikes"] = "mod_melee_spikes",
        ["mod_wrapped_grip"] = "mod_grip_wrapped",
        ["mod_shock_coil"] = "mod_melee_shock",
        ["mod_armor_scrap_plates"] = "mod_armor_scrap_plates",
        ["mod_armor_ceramic_plates"] = "mod_armor_ceramic_plates",
        ["mod_armor_padding"] = "mod_armor_padding",
        ["mod_armor_webbing"] = "mod_armor_webbing",
        ["mod_camo_urban"] = "mod_cloth_camo_urban",
        ["mod_camo_wasteland"] = "mod_cloth_camo_wasteland",
        ["mod_camo_foliage"] = "mod_cloth_camo_foliage",
        ["mod_waterproofing"] = "mod_cloth_waterproof",
        ["mod_network_weave"] = "mod_network_weave",
        ["mod_helmet_visor"] = "mod_helmet_visor",
        ["mod_dust_filter"] = "mod_helmet_filter",
    };

    private static Dictionary<string, Color>? _palette;

    /// <summary>The gear model for an item id (quality, Mk and mods stripped): <c>item_{definedId}</c>.</summary>
    public static string ItemModel(string itemId) => $"item_{GearModsData.Base(GearModsData.Parse(itemId).BaseId)}";

    /// <summary>The model for a mod item (any quality), or null when none is known.</summary>
    public static string? ModModel(ModelLibrary library, string modItemId)
    {
        var id = GearModsData.Base(modItemId);
        if (ModModels.TryGetValue(id, out var mapped) && library.Has(mapped)) return mapped;
        var data = GameDataPaths.LoadScavenge().GearMods?.Mod(id)?.Model;
        return data is not null && library.Has(data) ? data : null;
    }

    /// <summary>
    /// The gear's slot nodes a mod in data slot <paramref name="slot"/> may sit on, best first: the mod model's own
    /// <c>params.slots</c>, then the data slot (<c>stock</c> → <c>mod_stock</c> else <c>mod_grip</c>; <c>visor</c> →
    /// <c>mod_visor</c> else <c>mod_filter</c>, a filter the other way round).
    /// </summary>
    public static IEnumerable<string> SlotNodes(ModelLibrary library, string? modModel, string modItemId, string slot)
    {
        if (modModel is not null && library.RawParams(modModel) is { } p && p.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Array)
            foreach (var s in slots.EnumerateArray())
                if (s.ValueKind == JsonValueKind.String) yield return s.GetString()!;
        switch (slot)
        {
            case "stock":
                yield return "mod_stock";
                yield return "mod_grip";
                break;
            case "visor" when GearModsData.Base(modItemId).Contains("filter"):
                yield return "mod_filter";
                yield return "mod_visor";
                break;
            case "visor":
                yield return "mod_visor";
                yield return "mod_filter";
                break;
            default:
                yield return $"mod_{slot}";
                break;
        }
    }

    /// <summary>Fits the item's mods on its instanced model <paramref name="gear"/> (built from <see cref="ItemModel"/>).</summary>
    public static void FitMods(ModelLibrary library, Node3D gear, string itemId)
    {
        if (!GearModsData.IsComposite(itemId)) return;
        var data = GameDataPaths.LoadScavenge();
        if (data.GearMods is not { } mods) return;
        var gearModel = ItemModel(itemId);
        var points = library.ModSlots(gearModel);
        var used = new HashSet<string>();
        foreach (var mod in GearModsData.Parse(itemId).Mods)
        {
            if (mods.Mod(mod) is not { } def) continue;
            var model = ModModel(library, mod);
            if (model is null) continue;
            if (library.RawParams(model) is { } p && p.TryGetProperty("apply", out var apply) && apply.ValueKind == JsonValueKind.String
                && apply.GetString() == "recolor")
            {
                Recolor(gear, p);
                continue;
            }
            foreach (var nodeName in SlotNodes(library, model, mod, def.Slot))
            {
                if (used.Contains(nodeName)) continue;
                var node = gear.FindChild(nodeName, true, false) as Node3D;
                if (node is null && !points.ContainsKey(nodeName)) continue;
                if (library.TryInstance(model) is not { } piece) break;
                var mount = library.AttachPoints(model).TryGetValue("mount", out var m) ? m.Point : Vector3.Zero;
                piece.Name = $"Mod_{def.Slot}";
                piece.Position = -mount;
                if (node is not null) node.AddChild(piece);
                else
                {
                    // No node in the glb: a holder at the manifest point, turned by its rot_deg.
                    var at = points[nodeName];
                    var holder = new Node3D { Name = nodeName, Position = at.Point, RotationDegrees = at.RotationDegrees };
                    gear.AddChild(holder);
                    holder.AddChild(piece);
                }
                used.Add(nodeName);
                break;
            }
        }
    }

    /// <summary>
    /// Camo and waterproofing: each surface whose material is named in <c>params.recolor</c> gets a copy with the
    /// palette colour it maps to (and <c>params.roughness</c> when given).
    /// </summary>
    private static void Recolor(Node node, JsonElement p)
    {
        if (!p.TryGetProperty("recolor", out var map) || map.ValueKind != JsonValueKind.Object) return;
        var palette = Palette();
        if (palette.Count == 0) return;
        var to = new Dictionary<string, Color>();
        foreach (var kv in map.EnumerateObject())
            if (kv.Value.ValueKind == JsonValueKind.String && palette.TryGetValue(kv.Value.GetString()!, out var c)) to[kv.Name] = c;
        float? roughness = p.TryGetProperty("roughness", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetSingle() : null;
        Apply(node, to, roughness);
    }

    private static void Apply(Node node, Dictionary<string, Color> to, float? roughness)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh is { } m)
            for (var i = 0; i < m.GetSurfaceCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is not BaseMaterial3D material) continue;
                var name = MaterialName(material, m, i);
                if (name is null || !to.TryGetValue(name, out var color) || material.Duplicate() is not BaseMaterial3D copy) continue;
                copy.AlbedoColor = color;
                if (roughness is { } rough) copy.Roughness = rough;
                mesh.SetSurfaceOverrideMaterial(i, copy);
            }
        foreach (var child in node.GetChildren())
            if (child is not Node3D { Name: var n } || !n.ToString().StartsWith("Mod_")) Apply(child, to, roughness);
    }

    /// <summary>A surface's palette name: the material's resource name (glTF material name), else the surface name.</summary>
    private static string? MaterialName(BaseMaterial3D material, Mesh mesh, int surface)
    {
        if (!string.IsNullOrEmpty(material.ResourceName)) return material.ResourceName;
        return mesh is ArrayMesh array && array.SurfaceGetName(surface) is { Length: > 0 } s ? s : null;
    }

    /// <summary>The asset style's palette (<c>style.json</c> <c>palette</c>: name → "#rrggbb"); empty when it is not synced.</summary>
    public static Dictionary<string, Color> Palette()
    {
        if (_palette is not null) return _palette;
        _palette = new Dictionary<string, Color>();
        if (!FileAccess.FileExists(StylePath)) return _palette;
        try
        {
            using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(StylePath));
            if (doc.RootElement.TryGetProperty("palette", out var palette) && palette.ValueKind == JsonValueKind.Object)
                foreach (var kv in palette.EnumerateObject())
                    if (kv.Value.ValueKind == JsonValueKind.String && kv.Value.GetString() is { } hex && Color.HtmlIsValid(hex))
                        _palette[kv.Name] = new Color(hex);
        }
        catch (JsonException e)
        {
            GD.PushWarning($"Ignoring invalid {StylePath}: {e.Message}");
        }
        return _palette;
    }
}
