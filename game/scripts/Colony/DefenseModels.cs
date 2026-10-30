using System.Collections.Generic;
using Godot;
using Remnants.Core.Raids;
using Side = Remnants.Core.Raids.Side;

namespace Remnants.Game.Colony;

/// <summary>
/// Models for placed defenses (purely visual). Looks a defense up by its data <c>model</c>, else <c>defense_{id}</c>,
/// then falls back by kind to kit pieces the town already uses (walls → <c>wall_segment</c> / <c>cover_high_wall</c>,
/// barricades → <c>cover_low_barrier</c>, nests → <c>cover_low_sandbags</c>, turrets → <c>device_turret</c>), and last
/// to a primitive stand-in (tower frame, gate posts, lamp mast, bell post, flat trap markers). One cell is 2 m; models
/// face +Z, turned to the defense's facing (walls and gates run across it).
/// </summary>
public static class DefenseModels
{
    /// <summary>Kit ids tried after <c>defense_{id}</c>, by kind.</summary>
    public static IReadOnlyList<string> Fallbacks(DefenseKind kind) => kind switch
    {
        DefenseKind.Wall => ["wall_segment", "cover_high_wall"],
        DefenseKind.Barricade => ["cover_low_barrier", "cover_low_sandbags"],
        DefenseKind.Post => ["cover_low_sandbags"],
        DefenseKind.Turret => ["device_turret"],
        _ => [],
    };

    /// <summary>The model ids looked up for a defense, best first (for the asset request and the fallbacks).</summary>
    public static IEnumerable<string> Candidates(DefenseDef def)
    {
        if (def.Model is { Length: > 0 } model) yield return model;
        yield return $"defense_{def.Id}";
        foreach (var id in Fallbacks(def.Kind)) yield return id;
    }

    /// <summary>The defense's model under a root node at the cell centre (y = 0), turned to <paramref name="facing"/>.</summary>
    public static Node3D Build(ModelLibrary models, DefenseDef def, Side facing)
    {
        var root = new Node3D { Name = $"Defense_{def.Id}" };
        Node3D? model = null;
        foreach (var id in Candidates(def))
            if ((model = models.TryInstance(id)) is not null) break;
        model ??= Placeholder(def);
        if (model.Name == "Placeholder" || !models.Has($"defense_{def.Id}")) Dress(model, def);
        root.AddChild(model);
        root.Rotation = new Vector3(0, Yaw(facing), 0);
        return root;
    }

    /// <summary>Yaw that turns +Z toward a side (South = +Z = 0).</summary>
    public static float Yaw(Side side)
    {
        var (x, y) = DefenseGrid.Facing(side);
        return Mathf.Atan2(x, y);
    }

    /// <summary>Kit fallbacks shared between tiers: taller and tinted by tier so an upgrade reads.</summary>
    private static void Dress(Node3D model, DefenseDef def)
    {
        var tier = def.Id.EndsWith("_scrap") || def.Id.EndsWith("_2") ? 1 : def.Id.EndsWith("_concrete") || def.Id.EndsWith("_3") ? 2 : 0;
        if (def.Kind == DefenseKind.Wall)
        {
            model.Scale = new Vector3(1, 1 + 0.15f * tier, 1);
            Tint(model, tier switch
            {
                0 => new Color(0.45f, 0.3f, 0.15f, 0.35f),
                1 => new Color(0.5f, 0.28f, 0.18f, 0.2f),
                _ => new Color(0.75f, 0.74f, 0.7f, 0.3f),
            });
        }
    }

    /// <summary>Sets a translucent overlay on every mesh under <paramref name="node"/> (null clears it).</summary>
    public static void Tint(Node node, Color? color)
    {
        StandardMaterial3D? overlay = null;
        if (color is { } c)
        {
            overlay = new StandardMaterial3D
            {
                AlbedoColor = c,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
        }
        Apply(node, overlay);
    }

    private static void Apply(Node node, Material? overlay)
    {
        if (node is GeometryInstance3D g) g.MaterialOverlay = overlay;
        foreach (var child in node.GetChildren()) Apply(child, overlay);
    }

    // ---- primitive stand-ins ----

    private static Node3D Placeholder(DefenseDef def)
    {
        var root = new Node3D { Name = "Placeholder" };
        var wood = Placeholders.Material(new Color(0.42f, 0.3f, 0.19f));
        var metal = Placeholders.Material(new Color(0.36f, 0.35f, 0.33f));
        void Box(Vector3 size, Vector3 at, Material m, Vector3? rot = null) =>
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = at, Rotation = rot ?? Vector3.Zero });
        void Pole(float height, Vector3 at, Material m, float radius = 0.07f) =>
            root.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = radius * 0.8f, BottomRadius = radius, Height = height, RadialSegments = 6 },
                MaterialOverride = m, Position = at + new Vector3(0, height / 2, 0),
            });

        switch (def.Kind)
        {
            case DefenseKind.Wall:
            case DefenseKind.Barricade:
                Box(new Vector3(1.9f, def.Kind == DefenseKind.Wall ? 1.8f : 1f, 0.35f), new Vector3(0, def.Kind == DefenseKind.Wall ? 0.9f : 0.5f, 0), wood);
                break;
            case DefenseKind.Gate:
                // Two posts and a pair of plank leaves, a little open.
                Pole(2.2f, new Vector3(-0.95f, 0, 0), wood, 0.1f);
                Pole(2.2f, new Vector3(0.95f, 0, 0), wood, 0.1f);
                Box(new Vector3(0.9f, 1.6f, 0.1f), new Vector3(-0.45f, 0.9f, 0.05f), wood, new Vector3(0, 0.25f, 0));
                Box(new Vector3(0.9f, 1.6f, 0.1f), new Vector3(0.45f, 0.9f, 0.05f), wood, new Vector3(0, -0.25f, 0));
                Box(new Vector3(2f, 0.12f, 0.12f), new Vector3(0, 2.15f, 0), wood);
                break;
            case DefenseKind.Post:
                for (var i = 0; i < 6; i++)
                {
                    var a = i * Mathf.Tau / 6;
                    Box(new Vector3(0.7f, 0.5f, 0.35f), new Vector3(Mathf.Cos(a) * 0.65f, 0.25f, Mathf.Sin(a) * 0.65f),
                        Placeholders.Material(new Color(0.55f, 0.5f, 0.38f)), new Vector3(0, -a + Mathf.Pi / 2, 0));
                }
                break;
            case DefenseKind.Tower:
            {
                var tier = def.Id.EndsWith("_3") ? 2 : def.Id.EndsWith("_2") ? 1 : 0;
                var h = 3.2f + tier * 0.9f;
                foreach (var (x, z) in new[] { (-0.6f, -0.6f), (0.6f, -0.6f), (-0.6f, 0.6f), (0.6f, 0.6f) }) Pole(h, new Vector3(x, 0, z), wood);
                Box(new Vector3(1.7f, 0.12f, 1.7f), new Vector3(0, h, 0), wood);
                Box(new Vector3(1.7f, 0.5f, 0.06f), new Vector3(0, h + 0.3f, 0.82f), tier == 2 ? metal : wood);
                Box(new Vector3(1.7f, 0.5f, 0.06f), new Vector3(0, h + 0.3f, -0.82f), tier == 2 ? metal : wood);
                if (tier >= 1)
                {
                    foreach (var (x, z) in new[] { (-0.8f, -0.8f), (0.8f, -0.8f), (-0.8f, 0.8f), (0.8f, 0.8f) }) Pole(1.2f, new Vector3(x, h, z), wood, 0.04f);
                    Box(new Vector3(2f, 0.1f, 2f), new Vector3(0, h + 1.25f, 0), tier == 2 ? metal : wood);
                }
                break;
            }
            case DefenseKind.Light:
            {
                Pole(3.2f, Vector3.Zero, metal, 0.06f);
                var lamp = Placeholders.Material(new Color(0.95f, 0.95f, 0.85f));
                lamp.EmissionEnabled = true;
                lamp.Emission = new Color(0.95f, 0.95f, 0.85f);
                lamp.EmissionEnergyMultiplier = 0;
                root.AddChild(new MeshInstance3D
                {
                    Name = "Lamp", Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.3f, 0.25f) }, MaterialOverride = lamp,
                    Position = new Vector3(0, 3.2f, 0.12f), Rotation = new Vector3(0.5f, 0, 0),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
                break;
            }
            case DefenseKind.Turret:
                Box(new Vector3(0.9f, 0.5f, 0.9f), new Vector3(0, 0.25f, 0), metal);
                Box(new Vector3(0.6f, 0.4f, 0.6f), new Vector3(0, 0.7f, 0), metal);
                root.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.9f, RadialSegments = 6 }, MaterialOverride = metal,
                    Position = new Vector3(0, 0.75f, 0.6f), Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
                });
                break;
            case DefenseKind.Alarm:
                Pole(2.4f, Vector3.Zero, wood, 0.06f);
                Box(new Vector3(0.7f, 0.08f, 0.08f), new Vector3(0, 2.4f, 0), wood);
                root.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.2f, Height = 0.3f, RadialSegments = 8 },
                    MaterialOverride = Placeholders.Material(new Color(0.7f, 0.55f, 0.25f)), Position = new Vector3(0.25f, 2.2f, 0),
                });
                break;
            default:
                TrapMarker(root, def);
                break;
        }
        return root;
    }

    /// <summary>Traps lie low: a dark scuffed patch with a mark by type (stakes, a pit, a wire, a mine).</summary>
    private static void TrapMarker(Node3D root, DefenseDef def)
    {
        var dirt = Placeholders.Material(new Color(0.2f, 0.17f, 0.13f));
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.8f, Height = 0.04f, RadialSegments = 12 }, MaterialOverride = dirt,
            Position = new Vector3(0, 0.02f, 0),
        });
        var accent = Placeholders.Material(def.Lure ? new Color(0.75f, 0.6f, 0.25f)
            : def.Hits.Contains("linked") && !def.Hits.Contains("husk") ? new Color(0.55f, 0.6f, 0.68f)
            : def.TrapDamage >= 10 ? new Color(0.65f, 0.28f, 0.18f) : new Color(0.55f, 0.45f, 0.3f));
        if (def.Id.Contains("spike") || def.Id.Contains("pit"))
        {
            for (var i = 0; i < 5; i++)
            for (var j = 0; j < 2; j++)
                root.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.05f, Height = 0.3f, RadialSegments = 4 }, MaterialOverride = accent,
                    Position = new Vector3(-0.5f + i * 0.25f, 0.15f, -0.15f + j * 0.3f), Rotation = new Vector3(0.3f * (j * 2 - 1), 0, 0),
                });
        }
        else if (def.TrapDamage == 0)
        {
            // Tripwire: two pegs and a wire with cans.
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.4f, 0.02f, 0.02f) }, MaterialOverride = accent, Position = new Vector3(0, 0.25f, 0) });
            foreach (var x in new[] { -0.7f, 0.7f })
                root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.35f, 0.06f) }, MaterialOverride = accent, Position = new Vector3(x, 0.17f, 0) });
        }
        else
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.32f, Height = 0.12f, RadialSegments = 10 }, MaterialOverride = accent,
                Position = new Vector3(0, 0.08f, 0),
            });
        }
    }
}
