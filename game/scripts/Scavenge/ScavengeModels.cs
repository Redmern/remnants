using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Anim;
using Remnants.Game.Colony;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Scavenge model lookup on top of the shared <see cref="ModelLibrary"/>: map props (pylon tiles: <c>network_pylon</c>),
/// enemies by definition (<c>enemy_{defId}</c>: husk, husk_crawler, linked, linked_drone, linked_warden), device units
/// (<c>device_camera</c> / <c>device_turret</c>, <c>_hacked</c> once they work for a party), the scout drone (<c>scout_drone</c>,
/// hovering), hackable devices (<see cref="DeviceViews"/>) and survivor
/// bodies dressed by <see cref="SurvivorDressing"/> (prosthetics, missing limbs, worn gear). Rigged models come with
/// their <see cref="CharacterRig"/>.
/// Every lookup falls back to a primitive so the mode runs with no generated assets.
/// </summary>
public sealed class ScavengeModels(ModelLibrary library)
{
    public int ModelCount => library.ModelCount;

    /// <summary>Ground variants that read as road (painted markings); kept off the open ground so it stays calm.</summary>
    private static readonly HashSet<string> RoadVariants = ["asphalt"];

    /// <summary>
    /// Ground tile ids from the manifest (tags <c>tile</c> + <c>ground</c>), the calm base first, then patch variants.
    /// A new <c>ground_tile_*</c> synced from remnants-assets joins the patches with no code change.
    /// </summary>
    public IReadOnlyList<string> GroundVariants { get; } = GroundIds(library);

    /// <summary>Low cover props (tag <c>cover_low</c>) and stand-alone high cover (tag <c>cover_high</c>).</summary>
    public IReadOnlyList<string> LowCover { get; } = library.Tagged("cover_low");
    public IReadOnlyList<string> HighCover { get; } = library.Tagged("cover_high");

    private static List<string> GroundIds(ModelLibrary library)
    {
        var ids = library.Tagged("tile", "ground");
        var calm = ids.Where(id => !RoadVariants.Contains(library.Param(id, "variant") ?? "")).ToList();
        if (calm.Count == 0) return ids.ToList();
        // Concrete is the base where it exists: the plainest surface, so props and units read against it.
        return calm.OrderBy(id => library.Param(id, "variant") == "concrete" ? 0 : 1).ThenBy(id => id, System.StringComparer.Ordinal).ToList();
    }

    /// <summary>A ground tile: the variant with this index into <see cref="GroundVariants"/>, else a flat slab.</summary>
    public Node3D Ground(int variant = 0) =>
        (GroundVariants.Count > 0 ? library.TryInstance(GroundVariants[variant % GroundVariants.Count]) : null)
        ?? Box(new Vector3(1.96f, 0.1f, 1.96f), new Color(0.32f, 0.31f, 0.29f));

    /// <summary>A model by manifest id (cover, markers); null when it is not there.</summary>
    public Node3D? Prop(string id) => library.TryInstance(id);

    public Node3D? ExitMarker() => library.TryInstance("exit_marker");

    public Node3D Wall() => library.TryInstance("wall_segment") ?? Box(new Vector3(2f, 2.2f, 0.3f), new Color(0.5f, 0.48f, 0.44f));

    public Node3D Rubble() => library.TryInstance("rubble") ?? Box(new Vector3(1.6f, 0.8f, 1.4f), new Color(0.45f, 0.42f, 0.38f));

    public Node3D Car() => library.TryInstance("wrecked_car") ?? Box(new Vector3(3.9f, 1.3f, 2f), new Color(0.55f, 0.3f, 0.2f));

    /// <summary>A Network relay pylon (<c>network_pylon</c>), else a cyan-tipped mast.</summary>
    public Node3D Pylon()
    {
        if (library.TryInstance("network_pylon") is { } pylon) return pylon;
        var root = Box(new Vector3(0.5f, 2.6f, 0.5f), new Color(0.35f, 0.37f, 0.4f));
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f },
            MaterialOverride = Placeholders.Material(new Color(0.3f, 0.85f, 0.95f), unshaded: true),
            Position = new Vector3(0, 2.8f, 0),
        });
        return root;
    }

    /// <summary>A generated model by id, or null (set pieces such as the watchtower).</summary>
    public Node3D? Instance(string id) => library.TryInstance(id);

    public Node3D Crate() => library.TryInstance("loot_crate") ?? Box(new Vector3(1.1f, 0.65f, 0.65f), new Color(0.6f, 0.5f, 0.25f));

    /// <param name="hacked">Device units: whether it works for a party (the <c>_hacked</c> variant).</param>
    public Node3D Unit(Unit unit, bool hacked = false)
    {
        if (unit.Kind == UnitKind.Device)
            return library.TryInstance($"device_{unit.EnemyDefId}{(hacked ? "_hacked" : "")}") ?? library.TryInstance($"device_{unit.EnemyDefId}")
                   ?? Capsule(1.2f, new Color(0.3f, 0.75f, 0.85f));
        if (unit.Kind == UnitKind.Drone)
        {
            // It hovers: lifted above the ground so it reads as flying.
            var drone = Rigged("scout_drone") ?? Capsule(0.4f, new Color(0.85f, 0.6f, 0.3f));
            var hover = new Node3D { Name = "Hover", Position = new Vector3(0, 1.3f, 0) };
            hover.AddChild(drone);
            return hover;
        }
        if (Story(unit) is { } storyModel) return storyModel;
        if (unit.Kind == UnitKind.Enemy)
        {
            var linked = unit.EnemyDefId?.StartsWith("linked") == true;
            return Rigged($"enemy_{unit.EnemyDefId}")
                   ?? Capsule(linked ? 2.1f : 1.7f, linked ? new Color(0.3f, 0.75f, 0.85f) : new Color(0.55f, 0.45f, 0.3f));
        }

        var child = unit.Origin?.IsChild ?? false;
        var id = ModelLibrary.SurvivorId(child, unit.Origin?.Sex ?? Sex.Male);
        if (library.TryInstance(id) is not { } model) return Placeholders.Survivor(child);
        SurvivorDressing.FitLimbs(library, model, unit.Body, child);
        SurvivorDressing.AttachGear(library, model, id, unit.Equipment);
        CharacterRig.Attach(library, model, id);
        return model;
    }

    /// <summary>A model with its <see cref="CharacterRig"/> when the asset is rigged (manifest v2), else as it is.</summary>
    private Node3D? Rigged(string id)
    {
        if (library.TryInstance(id) is not { } model) return null;
        CharacterRig.Attach(library, model, id);
        return model;
    }

    /// <summary>
    /// Story units with art of their own: named characters (Juniper bandaged once treated), the Tally band (a member
    /// variant by unit id, Cole himself), and structures to defend (the Radio Mast).
    /// </summary>
    private Node3D? Story(Unit unit)
    {
        if (unit.Tag == "mast") return library.TryInstance("radio_mast_l1");
        if (unit.CharacterId == "juniper") return library.TryInstance(unit.FollowingId is null ? "story_juniper" : "story_juniper_bandaged");
        if (unit.CharacterId is { } character) return library.TryInstance($"story_{character}");
        return unit.EnemyDefId switch
        {
            "cole" => library.TryInstance("story_cole_brannigan"),
            "tally_scav" => library.TryInstance($"story_tally_member_{unit.Id % 5 + 1}"),
            _ => null,
        };
    }

    private static Node3D Box(Vector3 size, Color color)
    {
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = Placeholders.Material(color),
            Position = new Vector3(0, size.Y / 2, 0),
        });
        return root;
    }

    private static Node3D Capsule(float height, Color color)
    {
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.32f, Height = height },
            MaterialOverride = Placeholders.Material(color),
            Position = new Vector3(0, height / 2, 0),
        });
        return root;
    }
}
