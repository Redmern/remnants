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
    private ColonyHud _hud = null!;

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
            _hud.Expedition.Toggle();
            _hud.Expedition.Pick(_session.Sim.State.Survivors.Where(s => !s.IsChild).Select(s => s.Id).Take(2));
        }
        if (DebugCapture.HasFlag("--world-demo") && _session.Sim.State.Tick == 0) RunWorldDemo();
        if (DebugCapture.HasFlag("--world-map") || DebugCapture.HasFlag("--world-demo")) _hud.WorldMap.Open(DebugCapture.Value("--zone"));
        if (DebugCapture.HasFlag("--pause-menu")) _hud.CallDeferred(ColonyHud.MethodName.OpenPause);
        if (DebugCapture.HasFlag("--settings")) _hud.Settings.CallDeferred(Hud.Overlay.MethodName.Open);
        if (DebugCapture.HasFlag("--build-menu")) CallDeferred(MethodName.ToggleBuildMenu);
        if (int.TryParse(DebugCapture.Value("--select-survivor"), out var who)) _hud.Roster.Select(who);
        if (int.TryParse(DebugCapture.Value("--survivor-tab"), out var tab)) _hud.Survivor.ShowTab(tab);
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
        _hud.Report.ShowReport(report);
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
        _hud.Roster.Select(adults.FirstOrDefault());
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
        _hud.Roster.Select(woman.Id);
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
        _hud.Inventory.Toggle();
        if (armory is { } sel) _town.Select(sel);
        _session.SetSpeed(1);
    }

    public override void _Process(double delta) => _session.Advance(delta);

    private void BuildHud()
    {
        _hud = new ColonyHud(_session);
        AddChild(_hud);
        _hud.Log.Reset();
    }

    private void Wire()
    {
        _session.Changed += RefreshAll;
        _session.Replaced += () =>
        {
            _town.Reset();
            _walkers.Reset();
            _hud.Roster.Select(null);
            _hud.Log.Reset();
        };

        _hud.MainMenuRequested += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        _hud.BackRequested += () =>
        {
            if (_town.PlacingDefId is not null) _town.CancelPlacing();
            else if (_town.SelectedBuildingId is not null) _town.Select(null);
            else return false;
            return true;
        };
        _hud.TopBar.ExpeditionPressed += _hud.Expedition.Toggle;
        _hud.Expedition.WorldMapPressed += () => _hud.WorldMap.Open();
        _hud.WorldMap.PlanPressed += _hud.Expedition.PlanFor;
        _hud.Expedition.OnlinePressed += () =>
        {
            // The multiplayer menu picks this expedition's party up (MultiplayerSession.PickParty) and brings the result back here.
            ColonySession.Current = _session;
            _session.Save();
            GetTree().ChangeSceneToFile(Remnants.Game.Multiplayer.MultiplayerSession.MenuScene);
        };
        _hud.Expedition.PlayPressed += () =>
        {
            _session.PlayExpedition(GetTree());
            CallDeferred(MethodName.ShowUnseenReport); // the autopilot resolves in place; the scavenge screen comes back through _Ready
        };
        _hud.TopBar.BuildPressed += ToggleBuildMenu;
        _hud.BuildMenu.Closed += ToggleBuildMenu;
        _hud.BuildMenu.TypeChosen += defId => _town.BeginPlacing(defId);
        _town.PlacingChanged += () =>
            _hud.BuildMenu.SetPlacing(_town.PlacingDefId is { } id ? _session.Sim.Data.Buildings[id] : null);
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
            _hud.Building.Show(id);
            _hud.MedBay.Show(id);
            _hud.Production.Show(id);
        };
        _hud.Building.Closed += () => _town.Select(null);
        _hud.Roster.SelectionChanged += id => _walkers.SelectedSurvivorId = id;
    }

    private void ToggleBuildMenu()
    {
        Hud.Motion.Toggle(_hud.BuildMenu, new Vector2(12, 0));
        if (!_hud.BuildMenu.Visible) _town.CancelPlacing();
        _hud.BuildMenu.SetPlacing(null);
    }

    private void RefreshAll()
    {
        if (_session.UnseenReport is not null) ShowUnseenReport();  // a party walked home from a far zone
        _town.Sync();
        _walkers.Sync();
        _hud.Refresh();
    }
}
