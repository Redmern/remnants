using Godot;
using Remnants.Core.Data;

namespace Remnants.Game.Colony;

/// <summary>Primitive stand-ins used until (or wherever) generated models are missing.</summary>
public static class Placeholders
{
    public static Color CategoryColor(BuildingCategory category) => category switch
    {
        BuildingCategory.Core => new Color(0.72f, 0.38f, 0.2f),      // rust
        BuildingCategory.Housing => new Color(0.62f, 0.58f, 0.5f),   // concrete
        BuildingCategory.Gathering => new Color(0.38f, 0.5f, 0.3f),  // moss
        BuildingCategory.Production => new Color(0.4f, 0.45f, 0.52f),
        BuildingCategory.Care => new Color(0.75f, 0.8f, 0.8f),
        _ => Colors.Gray,
    };

    /// <summary>A box per building: color by category, footprint and height grow with level.</summary>
    public static Node3D Building(BuildingCategory category, int level)
    {
        var footprint = 3.5f + 0.6f * level;
        var height = 1.5f + 1.2f * level;
        var box = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(footprint, height, footprint) },
            MaterialOverride = Material(CategoryColor(category)),
            Position = new Vector3(0, height / 2, 0),
        };
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(box);
        return root;
    }

    public static Node3D Survivor(bool child)
    {
        var height = child ? 1.1f : 1.7f;
        var capsule = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = child ? 0.22f : 0.3f, Height = height },
            MaterialOverride = Material(child ? new Color(0.85f, 0.7f, 0.45f) : new Color(0.55f, 0.65f, 0.75f)),
            Position = new Vector3(0, height / 2, 0),
        };
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(capsule);
        return root;
    }

    public static StandardMaterial3D Material(Color color, bool unshaded = false) => new()
    {
        AlbedoColor = color,
        Roughness = 0.9f,
        ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
        Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };
}
