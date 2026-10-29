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
    private ExpeditionPanel _expedition = null!;
    private MedBayPanel _medBay = null!;
    private WorkshopPanel _workshop = null!;
    private AfterActionDialog _report = null!;

    public override void _Ready()
    {
        var data = GameDataPaths.Load();
        // The session outlives this scene so a party can leave for the scavenge scene and come back to it.
        _session = ColonySession.Current ??= new ColonySession(NewGame.Create(data, NewGameSeed, GameDataPaths.LoadScavenge()));
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

        if ((DebugCapture.HasFlag("--demo") || DebugCapture.HasFlag("--family-demo")) && _session.Sim.State.Tick == 0) RunDemo();
        if (DebugCapture.HasFlag("--family-demo")) RunFamilyDemo();
        if (DebugCapture.HasFlag("--loop-demo") && _session.Sim.State.NextExpeditionId == 1)
        {
            ScavengeHandoff.UseAutopilot = DebugCapture.HasFlag("--autopilot");
            RunLoopDemo();
        }
        if (DebugCapture.HasFlag("--expedition"))
        {
            _expedition.Toggle();
            _expedition.Pick(_session.Sim.State.Survivors.Where(s => !s.IsChild).Select(s => s.Id).Take(2));
        }
        if (DebugCapture.FromCommandLine() is { } capture) AddChild(capture);
        CallDeferred(MethodName.ShowUnseenReport);
    }

    public override void _ExitTree() => _session.DetachViews();

    private void ShowUnseenReport()
    {
        if (_session.UnseenNotice is { } notice)
        {
            _session.UnseenNotice = null;
            _session.Notify(notice);
        }
        if (_session.UnseenReport is not { } report) return;
        _session.UnseenReport = null;
        _report.ShowReport(_session.Sim, report);
    }

    /// <summary>
    /// Debug: send the first adults to the outskirts with gear and play the run (on the scavenge screen, or in place
    /// with <c>--autopilot</c>), then build a Med Bay and Workshop to come back to.
    /// </summary>
    private void RunLoopDemo()
    {
        var sim = _session.Sim;
        var adults = sim.State.Survivors.Where(s => !s.IsChild).Take(sim.MaxPartySize).ToList();
        var members = adults.Select((s, i) => new ExpeditionMember(s.Id,
            new() { [Remnants.Core.Items.EquipSlot.Weapon] = i == 0 ? "pipe" : "spear" }, i == 0 ? ["bandage"] : [])).ToList();
        if (_session.Execute(new LaunchExpedition("outskirts", members)).Ok) _session.PlayExpedition(GetTree());
        if (_session.Execute(new Construct("med_bay", 8)).EntityId is not { } bay) return;
        _session.Execute(new Construct("workshop", 9));
        foreach (var s in sim.State.Present.Where(s => s.IsInjured).ToList()) _session.Execute(new AdmitPatient(s.Id, bay));
        _town.Select(bay);
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

    /// <summary>
    /// Debug: encourage the first man and woman, fast-forward until a baby is born (and a little after), then show
    /// the mother's family in the roster (for screenshots of population life).
    /// </summary>
    private void RunFamilyDemo()
    {
        var sim = _session.Sim;
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        if (adults.FirstOrDefault(s => s.Sex == Remnants.Core.Survivors.Sex.Female) is not { } woman
            || adults.FirstOrDefault(s => s.Sex == Remnants.Core.Survivors.Sex.Male) is not { } man) return;
        _session.Execute(new Matchmake(woman.Id, man.Id, PairStance.Encourage));
        sim.State.Stockpile.Add(sim.Data.Rules.Food.ResourceId, 200);  // so the colony can afford a family right away
        _session.FastForward(20_000, s => s.State.Events.Any(e => e.Type == ColonyEventType.Born));
        _session.FastForward(30, _ => false);
        _roster.Select(woman.Id);
        _session.SetSpeed(1);
    }

    public override void _Process(double delta) => _session.Advance(delta);

    private void BuildHud()
    {
        HudTheme.Apply(GetTree().Root);
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

        _expedition = new ExpeditionPanel(_session);
        middle.AddChild(_expedition);

        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddChild(right);
        _buildMenu = new BuildMenu(_session);
        _buildingPanel = new BuildingPanel(_session);
        _medBay = new MedBayPanel(_session);
        _workshop = new WorkshopPanel(_session);
        right.AddChild(_buildMenu);
        right.AddChild(_buildingPanel);
        right.AddChild(_medBay);
        right.AddChild(_workshop);

        _report = new AfterActionDialog();
        layer.AddChild(_report);

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
        _topBar.ExpeditionPressed += _expedition.Toggle;
        _expedition.PlayPressed += () =>
        {
            _session.PlayExpedition(GetTree());
            CallDeferred(MethodName.ShowUnseenReport); // the autopilot resolves in place; the scavenge screen comes back through _Ready
        };
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

        _town.SelectionChanged += id =>
        {
            _buildingPanel.Show(id);
            _medBay.Show(id);
            _workshop.Show(id);
        };
        _roster.SelectionChanged += id => _walkers.SelectedSurvivorId = id;
    }

    private void RefreshAll()
    {
        _town.Sync();
        _walkers.Sync();
        _topBar.Refresh();
        _roster.Refresh();
        _buildingPanel.Refresh();
        _medBay.Refresh();
        _workshop.Refresh();
        _expedition.Refresh();
        if (_buildMenu.Visible) _buildMenu.Refresh();
        _log.Refresh();
    }
}
