using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using TileMap = Remnants.Core.Scavenge.TileMap;
using Remnants.Game.Colony;

namespace Remnants.Game.Scavenge;

/// <summary>How a highlighted tile reads: where the unit can move, whom it can hit, whom an item can reach.</summary>
public enum HighlightKind { Move, Attack, Use, Selected, Hover }

/// <summary>
/// The tactical map: terrain props per tile (ground, walls, rubble, wrecked cars), exit markers, loot crates,
/// fog of war for the local side (hidden / seen before, dimmed / visible) and the tile highlights.
/// Reads Core state; never changes it.
/// </summary>
public partial class MapView : Node3D
{
    private static readonly Color MoveColor = new(0.35f, 0.75f, 0.95f, 0.38f);
    private static readonly Color AttackColor = new(0.95f, 0.3f, 0.22f, 0.5f);
    private static readonly Color UseColor = new(0.45f, 0.9f, 0.45f, 0.45f);
    private static readonly Color SelectedColor = new(1f, 0.82f, 0.35f, 0.55f);
    private static readonly Color HoverColor = new(1f, 1f, 1f, 0.25f);
    private static readonly Color ExitColor = new(0.4f, 0.95f, 0.5f, 0.45f);

    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;

    /// <summary>Everything drawn for a tile (ground, prop, exit marker), hidden while the tile is unexplored.</summary>
    private Node3D[] _tiles = [];
    /// <summary>Meshes per tile, dimmed with an overlay while the tile is remembered but not in sight.</summary>
    private MeshInstance3D[][] _tileMeshes = [];
    private readonly Dictionary<int, Node3D> _crates = new();
    private readonly Node3D _highlights = new() { Name = "Highlights" };
    private readonly MeshInstance3D _hover = Quad(HoverColor, 0.06f);
    private readonly Dictionary<HighlightKind, StandardMaterial3D> _highlightMaterials = new();
    private StandardMaterial3D _fogMaterial = null!;

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _fogMaterial = Placeholders.Material(new Color(0.01f, 0.01f, 0.03f, 0.72f), unshaded: true);
        foreach (var (kind, color) in new[]
                 {
                     (HighlightKind.Move, MoveColor), (HighlightKind.Attack, AttackColor), (HighlightKind.Use, UseColor),
                     (HighlightKind.Selected, SelectedColor), (HighlightKind.Hover, HoverColor),
                 })
            _highlightMaterials[kind] = Placeholders.Material(color, unshaded: true);
        BuildTerrain();
        AddChild(_highlights);
        _hover.Visible = false;
        AddChild(_hover);
    }

    private void BuildTerrain()
    {
        var map = _presenter.State.Map;
        _tiles = new Node3D[map.Width * map.Height];
        _tileMeshes = new MeshInstance3D[_tiles.Length][];
        var terrain = new Node3D { Name = "Terrain" };
        AddChild(terrain);
        var carsPlaced = new HashSet<Pos>();

        foreach (var p in map.AllPositions)
        {
            var index = map.Index(p);
            var tile = new Node3D { Name = $"Tile{p.X}_{p.Y}", Position = TileGrid.ToWorld(p) };
            terrain.AddChild(tile);
            _tiles[index] = tile;
            tile.AddChild(_models.Ground());

            switch (map[p])
            {
                case Terrain.Wall:
                    AddWall(tile, map, p);
                    break;
                case Terrain.Rubble:
                    var rubble = _models.Rubble();
                    rubble.RotationDegrees = new Vector3(0, (p.X * 37 + p.Y * 91) % 4 * 90, 0);
                    tile.AddChild(rubble);
                    break;
                case Terrain.Cover:
                    AddCar(tile, map, p, carsPlaced);
                    break;
                case Terrain.Water:
                    tile.AddChild(Quad(new Color(0.18f, 0.32f, 0.38f, 0.85f), 0.1f, 1.96f));
                    break;
            }
            if (map.IsExit(p))
            {
                tile.AddChild(Quad(ExitColor, 0.09f, 1.7f));
                tile.AddChild(new Label3D
                {
                    Text = "EXIT", FontSize = 40, PixelSize = 0.01f, Position = new Vector3(0, 0.35f, 0),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = new Color(0.6f, 1f, 0.65f),
                    OutlineSize = 8,
                });
            }

            _tileMeshes[index] = Meshes(tile);
        }
    }

    /// <summary>A wall segment runs along X; turn it to follow neighbouring walls, two crossed at corners.</summary>
    private void AddWall(Node3D tile, TileMap map, Pos p)
    {
        bool WallAt(int dx, int dy) => map.InBounds(p + new Pos(dx, dy)) && map[p + new Pos(dx, dy)] == Terrain.Wall;
        var eastWest = WallAt(1, 0) || WallAt(-1, 0);
        var northSouth = WallAt(0, 1) || WallAt(0, -1);
        if (eastWest || !northSouth) tile.AddChild(_models.Wall());
        if (northSouth)
        {
            var wall = _models.Wall();
            wall.RotationDegrees = new Vector3(0, 90, 0);
            tile.AddChild(wall);
        }
    }

    /// <summary>The car model covers two tiles along X: pair cover tiles where possible, shrink a lone one.</summary>
    private void AddCar(Node3D tile, TileMap map, Pos p, HashSet<Pos> placed)
    {
        if (placed.Contains(p)) return;
        placed.Add(p);
        var car = _models.Car();
        var east = p + new Pos(1, 0);
        var south = p + new Pos(0, 1);
        if (map.InBounds(east) && map[east] == Terrain.Cover && placed.Add(east))
            car.Position = new Vector3(TileGrid.TileSize / 2, 0, 0);
        else if (map.InBounds(south) && map[south] == Terrain.Cover && placed.Add(south))
        {
            car.Position = new Vector3(0, 0, TileGrid.TileSize / 2);
            car.RotationDegrees = new Vector3(0, 90, 0);
        }
        else
        {
            car.Scale = Vector3.One * 0.5f;
            car.RotationDegrees = new Vector3(0, (p.X + p.Y) % 2 == 0 ? 30 : -30, 0);
        }
        tile.AddChild(car);
    }

    /// <summary>Fog and crates from the latest state.</summary>
    public void Sync()
    {
        var map = _presenter.State.Map;
        var visible = _presenter.Visible;
        var explored = _presenter.Explored;
        foreach (var p in map.AllPositions)
        {
            var i = map.Index(p);
            var seen = explored.Contains(i) || visible.Contains(i);
            _tiles[i].Visible = seen;
            var overlay = seen && !visible.Contains(i) ? _fogMaterial : null;
            foreach (var mesh in _tileMeshes[i]) mesh.MaterialOverlay = overlay;
        }

        var caches = _presenter.State.Caches;
        foreach (var id in _crates.Keys.Where(id => caches.All(c => c.Id != id)).ToList())
        {
            _crates[id].QueueFree();
            _crates.Remove(id);
        }
        foreach (var cache in caches)
        {
            if (!_crates.TryGetValue(cache.Id, out var crate))
            {
                crate = _models.Crate();
                crate.Position = TileGrid.ToWorld(cache.Pos);
                crate.RotationDegrees = new Vector3(0, cache.Id * 47 % 90 - 45, 0);
                AddChild(crate);
                _crates[cache.Id] = crate;
            }
            crate.Visible = _presenter.IsExplored(cache.Pos) || _presenter.IsVisible(cache.Pos);
            var dim = _presenter.IsVisible(cache.Pos) ? null : _fogMaterial;
            foreach (var mesh in Meshes(crate)) mesh.MaterialOverlay = dim;
        }
    }

    private static MeshInstance3D[] Meshes(Node node) =>
        node.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().ToArray();

    public void ClearHighlights()
    {
        foreach (var child in _highlights.GetChildren()) child.QueueFree();
    }

    /// <summary>Marks a tile; an optional label floats above it (AP cost, hit chance).</summary>
    public void Highlight(Pos p, HighlightKind kind, string? label = null)
    {
        var quad = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1.8f, 1.8f) },
            MaterialOverride = _highlightMaterials[kind],
            Position = TileGrid.ToWorld(p) + new Vector3(0, kind == HighlightKind.Selected ? 0.13f : 0.12f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _highlights.AddChild(quad);
        if (label is null) return;
        _highlights.AddChild(new Label3D
        {
            Text = label, FontSize = kind == HighlightKind.Attack ? 44 : 32, PixelSize = 0.01f, OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            Position = TileGrid.ToWorld(p) + new Vector3(0, kind == HighlightKind.Attack ? 2.9f : 0.5f, 0),
            Modulate = kind == HighlightKind.Attack ? new Color(1f, 0.75f, 0.65f) : new Color(0.8f, 0.95f, 1f),
        });
    }

    public void SetHover(Pos? p)
    {
        _hover.Visible = p is { } tile && _presenter.State.Map.InBounds(tile);
        if (p is { } at) _hover.Position = TileGrid.ToWorld(at) + new Vector3(0, 0.14f, 0);
    }

    private static MeshInstance3D Quad(Color color, float height, float size = 1.9f) => new()
    {
        Mesh = new PlaneMesh { Size = new Vector2(size, size) },
        MaterialOverride = Placeholders.Material(color, unshaded: true),
        Position = new Vector3(0, height, 0),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
