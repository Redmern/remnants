using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Side = Remnants.Core.Raids.Side;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony;

/// <summary>
/// The colony's defenses on the town (purely visual, sends no commands): one model per placed defense (sprung traps
/// dimmed, idle ones greyed, destroyed ones knocked down), and the defend mode — a faint grid of the free cells, a ghost of
/// the defense being placed that follows the mouse snapped to the grid (green or red by Core's
/// <see cref="DefenseHandlers.CanPlace"/>, the reason over it), a line of ghosts while dragging walls or traps, R to turn
/// it, and the coverage / fire arc (<see cref="DefenseGrid.Coverage"/>) of the ghost and of the hovered and selected
/// defense. Clicking a placed defense selects it (in or out of the mode). <see cref="ColonyScreen"/> sends the commands.
/// <para>Geometry: the defense grid is the plot grid at <see cref="GridRules.CellsPerPlot"/> cells per plot plus a margin,
/// centred like <see cref="TownLayout"/> on the origin; cell (0, 0) is the north-west corner, x east (+X), y south (+Z,
/// toward the default camera and the road).</para>
/// </summary>
public partial class DefenseView : Node3D
{
    private const float OverlayY = 0.13f;

    private static readonly Color FreeCell = new(0.7f, 0.85f, 0.6f, 0.07f);
    private static readonly Color GoodCell = new(0.45f, 0.9f, 0.4f, 0.5f);
    private static readonly Color BadCell = new(0.95f, 0.3f, 0.2f, 0.55f);
    private static readonly Color GoodGhost = new(0.45f, 0.95f, 0.45f, 0.45f);
    private static readonly Color BadGhost = new(1f, 0.3f, 0.2f, 0.5f);
    private static readonly Color CoverCell = new(0.92f, 0.65f, 0.24f, 0.24f);
    private static readonly Color SelectedCell = new(0.92f, 0.65f, 0.24f, 0.8f);
    private static readonly Color HoverCell = new(0.92f, 0.85f, 0.7f, 0.35f);

    private readonly Dictionary<int, (Node3D Node, string Key)> _nodes = new();
    private ColonySession _session = null!;
    private ModelLibrary _models = null!;
    private Camera3D _camera = null!;
    private DayNightLighting? _lighting;
    private Node3D? _dressing;
    private HashSet<(int, int)>? _fenceCells;
    private MeshInstance3D _grid = null!;
    private MeshInstance3D _cover = null!;
    private MeshInstance3D _marks = null!;
    private Label3D _ghostLabel = null!;
    private Node3D? _ghost;
    private string _ghostKey = "";
    private string _gridSignature = "";
    private (int X, int Y)? _hover;
    private (int X, int Y)? _dragFrom;
    private Vector2? _rightPress;

    /// <summary>Defend mode: the grid shows and clicks select defenses instead of buildings.</summary>
    public bool Active { get; private set; }

    /// <summary>The defense being placed, or null.</summary>
    public string? PlacingDefId { get; private set; }

    /// <summary>The side the next defense faces (R turns it).</summary>
    public Side Facing { get; private set; } = Side.South;

    public int? SelectedDefenseId { get; private set; }

    /// <summary>Core's verdict on the ghost's cell (null when the mouse is off the grid or nothing is being placed).</summary>
    public CommandResult? GhostResult { get; private set; }

    /// <summary>The cell under the mouse, if on the grid.</summary>
    public (int X, int Y)? Hover => _hover;

    /// <summary>Defend mode turned on or off, or placement started or ended.</summary>
    public event Action? ModeChanged;

    /// <summary>The ghost moved, turned or changed verdict.</summary>
    public event Action? GhostChanged;

    public event Action<int?>? SelectionChanged;

    /// <summary>The player clicked (or dragged a line) to place: the commands to send, in order.</summary>
    public event Action<IReadOnlyList<PlaceDefense>>? PlaceRequested;

    public void Setup(ColonySession session, ModelLibrary models, Camera3D camera, DayNightLighting? lighting, Node3D? dressing)
    {
        _session = session;
        _models = models;
        _camera = camera;
        _lighting = lighting;
        _dressing = dressing;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        MeshInstance3D Layer(string name, int priority)
        {
            var m = (StandardMaterial3D)material.Duplicate();
            m.RenderPriority = priority;
            var mesh = new MeshInstance3D { Name = name, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(mesh);
            return mesh;
        }
        _grid = Layer("GridOverlay", 0);
        _cover = Layer("Coverage", 1);
        _marks = Layer("Marks", 2);
        _ghostLabel = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = 0.01f, OutlineSize = 10, NoDepthTest = true,
            Modulate = Palette.Danger, Visible = false, Font = Fonts.BodyBold,
        };
        AddChild(_ghostLabel);
        Sync();
    }

    private RaidData? Raids => _session.Sim.Raids;
    private DefenseGrid? Grid => Raids is { } r ? DefenseGrid.Of(_session.Sim, r) : null;
    private float CellSize => (float)(Raids?.Def.Grid.CellSize ?? 2);

    /// <summary>World position of a cell's centre on the ground.</summary>
    public Vector3 CellCenter(int x, int y)
    {
        var g = Grid!;
        var cs = CellSize;
        return new Vector3((x + 0.5f) * cs - g.Width * cs / 2, 0, (y + 0.5f) * cs - g.Height * cs / 2);
    }

    // ---- mode ----

    public void SetActive(bool on)
    {
        if (Active == on || (on && Raids is null)) return;
        Active = on;
        if (!on)
        {
            PlacingDefId = null;
            _dragFrom = null;
        }
        Refresh();
        ModeChanged?.Invoke();
    }

    public void BeginPlacing(string defId)
    {
        if (Raids is not { } raids || !raids.Defenses.ContainsKey(defId)) return;
        Active = true;
        PlacingDefId = defId;
        _dragFrom = null;
        Refresh();
        ModeChanged?.Invoke();
    }

    public void CancelPlacing()
    {
        if (PlacingDefId is null) return;
        PlacingDefId = null;
        _dragFrom = null;
        Refresh();
        ModeChanged?.Invoke();
    }

    /// <summary>R: turn the ghost a quarter clockwise (seen from above: south → west → north → east).</summary>
    public void Rotate()
    {
        Facing = Facing switch { Side.South => Side.West, Side.West => Side.North, Side.North => Side.East, _ => Side.South };
        Refresh();
        GhostChanged?.Invoke();
    }

    public void Select(int? defenseId)
    {
        if (SelectedDefenseId == defenseId) return;
        SelectedDefenseId = defenseId;
        Refresh();
        SelectionChanged?.Invoke(defenseId);
    }

    /// <summary>Debug (screenshots): put the mouse over a cell.</summary>
    public void DebugHover(int x, int y)
    {
        _hover = (x, y);
        Refresh();
    }

    /// <summary>After a load: ids may mean other defenses.</summary>
    public void Reset()
    {
        foreach (var (node, _) in _nodes.Values) node.QueueFree();
        _nodes.Clear();
        _gridSignature = "";
        SetActive(false);
        Select(null);
        Sync();
    }

    // ---- models ----

    public void Sync()
    {
        var sim = _session.Sim;
        if (Raids is not { } raids)
        {
            Visible = false;
            return;
        }
        Visible = true;
        var alive = new HashSet<int>();
        foreach (var d in sim.State.Defense.Defenses)
        {
            if (!raids.Defenses.TryGetValue(d.DefId, out var def)) continue;
            alive.Add(d.Id);
            var state = d.Hp <= 0 ? "down" : d.Sprung ? "sprung" : sim.State.Defense.Unpaid.Contains(d.Id) ? "idle" : "ok";
            var key = $"{d.DefId}|{d.Facing}|{d.X},{d.Y}|{state}";
            if (_nodes.TryGetValue(d.Id, out var known) && known.Key == key) continue;
            known.Node?.QueueFree();
            var node = DefenseModels.Build(_models, def, d.Facing);
            node.Name = $"Defense{d.Id}";
            node.Position = CellCenter(d.X, d.Y);
            switch (state)
            {
                case "down":
                    // Knocked flat: low, dark, askew.
                    node.Scale = new Vector3(1, 0.3f, 1);
                    node.Rotation += new Vector3(0.12f, 0, 0.08f);
                    DefenseModels.Tint(node, new Color(0.06f, 0.05f, 0.04f, 0.7f));
                    break;
                case "sprung":
                    DefenseModels.Tint(node, new Color(0.08f, 0.08f, 0.08f, 0.55f));
                    break;
                case "idle":
                    DefenseModels.Tint(node, new Color(0.35f, 0.35f, 0.35f, 0.4f));
                    break;
            }
            if (def.Lights && state == "ok" && node.FindChild("Lamp", true, false) is MeshInstance3D { MaterialOverride: StandardMaterial3D lamp })
                _lighting?.AddGlow(lamp, node, 6f);
            AddChild(node);
            _nodes[d.Id] = (node, key);
        }
        foreach (var gone in _nodes.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _nodes[gone].Node.QueueFree();
            _nodes.Remove(gone);
        }
        if (SelectedDefenseId is { } sel && !alive.Contains(sel)) Select(null);
        HideFenceUnderDefenses(sim);
        Refresh();
    }

    /// <summary>The decorative fence (<see cref="TownDressing"/>) steps aside where a real defense stands.</summary>
    private void HideFenceUnderDefenses(ColonySim sim)
    {
        if (_dressing is null || Grid is not { } g) return;
        var cells = sim.State.Defense.Defenses.Select(d => (d.X, d.Y)).ToHashSet();
        // Every tick syncs; walk the dressing only when the defenses' cells changed.
        if (_fenceCells is not null && _fenceCells.SetEquals(cells)) return;
        _fenceCells = cells;
        var cs = CellSize;
        foreach (var child in _dressing.GetChildren())
        {
            if (child is not Node3D prop || !prop.HasMeta(TownDressing.FenceMeta)) continue;
            var x = Mathf.FloorToInt((prop.Position.X + g.Width * cs / 2) / cs);
            var y = Mathf.FloorToInt((prop.Position.Z + g.Height * cs / 2) / cs);
            prop.Visible = !cells.Contains((x, y));
        }
    }

    // ---- input ----

    public override void _UnhandledInput(InputEvent e)
    {
        if (Raids is null) return;
        switch (e)
        {
            case InputEventMouseMotion m:
            {
                var cell = CellUnder(m.Position);
                if (cell != _hover)
                {
                    _hover = cell;
                    Refresh();
                }
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                OnLeft(b);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } r when Active:
                // A right click (not a drag, which orbits the camera) backs out: placement first, then the mode.
                if (r.Pressed) _rightPress = r.Position;
                else if (_rightPress is { } from && from.DistanceTo(r.Position) < 6)
                {
                    _rightPress = null;
                    if (PlacingDefId is not null) CancelPlacing();
                    else SetActive(false);
                    GetViewport().SetInputAsHandled();
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } when PlacingDefId is not null && Hotkeys.Pressed(e, "camera_rotate_right"):
                Rotate();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void OnLeft(InputEventMouseButton b)
    {
        var cell = CellUnder(b.Position);
        if (PlacingDefId is { } defId)
        {
            if (b.Pressed)
            {
                _dragFrom = cell;
                _hover = cell;
                Refresh();
            }
            else if (_dragFrom is { } from && cell is { } to)
            {
                var facing = LineFacing(from, to);
                var line = Lines(defId) ? Line(from, to) : [to];
                _dragFrom = null;
                PlaceRequested?.Invoke(line.Select(c => new PlaceDefense(defId, c.X, c.Y, facing)).ToList());
                Refresh();
            }
            else _dragFrom = null;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!b.Pressed) return;
        var hit = cell is { } c ? _session.Sim.State.Defense.Defenses.FirstOrDefault(d => d.X == c.X && d.Y == c.Y) : null;
        if (hit is not null || Active)
        {
            Select(hit?.Id);
            GetViewport().SetInputAsHandled();
        }
        else Select(null);  // and the town picks the plot
    }

    /// <summary>Walls, barricades and traps go down in lines when dragged.</summary>
    private bool Lines(string defId) =>
        Raids!.Defenses[defId].Kind is DefenseKind.Wall or DefenseKind.Barricade or DefenseKind.Trap;

    /// <summary>A straight run of cells from <paramref name="from"/> along the longer axis toward <paramref name="to"/>.</summary>
    private static List<(int X, int Y)> Line((int X, int Y) from, (int X, int Y) to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var list = new List<(int, int)>();
        if (Math.Abs(dx) >= Math.Abs(dy))
            for (var i = 0; i <= Math.Abs(dx); i++) list.Add((from.X + Math.Sign(dx) * i, from.Y));
        else
            for (var i = 0; i <= Math.Abs(dy); i++) list.Add((from.X, from.Y + Math.Sign(dy) * i));
        return list;
    }

    /// <summary>A dragged wall runs along the drag: across x it faces north/south, across y east/west.</summary>
    private Side LineFacing((int X, int Y) from, (int X, int Y) to)
    {
        if (from == to || PlacingDefId is null || !Lines(PlacingDefId)) return Facing;
        var alongX = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        if (alongX) return Facing is Side.North or Side.South ? Facing : Side.South;
        return Facing is Side.East or Side.West ? Facing : Side.East;
    }

    private (int X, int Y)? CellUnder(Vector2 screen)
    {
        if (Grid is not { } g) return null;
        var hit = new Plane(Vector3.Up, 0).IntersectsRay(_camera.ProjectRayOrigin(screen), _camera.ProjectRayNormal(screen));
        if (hit is not { } p) return null;
        var cs = CellSize;
        var x = Mathf.FloorToInt((p.X + g.Width * cs / 2) / cs);
        var y = Mathf.FloorToInt((p.Z + g.Height * cs / 2) / cs);
        return g.Inside(x, y) ? (x, y) : null;
    }

    // ---- overlays ----

    /// <summary>Re-reads the ghost and redraws the grid, coverage and marks.</summary>
    private void Refresh()
    {
        if (Raids is not { } raids || Grid is not { } g) return;
        var sim = _session.Sim;
        RefreshGhost(sim, raids);

        // Free cells, only in defend mode; rebuilt when the town or the defenses change.
        var signature = Active ? $"{sim.State.Buildings.Count}|{string.Join(",", sim.State.Defense.Defenses.Select(d => $"{d.X}.{d.Y}"))}" : "";
        if (signature != _gridSignature)
        {
            _gridSignature = signature;
            var occupied = sim.State.Defense.Defenses.Select(d => (d.X, d.Y)).ToHashSet();
            var free = new List<(int, int)>();
            if (Active)
                for (var y = 0; y < g.Height; y++)
                for (var x = 0; x < g.Width; x++)
                    if (!occupied.Contains((x, y)) && (g.PlotAt(x, y) is var plot && (plot < 0 || sim.IsPlotFree(plot)))) free.Add((x, y));
            _grid.Mesh = Quads(free.Select(c => (c, FreeCell)), 0.18f);
        }

        // Coverage of the ghost, the hovered defense and the selected one.
        var focus = new List<(PlacedDefense D, DefenseDef Def)>();
        if (SelectedDefenseId is { } sel && sim.State.Defense.Find(sel) is { } selected) focus.Add((selected, raids.Defense(selected.DefId)));
        var hovered = _hover is { } h ? sim.State.Defense.Defenses.FirstOrDefault(d => d.X == h.X && d.Y == h.Y) : null;
        if (hovered is not null && hovered.Id != SelectedDefenseId && (Active || PlacingDefId is null)) focus.Add((hovered, raids.Defense(hovered.DefId)));
        if (PlacingDefId is { } placing && _hover is { } gh)
            focus.Add((new PlacedDefense { X = gh.X, Y = gh.Y, Facing = GhostFacing, DefId = placing }, raids.Defense(placing)));
        var covered = focus.Where(f => f.Def.Range > 0).SelectMany(f => g.Coverage(f.D, f.Def)).Distinct();
        _cover.Mesh = Quads(covered.Select(c => (c, CoverCell)), 0.04f);

        // Marks: the ghost's cells, the selected and hovered defense's cell.
        var marks = new List<((int, int), Color)>();
        if (PlacingDefId is { } id && _hover is { } at)
        {
            var cells = _dragFrom is { } from && Lines(id) ? Line(from, at) : [at];
            foreach (var c in cells)
                marks.Add((c, DefenseHandlers.CanPlace(sim, raids, new PlaceDefense(id, c.X, c.Y, GhostFacing)).Ok ? GoodCell : BadCell));
        }
        if (SelectedDefenseId is { } s && sim.State.Defense.Find(s) is { } sd) marks.Add(((sd.X, sd.Y), SelectedCell));
        else if (hovered is not null && PlacingDefId is null) marks.Add(((hovered.X, hovered.Y), HoverCell));
        _marks.Mesh = Quads(marks, 0.02f, 0.01f);
    }

    private Side GhostFacing => _dragFrom is { } from && _hover is { } to ? LineFacing(from, to) : Facing;

    private void RefreshGhost(ColonySim sim, RaidData raids)
    {
        CommandResult? result = null;
        if (PlacingDefId is { } id && _hover is { } cell && raids.Defenses.TryGetValue(id, out var def))
        {
            var facing = GhostFacing;
            result = DefenseHandlers.CanPlace(sim, raids, new PlaceDefense(id, cell.X, cell.Y, facing));
            var key = $"{id}|{facing}|{result.Ok}";
            if (key != _ghostKey || _ghost is null)
            {
                _ghost?.QueueFree();
                _ghost = DefenseModels.Build(_models, def, facing);
                _ghost.Name = "Ghost";
                DefenseModels.Tint(_ghost, result.Ok ? GoodGhost : BadGhost);
                SetShadows(_ghost);
                AddChild(_ghost);
                _ghostKey = key;
            }
            _ghost.Position = CellCenter(cell.X, cell.Y);
            _ghost.Visible = true;
            _ghostLabel.Text = result.Ok ? "" : result.Message;
            _ghostLabel.Visible = !result.Ok;
            _ghostLabel.Position = CellCenter(cell.X, cell.Y) + new Vector3(0, 2.6f, 0);
        }
        else
        {
            if (_ghost is not null) _ghost.Visible = false;
            _ghostLabel.Visible = false;
        }
        var changed = result?.Message != GhostResult?.Message || result?.Ok != GhostResult?.Ok;
        GhostResult = result;
        if (changed) GhostChanged?.Invoke();
    }

    private static void SetShadows(Node node)
    {
        if (node is GeometryInstance3D g) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        foreach (var child in node.GetChildren()) SetShadows(child);
    }

    /// <summary>One flat quad per cell (inset a little so the grid reads), vertex-coloured, just above the plots.</summary>
    private ArrayMesh? Quads(IEnumerable<((int X, int Y) Cell, Color Color)> cells, float inset, float lift = 0)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var any = false;
        var half = CellSize / 2 - inset;
        foreach (var ((x, y), color) in cells)
        {
            any = true;
            var c = CellCenter(x, y) + new Vector3(0, OverlayY + lift, 0);
            var a = c + new Vector3(-half, 0, -half);
            var b = c + new Vector3(half, 0, -half);
            var d = c + new Vector3(half, 0, half);
            var e = c + new Vector3(-half, 0, half);
            st.SetColor(color);
            foreach (var v in new[] { a, b, d, a, d, e }) st.AddVertex(v);
        }
        return any ? st.Commit() : null;
    }
}
