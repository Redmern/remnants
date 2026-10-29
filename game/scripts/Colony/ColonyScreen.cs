using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Colony.Ui;

namespace Remnants.Game.Colony;

/// <summary>
/// The colony screen: builds the 3D town, survivors, camera and HUD around one <see cref="ColonySession"/>
/// and routes events between them. Every state change goes through the session to Core.
/// </summary>
public partial class ColonyScreen : Node3D
{
    private const ulong NewGameSeed = 1;

    private ColonySession _session = null!;
    private TownView _town = null!;
    private SurvivorWalkers _walkers = null!;
    private TopBar _topBar = null!;
    private RosterPanel _roster = null!;
    private BuildingPanel _buildingPanel = null!;
    private BuildMenu _buildMenu = null!;
    private EventLog _log = null!;

    public override void _Ready()
    {
        var data = GameDataPaths.Load();
        _session = new ColonySession(NewGame.Create(data, NewGameSeed));
        var layout = new TownLayout(data.Rules.Layout);
        var models = ModelLibrary.Load();
        GD.Print($"Colony: data from {GameDataPaths.Source}, {models.ModelCount} generated models.");

        var camera = new OrbitCamera { Name = "Camera" };
        AddChild(camera);
        camera.SetBounds(layout.Extent / 2 + new Vector2(8, 8));
        if (_session.Sim.State.Buildings.FirstOrDefault(b => b.DefId == data.Rules.TownHallBuildingId) is { } hall)
            camera.Focus(layout.Center(hall.Plot) + new Vector3(TownLayout.PlotSize, 0, TownLayout.PlotSize));

        _town = new TownView { Name = "Town" };
        AddChild(_town);
        _town.Setup(_session, layout, models, camera.Camera);

        _walkers = new SurvivorWalkers { Name = "Survivors" };
        AddChild(_walkers);
        _walkers.Setup(_session, layout, models);

        BuildHud();
        Wire();
        RefreshAll();

        if (DebugCapture.HasFlag("--demo")) RunDemo();
        if (DebugCapture.FromCommandLine() is { } capture) AddChild(capture);
    }

    /// <summary>Debug: build a farm and woodcutter, put the adults to work and open the panels (for screenshots).</summary>
    private void RunDemo()
    {
        var sim = _session.Sim;
        var farm = _session.Execute(new Construct("farm", 8)).EntityId;
        var woodcutter = _session.Execute(new Construct("woodcutter", 9)).EntityId;
        var adults = sim.State.Survivors.Where(s => !s.IsChild).Select(s => s.Id).ToList();
        for (var i = 0; i < adults.Count; i++)
            if ((i % 2 == 0 ? farm : woodcutter) is { } at) _session.Execute(new AssignJob(adults[i], at));
        _roster.Select(adults.FirstOrDefault());
        if (farm is { } f) _town.Select(f);
        _session.SetSpeed(4);
    }

    public override void _Process(double delta) => _session.Advance(delta);

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        var root = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) root.AddThemeConstantOverride($"margin_{side}", 8);
        layer.AddChild(root);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(column);

        _topBar = new TopBar(_session);
        column.AddChild(_topBar);

        var middle = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(middle);

        _roster = new RosterPanel(_session);
        middle.AddChild(_roster);
        middle.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddChild(right);
        _buildMenu = new BuildMenu(_session);
        _buildingPanel = new BuildingPanel(_session);
        right.AddChild(_buildMenu);
        right.AddChild(_buildingPanel);

        _log = new EventLog(_session);
        column.AddChild(_log);
        _log.Reset();
    }

    private void Wire()
    {
        _session.Changed += RefreshAll;
        _session.Replaced += () =>
        {
            _town.Reset();
            _walkers.Reset();
            _roster.Select(null);
            _log.Reset();
        };

        _topBar.MenuPressed += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        _topBar.BuildPressed += () =>
        {
            _buildMenu.Visible = !_buildMenu.Visible;
            if (!_buildMenu.Visible) _town.CancelPlacing();
            _buildMenu.SetPlacing(null);
        };

        _buildMenu.TypeChosen += defId => _town.BeginPlacing(defId);
        _town.PlacingChanged += () =>
            _buildMenu.SetPlacing(_town.PlacingDefId is { } id ? _session.Sim.Data.Buildings[id] : null);
        _town.PlotChosen += (defId, plot) =>
        {
            if (_session.Execute(new Construct(defId, plot)) is { Ok: true, EntityId: { } built })
            {
                _town.CancelPlacing();
                _town.Select(built);
            }
        };

        _town.SelectionChanged += id => _buildingPanel.Show(id);
        _roster.SelectionChanged += id => _walkers.SelectedSurvivorId = id;
    }

    private void RefreshAll()
    {
        _town.Sync();
        _walkers.Sync();
        _topBar.Refresh();
        _roster.Refresh();
        _buildingPanel.Refresh();
        if (_buildMenu.Visible) _buildMenu.Refresh();
        _log.Refresh();
    }
}
