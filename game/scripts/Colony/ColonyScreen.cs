using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Schematics;
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
    private DefenseView _defenses = null!;
    private ColonyHud _hud = null!;
    private OrbitCamera _camera = null!;
    private SiteTooltip _siteTip = null!;

    /// <summary>The raid at the fence time was stopped for (once each).</summary>
    private int? _pausedForRaid;
    private TownLayout _layout = null!;
    private PowerOverlay _power = null!;

    /// <summary>A new game nobody has played yet (debug demos only stage on one).</summary>
    private bool IsFreshGame => _session.Sim.State.Tick == _session.Sim.Data.Rules.NewGame.StartHour;

    public override void _Ready()
    {
        Hud.GameSettings.Boot(GetTree());
        var data = GameDataPaths.Load();
        // The session outlives this scene so a party can leave for the scavenge scene and come back to it.
        // `--load-save=PATH` (debug, an OS path) opens a colony save, e.g. the grown one tools/Remnants.Bench writes.
        _session = ColonySession.Current ??= DebugCapture.Value("--load-save") is { Length: > 0 } savePath
            ? new ColonySession(Remnants.Core.Serialization.SaveGame.Load(data, System.IO.File.ReadAllText(savePath), GameDataPaths.LoadScavenge()))
            : new ColonySession(NewGame.Create(data, NewGameSeed, GameDataPaths.LoadScavenge()));
        // `--story` (with --colony) founds a Story colony.
        if (DebugCapture.HasFlag("--story") && _session.Sim.State.Campaign is null && IsFreshGame) Remnants.Core.Story.Campaign.Start(_session.Sim);
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

        // After the town, so a click on a defense reaches it before the town's plot picking.
        _defenses = new DefenseView { Name = "Defenses" };
        AddChild(_defenses);
        _defenses.Setup(_session, models, camera.Camera, _town.Lighting, _town.GetNodeOrNull<Node3D>("Dressing"));
        camera.KeyRotationBlocked = () => _defenses.PlacingDefId is not null;
        _camera = camera;
        _layout = layout;
        _power = new PowerOverlay { Name = "PowerGrid" };
        AddChild(_power);
        _power.Setup(_session, layout);

        BuildHud();
        Wire();
        RefreshAll();
        var audio = new Audio.ColonyAudio { Name = "Audio" };
        AddChild(audio);
        audio.Setup(_session, layout, camera, _hud);
        var weatherView = new WeatherFx.WeatherView { Name = "WeatherView" };
        AddChild(weatherView);
        weatherView.Setup(camera.Camera, _town.Lighting, 28f);
        var weather = new WeatherFx.ColonyWeather { Name = "Weather" };
        AddChild(weather);
        weather.Setup(_session, weatherView);

        // Debug scenes stage finished buildings at once; play (and --construction-demo's own sites) builds on sites.
        _session.Sim.InstantConstruction = true;
        if ((DebugCapture.HasFlag("--demo") || DebugCapture.HasFlag("--family-demo")) && IsFreshGame) RunDemo();
        if (DebugCapture.HasFlag("--family-demo")) RunFamilyDemo();
        if (DebugCapture.HasFlag("--crafting-demo") && IsFreshGame) RunCraftingDemo();
        if (DebugCapture.HasFlag("--food-demo") && IsFreshGame) RunFoodDemo(DebugCapture.Value("--food-view") ?? "kitchen");
        if (DebugCapture.HasFlag("--schematics-demo") && IsFreshGame) RunSchematicsDemo();
        if (DebugCapture.HasFlag("--perks-demo") && IsFreshGame) RunPerksDemo();
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
        if (DebugCapture.HasFlag("--cyber-demo") && IsFreshGame) RunCyberDemo();
        if (DebugCapture.HasFlag("--medicine-demo") && IsFreshGame) RunMedicineDemo();
        if (DebugCapture.HasFlag("--gear-demo") && IsFreshGame) RunGearDemo();
        if (DebugCapture.HasFlag("--world-demo") && IsFreshGame) RunWorldDemo();
        if (DebugCapture.HasFlag("--garage-demo") && IsFreshGame) RunGarageDemo();
        if (DebugCapture.HasFlag("--expedition"))
        {
            if (DebugCapture.HasFlag("--garage-demo")) _town.Select(null);
            if (DebugCapture.Value("--zone") is { } planZone) _hud.Expedition.PlanFor(planZone);
            if (DebugCapture.Value("--vehicle") is { } pickTier) _hud.Expedition.PickVehicle(pickTier);
            // `--expedition-step=1..5` shows that step of the planner (screenshots).
            if (int.TryParse(DebugCapture.Value("--expedition-step"), out var step)) _hud.Expedition.ShowStep(step - 1);
        }
        if (DebugCapture.Value("--worker-demo") is { Length: > 0 } workerDemo && IsFreshGame) RunWorkerDemo(workerDemo);
        if (DebugCapture.HasFlag("--placing-demo")) RunPlacingDemo();
        if (DebugCapture.HasFlag("--night-demo") && IsFreshGame) RunNightDemo();
        if (DebugCapture.Value("--raid-demo") is { } raidDemo && IsFreshGame) RunRaidDemo(raidDemo);
        if (DebugCapture.HasFlag("--power-demo") && IsFreshGame) RunPowerDemo();
        if (DebugCapture.HasFlag("--power-overlay")) TogglePowerOverlay();
        if (int.TryParse(DebugCapture.Value("--hour"), out var hour)) JumpToHour(hour);
        // `--speed=N` (debug, after --hour holds its hour): run at that speed, for --perf-log runs.
        if (int.TryParse(DebugCapture.Value("--speed"), out var speed)) GetTree().CreateTimer(0.5).Timeout += () => _session.SetSpeed(speed);
        // `--anim-demo=colony` (with --demo): close on the Woodcutter and Farm so the work loops read (FrameRecorder).
        if (DebugCapture.Value("--anim-demo") == "colony" && _session.Sim.State.Buildings.FirstOrDefault(b => b.DefId == "woodcutter") is { } cutter)
        {
            camera.Focus(layout.Center(cutter.Plot) + new Vector3(-TownLayout.PlotSize * 0.25f, 0, TownLayout.PlotSize * 0.25f));
            camera.ZoomTo(24);
            _session.SetSpeed(1);
            _walkers.Sync();
            _walkers.PlaceAtWork();
        }
        if (DebugCapture.HasFlag("--no-select"))
        {
            _hud.Roster.Select(null);
            _town.Select(null);
        }
        if (DebugCapture.Value("--story-demo") is { Length: > 0 } storyStage && IsFreshGame) RunStoryDemo(storyStage);
        if (DebugCapture.HasFlag("--world-map") || DebugCapture.HasFlag("--world-demo")) _hud.WorldMap.Open(DebugCapture.Value("--zone"));
        if (DebugCapture.HasFlag("--pause-menu")) _hud.CallDeferred(ColonyHud.MethodName.OpenPause);
        if (DebugCapture.HasFlag("--settings")) _hud.Settings.CallDeferred(Hud.Overlay.MethodName.Open);
        if (DebugCapture.HasFlag("--build-menu")) CallDeferred(MethodName.ToggleBuildMenu);
        if (int.TryParse(DebugCapture.Value("--select-survivor"), out var who)) _hud.Roster.Select(who);
        if (DebugCapture.HasFlag("--archive"))
        {
            _hud.Roster.Select(null);
            _hud.Inventory.Open(archive: true);
            if (DebugCapture.Value("--archive-filter") is { } filter) _hud.Inventory.Archive.ShowFilter(filter);
        }
        if (DebugCapture.HasFlag("--collapse-log")) _hud.Log.ToggleCollapsed();
        if (DebugCapture.HasFlag("--research")) SelectFirst(_session.Sim.Data.Schematics.Rules.Research.Building);
        if (DebugCapture.Value("--radio") is { } radioTab)
        {
            SelectFirst(_session.Sim.Data.Schematics.Rules.Radio.Building);
            _hud.Radio.ShowTab(radioTab);
        }
        if (int.TryParse(DebugCapture.Value("--survivor-tab"), out var tab)) _hud.Survivor.ShowTab(tab);
        CallDeferred(MethodName.ShowUnseenReport);
        // A live trade cannot outlive its connection: anything still in escrow for one goes back to the stores.
        if (Remnants.Game.Multiplayer.MultiplayerSession.Client is null
            && Remnants.Core.Trade.ColonyTrade.RefundAll(_session.Sim, Remnants.Net.LiveTradeSync.EscrowPrefix) > 0)
            _session.Notify("Goods set aside for an unfinished trade are back in the stores.");
        if (DebugCapture.HasFlag("--courier-demo") || DebugCapture.HasFlag("--trading-post") || DebugCapture.Value("--trading-post") is not null)
            Trade.TradeUi.EnsureMastForDebug(_session.Sim);
        if (DebugCapture.HasFlag("--courier-demo")) RunCourierDemo();
        _session.Sim.InstantConstruction = false;
        if (DebugCapture.Value("--construction-demo") is { } constructionDemo && IsFreshGame) RunConstructionDemo(constructionDemo);
        if (DebugCapture.HasFlag("--trading-post") || DebugCapture.Value("--trading-post") is not null)
        {
            var address = DebugCapture.Value("--trading-post");
            Callable.From(() =>
            {
                _hud.OpenTradingPost(address);
                if (DebugCapture.Value("--trading-post-tab") is { Length: > 0 } tab) _hud.TradingPost.ShowTab(tab);
            }).CallDeferred();
        }
    }

    /// <summary>
    /// `--courier-demo`: two trade couriers on the road (one arriving in `--courier-days`, default a quarter day, for the
    /// delivery toast; one two days out for the world map), at full speed.
    /// </summary>
    private void RunCourierDemo()
    {
        var sim = _session.Sim;
        var days = double.TryParse(DebugCapture.Value("--courier-days"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0.25;
        Remnants.Core.Trade.ColonyTrade.Dispatch(sim, $"demo-{sim.State.Tick}-a",
            [Remnants.Core.Trade.TradeGood.Resource("scrap", 20), Remnants.Core.Trade.TradeGood.Item("sch_rifle")], "Bob", days);
        Remnants.Core.Trade.ColonyTrade.Dispatch(sim, $"demo-{sim.State.Tick}-b",
            [Remnants.Core.Trade.TradeGood.Resource("electronics", 6)], "Rook", 2);
        _session.SetSpeed(DebugCapture.HasFlag("--world-map") ? 0 : 4);
        RefreshAll();
    }

    public override void _ExitTree()
    {
        _session.DetachViews();
        Hud.Help.HelpLibrary.Ambient = null;
    }

    /// <summary>What help breakdowns read here: the colony and the survivor or building whose panel is open.</summary>
    private Remnants.Core.Help.HelpContext HelpContext() => new()
    {
        Colony = _session.Sim,
        Survivor = _hud.Survivor.SurvivorId is { } s ? _session.Sim.State.FindSurvivor(s) : null,
        Building = _hud.Building.BuildingId is { } b ? _session.Sim.State.FindBuilding(b) : null,
    };

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
        Pantry.Add(sim.State, sim.Data, "potato", 50);
        var adults = sim.State.Survivors.Where(s => !s.IsChild).Take(2)
            .Select(s => new ExpeditionMember(s.Id, new() { [Remnants.Core.Items.EquipSlot.Weapon] = "pipe" })).ToList();
        // --vehicle=TIER: the party drives (fuel in the stores), so the map shows the vehicle token on the road.
        int? vehicleId = null;
        if (DebugCapture.Value("--vehicle") is { } tier && sim.Data.VehicleTiers.ContainsKey(tier))
        {
            sim.State.Stockpile.Add(sim.Data.Vehicles.FuelResource, 50);
            var v = VehicleDemo(sim, tier, sim.Data.VehicleTiers[tier].Name);
            v.Upgrades.Add(FittedUpgrade.Standard("headlights"));
            vehicleId = v.Id;
        }
        if (_session.Execute(new LaunchExpedition("suburb_ruins", adults, VehicleId: vehicleId)).Ok)
        {
            _session.FastForward(vehicleId is null ? 10 : 4, _ => false);
            if (vehicleId is not null) _session.SetSpeed(0);
        }
    }

    /// <summary>
    /// Debug (screenshots of vehicles): a Garage at level 3 with a mechanic and parts of every quality, a buggy with
    /// tyres, a muffler and headlights (one part worn), a truck and the handcart; the Garage selected with the buggy and a
    /// cargo-rack preview open, and fuel in the stores. With <c>--expedition</c> the planner can then pick a vehicle
    /// (<c>--vehicle=TIER</c>).
    /// </summary>
    private void RunGarageDemo()
    {
        var sim = _session.Sim;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 400);
        sim.State.Schematics.Known.UnionWith(sim.Data.Vehicles.Upgrades.Select(u => u.Schematic).OfType<string>());
        sim.State.Schematics.Known.UnionWith(["sch_garage_2", "sch_garage_3", "sch_buggy", "sch_truck", "sch_motorbike", "sch_small_engine", "sch_biofuel_still"]);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        _session.Execute(new Upgrade(hall));
        if (_session.Execute(new Construct("garage", 8)).EntityId is not { } garage) return;
        _session.Execute(new Upgrade(garage));
        _session.Execute(new Upgrade(garage));
        _session.Execute(new Construct("fuel_still", 9));
        foreach (var (item, n) in new[] { ("wheel@fine", 4), ("wheel", 6), ("wheel@crude", 2), ("spare_parts", 5), ("spare_parts@fine", 2),
                     ("armor_plate", 5), ("chassis_frame@crude", 1), ("engine_small", 1), ("fuel_can", 2) })
            sim.State.Inventory.Add(item, n);
        var mechanic = sim.State.Survivors.First(s => !s.IsChild);
        mechanic.Set(Remnants.Core.Survivors.Skill.Mechanics, 40);
        _session.Execute(new AssignJob(mechanic.Id, garage));
        _session.Execute(new QueueRecipe(garage, "build_motorbike"));
        // Debug only: the vehicles a few weeks of work would have built.
        var buggy = VehicleDemo(sim, "buggy", "Dust Devil");
        var truck = VehicleDemo(sim, "truck", "Old Faithful");
        _session.Execute(new FitUpgrade(buggy.Id, "offroad_tyres"));
        _session.Execute(new FitUpgrade(buggy.Id, "muffler"));
        _session.Execute(new FitUpgrade(buggy.Id, "headlights"));
        _session.Execute(new FitUpgrade(truck.Id, "armor_plates"));
        buggy.Upgrades.First(u => u.UpgradeId == "muffler").Condition = 0.45;
        buggy.Condition *= 0.7;
        truck.Condition *= 0.4;
        _session.FastForward(6, _ => false);
        _town.Select(garage);
        _hud.Garage.Focus(buggy.Id, "cargo_rack");
        _session.SetSpeed(0);
    }

    private static Vehicle VehicleDemo(ColonySim sim, string tier, string name)
    {
        var v = new Vehicle { Id = sim.State.NextVehicleId++, TierId = tier, Name = name, Condition = sim.Data.VehicleTiers[tier].MaxCondition };
        sim.State.Vehicles.Add(v);
        return v;
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
    /// Debug: the demo town plus a Quarry and Workshop, a Floodlight and the Night Shift on the Woodcutter (selected),
    /// two days in so people have homes (for screenshots of the night).
    /// </summary>
    private void RunNightDemo()
    {
        RunDemo();
        var sim = _session.Sim;
        foreach (var (res, amount) in new[] { ("wood", 200.0), ("stone", 100.0), ("scrap", 40.0) }) sim.State.Stockpile.Add(res, amount);
        _session.Execute(new Construct("quarry", 10));
        _session.Execute(new Construct("workshop", 14));
        _session.Execute(new Construct("kitchen", 15));
        if (sim.State.Buildings.FirstOrDefault(b => b.DefId == "woodcutter") is { } cutter)
        {
            _session.Execute(new InstallFloodlight(cutter.Id));
            _session.Execute(new SetNightShift(cutter.Id, true));
            _town.Select(cutter.Id);
        }
        _session.FastForward(2 * sim.Data.Rules.TicksPerDay, _ => false);
    }

    /// <summary>
    /// Debug (screenshots of electronics and power, <c>--power-demo</c>): Town Hall 3; a Fuel Generator, Solar Array and Battery
    /// Bank; a level 3 Electronics Lab staffed by two skilled engineers with a component, a tier 3 (needs power) chip, a device
    /// and a disassembly queued; a level 3 Med Bay switched off the grid; two drained sensor kits on the charging bench. Runs to
    /// midday so the sun is up and the batteries are filling, then opens the lab's bench (<c>--bench-tab=NAME</c> picks the tab).
    /// </summary>
    private void RunPowerDemo()
    {
        var sim = _session.Sim;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 2000);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        _session.Execute(new Upgrade(hall));
        int? Build(string defId, int plot, int level)
        {
            var built = _session.Execute(new Construct(defId, plot));
            if (built.EntityId is not { } id)
            {
                GD.Print($"power-demo: {defId}: {built.Message}");
                return null;
            }
            for (var l = 1; l < level; l++) _session.Execute(new Upgrade(id));
            return id;
        }
        // The schematics a few runs would have brought home: Lab and Med Bay L3, solar, batteries, the chip fab and the EMP.
        foreach (var schematic in new[] { "sch_lab_3", "sch_med_bay_3", "sch_solar_array", "sch_battery_bank", "sch_microchip", "sch_emp_grenade" })
            Remnants.Core.Schematics.Schematics.Grant(sim, schematic, Remnants.Core.Schematics.SchematicSource.Loot);
        var lab = Build("electronics_lab", 8, 3);
        Build("fuel_generator", 9, 1);
        Build("solar_array", 15, 2);
        Build("battery_bank", 14, 1);
        var bay = Build("med_bay", 10, 3);
        var farm = Build("farm", 16, 1);
        foreach (var (id, n) in new[] { ("wiring", 6), ("circuit_board", 4), ("power_cell", 3), ("broken_gadget", 4), ("smashed_drone", 1), ("antenna", 2) })
            sim.State.Inventory.Add(id, n);
        if (lab is not { } l) return;

        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        // Debug only: engineers as if they had worked the bench for weeks.
        foreach (var s in adults.Take(2))
        {
            s.Set(Remnants.Core.Survivors.Skill.Electronics, 42);
            _session.Execute(new AssignJob(s.Id, l));
        }
        if (farm is { } f)
            foreach (var s in adults.Skip(2)) _session.Execute(new AssignJob(s.Id, f));
        _session.Execute(new QueueRecipe(l, "microchip", 2));
        _session.Execute(new QueueRecipe(l, "circuit_board", 3));
        _session.Execute(new QueueRecipe(l, "wiring", 1, Repeat: true));
        _session.Execute(new QueueRecipe(l, "emp_grenade"));
        _session.Execute(new QueueRecipe(l, Remnants.Core.Data.DisassemblyDef.RecipePrefix + "broken_gadget", 3));
        if (bay is { } b) _session.Execute(new SetPower(b, false));
        // Debug only: two sensor kits came home flat from a run.
        sim.State.Charging.Add(new ChargingItem { ItemId = "motion_sensor_mk2", Work = 30, Done = 0 });
        sim.State.Charging.Add(new ChargingItem { ItemId = "motion_sensor_mk3", Work = 24, Done = 0 });
        // Midday: the sun is up, the batteries are filling, the bench is busy.
        _session.FastForward(sim.Data.Rules.TicksPerDay, s => s.Hour == 12);
        _town.Select(l);
        _hud.CallDeferred(ColonyHud.MethodName.ScrollSideTo, _hud.Production);
        if (DebugCapture.Value("--bench-tab") is { Length: > 0 } tab) _hud.Production.ShowGroup(tab);
        _session.SetSpeed(0);
    }

    private void TogglePowerOverlay()
    {
        _power.Toggle();
        _hud.TopBar.SetPowerOverlay(_power.Visible);
    }

    /// <summary>Debug: run to the next <paramref name="hour"/>:00, pause there and put everyone where that hour has them.</summary>
    private void JumpToHour(int hour)
    {
        _session.FastForward(_session.Sim.Data.Rules.TicksPerDay, s => s.Hour == hour);
        _session.SetSpeed(0);
        // A demo's speed can come back once the HUD settles; hold the hour for the capture.
        GetTree().CreateTimer(0.25).Timeout += () => _session.SetSpeed(0);
        _walkers.Settle();
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
        Pantry.Add(sim.State, sim.Data, "beans", 200);  // so the colony can afford a family right away
        _session.FastForward(20_000, s => s.State.Events.Any(e => e.Type == ColonyEventType.Born));
        _session.FastForward(30, _ => false);
        _hud.Roster.Select(woman.Id);
        _session.SetSpeed(1);
    }

    /// <summary>
    /// Debug (screenshots of perks and study): Town Hall 2 with an Electronics Lab, a few days of work behind everyone,
    /// a hacker with the Tech perks and the Network Ghost schematic, two classes at the Lab (Hacking with a mentor, a child on
    /// Drone Piloting with a manual); selects the hacker's apprentice and the Lab.
    /// </summary>
    private void RunPerksDemo()
    {
        var sim = _session.Sim;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 500);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        var lab = _session.Execute(new Construct("electronics_lab", 8)).EntityId;
        var farm = _session.Execute(new Construct("farm", 9)).EntityId;
        if (lab is not { } l) return;
        _session.Execute(new Upgrade(l));
        sim.State.Inventory.Add("manual_drone_ops");
        Schematics.Grant(sim, "sch_network_ghost", SchematicSource.Debug);

        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        var children = sim.State.Survivors.Where(s => s.IsChild && !s.IsInfant).ToList();
        if (adults.Count < 3) return;
        // Debug only: a veteran hacker, as if they had worked the Lab for weeks.
        var hacker = adults[0];
        hacker.Set(Remnants.Core.Survivors.Skill.Electronics, 42);
        hacker.Set(Remnants.Core.Survivors.Skill.Hacking, 38);
        hacker.Set(Remnants.Core.Survivors.AttributeKind.Intellect, 55);
        hacker.Set(Remnants.Core.Survivors.AttributeKind.Perception, 50);
        Progression.GrantXp(sim.State, sim.Data, hacker, 900, Remnants.Core.Survivors.XpSource.Work);
        foreach (var perk in new[] { "hacking_1", "jury_rig", "hacking_2" })
        {
            hacker.PerkPoints = System.Math.Max(hacker.PerkPoints, 1);
            _session.Execute(new ChoosePerk(hacker.Id, perk));
        }
        var apprentice = adults[1];
        apprentice.Set(Remnants.Core.Survivors.Skill.Hacking, 14);
        apprentice.Set(Remnants.Core.Survivors.AttributeKind.Intellect, 48);
        apprentice.Set(Remnants.Core.Survivors.AttributeKind.Perception, 46);
        Progression.GrantXp(sim.State, sim.Data, apprentice, 260, Remnants.Core.Survivors.XpSource.Work);
        _session.Execute(new ChoosePerk(apprentice.Id, "hacking_1"));
        _session.Execute(new StartStudy(apprentice.Id, l, Remnants.Core.Survivors.Skill.Hacking, MentorId: hacker.Id));
        if (children.FirstOrDefault() is { } kid)
            _session.Execute(new StartStudy(kid.Id, l, Remnants.Core.Survivors.Skill.DronePiloting, ManualItemId: "manual_drone_ops"));
        if (farm is { } f)
            foreach (var s in adults.Skip(2)) _session.Execute(new AssignJob(s.Id, f));
        _session.FastForward(30, _ => false);
        _hud.Roster.Select(apprentice.Id);
        _town.Select(l);
        _session.SetSpeed(0);
    }

    /// <summary>
    /// Debug (screenshots of cybernetics, <c>--cyber-demo</c>): Town Hall 3 with a Med Bay, Workshop and Electronics Lab
    /// at level 2; a Cyber-Surgeon at the Med Bay and a Biomech / Neural Interface engineer; the prosthetics schematics
    /// known and a few designs and components in the stores; a veteran wearing mixed tiers (a mechanical leg, a cyborg
    /// runner leg, a blade arm and a Network-grade arm as replacements, a scope eye as an augmentation, so humanity is
    /// low); and the engineer booked for
    /// surgery on a lost leg. Selects the Med Bay and the veteran; <c>--cyber-tree</c> opens the tree on the legs.
    /// </summary>
    private void RunCyberDemo()
    {
        var sim = _session.Sim;
        if (sim.Data.Cybernetics is not { } cyber || sim.Scavenge is not { } items) return;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 2000);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        _session.Execute(new Upgrade(hall));
        int? Build(string defId, int plot, int level)
        {
            if (_session.Execute(new Construct(defId, plot)).EntityId is not { } id) return null;
            for (var l = 1; l < level; l++) _session.Execute(new Upgrade(id));
            return id;
        }
        var bay = Build("med_bay", 8, 2);
        Build("workshop", 9, 2);
        var lab = Build("electronics_lab", 10, 2);
        Build("house", 14, 1);
        Build("farm", 15, 2);
        foreach (var id in new[] { "sch_mech_limbs", "sch_powered_limbs", "sch_cybernetics", "sch_spec_runner", "sch_spec_silent", "sch_spec_precision",
                     "sch_spec_labor", "sch_spec_hacker", "sch_spec_blade", "sch_spec_scope_eye" })
            if (sim.Data.Schematics.Find(id) is not null) Schematics.Grant(sim, id, SchematicSource.Debug);
        foreach (var id in new[] { "mech_leg", "mech_arm", "runner_leg", "silent_leg", "precision_arm", "labor_arm",
                     "servo", "servo", "power_cell", "neural_link", "myomer_bundle", "cyber_leg", "blade_arm" })
            sim.State.Inventory.Add(id);

        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        if (adults.Count < 3 || bay is not { } medBay || lab is not { } l2) return;
        // Debug only: veterans, as if they had worked for months.
        var medic = adults[0];
        medic.Set(Remnants.Core.Survivors.Skill.Medicine, 46);
        medic.Set(Remnants.Core.Survivors.Skill.Electronics, 18);
        medic.Perks.UnionWith(["field_medic", "surgery_1", "surgery_2", "cyber_surgeon"]);
        var engineer = adults[1];
        engineer.Set(Remnants.Core.Survivors.Skill.Crafting, 34);
        engineer.Set(Remnants.Core.Survivors.Skill.Electronics, 36);
        engineer.Perks.UnionWith(["tinkerer", "biomech", "neural_interface"]);
        var veteran = adults[2];
        foreach (var (part, item, elective) in new[]
                 {
                     (Remnants.Core.Survivors.BodyPart.LeftLeg, "mech_leg", false),
                     (Remnants.Core.Survivors.BodyPart.RightLeg, "runner_leg_mk2", false),
                     (Remnants.Core.Survivors.BodyPart.LeftArm, "network_arm", false),
                     (Remnants.Core.Survivors.BodyPart.RightArm, "blade_arm", false),
                     (Remnants.Core.Survivors.BodyPart.Eyes, "scope_eye", true),
                 })
        {
            if (!elective) veteran.Body.Lose(part);
            Cybernetics.Fit(sim.State, cyber, veteran, part, items.Items[item], elective);
        }
        veteran.Body[Remnants.Core.Survivors.BodyPart.RightLeg].ServiceTicks = 6 * sim.Data.Rules.TicksPerDay;

        _session.Execute(new AssignJob(medic.Id, medBay));
        _session.Execute(new AssignJob(veteran.Id, l2));
        engineer.Body.Lose(Remnants.Core.Survivors.BodyPart.LeftLeg);
        _session.Execute(new ScheduleSurgery(engineer.Id, Remnants.Core.Survivors.BodyPart.LeftLeg, "runner_leg"));
        _session.FastForward(6, _ => false);
        _hud.Roster.Select(veteran.Id);
        _town.Select(medBay);
        _session.SetSpeed(0);
        if (DebugCapture.HasFlag("--cyber-tree"))
            _hud.Cybernetics.Open(Remnants.Core.Data.CyberSlot.Leg, veteran.Id, Remnants.Core.Survivors.BodyPart.RightLeg, "jumper_leg");
    }

    /// <summary>
    /// Debug (screenshots of raids and defenses, <c>--raid-demo=placement|warning|prep|report</c>): Town Hall 2, a few
    /// buildings, a wooden wall along the south fence with a gate, a tower and a sandbag nest with guards (the tower on the
    /// night watch), floodlights, a turret, an alarm bell, traps on the approach and a barricade line to the north; one
    /// wall hurt, one barricade knocked down, a spike strip sprung, the Farm scuffed. Then: <c>placement</c> opens defend
    /// mode placing a turret over a free cell (its fire arc showing) with the nest selected; <c>warning</c> sends a raider
    /// band from the south that the watch has seen; <c>prep</c> also opens the prep screen; <c>report</c> lets it arrive
    /// and shows the after-raid report.
    /// </summary>
    private void RunRaidDemo(string mode)
    {
        var sim = _session.Sim;
        if (sim.Raids is not { } raids) return;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 600);
        var hall = sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        var farm = _session.Execute(new Construct("farm", 20)).EntityId;
        _session.Execute(new Construct("woodcutter", 21));
        _session.Execute(new Construct("workshop", 14));
        _session.Execute(new Construct("house", 15));
        _session.Execute(new Construct("quarry", 8));
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        var jobs = sim.State.Buildings.Where(b => sim.Data.Buildings[b.DefId].Job is not null).ToList();
        for (var i = 3; i < adults.Count && jobs.Count > 0; i++) _session.Execute(new AssignJob(adults[i].Id, jobs[i % jobs.Count].Id));
        // Debug only: schematics a colony would find over a few raids.
        foreach (var id in new[] { "sch_sandbag_nest", "sch_floodlight_post", "sch_defense_turret", "sch_pit_trap", "sch_snare", "sch_barricade", "sch_wall_scrap" })
            Schematics.Grant(sim, id, SchematicSource.Debug);

        int? Place(string defId, int x, int y, Remnants.Core.Raids.Side facing = Remnants.Core.Raids.Side.South) =>
            _session.Execute(new Remnants.Core.Raids.PlaceDefense(defId, x, y, facing)).EntityId;
        for (var x = 6; x <= 25; x++)
            if (x is not (15 or 16)) Place("wall_wood", x, 21);
        Place("gate_wood", 15, 21);
        Place("gate_wood", 16, 21);
        var tower = Place("watchtower_1", 4, 20);
        var nest = Place("sandbag_nest", 27, 20);
        Place("floodlight_post", 11, 20);
        Place("floodlight_post", 20, 20);
        Place("defense_turret", 18, 20);
        Place("alarm_bell", 2, 12, Remnants.Core.Raids.Side.West);
        foreach (var x in new[] { 10, 11, 12, 13 }) Place("spike_strip", x, 23);
        Place("pit_trap", 18, 23);
        Place("snare", 20, 23);
        for (var x = 12; x <= 18; x++) Place("barricade", x, 2, Remnants.Core.Raids.Side.North);
        foreach (var d in sim.State.Defense.Defenses.Where(d => d.DefId == "wall_wood" && d.X is 22 or 23).ToList())
            _session.Execute(new Remnants.Core.Raids.UpgradeDefense(d.Id));
        if (adults.Count >= 3 && nest is { } n && tower is { } t)
        {
            _session.Execute(new Remnants.Core.Raids.AssignGuard(adults[0].Id, n));
            _session.Execute(new Remnants.Core.Raids.AssignGuard(adults[1].Id, n));
            _session.Execute(new Remnants.Core.Raids.AssignGuard(adults[2].Id, t));
            _session.Execute(new Remnants.Core.Raids.SetNightWatch(t, true));
        }

        // Debug only: the marks of an earlier raid.
        var state = sim.State.Defense;
        if (state.Defenses.FirstOrDefault(d => d.DefId == "wall_wood" && d.X == 8) is { } hurt) hurt.Hp *= 0.4;
        if (state.Defenses.FirstOrDefault(d => d.DefId == "barricade" && d.X == 14) is { } down) down.Hp = 0;
        if (state.Defenses.FirstOrDefault(d => d.DefId == "spike_strip" && d.X == 13) is { } sprung) sprung.Sprung = true;
        if (farm is { } f) state.BuildingDamage[f] = 25;

        _camera.Focus(new Vector3(0, 0, _layout.Extent.Y / 2 - 2));
        _camera.ZoomTo(58);
        _session.SetSpeed(0);
        switch (mode)
        {
            case "placement":
                _defenses.SetActive(true);
                _hud.Defend.ShowCategoryOf("defense_turret");
                _defenses.BeginPlacing("defense_turret");
                _defenses.DebugHover(14, 22);
                if (nest is { } post) _defenses.Select(post);
                break;
            case "battle":
                RunBattleDemo();
                break;
            case "warning":
            case "prep":
            case "report":
            {
                state.Raid = new Remnants.Core.Raids.Raid
                {
                    Id = state.NextRaidId++, Kind = Remnants.Core.Raids.ThreatKind.Raider, Size = mode == "report" ? 9 : 7,
                    Approach = Remnants.Core.Raids.Side.South, SetOutTick = sim.State.Tick, ArrivalTick = sim.State.Tick + (mode == "report" ? 3 : 9),
                    Stage = Remnants.Core.Raids.RaidStage.Approaching,
                };
                state.Raid.WarnTick = state.Raid.ArrivalTick;
                _session.FastForward(4, s => s.State.Defense.Raid is { Stage: Remnants.Core.Raids.RaidStage.Warned });
                if (mode == "prep") _hud.CallDeferred(ColonyHud.MethodName.OpenPrep);
                if (mode == "report") _session.FastForward(12, s => s.State.Defense.Raid is null);
                _session.SetSpeed(0);
                break;
            }
        }
    }

    /// <summary>
    /// Debug (<c>--raid-demo=battle</c>): on the raid demo's town, a Husk horde from the north arrives at 22:00 and is
    /// fought; the battle opens a few rounds in (the defenders hold their ground, the Husks come on), at night.
    /// </summary>
    private void RunBattleDemo()
    {
        var sim = _session.Sim;
        var state = sim.State.Defense;
        _session.FastForward(sim.Data.Rules.TicksPerDay, s => s.Hour == 21);
        state.Raid = new Remnants.Core.Raids.Raid
        {
            Id = state.NextRaidId++, Kind = Remnants.Core.Raids.ThreatKind.Husk, Size = 8, Approach = Remnants.Core.Raids.Side.North,
            SetOutTick = sim.State.Tick, ArrivalTick = sim.State.Tick + 1, Stage = Remnants.Core.Raids.RaidStage.Approaching, Fight = true,
        };
        state.Raid.WarnTick = state.Raid.ArrivalTick;
        _session.FastForward(4, s => s.State.Defense.Raid is { Stage: Remnants.Core.Raids.RaidStage.Battle });
        _session.SetSpeed(0);
        if (state.Raid is not { Stage: Remnants.Core.Raids.RaidStage.Battle }) return;
        // Debug only: a few rounds pass with everyone holding position, so the horde is at the barricades.
        var battle = Remnants.Core.Raids.RaidBattle.Open(sim);
        var player = ColonySim.ExpeditionPlayerId;
        for (var i = 0; i < 400 && battle.State.Round < 4 && !(battle.State.FindParty(player)?.Done ?? true); i++)
        {
            if (battle.State.CurrentUnit is not { } unit || unit.PlayerId != player) break;
            if (!battle.Execute(new EndTurn(player, unit.Id)).Ok) break;
        }
        _session.PlayRaidBattle(GetTree());
    }

    /// <summary>
    /// Debug: a fed town for screenshots of food and cooking. A Kitchen L3 with a Campfire, Smokehouse, Cold Cellar,
    /// Hunting Lodge, a Farm on beans and a Greenhouse; a few food schematics studied (not all, so the recipe book shows
    /// locked dishes); a pantry of mixed lots, some close to spoiling, one spoiled and one tainted; cooks at work; then a
    /// few hours so people eat and carry their buffs. <paramref name="view"/>: kitchen (the recipe book), pantry, farm
    /// (the crop picker) or roster (nothing open, the buff badges).
    /// </summary>
    private void RunFoodDemo(string view)
    {
        var sim = _session.Sim;
        var state = sim.State;
        foreach (var res in sim.Data.Resources.Keys) state.Stockpile.Add(res, 2000);
        foreach (var sch in new[] { "sch_feast", "sch_medicinal_broth", "sch_cold_cellar", "sch_greenhouse", "sch_pickling" }) Schematics.Grant(sim, sch, SchematicSource.Debug);
        var hall = state.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id;
        _session.Execute(new Upgrade(hall));
        _session.Execute(new Upgrade(hall));
        int? Build(string defId, int plot, int level)
        {
            if (_session.Execute(new Construct(defId, plot)).EntityId is not { } id) return null;
            for (var l = 1; l < level; l++) _session.Execute(new Upgrade(id));
            return id;
        }
        var kitchen = Build("kitchen", 8, 3);
        var farm = Build("farm", 2, 2);
        Build("campfire", 9, 1);
        var smoke = Build("smokehouse", 10, 2);
        Build("cold_cellar", 14, 1);
        var lodge = Build("hunting_lodge", 3, 1);
        Build("greenhouse", 15, 1);
        if (farm is { } f) _session.Execute(new SetHarvest(f, "beans"));

        void Add(string item, int n, double ageDays = 0, string? quality = null, bool? tainted = null)
        {
            Pantry.Add(state, sim.Data, item, n, quality, tainted);
            if (ageDays > 0) state.Food.Lots[item].Last().Age = ageDays * sim.Data.Rules.TicksPerDay;
        }
        Add("potato", 24, 3);
        Add("potato", 10, 12.6);
        Add("beans", 18);
        Add("corn", 12, 2);
        Add("herbs", 8, 4.5);
        Add("meat", 6, 2.2);
        Add("husk_meat", 4, 0.5);
        Add("mushrooms", 5, 5);
        Add("water", 10);
        Add("salt", 3);
        Add("spices", 2);
        Add("stew", 6, 0.3, "fine");
        Add("stew", 3, 0.5, "standard");
        Add("pie", 4, 1, "standard");
        Add("flatbread", 6, 2, "crude");
        Add("smoked_meat", 6, 4, "standard");
        Add("roast", 2, 0.2, "standard", tainted: true);
        Add("canned_food", 4);

        var adults = state.Survivors.Where(s => !s.IsChild).ToList();
        if (kitchen is { } k && adults.Count > 0)
        {
            adults[0].Set(Remnants.Core.Survivors.Skill.Cooking, 35);
            _session.Execute(new AssignJob(adults[0].Id, k));
            _session.Execute(new QueueRecipe(k, "stew", 2));
            _session.Execute(new QueueRecipe(k, "medicinal_broth"));
            _session.Execute(new QueueRecipe(k, "boiled_potatoes", 1, Repeat: true));
        }
        if (smoke is { } sm && adults.Count > 1)
        {
            _session.Execute(new AssignJob(adults[1].Id, sm));
            _session.Execute(new QueueRecipe(sm, "smoke_meat"));
        }
        if (lodge is { } l && adults.Count > 2) _session.Execute(new AssignJob(adults[2].Id, l));
        foreach (var child in state.Survivors.Where(s => s.IsChild && !s.IsInfant).Take(1))
            if (farm is { } fa) _session.Execute(new AssignJob(child.Id, fa));
        _session.FastForward(6, _ => false);
        // A colony a while in: one ate the tainted roast, one had stim tea, the youngest lived on roots for a while.
        var tick = state.Tick;
        if (adults.Count > 1) FoodEffects.Give(adults[1], "sick", tick + 18, 1);
        if (adults.Count > 2) FoodEffects.Give(adults[2], "energised", tick + 7, 1);
        if (state.Survivors.FirstOrDefault(s => s.IsChild) is { } kid)
        {
            kid.Nutrition = 0.4;
            FoodEffects.Give(kid, "malnourished", tick + 1, 1);
        }
        if (adults.Count > 0) FoodEffects.Give(adults[0], "comfort", tick + 10, 1);
        _session.SetSpeed(0);
        switch (view)
        {
            case "pantry":
                _hud.Pantry.Toggle();
                break;
            case "farm":
                if (farm is { } farmId) _town.Select(farmId);
                break;
            case "roster":
                break;
            default:
                if (kitchen is { } kitchenId) _town.Select(kitchenId);
                _hud.Log.ToggleCollapsed();
                ScrollToBookLater();
                break;
        }
        if (adults.Count > 0) _hud.Roster.Select(view == "roster" ? adults[1].Id : null);
    }

    /// <summary>Scrolls the right column to the recipe book once the panels have been laid out (a few frames).</summary>
    private async void ScrollToBookLater()
    {
        for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(_hud)) _hud.ScrollRightTo(_hud.Production.Book);
    }

    /// <summary>
    /// Debug (screenshots of gear mods, <c>--gear-demo</c>): Town Hall 2 with an Armory, Workshop and Tailor (staffed,
    /// a few mod and kit orders under way); a pistol, rifle, padded vest and scrap helmet in the stores with mods and kits;
    /// the pistol taken to Mk II with a suppressor, the vest given scrap plates. Opens the Gear Bench on the pistol with a
    /// Reflex Sight previewed (<c>--gear-item=ID</c> and <c>--gear-preview=MOD</c> pick another piece and mod).
    /// </summary>
    private void RunGearDemo()
    {
        var sim = _session.Sim;
        if (sim.Scavenge?.GearMods is null) return;
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 2000);
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
        var workshop = Build("workshop", 9, 1);
        var tailor = Build("tailor", 10, 1);
        Build("house", 14, 1);
        foreach (var id in new[] { "pistol", "rifle", "padded_vest", "helmet_scrap", "scout_jacket", "pipe",
                     "mod_reflex_sight", "mod_suppressor", "mod_scope", "mod_iron_sight", "mod_laser", "mod_emp_coil", "mod_stock",
                     "mod_armor_scrap_plates", "mod_armor_padding", "mod_armor_webbing", "mod_camo_urban", "mod_helmet_visor",
                     "mod_sharpened_edge", "kit_sidearm_mk2", "kit_armor_mk2", "mod_camo_urban" })
            sim.State.Inventory.Add(id);
        _session.Execute(new UpgradeGear("pistol", "kit_sidearm_mk2"));
        _session.Execute(new InstallMod("pistol+mk2", "mod_suppressor"));
        _session.Execute(new InstallMod("padded_vest", "mod_armor_scrap_plates"));
        _session.Execute(new InstallMod("scout_jacket", "mod_camo_urban"));

        var adults = sim.State.Survivors.Where(s => !s.IsChild).Select(s => s.Id).ToList();
        var shops = new[] { armory, workshop, tailor };
        for (var i = 0; i < adults.Count; i++)
            if (shops[i % shops.Length] is { } at) _session.Execute(new AssignJob(adults[i], at));
        if (armory is { } a) _session.Execute(new QueueRecipe(a, "kit_armor_mk2", 3));
        if (workshop is { } w) _session.Execute(new QueueRecipe(w, "mod_wrapped_grip", 1, Repeat: true));
        if (tailor is { } t) _session.Execute(new QueueRecipe(t, "mod_armor_webbing", 4));
        _session.FastForward(6, _ => false);
        if (armory is { } sel) _town.Select(sel);
        _session.SetSpeed(0);
        var pistol = sim.State.Inventory.Counts.Keys.FirstOrDefault(k => k.StartsWith("pistol+")) ?? "pistol";
        _hud.GearBench.Open(DebugCapture.Value("--gear-item") ?? pistol);
        _hud.GearBench.PreviewMod(DebugCapture.Value("--gear-preview") ?? "mod_reflex_sight");
    }

    /// <summary>
    /// Debug: a grown town for screenshots of crafting tiers. Stocks up, raises the Town Hall to level 3, builds every
    /// production building and a House, upgrades a few to level 2 and 3, studies a blueprint, staffs the Armory and
    /// Tailor, fills their queues and opens the Armory's production panel and the item list.
    /// </summary>
    private void RunCraftingDemo(bool open = true)
    {
        var sim = _session.Sim;
        foreach (var res in sim.Data.Resources.Keys.Where(r => r != sim.Data.Schematics.Rules.ResearchDataResource)) sim.State.Stockpile.Add(res, 2000);
        // Debug only: the schematics a grown town would have found (Riot Armor, and the Tailor's third level).
        foreach (var id in new[] { "sch_riot_armor", "sch_tailor_3" })
            if (sim.Data.Schematics.Find(id) is not null) Schematics.Grant(sim, id, SchematicSource.Debug);
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
        if (!open) return;
        _hud.Inventory.Toggle();
        if (armory is { } sel) _town.Select(sel);
        _session.SetSpeed(1);
    }

    /// <summary>
    /// Debug (screenshots of schematics, <c>--schematics-demo</c>): the crafting demo's grown town plus a Radio Mast and an
    /// Electronics Lab L2; some schematics known, some heard of, three encrypted chips (one queued for decrypting),
    /// Research Data, the Evac Beacon being researched, Suburb Ruins scouted, the trader parked outside with its call
    /// waiting and a contract on offer. Paused. <c>--archive</c>, <c>--research</c> and <c>--radio=calls|trader|contracts</c>
    /// open the screens.
    /// </summary>
    private void RunSchematicsDemo()
    {
        var sim = _session.Sim;
        RunCraftingDemo(open: false);
        _session.Execute(new Construct(sim.Data.Schematics.Rules.Radio.Building));
        if (sim.State.Buildings.FirstOrDefault(b => b.DefId == "electronics_lab") is { Level: < 2 } lab) _session.Execute(new Upgrade(lab.Id));
        foreach (var zone in new[] { "outskirts", "scrapyard_flats", "suburb_ruins" })
            if (sim.Scavenge?.Zones.ContainsKey(zone) == true) sim.State.Zones[zone] = new ZoneStatus { Intel = ZoneIntel.Scouted, Visits = 1 };

        // Debug only: what a few weeks of runs, a trader and a terminal would have brought.
        foreach (var id in new[] { "sch_watchtower", "sch_camo_cloak", "sch_rifle", "sch_scout_drone", "sch_mech_limbs", "sch_network_ghost", "sch_workshop_3" })
            Schematics.Grant(sim, id, SchematicSource.Loot);
        foreach (var (id, where) in new[]
                 {
                     ("sch_evac_beacon", "in a story dialogue"), ("sch_greenhouse", "on a terminal in Suburb Ruins"),
                     ("sch_powered_limbs", "dropped by a Linked in Suburb Ruins"), ("sch_signal_jammer", "on a terminal in Rail Depot"),
                     ("sch_armory_3", "in the trader's stock"), ("sch_network_grade", "on a Warden in the Core Spire"),
                 })
            Schematics.HearOf(sim, id, where);
        foreach (var id in new[] { "sch_override", "sch_cyberdeck", "sch_linked_plating" }) Schematics.Receive(sim, id, SchematicSource.Terminal);
        sim.State.Inventory.Add("sch_rifle");  // a copy to sell
        var rd = sim.Data.Schematics.Rules.ResearchDataResource;
        sim.State.Stockpile.Add(rd, 23);
        if (sim.State.Buildings.FirstOrDefault(b => b.DefId == "electronics_lab") is { } lab2)
            _session.Execute(new QueueRecipe(lab2.Id, Schematics.DecryptRecipeId("sch_linked_plating")));
        var researcher = sim.State.Survivors.Where(s => !s.IsChild).OrderByDescending(s => Research.Factor(sim, s)).FirstOrDefault();
        if (researcher is not null) _session.Execute(new StartResearch("sch_evac_beacon", researcher.Id));
        _session.FastForward(20, _ => false);

        // Debug only: park the trader now (as RadioMast does on a visit) with a contract on offer.
        var radio = sim.State.Radio;
        var tpd = sim.Data.Rules.TicksPerDay;
        if (RadioMast.Trader(sim) is { } trader)
        {
            radio.Stock = ["sch_greenhouse", "sch_armory_3"];
            radio.TraderLeavesTick = sim.State.Tick + tpd + 7;
            radio.NextVisitTick = sim.State.Tick + 10 * tpd;
            radio.Visits++;
            foreach (var id in radio.Stock) Schematics.HearOf(sim, id, $"in {trader.Name}'s stock");
            RadioMast.Enqueue(sim, RadioCallKind.Trader, trader.Id, trader.Name, trader.Greeting, expiresInTicks: radio.TraderLeavesTick - sim.State.Tick);
        }
        radio.Contract = new RadioContract
        {
            Id = radio.NextContractId++, ZoneId = "suburb_ruins", DueTick = sim.State.Tick + 6 * tpd,
            Reward = new() { [rd] = 4, ["scrap"] = 15 },
        };
        radio.NextContractTick = sim.State.Tick + 6 * tpd;
        RadioMast.Enqueue(sim, RadioCallKind.Contract, "radio", "A voice on the radio",
            "Need someone to run Suburb Ruins in the next 6 days. Pays 4 Research Data, 15 Scrap.", radio.Contract.Id.ToString(), 6 * tpd);
        RadioMast.Enqueue(sim, RadioCallKind.News, "radio", "Static, then a voice",
            "…anyone on this band: the Rail Depot lights came on last night. Stay clear of the yard.");
        _session.SetSpeed(0);
    }

    /// <summary>Selects the colony's first building of this type, if any.</summary>
    private void SelectFirst(string defId)
    {
        if (_session.Sim.State.Buildings.Where(b => b.DefId == defId).MaxBy(b => b.Level) is { } b) _town.Select(b.Id);
    }

    public override void _Process(double delta) => _session.Advance(delta);

    private void BuildHud()
    {
        _hud = new ColonyHud(_session);
        Hud.Help.HelpLibrary.Ambient = HelpContext;
        AddChild(_hud);
        _siteTip = new SiteTooltip(_session, _town);
        _hud.AddChild(_siteTip);
        _hud.Log.Reset();
    }

    private void Wire()
    {
        _session.Changed += RefreshAll;
        _session.Replaced += () =>
        {
                _defenses.Reset();
            _town.Reset();
            _walkers.Reset();
            _hud.Roster.Select(null);
            _hud.Log.Reset();
        };

        _hud.MainMenuRequested += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        _hud.BackRequested += () =>
        {
            if (_town.PlacingDefId is not null) _town.CancelPlacing();
            else if (_defenses.PlacingDefId is not null) _defenses.CancelPlacing();
            else if (_defenses.SelectedDefenseId is not null) _defenses.Select(null);
            else if (_town.SelectedBuildingId is not null) _town.Select(null);
            else if (_defenses.Active) _defenses.SetActive(false);
            else return false;
            return true;
        };
        _hud.TopBar.ExpeditionPressed += _hud.Expedition.Toggle;
        _hud.Expedition.WorldMapPressed += () => _hud.WorldMap.Open();
        _hud.Expedition.GearBenchPressed += item => _hud.GearBench.Open(item);
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
        _hud.TopBar.PowerPressed += TogglePowerOverlay;
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
            if (id is not null) _defenses.Select(null);
            _hud.Building.Show(id);
            _hud.MedBay.Show(id);
            _hud.Production.Show(id);
            _hud.Study.Show(id);
            _hud.Garage.Show(id);
            _hud.Harvest.Show(id);
            _hud.Research.Show(id);
            _hud.Radio.Show(id);
        };
        _hud.ResearchRequested += () => SelectFirst(_session.Sim.Data.Schematics.Rules.Research.Building);
        _hud.BuildingRequested += SelectFirst;
        _hud.Building.Closed += () => _town.Select(null);

        // Defend mode: catalogue ↔ the town's ghost; selecting a defense opens its panel.
        _hud.TopBar.DefendPressed += () => _defenses.SetActive(!_defenses.Active);
        _hud.Defend.Closed += () => _defenses.SetActive(false);
        _hud.Defend.TypeChosen += defId => _defenses.BeginPlacing(defId);
        _defenses.ModeChanged += SyncDefendUi;
        _defenses.GhostChanged += SyncDefendUi;
        _defenses.SelectionChanged += id =>
        {
            if (id is not null) _town.Select(null);
            _hud.Defense.Show(id);
        };
        _hud.Defense.Closed += () => _defenses.Select(null);
        _hud.Banner.FightPressed += () => _session.PlayRaidBattle(GetTree());
        _defenses.PlaceRequested += commands =>
        {
            if (commands.Count == 1) _session.Execute(commands[0]);
            else _session.ExecuteAll(commands, $"Built {commands.Count} in a row.");
        };
        _hud.Roster.SelectionChanged += id => _walkers.SelectedSurvivorId = id;
    }

    /// <summary>The catalogue follows defend mode; building placement and defend mode exclude each other.</summary>
    private void SyncDefendUi()
    {
        if (_defenses.Active && !_hud.Defend.Visible)
        {
            if (_hud.BuildMenu.Visible) ToggleBuildMenu();
            _town.CancelPlacing();
            Hud.Motion.Reveal(_hud.Defend, new Vector2(12, 0));
        }
        else if (!_defenses.Active) _hud.Defend.Visible = false;
        _hud.Defend.SetPlacing(_defenses.PlacingDefId, _defenses.Facing, _defenses.GhostResult);
    }

    private void ToggleBuildMenu()
    {
        if (!_hud.BuildMenu.Visible) _defenses.SetActive(false);
        Hud.Motion.Toggle(_hud.BuildMenu, new Vector2(12, 0));
        if (!_hud.BuildMenu.Visible) _town.CancelPlacing();
        _hud.BuildMenu.SetPlacing(null);
    }

    private void RefreshAll()
    {
        if (_session.UnseenReport is not null) ShowUnseenReport();  // a party walked home from a far zone
        _town.Sync();
        _walkers.Sync();
        _defenses.Sync();
        _power.Sync();
        _hud.Refresh();
        // They are at the fence: time stops once so the player can go fight (letting it run settles it after a while).
        if (_session.Sim.State.Defense.Raid is { Stage: Remnants.Core.Raids.RaidStage.Battle } fence && fence.Id != _pausedForRaid)
        {
            _pausedForRaid = fence.Id;
            if (_session.Speed > 0) _session.SetSpeed(0);
        }
        if (_session.Sim.State.Defense.LastReport is { } raid && !ReferenceEquals(raid, _session.SeenRaidReport))
        {
            _session.SeenRaidReport = raid;
            _hud.RaidReport.ShowReport(raid);
        }
    }
}
