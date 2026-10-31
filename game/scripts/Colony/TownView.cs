using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony;

/// <summary>
/// Renders the town from Core state: ground and scenery (<see cref="TownDressing"/>), one node per building on a soft patch
/// of trodden earth, and the plot grid, which is drawn only while placing (a faint footprint on each free plot, the hovered
/// one green or red). Handles plot picking (select a building, or choose a plot while placing a new one). Sends no
/// commands itself.
/// </summary>
public partial class TownView : Node3D
{
    private static readonly Color PatchColor = new(0.3f, 0.27f, 0.22f, 0.55f);
    private static readonly Color FreeHover = new(0.55f, 0.9f, 0.45f, 0.9f);
    private static readonly Color BlockedHover = new(0.95f, 0.35f, 0.25f, 0.9f);
    private static readonly Color FreeFootprint = new(0.85f, 0.82f, 0.7f, 0.22f);
    private static readonly Color SelectedColor = new(0.85f, 0.65f, 0.25f, 0.6f);

    /// <summary>Most lamp OmniLights the town runs at once; lamps past it glow without casting light.</summary>
    private const int MaxLampLights = 12;

    private readonly Dictionary<int, (Node3D Node, string DefId, int Level, int Plot, Label3D Label, bool Lit, int Lights, int Wreck)> _buildings = new();
    private readonly List<(MeshInstance3D Mesh, StandardMaterial3D Material)> _footprints = new();
    private readonly List<(MeshInstance3D Mesh, StandardMaterial3D Material)> _patches = new();

    private ColonySession _session = null!;
    private TownLayout _layout = null!;
    private ModelLibrary _models = null!;
    private Camera3D _camera = null!;
    private int _hoverPlot = -1;

    public int? SelectedBuildingId { get; private set; }

    /// <summary>Sun, moon, sky and the night lights, following the colony clock.</summary>
    public DayNightLighting Lighting { get; private set; } = null!;

    /// <summary>Building def being placed, or null. While set, clicking a plot raises <see cref="PlotChosen"/>.</summary>
    public string? PlacingDefId { get; private set; }

    public event Action<int?>? SelectionChanged;
    public event Action<string, int>? PlotChosen;
    public event Action? PlacingChanged;

    public void Setup(ColonySession session, TownLayout layout, ModelLibrary models, Camera3D camera)
    {
        _session = session;
        _layout = layout;
        _models = models;
        _camera = camera;
        BuildGround();
        Sync();
    }

    public void Select(int? buildingId)
    {
        SelectedBuildingId = buildingId;
        RefreshPlots();
        SelectionChanged?.Invoke(buildingId);
    }

    public void BeginPlacing(string defId)
    {
        PlacingDefId = defId;
        RefreshPlots();
        PlacingChanged?.Invoke();
    }

    /// <summary>Debug (screenshots): show <paramref name="plot"/> as hovered, as if the cursor were over it.</summary>
    public void HoverPlot(int plot)
    {
        _hoverPlot = plot;
        RefreshPlots();
    }

    public void CancelPlacing()
    {
        if (PlacingDefId is null) return;
        PlacingDefId = null;
        RefreshPlots();
        PlacingChanged?.Invoke();
    }

    /// <summary>Drop all building nodes (after a load, ids may mean different buildings).</summary>
    public void Reset()
    {
        foreach (var entry in _buildings.Values) entry.Node.QueueFree();
        _buildings.Clear();
        CancelPlacing();
        Select(null);
        Sync();
    }

    public void Sync()
    {
        var sim = _session.Sim;
        var alive = new HashSet<int>();
        foreach (var b in sim.State.Buildings)
        {
            alive.Add(b.Id);
            var wreck = Wreck(sim, b);
            if (_buildings.TryGetValue(b.Id, out var known) && known.Level == b.Level && known.DefId == b.DefId && known.Plot == b.Plot
                && known.Lit == b.Floodlight && known.Wreck == wreck)
                continue;
            if (known.Node is not null) known.Node.QueueFree();

            var def = sim.Data.Buildings[b.DefId];
            var node = new Node3D { Name = $"Building{b.Id}", Position = _layout.Center(b.Plot) };
            var model = _models.Building(def, b.Level);
            node.AddChild(model);
            // Shown only for the hovered and selected building (see RefreshPlots), so names never pile up over the town.
            var label = new Label3D
            {
                Text = ColonyText.BuildingName(sim, b),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Position = new Vector3(0, Height(model) + 0.8f, 0),
                FontSize = 48,
                PixelSize = 0.01f,
                OutlineSize = 12,
                NoDepthTest = true,
                Visible = false,
            };
            node.AddChild(label);
            var budget = MaxLampLights - _buildings.Where(kv => kv.Key != b.Id).Sum(kv => kv.Value.Lights);
            var lights = wreck == 2 ? 0 : Lamps(node, b.Id, def, b, budget);
            ShowDamage(node, model, wreck);
            AddChild(node);
            _buildings[b.Id] = (node, b.DefId, b.Level, b.Plot, label, b.Floodlight, lights, wreck);
        }

        foreach (var gone in _buildings.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _buildings[gone].Node.QueueFree();
            _buildings.Remove(gone);
            _crops.Remove(gone);
        }
        foreach (var b in sim.State.Buildings) SyncCrops(sim, b);
        if (SelectedBuildingId is { } sel && !alive.Contains(sel)) Select(null);
        RefreshPlots();
    }

    /// <summary>Raid damage as a look: 0 whole, 1 damaged, 2 wrecked (rubble, see <c>RaidSystem.IsRubble</c>).</summary>
    private static int Wreck(Remnants.Core.Colony.ColonySim sim, Remnants.Core.Colony.Building b) =>
        sim.Raids is null ? 0
        : Remnants.Core.Raids.RaidSystem.IsRubble(sim, b) ? 2
        : Remnants.Core.Raids.RaidSystem.Damage(sim.State, b) > 0 ? 1 : 0;

    /// <summary>A damaged building is scorched a little; a wrecked one is slumped, dark and strewn with rubble.</summary>
    private void ShowDamage(Node3D node, Node3D model, int wreck)
    {
        if (wreck == 0) return;
        DefenseModels.Tint(model, wreck == 2 ? new Color(0.05f, 0.04f, 0.035f, 0.6f) : new Color(0.25f, 0.12f, 0.06f, 0.22f));
        if (wreck != 2) return;
        model.Scale *= new Vector3(1, 0.45f, 1);
        model.Rotation += new Vector3(0.04f, 0, -0.05f);
        var spots = new[] { new Vector3(-1.8f, 0, 1.6f), new Vector3(1.9f, 0, 1.2f), new Vector3(0.4f, 0, 2.3f), new Vector3(-1.2f, 0, -2f) };
        for (var i = 0; i < spots.Length; i++)
        {
            if (_models.TryInstance("rubble") is not { } rubble) break;
            rubble.Position = spots[i];
            rubble.RotateY(i * 1.7f);
            node.AddChild(rubble);
        }
    }

    /// <summary>Crop rows on a Farm by what it grows and how far along it is (stage 1–3); the key they were built for.</summary>
    private readonly Dictionary<int, string> _crops = new();

    /// <summary>Farm harvest id → crop row model family (manifest <c>crop_{family}_s1..s3</c>).</summary>
    private static readonly Dictionary<string, string> CropRows = new()
    {
        ["potatoes"] = "potato", ["beans"] = "beans", ["corn"] = "corn", ["herbs"] = "herbs",
    };

    /// <summary>
    /// Lays the farm's crop rows over its field: the row model of its crop at the growth stage of the standing crop (1 just
    /// planted, 2 half grown, 3 ripe and coming in). Rebuilt only when the crop or stage changes.
    /// </summary>
    private void SyncCrops(ColonySim sim, Building b)
    {
        if (b.DefId != "farm" || !_buildings.TryGetValue(b.Id, out var entry)) return;
        var harvest = Harvesting.Current(sim.Data, b);
        if (harvest is null || !CropRows.TryGetValue(harvest.Id, out var family))
        {
            if (_crops.Remove(b.Id)) entry.Node.GetNodeOrNull("Crops")?.QueueFree();
            return;
        }
        var grow = harvest.GrowDays * sim.Data.Rules.TicksPerDay;
        var wait = Harvesting.TicksUntilReady(sim.State, b);
        var stage = wait <= 0 || grow <= 0 ? 3 : 1 - wait / grow < 0.5 ? 1 : 2;
        var key = $"{family}:{stage}:{entry.Level}";
        if (_crops.GetValueOrDefault(b.Id) == key && entry.Node.GetNodeOrNull("Crops") is not null) return;
        _crops[b.Id] = key;
        entry.Node.GetNodeOrNull("Crops")?.Free();
        var rows = new Node3D { Name = "Crops" };
        // The field is the front half of the 6 x 6 m farm (+Z); rows run front to back, 0.6 m apart.
        for (var i = 0; i < 7; i++)
            if (_models.TryInstance($"crop_{family}_s{stage}") is { } row)
            {
                row.Position = new Vector3(-1.8f + 0.6f * i, 0.02f, 1.5f);
                rows.AddChild(row);
            }
        entry.Node.AddChild(rows);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m:
                var plot = PlotUnder(m.Position);
                if (plot != _hoverPlot)
                {
                    _hoverPlot = plot;
                    RefreshPlots();
                }
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b:
                OnClick(PlotUnder(b.Position));
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } when PlacingDefId is not null:
            case InputEventKey { Pressed: true, Keycode: Key.Escape } when PlacingDefId is not null:
                CancelPlacing();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void OnClick(int plot)
    {
        if (PlacingDefId is { } defId)
        {
            if (plot >= 0) PlotChosen?.Invoke(defId, plot);
            return;
        }
        var building = plot < 0 ? null : _session.Sim.State.Buildings.FirstOrDefault(b => b.Plot == plot);
        Select(building?.Id);
    }

    /// <summary>
    /// Night lights for a building: a lantern on a post by the front of the plot; homes and the Town Hall also glow from
    /// inside; a lit outdoor workplace (level 2+, or a Floodlight) gets a floodlight on a mast that burns while its
    /// Night Shift is on. Returns the OmniLights used (at most <paramref name="budget"/>; the rest only glow).
    /// </summary>
    private int Lamps(Node3D node, int id, Remnants.Core.Data.BuildingDef def, Remnants.Core.Colony.Building b, int budget)
    {
        var used = 0;
        var post = Placeholders.Material(new Color(0.28f, 0.26f, 0.24f));
        OmniLight3D? Light(Vector3 at, float range, Color color)
        {
            if (used >= budget) return null;
            used++;
            var light = new OmniLight3D { Position = at, OmniRange = range, LightColor = color, LightEnergy = 0, ShadowEnabled = false };
            node.AddChild(light);
            return light;
        }
        StandardMaterial3D Glass(Color color)
        {
            var m = Placeholders.Material(color);
            m.EmissionEnabled = true;
            m.Emission = color;
            m.EmissionEnergyMultiplier = 0;
            return m;
        }

        var warm = new Color(1f, 0.72f, 0.4f);
        var lantern = new Vector3(TownLayout.PlotSize * 0.36f, 0, TownLayout.PlotSize * 0.38f);
        node.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 1.5f, RadialSegments = 6 },
            MaterialOverride = post, Position = lantern + new Vector3(0, 0.75f, 0),
        });
        var lanternGlass = Glass(warm);
        node.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.26f, 0.2f) }, MaterialOverride = lanternGlass,
            Position = lantern + new Vector3(0, 1.6f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Lighting.AddGlow(lanternGlass, node, 4f);
        if (Light(lantern + new Vector3(0, 1.7f, 0), 4.5f, warm) is { } l) Lighting.AddLight(l, 1.1f);

        var home = def.Levels.Any(x => x.Beds > 0) || def.Id == _session.Sim.Data.Rules.TownHallBuildingId;
        if (home && Light(new Vector3(0, 1.4f, 0.6f), 7f, new Color(1f, 0.68f, 0.38f)) is { } inside) Lighting.AddLight(inside, 1.4f);

        if (def.Outdoor && def.Job is not null && (b.Floodlight || b.Level >= _session.Sim.Data.Rules.DayNight.LitMinLevel))
        {
            var mast = new Vector3(-TownLayout.PlotSize * 0.38f, 0, TownLayout.PlotSize * 0.38f);
            node.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.08f, Height = 3.6f, RadialSegments = 6 },
                MaterialOverride = post, Position = mast + new Vector3(0, 1.8f, 0),
            });
            var cold = new Color(0.95f, 0.95f, 0.85f);
            var head = Glass(cold);
            node.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.3f, 0.25f) }, MaterialOverride = head,
                Position = mast + new Vector3(0.15f, 3.6f, -0.15f), Rotation = new Vector3(-0.5f, Mathf.Pi / 4, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            bool On() => _session.Sim.State.FindBuilding(id) is { NightShift: true };
            Lighting.AddGlow(head, node, 5f, on: On);
            if (Light(new Vector3(-1.2f, 3.4f, 1.2f), 10f, cold) is { } flood) Lighting.AddLight(flood, 2.6f, on: On);
        }
        return used;
    }

    /// <summary>Top of a model's meshes in its parent's space (works before the node enters the tree).</summary>
    private static float Height(Node3D model)
    {
        var top = 0f;
        void Visit(Node node, Transform3D parent)
        {
            var xf = node is Node3D n3 ? parent * n3.Transform : parent;
            if (node is MeshInstance3D { Mesh: { } mesh }) top = Mathf.Max(top, (xf * mesh.GetAabb()).End.Y);
            foreach (var child in node.GetChildren()) Visit(child, xf);
        }
        Visit(model, Transform3D.Identity);
        return top;
    }

    private int PlotUnder(Vector2 screen)
    {
        var from = _camera.ProjectRayOrigin(screen);
        var dir = _camera.ProjectRayNormal(screen);
        var hit = new Plane(Vector3.Up, 0).IntersectsRay(from, dir);
        return hit is { } point ? _layout.PlotAt(point) : -1;
    }

    private void RefreshPlots()
    {
        if (_footprints.Count == 0) return;
        var sim = _session.Sim;
        var selectedPlot = SelectedBuildingId is { } id ? sim.State.FindBuilding(id)?.Plot ?? -1 : -1;
        var placing = PlacingDefId is not null;
        for (var plot = 0; plot < _footprints.Count; plot++)
        {
            var free = sim.IsPlotFree(plot);
            // Footprints only in build mode: free plots faintly, the hovered one green (free) or red (taken).
            var (footprint, footMaterial) = _footprints[plot];
            footprint.Visible = placing && (free || plot == _hoverPlot);
            if (footprint.Visible) footMaterial.AlbedoColor = plot == _hoverPlot ? free ? FreeHover : BlockedHover : FreeFootprint;
            // The soft earth patch sits under each building; it warms for the selected one and lifts for the hovered one.
            var (patch, patchMaterial) = _patches[plot];
            patch.Visible = !free;
            if (patch.Visible)
                patchMaterial.AlbedoColor = plot == selectedPlot ? SelectedColor : plot == _hoverPlot && !placing ? PatchColor.Lightened(0.2f) : PatchColor;
        }
        foreach (var (buildingId, entry) in _buildings)
            entry.Label.Visible = buildingId == SelectedBuildingId || entry.Plot == _hoverPlot;
    }

    /// <summary>The build-mode footprint: a thin rounded outline with a barely-there fill, transparent outside.</summary>
    private static ImageTexture Footprint()
    {
        const int size = 128;
        const float border = 3f, radius = 14f;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            // Distance inside a rounded square, so the corners are soft and nothing reads as a hard tile.
            var qx = Mathf.Max(Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius), 0);
            var qy = Mathf.Max(Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius), 0);
            var inside = radius - Mathf.Sqrt(qx * qx + qy * qy);
            var edge = Mathf.Clamp(1 - Mathf.Abs(inside - border) / border, 0, 1);
            var alpha = inside < 0 ? 0 : Mathf.Max(edge, 0.12f);
            image.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The ground under a building: a round patch of darker trodden earth that fades out into the ground.</summary>
    private static GradientTexture2D Patch()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, Colors.White);
        gradient.SetColor(1, new Color(1, 1, 1, 0));
        gradient.AddPoint(0.55f, new Color(1, 1, 1, 0.75f));
        return new GradientTexture2D
        {
            Gradient = gradient, Width = 128, Height = 128, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
        };
    }

    private void BuildGround()
    {
        var sim = _session.Sim;
        var hall = sim.State.Buildings.FirstOrDefault(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
        var (campfire, playground) = hall is null ? (new Vector3(0, 0, -_layout.Extent.Y / 2 - 4), Vector3.Zero) : _layout.Commons(hall.Plot);
        var fires = TownDressing.Build(this, _layout, _models, campfire, playground);

        // No lots on the ground: each building stands on a soft patch of earth, and the grid shows only in build mode.
        var footprint = Footprint();
        var patch = Patch();
        var footMesh = new PlaneMesh { Size = new Vector2(TownLayout.PlotSize - 0.5f, TownLayout.PlotSize - 0.5f) };
        var patchMesh = new PlaneMesh { Size = new Vector2(TownLayout.PlotSize * 1.05f, TownLayout.PlotSize * 1.05f) };
        for (var plot = 0; plot < _layout.Count; plot++)
        {
            var foot = Placeholders.Material(FreeFootprint, unshaded: true);
            foot.AlbedoTexture = footprint;
            var footNode = new MeshInstance3D
            {
                Name = $"Footprint{plot}", Mesh = footMesh, MaterialOverride = foot, Visible = false,
                Position = _layout.Center(plot) + new Vector3(0, 0.06f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(footNode);
            _footprints.Add((footNode, foot));

            var earth = Placeholders.Material(PatchColor);
            earth.AlbedoTexture = patch;
            // Round in plan, turned per plot so neighbouring patches do not repeat.
            var patchNode = new MeshInstance3D
            {
                Name = $"Patch{plot}", Mesh = patchMesh, MaterialOverride = earth, Visible = false,
                Position = _layout.Center(plot) + new Vector3(0, 0.02f, 0), Rotation = new Vector3(0, plot * 1.3f, 0),
                Scale = new Vector3(1 + plot % 3 * 0.06f, 1, 1 - plot % 2 * 0.05f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(patchNode);
            _patches.Add((patchNode, earth));
        }

        Lighting = new DayNightLighting { Name = "Lighting" };
        AddChild(Lighting);
        Lighting.Setup(_session);
        foreach (var (light, energy) in fires) Lighting.AddLight(light, energy, dayEnergy: energy * 0.35f, flicker: true);
    }
}
