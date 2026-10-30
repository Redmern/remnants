using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
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
    private WorldMapPanel _worldMap = null!;
    private MedBayPanel _medBay = null!;
    private ProductionPanel _production = null!;
    private InventoryPanel _inventory = null!;
    private AfterActionDialog _report = null!;

    public override void _Ready()
    {
        Hud.GameSettings.Boot(GetTree());
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
        if (DebugCapture.HasFlag("--crafting-demo") && _session.Sim.State.Tick == 0) RunCraftingDemo();
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
        if (DebugCapture.HasFlag("--world-demo") && _session.Sim.State.Tick == 0) RunWorldDemo();
        if (DebugCapture.HasFlag("--world-map") || DebugCapture.HasFlag("--world-demo")) _worldMap.Open(DebugCapture.Value("--zone"));
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

    /// <summary>
    /// Debug (screenshots of the world map): a colony a few weeks in — the near zones scouted and picked over, rumors of
    /// the Iron Corridor — with a party on the road to the Suburb Ruins.
    /// </summary>
    private void RunWorldDemo()
    {
        var sim = _session.Sim;
        // Debug only: fake the knowledge a few earlier runs would have brought home.
        foreach (var (id, intel, depletion, visits) in new[]
                 {
                     ("outskirts", ZoneIntel.Scouted, 0.45, 3), ("scrapyard_flats", ZoneIntel.Scouted, 0.2, 1),
                     ("suburb_ruins", ZoneIntel.Scouted, 0.0, 1), ("flooded_mall", ZoneIntel.Rumored, 0.0, 0),
                     ("rail_depot", ZoneIntel.Scouted, 0.1, 1), ("network_relay", ZoneIntel.Rumored, 0.0, 0),
                 })
            sim.State.Zones[id] = new ZoneStatus { Intel = intel, Depletion = depletion, Visits = visits };
        sim.State.Stockpile.Add(sim.Data.Rules.Food.ResourceId, 50);
        var adults = sim.State.Survivors.Where(s => !s.IsChild).Take(2)
            .Select(s => new ExpeditionMember(s.Id, new() { [Remnants.Core.Items.EquipSlot.Weapon] = "pipe" })).ToList();
        if (_session.Execute(new LaunchExpedition("suburb_ruins", adults)).Ok) _session.FastForward(10, _ => false);
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

    /// <summary>
    /// Debug: a grown town for screenshots of crafting tiers. Stocks up, raises the Town Hall to level 3, builds every
    /// production building and a House, upgrades a few to level 2 and 3, studies a blueprint, staffs the Armory and
    /// Tailor, fills their queues and opens the Armory's production panel and the item list.
    /// </summary>
    private void RunCraftingDemo()
    {
        var sim = _session.Sim;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 2000);
        sim.State.Inventory.Add("bp_riot_armor");
        sim.State.Inventory.Add("bp_scanner");
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        _session.Execute(new Upgrade(hall));
        int? Build(string defId, int plot, int level)
        {
            if (_session.Execute(new Construct(defId, plot)).EntityId is not { } id) return null;
            for (var l = 1; l < level; l++) _session.Execute(new Upgrade(id));
            return id;
        }
        var armory = Build("armory", 8, 2);
        var tailor = Build("tailor", 9, 3);
        Build("electronics_lab", 10, 1);
        Build("kitchen", 14, 1);
        Build("workshop", 15, 2);
        Build("house", 16, 1);
        Build("farm", 2, 3);
        if (sim.State.Buildings.FirstOrDefault(b => b.DefId == "shelter") is { } shelter) _session.Execute(new Upgrade(shelter.Id));
        _session.Execute(new StudyBlueprint("bp_riot_armor"));

        var adults = sim.State.Survivors.Where(s => !s.IsChild).Select(s => s.Id).ToList();
        for (var i = 0; i < adults.Count; i++)
            if ((i < 2 ? armory : tailor) is { } at) _session.Execute(new AssignJob(adults[i], at));
        if (armory is { } a)
        {
            _session.Execute(new QueueRecipe(a, "riot_armor"));
            _session.Execute(new QueueRecipe(a, "machete", 5));
            _session.Execute(new QueueRecipe(a, "pistol", 1, Repeat: true));
        }
        if (tailor is { } t)
        {
            _session.Execute(new QueueRecipe(t, "padded_vest", 3));
            _session.Execute(new QueueRecipe(t, "scout_jacket"));
        }
        _session.FastForward(30, _ => false);
        if (armory is { } busy && sim.State.FindBuilding(busy) is { } shop)
            _session.FastForward(300, s => shop.Queue.FirstOrDefault(o => o.Paid) is { } o && o.Progress > s.Data.Recipes[o.RecipeId].Work * 0.4);
        _inventory.Toggle();
        if (armory is { } sel) _town.Select(sel);
        _session.SetSpeed(1);
    }

    public override void _Process(double delta) => _session.Advance(delta);

    private void BuildHud()
    {
        var layer = new CanvasLayer { Name = "Hud" };
        AddChild(layer);
        var root = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = HudTheme.Shared };
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

        _inventory = new InventoryPanel(_session);
        middle.AddChild(_inventory);
        _expedition = new ExpeditionPanel(_session);
        middle.AddChild(_expedition);

        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddChild(right);
        _buildMenu = new BuildMenu(_session);
        _buildingPanel = new BuildingPanel(_session);
        _medBay = new MedBayPanel(_session);
        _production = new ProductionPanel(_session);
        right.AddChild(_buildMenu);
        right.AddChild(_buildingPanel);
        right.AddChild(_medBay);
        right.AddChild(_production);

        _worldMap = new WorldMapPanel(_session) { Theme = HudTheme.Shared };
        layer.AddChild(_worldMap);

        _report = new AfterActionDialog { Theme = HudTheme.Shared };
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
        _topBar.ItemsPressed += _inventory.Toggle;
        _expedition.WorldMapPressed += () => _worldMap.Open();
        _worldMap.PlanPressed += _expedition.PlanFor;
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
            _production.Show(id);
        };
        _roster.SelectionChanged += id => _walkers.SelectedSurvivorId = id;
    }

    private void RefreshAll()
    {
        if (_session.UnseenReport is not null) ShowUnseenReport();  // a party walked home from a far zone
        _town.Sync();
        _walkers.Sync();
        _topBar.Refresh();
        _roster.Refresh();
        _buildingPanel.Refresh();
        _medBay.Refresh();
        _production.Refresh();
        _inventory.Refresh();
        _expedition.Refresh();
        _worldMap.Refresh();
        if (_buildMenu.Visible) _buildMenu.Refresh();
        _log.Refresh();
    }
}
