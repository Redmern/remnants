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
/// fog of war for the local side (hidden / seen before, dimmed / visible) and the tile highlights. On a fought raid's
/// colony map the buildings and defenses are drawn as in the town (<see cref="ColonyBattleDressing"/>).
/// Reads Core state; never changes it.
/// </summary>
public partial class MapView : Node3D
{
    private static readonly Color MoveColor = new(0.4f, 0.78f, 1f, 0.8f);
    private static readonly Color AttackColor = new(1f, 0.32f, 0.24f, 0.95f);
    private static readonly Color UseColor = new(0.45f, 0.95f, 0.5f, 0.9f);
    private static readonly Color SelectedColor = new(1f, 0.8f, 0.3f, 1f);
    private static readonly Color HoverColor = new(1f, 1f, 1f, 0.7f);
    private static readonly Color ExitColor = new(0.4f, 0.95f, 0.5f, 0.45f);
    private static readonly Color GridColor = new(0f, 0f, 0f, 0.22f);
    private static readonly Color WaterColor = new(0.13f, 0.22f, 0.26f, 0.9f);

    private ScavengePresenter _presenter = null!;
    private ScavengeModels _models = null!;

    /// <summary>Everything drawn for a tile (ground, prop, exit marker), hidden while the tile is unexplored.</summary>
    private Node3D[] _tiles = [];
    /// <summary>Meshes per tile, dimmed with an overlay while the tile is remembered but not in sight.</summary>
    private MeshInstance3D[][] _tileMeshes = [];
    private readonly Dictionary<int, Node3D> _crates = new();
    private readonly Node3D _highlights = new() { Name = "Highlights" };
    private MeshInstance3D _hover = null!;
    private readonly Dictionary<HighlightKind, StandardMaterial3D> _highlightMaterials = new();
    private StandardMaterial3D _fogMaterial = null!;
    /// <summary>
    /// Ground meshes are shaded a step darker so walls, cover and units stand out against them; patch variants (dirt,
    /// overgrown) are also pulled toward a flat earth tone so they read as a calm change of surface, not noise.
    /// </summary>
    private readonly Dictionary<MeshInstance3D, StandardMaterial3D> _ground = new();
    private StandardMaterial3D _groundShade = null!, _patchShade = null!;

    public void Setup(ScavengePresenter presenter, ScavengeModels models)
    {
        _presenter = presenter;
        _models = models;
        _fogMaterial = Placeholders.Material(new Color(0.01f, 0.01f, 0.03f, 0.72f), unshaded: true);
        _groundShade = Placeholders.Material(new Color(0.13f, 0.13f, 0.13f, 0.5f), unshaded: true);
        _patchShade = Placeholders.Material(new Color(0.12f, 0.115f, 0.1f, 0.55f), unshaded: true);
        // Highlights are framed squares: a strong edge so a field of tiles still shows its grid, a light fill.
        var frame = TileFrame(border: 4, fill: 0.3f);
        foreach (var (kind, color) in new[]
                 {
                     (HighlightKind.Move, MoveColor), (HighlightKind.Attack, AttackColor), (HighlightKind.Use, UseColor),
                     (HighlightKind.Selected, SelectedColor), (HighlightKind.Hover, HoverColor),
                 })
            _highlightMaterials[kind] = Framed(frame, color);
        BuildTerrain();
        AddChild(_highlights);
        _hover = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1.9f, 1.9f) }, MaterialOverride = _highlightMaterials[HighlightKind.Hover],
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
        };
        AddChild(_hover);
    }

    /// <summary>
    /// Ground in calm patches (<see cref="GroundPattern"/>) with a faint tactical grid; walls, rubble and cover props;
    /// exit markers facing the way out. Tile choices come from the manifest, so new variants drop in on sync.
    /// </summary>
    private void BuildTerrain()
    {
        var map = _presenter.State.Map;
        var seed = GroundPattern.Seed(map, _presenter.State.ZoneId);
        _tiles = new Node3D[map.Width * map.Height];
        _tileMeshes = new MeshInstance3D[_tiles.Length][];
        var terrain = new Node3D { Name = "Terrain" };
        AddChild(terrain);
        var carsPlaced = new HashSet<Pos>();
        var gridMesh = new PlaneMesh { Size = new Vector2(TileGrid.TileSize, TileGrid.TileSize) };
        var gridMaterial = Framed(TileFrame(border: 1, fill: 0f), GridColor);
        var waterMaterial = Placeholders.Material(WaterColor, unshaded: true);
        var colony = ColonyBattleDressing.For(_presenter.State, _models.Library);

        foreach (var p in map.AllPositions)
        {
            var index = map.Index(p);
            var tile = new Node3D { Name = $"Tile{p.X}_{p.Y}", Position = TileGrid.ToWorld(p) };
            terrain.AddChild(tile);
            _tiles[index] = tile;
            var variant = GroundPattern.Variant(seed, p, _models.GroundVariants.Count);
            var ground = _models.Ground(variant);
            if (GroundPattern.Flip(seed, p)) ground.RotationDegrees = new Vector3(0, 180, 0);
            tile.AddChild(ground);
            foreach (var mesh in Meshes(ground)) _ground[mesh] = variant == 0 ? _groundShade : _patchShade;
            var dressed = colony?.Dress(tile, p, map[p]) ?? false;

            switch (map[p])
            {
                case Terrain.Wall when dressed:
                case Terrain.Cover when dressed:
                    break;
                case Terrain.Wall:
                    AddWall(tile, map, p, seed);
                    break;
                case Terrain.Rubble:
                    var rubble = _models.Rubble();
                    rubble.RotationDegrees = new Vector3(0, GroundPattern.Pick(seed, p, 4, 5) * 90, 0);
                    tile.AddChild(rubble);
                    break;
                case Terrain.Cover:
                    AddCover(tile, map, p, carsPlaced, seed);
                    break;
                case Terrain.Pylon:
                    tile.AddChild(_models.Pylon());
                    break;
                case Terrain.Vehicle or Terrain.ArmoredVehicle:
                    AddParked(tile, map, p);
                    break;
                case Terrain.Water:
                    tile.AddChild(new MeshInstance3D
                    {
                        Mesh = new PlaneMesh { Size = new Vector2(1.96f, 1.96f) }, MaterialOverride = waterMaterial,
                        Position = new Vector3(0, 0.1f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    });
                    break;
            }
            if (map[p] != Terrain.Wall)
                tile.AddChild(new MeshInstance3D
                {
                    Name = "Grid", Mesh = gridMesh, MaterialOverride = gridMaterial, Position = new Vector3(0, 0.09f, 0),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            if (map.IsExit(p)) AddExit(tile, map, p);

            _tileMeshes[index] = Meshes(tile);
        }
    }

    private static bool Is(TileMap map, Pos p, params Terrain[] kinds) => map.InBounds(p) && kinds.Contains(map[p]);

    /// <summary>
    /// A wall segment runs along X; turn it to follow neighbouring walls, two crossed at corners. A wall tile with no
    /// wall beside it is a stand-alone block: one of the high-cover props (container, barricade) when there are any.
    /// </summary>
    private void AddWall(Node3D tile, TileMap map, Pos p, ulong seed)
    {
        bool WallAt(int dx, int dy) => Is(map, p + new Pos(dx, dy), Terrain.Wall);
        var eastWest = WallAt(1, 0) || WallAt(-1, 0);
        var northSouth = WallAt(0, 1) || WallAt(0, -1);
        if (!eastWest && !northSouth && _models.HighCover.Count > 0
            && _models.Prop(_models.HighCover[GroundPattern.Pick(seed, p, _models.HighCover.Count, 6)]) is { } block)
        {
            block.RotationDegrees = new Vector3(0, GroundPattern.Pick(seed, p, 4, 7) * 90, 0);
            tile.AddChild(block);
            return;
        }
        if (eastWest || !northSouth) tile.AddChild(_models.Wall());
        if (northSouth)
        {
            var wall = _models.Wall();
            wall.RotationDegrees = new Vector3(0, 90, 0);
            tile.AddChild(wall);
        }
    }

    /// <summary>
    /// Two cover tiles side by side make a wrecked car (the model covers two tiles along X). A lone cover tile gets a
    /// low-cover prop (barrier, sandbags) lined up with neighbouring walls or cover; without those, a shrunken car.
    /// </summary>
    private void AddCover(Node3D tile, TileMap map, Pos p, HashSet<Pos> placed, ulong seed)
    {
        if (placed.Contains(p)) return;
        placed.Add(p);
        var east = p + new Pos(1, 0);
        var south = p + new Pos(0, 1);
        if (Is(map, east, Terrain.Cover) && !placed.Contains(east))
        {
            placed.Add(east);
            var car = _models.Car();
            car.Position = new Vector3(TileGrid.TileSize / 2, 0, 0);
            car.RotationDegrees = new Vector3(0, GroundPattern.Flip(seed, p) ? 180 : 0, 0);
            tile.AddChild(car);
            return;
        }
        if (Is(map, south, Terrain.Cover) && !placed.Contains(south))
        {
            placed.Add(south);
            var car = _models.Car();
            car.Position = new Vector3(0, 0, TileGrid.TileSize / 2);
            car.RotationDegrees = new Vector3(0, GroundPattern.Flip(seed, p) ? 270 : 90, 0);
            tile.AddChild(car);
            return;
        }

        if (_models.LowCover.Count > 0 && _models.Prop(_models.LowCover[GroundPattern.Pick(seed, p, _models.LowCover.Count, 8)]) is { } prop)
        {
            // Props run along X; turn them to continue a north-south line of walls or cover.
            var alongZ = Is(map, p + new Pos(0, 1), Terrain.Wall, Terrain.Cover) || Is(map, p + new Pos(0, -1), Terrain.Wall, Terrain.Cover);
            var alongX = Is(map, p + new Pos(1, 0), Terrain.Wall, Terrain.Cover) || Is(map, p + new Pos(-1, 0), Terrain.Wall, Terrain.Cover);
            var turned = alongZ && !alongX || !alongZ && !alongX && GroundPattern.Flip(seed, p);
            prop.RotationDegrees = new Vector3(0, turned ? 90 : 0, 0);
            tile.AddChild(prop);
            return;
        }

        var small = _models.Car();
        small.Scale = Vector3.One * 0.5f;
        small.RotationDegrees = new Vector3(0, (p.X + p.Y) % 2 == 0 ? 30 : -30, 0);
        tile.AddChild(small);
    }

    /// <summary>The exit marker's chevrons point along its +Z: turn them toward the nearest map edge, the way out.</summary>
    private void AddExit(Node3D tile, TileMap map, Pos p)
    {
        if (_models.ExitMarker() is { } marker)
        {
            var toEdge = new[]
            {
                (p.X, new Vector2(-1, 0)), (map.Width - 1 - p.X, new Vector2(1, 0)),
                (p.Y, new Vector2(0, -1)), (map.Height - 1 - p.Y, new Vector2(0, 1)),
            }.MinBy(e => e.Item1).Item2;
            marker.Rotation = new Vector3(0, Mathf.Atan2(toEdge.X, toEdge.Y), 0);
            tile.AddChild(marker);
            return;
        }
        tile.AddChild(Quad(ExitColor, 0.09f, 1.7f));
        tile.AddChild(new Label3D
        {
            Text = "EXIT", FontSize = 40, PixelSize = 0.01f, Position = new Vector3(0, 0.35f, 0), Font = Hud.Fonts.DisplayBold,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = Hud.Palette.Good,
            OutlineSize = 10, OutlineModulate = new Color(Hud.Palette.Void, 0.9f),
        });
    }

    private readonly Dictionary<Pos, Node3D> _parked = new();

    /// <summary>
    /// A party's parked vehicle on its tile, lengthwise along the map edge it stands on (it reaches into the neighbouring
    /// edge tiles a little, as a vehicle would), turned to face into the zone.
    /// </summary>
    private void AddParked(Node3D tile, TileMap map, Pos p)
    {
        if (ParkedVehicles.At(_presenter.State, p) is not { } vehicle) return;
        var model = _models.ParkedVehicle(vehicle, TileGrid.TileSize * 1.6f);
        var alongX = p.Y == 0 || p.Y == map.Height - 1;
        model.RotationDegrees = new Vector3(0, alongX ? 90 : 0, 0);
        tile.AddChild(model);
        _parked[p] = model;
    }

    private StandardMaterial3D? _wreckMaterial;

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
            foreach (var mesh in _tileMeshes[i]) mesh.MaterialOverlay = overlay ?? _ground.GetValueOrDefault(mesh);
        }

        // A wrecked vehicle stays as a burnt-out hulk.
        foreach (var v in _presenter.State.Vehicles.Where(v => v.Wrecked))
            if (_parked.TryGetValue(v.Pos, out var hulk))
            {
                _wreckMaterial ??= Placeholders.Material(new Color(0.12f, 0.08f, 0.06f, 0.75f), unshaded: true);
                foreach (var mesh in Meshes(hulk)) mesh.MaterialOverlay = _wreckMaterial;
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
                crate = Pile(cache);
                AddChild(crate);
                Glint(crate);
                _crates[cache.Id] = crate;
            }
            crate.Visible = _presenter.IsExplored(cache.Pos) || _presenter.IsVisible(cache.Pos);
            var dim = _presenter.IsVisible(cache.Pos) ? null : _fogMaterial;
            foreach (var mesh in Meshes(crate)) mesh.MaterialOverlay = dim;
        }
    }

    /// <summary>
    /// A pile on its tile: a zone cache is the full crate; remains and dropped things a small crate (the stand-in for a
    /// loot sack until one is modelled) set off the tile centre so the body's tile still reads; a stash is amber.
    /// Everything but a zone cache glints, so a kill's loot is easy to spot.
    /// </summary>
    private Node3D Pile(LootCache cache)
    {
        var root = new Node3D { Name = $"Pile{cache.Id}", Position = TileGrid.ToWorld(cache.Pos) };
        var crate = _models.Crate();
        crate.RotationDegrees = new Vector3(0, cache.Id * 47 % 90 - 45, 0);
        root.AddChild(crate);
        if (cache.Kind == LootPileKind.Cache) return root;
        crate.Scale = Vector3.One * (cache.Kind == LootPileKind.Stash ? 0.8f : 0.65f);
        crate.Position = cache.Kind == LootPileKind.Stash ? Vector3.Zero : new Vector3(0.45f, 0, 0.35f);
        if (cache.Kind == LootPileKind.Stash)
            foreach (var mesh in Meshes(crate)) mesh.MaterialOverride = Placeholders.Material(Hud.Palette.Amber);
        // The glint: a small amber gem bobbing and turning over the pile.
        var top = crate.Position + new Vector3(0, 1.0f, 0);
        var glint = new MeshInstance3D
        {
            Name = "Glint", Mesh = new SphereMesh { Radius = 0.2f, Height = 0.48f, RadialSegments = 4, Rings = 1 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Hud.Palette.Amber, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true, Emission = Hud.Palette.Amber,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = top,
        };
        root.AddChild(glint);
        return root;
    }

    /// <summary>Starts a pile's glint bobbing and turning (tweens need the node in the tree).</summary>
    private static void Glint(Node3D pile)
    {
        if (pile.GetNodeOrNull<MeshInstance3D>("Glint") is not { } glint) return;
        var top = glint.Position;
        var bob = glint.CreateTween().SetLoops();
        bob.TweenProperty(glint, "position:y", top.Y + 0.25f, 0.9f).SetTrans(Tween.TransitionType.Sine);
        bob.TweenProperty(glint, "position:y", top.Y, 0.9f).SetTrans(Tween.TransitionType.Sine);
        var spin = glint.CreateTween().SetLoops();
        spin.TweenProperty(glint, "rotation:y", Mathf.Tau, 2.4f).From(0f);
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
            Mesh = new PlaneMesh { Size = new Vector2(1.9f, 1.9f) },
            MaterialOverride = _highlightMaterials[kind],
            Position = TileGrid.ToWorld(p) + new Vector3(0, kind == HighlightKind.Selected ? 0.13f : 0.12f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _highlights.AddChild(quad);
        if (label is null) return;
        _highlights.AddChild(new Label3D
        {
            // Tile notes sit low (move cost, who an item reaches); text over units is the HUD's (Ui.WorldTags).
            Text = label, FontSize = 30, PixelSize = 0.01f, OutlineSize = 12, Font = Hud.Fonts.BodyBold,
            OutlineModulate = new Color(Hud.Palette.Void, 0.9f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            Position = TileGrid.ToWorld(p) + new Vector3(0, 0.5f, 0),
            Modulate = kind == HighlightKind.Use ? Hud.Palette.Good : Hud.Palette.Text,
        });
    }

    public void SetHover(Pos? p)
    {
        _hover.Visible = p is { } tile && _presenter.State.Map.InBounds(tile);
        if (p is { } at) _hover.Position = TileGrid.ToWorld(at) + new Vector3(0, 0.14f, 0);
    }

    /// <summary>A white square texture: opaque border <paramref name="border"/> texels wide (of 64), the inside at <paramref name="fill"/> alpha.</summary>
    private static ImageTexture TileFrame(int border, float fill)
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var edge = x < border || y < border || x >= size - border || y >= size - border;
            image.SetPixel(x, y, new Color(1, 1, 1, edge ? 1 : fill));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static StandardMaterial3D Framed(Texture2D frame, Color color)
    {
        var material = Placeholders.Material(color, unshaded: true);
        material.AlbedoTexture = frame;
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        return material;
    }

    private static MeshInstance3D Quad(Color color, float height, float size = 1.9f) => new()
    {
        Mesh = new PlaneMesh { Size = new Vector2(size, size) },
        MaterialOverride = Placeholders.Material(color, unshaded: true),
        Position = new Vector3(0, height, 0),
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
