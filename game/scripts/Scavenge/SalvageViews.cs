using System.Linq;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Salvage nodes as the local side knows them (purely visual, rebuilt on <see cref="Sync"/>): a node on open floor
/// draws its prop (salvage.json <c>model</c>: dead tree, shelf, bed, server rack, building shell); rubble and wrecked
/// cars are the terrain's own props already. Every node with lots left gets a faint amber ring, one being worked a
/// bright arc for its progress; a stripped node shows dimmed, without a ring.
/// </summary>
public partial class SalvageViews : Node3D
{
    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;
    private Node3D _root = null!;
    private Node3D _props = null!;
    private readonly System.Collections.Generic.Dictionary<int, Node3D> _built = new();

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _props = new Node3D { Name = "SalvageProps" };
        AddChild(_props);
        _root = new Node3D { Name = "SalvageRings" };
        AddChild(_root);
    }

    public void Sync()
    {
        foreach (var child in _root.GetChildren()) child.QueueFree();
        var data = _presenter.Sim.Data;
        foreach (var node in _presenter.KnownNodes)
        {
            if (data.Salvage.Node(node.Kind) is not { } def) continue;
            var at = TileGrid.ToWorld(node.Pos);
            if (def.Terrain == Terrain.Floor && !_built.ContainsKey(node.Id))
            {
                var prop = _models.Prop(def.Model) ?? Placeholder();
                prop.Position = at;
                prop.RotationDegrees = new Vector3(0, node.Id * 67 % 4 * 90, 0);
                // Big props (a building shell) are scaled down to sit on one tile.
                if (def.Model == "building_shell") prop.Scale = Vector3.One * 0.45f;
                _props.AddChild(prop);
                _built[node.Id] = prop;
            }
            if (_built.TryGetValue(node.Id, out var shown))
            {
                var dim = node.Depleted ? Colony.Placeholders.Material(new Color(0.05f, 0.05f, 0.05f, 0.55f), unshaded: true) : null;
                foreach (var mesh in shown.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()) mesh.MaterialOverlay = dim;
            }
            if (node.Depleted) continue;
            var ring = Arc(1, new Color(Hud.Palette.Amber, 0.35f), 0.04f);
            ring.Position = at + Vector3.Up * 0.12f;
            _root.AddChild(ring);
            if (node.Progress > 0)
            {
                var arc = Arc((float)node.Progress / def.Work, Hud.Palette.Amber, 0.1f);
                arc.Position = at + Vector3.Up * 0.14f;
                _root.AddChild(arc);
            }
        }
    }

    /// <summary>A flat ring arc around a tile, filled to <paramref name="fraction"/> clockwise from the top.</summary>
    private static Node3D Arc(float fraction, Color color, float width)
    {
        var mesh = new ImmediateMesh();
        var steps = Mathf.Max(2, (int)(48 * Mathf.Clamp(fraction, 0, 1)));
        mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip);
        for (var i = 0; i <= steps; i++)
        {
            var a = -Mathf.Pi / 2 + Mathf.Tau * fraction * i / steps;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            mesh.SurfaceAddVertex(dir * 0.82f);
            mesh.SurfaceAddVertex(dir * (0.82f + width));
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D
        {
            Mesh = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
    }

    private static Node3D Placeholder()
    {
        var root = new Node3D { Name = "Placeholder" };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.2f, 0.9f, 0.7f) },
            MaterialOverride = Colony.Placeholders.Material(new Color(0.42f, 0.33f, 0.24f)),
            Position = new Vector3(0, 0.45f, 0),
        });
        return root;
    }
}
