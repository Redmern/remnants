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
    private DefenseView _defenses = null!;
    private ColonyHud _hud = null!;
    private OrbitCamera _camera = null!;

    /// <summary>The raid at the fence time was stopped for (once each).</summary>
    private int? _pausedForRaid;
    private TownLayout _layout = null!;

    /// <summary>A new game nobody has played yet (debug demos only stage on one).</summary>
    private bool IsFreshGame => _session.Sim.State.Tick == _session.Sim.Data.Rules.NewGame.StartHour;

    public override void _Ready()
    {
        Hud.GameSettings.Boot(GetTree());
        var data = GameDataPaths.Load();
        // The session outlives this scene so a party can leave for the scavenge scene and come back to it.
        _session = ColonySession.Current ??= new ColonySession(NewGame.Create(data, NewGameSeed, GameDataPaths.LoadScavenge()));
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

        BuildHud();
        Wire();
        RefreshAll();
        var audio = new Audio.ColonyAudio { Name = "Audio" };
        AddChild(audio);
        audio.Setup(_session, layout, camera, _hud);

        if ((DebugCapture.HasFlag("--demo") || DebugCapture.HasFlag("--family-demo")) && IsFreshGame) RunDemo();
        if (DebugCapture.HasFlag("--family-demo")) RunFamilyDemo();
        if (DebugCapture.HasFlag("--crafting-demo") && IsFreshGame) RunCraftingDemo();
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
        if (DebugCapture.HasFlag("--world-demo") && IsFreshGame) RunWorldDemo();
        if (DebugCapture.HasFlag("--night-demo") && IsFreshGame) RunNightDemo();
        if (DebugCapture.Value("--raid-demo") is { } raidDemo && IsFreshGame) RunRaidDemo(raidDemo);
        if (int.TryParse(DebugCapture.Value("--hour"), out var hour)) JumpToHour(hour);
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

    /// <summary>Debug: run to the next <paramref name="hour"/>:00, pause there and put everyone where that hour has them.</summary>
    private void JumpToHour(int hour)
    {
        _session.FastForward(_session.Sim.Data.Rules.TicksPerDay, s => s.Hour == hour);
        _session.SetSpeed(0);
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
        sim.State.Stockpile.Add(sim.Data.Rules.Food.ResourceId, 200);  // so the colony can afford a family right away
        _session.FastForward(20_000, s => s.State.Events.Any(e => e.Type == ColonyEventType.Born));
        _session.FastForward(30, _ => false);
        _hud.Roster.Select(woman.Id);
        _session.SetSpeed(1);
    }

    /// <summary>
    /// Debug (screenshots of perks and study): Town Hall 2 with an Electronics Lab, a few days of work behind everyone,
    /// a hacker with the Tech perks and a studied datashard, two classes at the Lab (Hacking with a mentor, a child on
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
        sim.State.Inventory.Add("ds_network_ghost");
        sim.State.Inventory.Add("manual_drone_ops");
        _session.Execute(new StudyBlueprint("ds_network_ghost"));

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
    /// at level 2; a Cyber-Surgeon at the Med Bay and a Biomech / Neural Interface engineer; Blueprint: Cybernetics
    /// studied and a few designs and components in the stores; a veteran wearing mixed tiers (a mechanical leg, a cyborg
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
        foreach (var id in new[] { "bp_cybernetics", "mech_leg", "mech_arm", "runner_leg", "silent_leg", "precision_arm", "labor_arm",
                     "servo", "servo", "power_cell", "neural_link", "myomer_bundle", "cyber_leg", "blade_arm" })
            sim.State.Inventory.Add(id);
        _session.Execute(new StudyBlueprint("bp_cybernetics"));

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
            sim.State.Defense.KnownSchematics.Add(id);

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
            if (id is not null) _defenses.Select(null);
            _hud.Building.Show(id);
            _hud.MedBay.Show(id);
            _hud.Production.Show(id);
            _hud.Study.Show(id);
        };
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
