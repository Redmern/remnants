using System.Collections.Generic;
using Godot;
using Remnants.Game.Scavenge;

namespace Remnants.Game.Colony;

/// <summary>
/// Purely visual scenery around the town grid, all from the shared kit and fixed per layout: earth with soft dirt and
/// dry-grass variation, a campfire on the commons, a loose perimeter of barriers and sandbags with a gate, a rutted dirt
/// road out through the gate, and dead trees, rubble and ruined shells scattered beyond it. Keeps off the plots and
/// the commons; a missing model just leaves its spot empty.
/// </summary>
public static class TownDressing
{
    /// <summary>How far outside the plot grid the perimeter runs (the commons sit inside it).</summary>
    public const float PerimeterGap = 7.5f;

    private const float Tile = 2f;
    private const ulong Seed = 0x5EED_70A1;

    public static void Build(Node3D parent, TownLayout layout, ModelLibrary models, Vector3 campfire, Vector3 playground)
    {
        var root = new Node3D { Name = "Dressing" };
        parent.AddChild(root);
        root.AddChild(Ground(layout));
        root.AddChild(Campfire(campfire));

        var half = layout.Extent / 2 + new Vector2(PerimeterGap, PerimeterGap);
        // The gate is on the front edge (+Z, toward the default camera), in the middle; the road runs out from it.
        var gateX = layout.GutterX(0);
        Perimeter(root, models, half, gateX);
        Road(root, models, half, gateX);
        Outskirts(root, models, half, gateX, campfire, playground);
    }

    /// <summary>One big plane in earth tones; low-frequency noise gives patches of dry grass and bare dirt.</summary>
    private static MeshInstance3D Ground(TownLayout layout)
    {
        const float size = 260f, repeat = 48f;
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0.27f, 0.25f, 0.21f));
        ramp.SetColor(1, new Color(0.35f, 0.34f, 0.25f));
        ramp.AddPoint(0.5f, new Color(0.33f, 0.3f, 0.25f));
        var texture = new NoiseTexture2D
        {
            Width = 512, Height = 512, Seamless = true, ColorRamp = ramp,
            Noise = new FastNoiseLite { Frequency = 0.012f, FractalOctaves = 3, Seed = 7 },
        };
        var material = Placeholders.Material(Colors.White);
        material.AlbedoTexture = texture;
        material.Uv1Scale = new Vector3(size / repeat, size / repeat, 1);
        return new MeshInstance3D { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(size, size) }, MaterialOverride = material };
    }

    /// <summary>Stone ring, crossed logs, a low flame and a warm light. The idle sit round it (see <see cref="SurvivorWalkers"/>).</summary>
    private static Node3D Campfire(Vector3 at)
    {
        var fire = new Node3D { Name = "Campfire", Position = at };
        var stone = Placeholders.Material(new Color(0.42f, 0.4f, 0.37f));
        for (var i = 0; i < 9; i++)
        {
            var angle = i * Mathf.Tau / 9;
            fire.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.16f, 0.22f) }, MaterialOverride = stone,
                Position = new Vector3(Mathf.Cos(angle) * 0.55f, 0.08f, Mathf.Sin(angle) * 0.55f),
                Rotation = new Vector3(0, -angle + 0.3f * (i % 2), 0),
            });
        }
        var wood = Placeholders.Material(new Color(0.36f, 0.24f, 0.15f));
        for (var i = 0; i < 3; i++)
            fire.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.07f, Height = 0.8f, RadialSegments = 6 },
                MaterialOverride = wood, Position = new Vector3(0, 0.12f, 0),
                Rotation = new Vector3(Mathf.Pi / 2 - 0.25f, i * Mathf.Tau / 3, 0),
            });
        var flame = Placeholders.Material(new Color(1f, 0.55f, 0.15f), unshaded: true);
        var core = Placeholders.Material(new Color(1f, 0.85f, 0.4f), unshaded: true);
        fire.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.26f, Height = 0.55f, RadialSegments = 7 },
            MaterialOverride = flame, Position = new Vector3(0, 0.38f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        fire.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.13f, Height = 0.35f, RadialSegments = 6 },
            MaterialOverride = core, Position = new Vector3(0.04f, 0.3f, 0.03f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        fire.AddChild(new OmniLight3D
        {
            Position = new Vector3(0, 0.9f, 0), LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1.4f, OmniRange = 4.5f,
        });
        return fire;
    }

    /// <summary>
    /// A loose fence one prop wide along the rectangle: mostly low barriers and sandbags, the odd scrap wall, gaps
    /// where it has fallen, and an open gate in the middle of the front edge.
    /// </summary>
    private static void Perimeter(Node3D root, ModelLibrary models, Vector2 half, float gateX)
    {
        // Low pieces mostly, so the fence reads as a line and never hides the town; a tall one now and then.
        string[] kinds =
        [
            "cover_low_barrier", "cover_low_sandbags", "cover_low_barrier", "cover_low_sandbags", "cover_low_barrier",
            "cover_low_sandbags", "cover_low_barrier", "cover_low_sandbags", "cover_high_wall", "wall_segment",
        ];
        void Side(Vector3 from, Vector3 dir, int count, bool front)
        {
            for (var i = 0; i < count; i++)
            {
                var at = from + dir * (i + 0.5f) * Tile;
                if (front && Mathf.Abs(at.X - gateX) < Tile * 1.6f) continue;
                var h = Hash(at, 1);
                if (h % 100 < 18) continue; // fallen
                if (models.TryInstance(kinds[(int)((h >> 8) % (ulong)kinds.Length)]) is not { } prop) continue;
                prop.Position = at;
                prop.Rotation = new Vector3(0, Mathf.Atan2(dir.Z, dir.X) * -1 + ((h >> 20) % 2 == 0 ? 0 : Mathf.Pi), 0);
                prop.RotateY(((int)((h >> 24) % 7) - 3) * 0.03f);
                root.AddChild(prop);
            }
        }
        var nx = Mathf.RoundToInt(half.X * 2 / Tile);
        var nz = Mathf.RoundToInt(half.Y * 2 / Tile);
        Side(new Vector3(-half.X, 0, -half.Y), Vector3.Right, nx, false);
        Side(new Vector3(-half.X, 0, half.Y), Vector3.Right, nx, true);
        Side(new Vector3(-half.X, 0, -half.Y), Vector3.Back, nz, false);
        Side(new Vector3(half.X, 0, -half.Y), Vector3.Back, nz, false);
    }

    /// <summary>Rutted dirt tiles (ruts run along the tile's Y = world Z) from the gate out of view.</summary>
    private static void Road(Node3D root, ModelLibrary models, Vector2 half, float gateX)
    {
        for (var z = half.Y - Tile * 2.5f; z < half.Y + 60; z += Tile)
        {
            if (models.TryInstance("ground_tile_dirt") is not { } tile) return;
            tile.Position = new Vector3(gateX, -0.04f, z);
            if (Hash(tile.Position, 2) % 2 == 0) tile.RotateY(Mathf.Pi);
            root.AddChild(tile);
        }
    }

    /// <summary>Beyond the fence: dead trees, rubble and the odd ruined shell, thinning with distance, never on the road.</summary>
    private static void Outskirts(Node3D root, ModelLibrary models, Vector2 half, float gateX, Vector3 campfire, Vector3 playground)
    {
        var clear = new List<Vector3> { campfire, playground };
        bool NearClear(Vector3 p, float r)
        {
            foreach (var c in clear)
                if (c.DistanceTo(p) < r) return true;
            return false;
        }
        bool OnRoad(Vector3 p, float r) => p.Z > half.Y - Tile * 3 && Mathf.Abs(p.X - gateX) < r;

        var reach = 40f;
        for (var x = -half.X - reach; x <= half.X + reach; x += Tile)
        for (var z = -half.Y - reach; z <= half.Y + reach; z += Tile)
        {
            var p = new Vector3(x, 0, z);
            var outside = Mathf.Max(Mathf.Abs(x) - half.X, Mathf.Abs(z) - half.Y);
            var h = Hash(p, 3);
            var roll = (int)(h % 1000);

            if (outside < 2f || OnRoad(p, 4f)) continue;

            // Beyond the fence: sparser the further out.
            var density = Mathf.Lerp(90f, 25f, Mathf.Clamp((outside - 2f) / (reach - 2f), 0, 1));
            if (roll >= density) continue;
            var pick = (int)(h >> 12 & 0xFF);
            string id = pick < 120 ? "dead_tree" : pick < 230 ? "rubble" : "building_shell";
            if (id == "building_shell" && outside < 6f) id = "rubble";
            if (NearClear(p, id == "building_shell" ? 8f : 3.5f) || models.TryInstance(id) is not { } prop) continue;
            var jitter = new Vector3(((h >> 20) % 100 / 100f - 0.5f) * 1.2f, 0, ((h >> 28) % 100 / 100f - 0.5f) * 1.2f);
            prop.Position = p + jitter;
            prop.RotateY((h >> 36) % 360 * Mathf.Pi / 180);
            if (id == "building_shell") clear.Add(prop.Position);
            root.AddChild(prop);
        }
    }

    private static ulong Hash(Vector3 p, int salt) =>
        GroundPattern.Hash(Seed, Mathf.RoundToInt(p.X * 10), Mathf.RoundToInt(p.Z * 10), salt);
}
