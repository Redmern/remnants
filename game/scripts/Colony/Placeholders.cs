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
        BuildingCategory.Power => new Color(0.78f, 0.62f, 0.22f),   // hazard yellow
        _ => Colors.Gray,
    };

    /// <summary>
    /// A box per building: color by category, footprint and height grow with level. Power buildings (no models yet) get a
    /// shape that reads at a glance: a generator block with an exhaust stack, tilted solar panels, a row of battery cells.
    /// </summary>
    public static Node3D Building(BuildingCategory category, int level, string? defId = null)
    {
        if (category == BuildingCategory.Power) return PowerBuilding(defId ?? "", level);
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

    private static Node3D PowerBuilding(string defId, int level)
    {
        var root = new Node3D { Name = "Placeholder" };
        var body = Material(CategoryColor(BuildingCategory.Power));
        var metal = Material(new Color(0.36f, 0.36f, 0.38f));
        var dark = Material(new Color(0.12f, 0.16f, 0.22f));
        void Add(Mesh mesh, Material m, Vector3 at, Vector3? rot = null) =>
            root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = m, Position = at, RotationDegrees = rot ?? Vector3.Zero });
        var s = 1 + 0.15f * (level - 1);
        switch (defId)
        {
            case "solar_array":
                var panels = 1 + level;
                for (var i = 0; i < panels; i++)
                {
                    var x = (i - (panels - 1) / 2f) * 1.9f;
                    Add(new BoxMesh { Size = new Vector3(0.12f, 1.2f, 0.12f) }, metal, new Vector3(x, 0.6f, 0));
                    Add(new BoxMesh { Size = new Vector3(1.7f, 0.08f, 2.6f) }, dark, new Vector3(x, 1.3f, 0), new Vector3(28, 0, 0));
                }
                break;
            case "battery_bank":
                Add(new BoxMesh { Size = new Vector3(4.4f * s, 0.3f, 2.4f) }, metal, new Vector3(0, 0.15f, 0));
                var cells = 2 + level;
                for (var i = 0; i < cells; i++)
                {
                    var x = (i - (cells - 1) / 2f) * 1.0f * s;
                    Add(new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.38f, Height = 1.6f * s, RadialSegments = 12 }, body, new Vector3(x, 0.3f + 0.8f * s, 0));
                    Add(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.18f, RadialSegments = 8 }, metal, new Vector3(x, 0.4f + 1.6f * s, 0));
                }
                break;
            default: // fuel_generator
                Add(new BoxMesh { Size = new Vector3(3.2f * s, 1.6f * s, 2.2f * s) }, body, new Vector3(0, 0.8f * s, 0));
                Add(new BoxMesh { Size = new Vector3(3.4f * s, 0.2f, 2.4f * s) }, metal, new Vector3(0, 1.65f * s, 0));
                Add(new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 2.2f * s, RadialSegments = 10 }, metal, new Vector3(1.1f * s, 2.2f * s, -0.6f * s));
                Add(new BoxMesh { Size = new Vector3(1.0f, 0.9f, 0.9f) }, dark, new Vector3(-2.2f * s, 0.45f, 0.4f));
                break;
        }
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
