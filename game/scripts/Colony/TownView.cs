using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Remnants.Game.Colony;

/// <summary>
/// Renders the town from Core state: ground and scenery (<see cref="TownDressing"/>), plot grid and one node per building. Handles plot
/// picking (select a building, or choose a plot while placing a new one). Sends no commands itself.
/// </summary>
public partial class TownView : Node3D
{
    private static readonly Color PlotColor = new(0.42f, 0.39f, 0.33f);
    private static readonly Color FreeHover = new(0.35f, 0.6f, 0.3f);
    private static readonly Color BlockedHover = new(0.65f, 0.25f, 0.2f);
    private static readonly Color SelectedColor = new(0.85f, 0.65f, 0.25f);

    /// <summary>Most lamp OmniLights the town runs at once; lamps past it glow without casting light.</summary>
    private const int MaxLampLights = 12;

    private readonly Dictionary<int, (Node3D Node, string DefId, int Level, int Plot, Label3D Label, bool Lit, int Lights)> _buildings = new();
    private readonly List<StandardMaterial3D> _plotMaterials = new();

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
            if (_buildings.TryGetValue(b.Id, out var known) && known.Level == b.Level && known.DefId == b.DefId && known.Plot == b.Plot
                && known.Lit == b.Floodlight)
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
            var lights = Lamps(node, b.Id, def, b, budget);
            AddChild(node);
            _buildings[b.Id] = (node, b.DefId, b.Level, b.Plot, label, b.Floodlight, lights);
        }

        foreach (var gone in _buildings.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _buildings[gone].Node.QueueFree();
            _buildings.Remove(gone);
        }
        if (SelectedBuildingId is { } sel && !alive.Contains(sel)) Select(null);
        RefreshPlots();
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
        if (_plotMaterials.Count == 0) return;
        var sim = _session.Sim;
        var selectedPlot = SelectedBuildingId is { } id ? sim.State.FindBuilding(id)?.Plot ?? -1 : -1;
        for (var plot = 0; plot < _plotMaterials.Count; plot++)
        {
            var color = PlotColor;
            if (PlacingDefId is not null && plot == _hoverPlot) color = sim.IsPlotFree(plot) ? FreeHover : BlockedHover;
            else if (PlacingDefId is not null && sim.IsPlotFree(plot)) color = PlotColor.Lerp(FreeHover, 0.25f);
            else if (plot == selectedPlot) color = SelectedColor;
            else if (plot == _hoverPlot) color = PlotColor.Lightened(0.15f);
            _plotMaterials[plot].AlbedoColor = color;
        }
        foreach (var (buildingId, entry) in _buildings)
            entry.Label.Visible = buildingId == SelectedBuildingId || entry.Plot == _hoverPlot;
    }

    /// <summary>The plot top: a light kerb a few texels wide round a plain inside (the albedo colour tints both).</summary>
    private static ImageTexture PlotFrame()
    {
        const int size = 128, border = 2;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var edge = x < border || y < border || x >= size - border || y >= size - border;
            image.SetPixel(x, y, edge ? Colors.White : new Color(0.78f, 0.78f, 0.78f));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private void BuildGround()
    {
        var sim = _session.Sim;
        var hall = sim.State.Buildings.FirstOrDefault(b => b.DefId == sim.Data.Rules.TownHallBuildingId);
        var (campfire, playground) = hall is null ? (new Vector3(0, 0, -_layout.Extent.Y / 2 - 4), Vector3.Zero) : _layout.Commons(hall.Plot);
        var fires = TownDressing.Build(this, _layout, _models, campfire, playground);

        // Plots are cleared, packed-earth lots with a pale kerb, a step lighter than the ground around them.
        var frame = PlotFrame();
        var tile = new BoxMesh { Size = new Vector3(TownLayout.PlotSize - 0.6f, 0.1f, TownLayout.PlotSize - 0.6f) };
        for (var plot = 0; plot < _layout.Count; plot++)
        {
            var material = Placeholders.Material(PlotColor);
            material.AlbedoTexture = frame;
            _plotMaterials.Add(material);
            AddChild(new MeshInstance3D
            {
                Name = $"Plot{plot}",
                Mesh = tile,
                MaterialOverride = material,
                Position = _layout.Center(plot) + new Vector3(0, 0.05f, 0),
            });
        }

        Lighting = new DayNightLighting { Name = "Lighting" };
        AddChild(Lighting);
        Lighting.Setup(_session);
        foreach (var (light, energy) in fires) Lighting.AddLight(light, energy, dayEnergy: energy * 0.35f, flicker: true);
    }
}
