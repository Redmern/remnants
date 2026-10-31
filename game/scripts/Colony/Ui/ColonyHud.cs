using System;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The colony HUD's layout and keyboard: top bar; roster and survivor details down the left; the expedition and
/// item panels and the repair queue in the middle; the raid warning under the top bar; building, build menu, defend
/// catalogue, selected defense, Med Bay and production down the right above the event log;
/// toasts at the town's bottom-right; world map, after-action and raid reports, raid prep, pause menu and settings on top. Hotkeys
/// (<see cref="Hotkeys"/>) do what their buttons do. <see cref="ColonyScreen"/> wires the panels to the town.
/// </summary>
public partial class ColonyHud : CanvasLayer
{
    private readonly ColonySession _session;
    private int _speedBeforePause = 1;

    public TopBar TopBar { get; }
    public RosterPanel Roster { get; }
    public SurvivorPanel Survivor { get; }
    public BuildingPanel Building { get; }
    public BuildMenu BuildMenu { get; }
    public EventLog Log { get; }
    public ExpeditionPanel Expedition { get; }
    public WorldMapPanel WorldMap { get; }
    public MedBayPanel MedBay { get; }
    public ProductionPanel Production { get; }
    public StudyPanel Study { get; }
    public ResearchPanel Research { get; }
    public RadioPanel Radio { get; }
    public InventoryPanel Inventory { get; }
    public GaragePanel Garage { get; }
    public PantryPanel Pantry { get; }
    public HarvestPanel Harvest { get; }
    public AfterActionDialog Report { get; }

    /// <summary>Defend mode's catalogue, the selected defense, the repair queue, the raid warning, prep and report.</summary>
    public DefenseCatalog Defend { get; }
    public DefensePanel Defense { get; }
    public RepairPanel Repairs { get; }
    public RaidBanner Banner { get; }
    public RaidPrepDialog RaidPrep { get; }
    public RaidReportDialog RaidReport { get; }
    public Toasts Toasts { get; } = new();
    public PauseMenu Pause { get; }
    public SettingsPanel Settings { get; } = new();

    /// <summary>The cybernetics tree (from the Med Bay, Workshop and Electronics Lab).</summary>
    public CyberneticsPanel Cybernetics { get; }
    /// <summary>Story mode: radio, journal, briefings, dialogue (hidden in Sandbox).</summary>
    public Remnants.Game.Story.StoryHud Story { get; }

    /// <summary>The Trading Post (async market with other players) at the Radio Mast.</summary>
    public Trade.TradingPostPanel TradingPost { get; }
    /// <summary>The Gear Bench (from the Armory, Workshop and Tailor).</summary>
    public GearBenchPanel GearBench { get; }

    /// <summary>Esc with nothing else to close; the screen may cancel placement or deselect first.</summary>
    public event Func<bool>? BackRequested;

    public event Action? MainMenuRequested;

    /// <summary>The archive's "Research" on a heard-of schematic: the screen selects the Town Hall.</summary>
    public event Action? ResearchRequested;

    private Remnants.Core.Colony.ColonySim? _watched;

    /// <summary>Toasts for schematics learned (<see cref="Remnants.Core.Colony.ColonySim.SchematicUnlocked"/>), on the current sim.</summary>
    private void Watch(Remnants.Core.Colony.ColonySim sim)
    {
        if (_watched is not null) _watched.SchematicUnlocked -= OnSchematicUnlocked;
        _watched = sim;
        sim.SchematicUnlocked += OnSchematicUnlocked;
    }

    public override void _ExitTree()
    {
        if (_watched is not null) _watched.SchematicUnlocked -= OnSchematicUnlocked;
        _watched = null;
    }

    private void OnSchematicUnlocked(Remnants.Core.Schematics.SchematicUnlocked e)
    {
        var sim = _session.Sim;
        if (sim.Data.Schematics.Find(e.SchematicId) is not { } def) return;
        var unlocks = SchematicText.Unlocks(sim, def);
        var title = $"Schematic learned: {def.Name}";
        Toasts.Show(title, $"Unlocks {unlocks}.", "item_schematic", Palette.Good, "schematic-learned",
            n => ($"{n} schematics learned", $"Latest: {def.Name} ({unlocks}). See Items → Schematics."));
    }

    public ColonyHud() : this(null!) { }

    public ColonyHud(ColonySession session)
    {
        _session = session;
        Name = "Hud";
        var root = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = HudTheme.Shared };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) root.AddThemeConstantOverride($"margin_{side}", 8);
        root.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
        AddChild(root);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 8);
        root.AddChild(column);
        TopBar = new TopBar(session);
        column.AddChild(TopBar);
        // The raid warning sits under the top bar, centred, only while a raid is seen coming.
        var bannerRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        Banner = new RaidBanner(session);
        bannerRow.AddChild(Banner);
        column.AddChild(bannerRow);

        var middle = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddThemeConstantOverride("separation", 8);
        column.AddChild(middle);

        Roster = new RosterPanel(session);
        middle.AddChild(Roster);
        Survivor = new SurvivorPanel(session);
        middle.AddChild(Survivor);

        var center = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        middle.AddChild(center);
        var floating = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        Inventory = new InventoryPanel(session);
        Expedition = new ExpeditionPanel(session);
        Garage = new GaragePanel(session);
        Repairs = new RepairPanel(session);
        floating.AddChild(Inventory);
        Pantry = new PantryPanel(session);
        floating.AddChild(Pantry);
        floating.AddChild(Expedition);
        floating.AddChild(Garage);
        floating.AddChild(Repairs);
        center.AddChild(floating);

        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(360, 0) };
        right.AddThemeConstantOverride("separation", 8);
        middle.AddChild(right);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        right.AddChild(scroll);
        _rightScroll = scroll;
        var stack = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(stack);
        BuildMenu = new BuildMenu(session);
        Defend = new DefenseCatalog(session);
        Defense = new DefensePanel(session);
        Building = new BuildingPanel(session);
        Building.SurvivorRequested += id => Roster.Select(id);
        Defense.SurvivorRequested += id => Roster.Select(id);
        MedBay = new MedBayPanel(session);
        Production = new ProductionPanel(session);
        Study = new StudyPanel(session);
        Harvest = new HarvestPanel(session);
        Research = new ResearchPanel(session);
        Radio = new RadioPanel(session);
        stack.AddChild(BuildMenu);
        stack.AddChild(Defense);
        stack.AddChild(Defend);
        stack.AddChild(Building);
        stack.AddChild(Harvest);
        stack.AddChild(MedBay);
        stack.AddChild(Radio);
        stack.AddChild(Research);
        stack.AddChild(Production);
        stack.AddChild(Study);
        Log = new EventLog(session);
        right.AddChild(Log);

        // Toasts float over the town's bottom-right corner, beside the right column, anchored in a plain Control so
        // they never push panels around.
        var toastArea = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        center.AddChild(toastArea);
        Toasts.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        Toasts.GrowHorizontal = Control.GrowDirection.Begin;
        Toasts.GrowVertical = Control.GrowDirection.Begin;
        toastArea.AddChild(Toasts);

        Story = new Remnants.Game.Story.StoryHud(session);
        AddChild(Story);
        WorldMap = new WorldMapPanel(session) { Theme = HudTheme.Shared };
        AddChild(WorldMap);
        Report = new AfterActionDialog(session);
        AddChild(Report);
        RaidPrep = new RaidPrepDialog(session);
        AddChild(RaidPrep);
        RaidReport = new RaidReportDialog(session);
        AddChild(RaidReport);
        // Time stands still while the colony prepares.
        RaidPrep.Closed += ResumeTime;
        Banner.PreparePressed += OpenPrep;
        Defend.RepairsPressed += () => Repairs.Toggle();
        TopBar.RepairsPressed += () => Repairs.Toggle();

        Pause = new PauseMenu(
        [
            new("Resume", "ui_play", ClosePause, Hint: "Back to the colony (Esc)"),
            new("Save", "ui_save", () => { session.Save(); ClosePause(); }, Hint: "Save the colony"),
            new("Load", "ui_load", () => { session.Load(); ClosePause(); }, "Load the last save? Progress since then is lost.", "Load the last save"),
            new("Settings", "ui_settings", () => Settings.Open(), Hint: "Display, audio, camera, controls"),
            new("Main menu", "ui_menu", () => MainMenuRequested?.Invoke(),
                "Go to the main menu? The colony waits: Continue picks it up. Save to keep it after you quit.", "Back to the title screen"),
            new("Quit", "ui_quit", () => GetTree().Quit(), "Quit the game? Unsaved progress is lost.", "Quit to the desktop"),
        ]) { Theme = HudTheme.Shared };
        Pause.Closed += ResumeTime;
        Cybernetics = new CyberneticsPanel(session);
        AddChild(Cybernetics);
        TradingPost = new Trade.TradingPostPanel(session);
        AddChild(TradingPost);
        GearBench = new GearBenchPanel(session);
        AddChild(GearBench);
        AddChild(Pause);
        AddChild(Settings);
        MedBay.CyberneticsPressed += (who, part) =>
            Cybernetics.Open(part is { } p ? Remnants.Core.Data.CyberneticsData.SlotOf(p) : null, who, part);
        Production.CyberneticsPressed += () => Cybernetics.Open();
        Production.GearBenchPressed += () => GearBench.Open();

        Log.Important += n => Toasts.Show(n.Title, n.Text, n.Icon, n.Color, n.Key, n.Merged);
        Inventory.Archive.ResearchRequested += () => ResearchRequested?.Invoke();
        Watch(session.Sim);
        session.Replaced += () => Watch(session.Sim);
        TopBar.MenuPressed += OpenPause;
        TopBar.ItemsPressed += () => Inventory.Toggle();
        TopBar.PantryPressed += () => Pantry.Toggle();
        TopBar.WorldMapPressed += () => WorldMap.Open();
        Story.PlanMission += id => Expedition.PlanForMission(id);
        Story.CallArrived += caller => Toasts.Show("Incoming call", $"{caller} is on the radio.", "ui_radio", Palette.Amber, "story_call");
        WorldMap.MissionPressed += id => { WorldMap.Close(); Story.OpenBriefing(id); };
        session.Replaced += () => Story.Reset();
        Trade.TradingPostTab.OpenRequested += OpenTradingPostFromTab;
        TreeExiting += () => Trade.TradingPostTab.OpenRequested -= OpenTradingPostFromTab;
        Survivor.Closed += () => Roster.Select(null);
        Roster.SelectionChanged += Survivor.Show;
    }

    public void Refresh()
    {
        TopBar.Refresh();
        Banner.Refresh();
        RaidPrep.Refresh();
        Roster.Refresh();
        Survivor.Refresh();
        Building.Refresh();
        MedBay.Refresh();
        Production.Refresh();
        Study.Refresh();
        Research.Refresh();
        Radio.Refresh();
        Cybernetics.Refresh();
        GearBench.Refresh();
        Inventory.Refresh();
        Pantry.Refresh();
        Harvest.Refresh();
        Expedition.Refresh();
        Garage.Refresh();
        WorldMap.Refresh();
        Story.Refresh();
        if (BuildMenu.Visible) BuildMenu.Refresh();
        if (Defend.Visible) Defend.Refresh();
        Defense.Refresh();
        Repairs.Refresh();
        Log.Refresh();
    }

    private readonly ScrollContainer _rightScroll;

    /// <summary>Scrolls the right-hand panel column so <paramref name="control"/> is in view (after layout).</summary>
    public void ScrollRightTo(Control control) => _rightScroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, control);

    /// <summary>Scrolls the right-hand column so a panel shows (debug captures of long panels).</summary>
    public void ScrollSideTo(Control panel) => ScrollRightTo(panel);

    private void OpenTradingPostFromTab() => OpenTradingPost();

    /// <summary>Opens the Trading Post (needs a Radio Mast once the game has one); <paramref name="address"/> connects at once.</summary>
    public void OpenTradingPost(string? address = null)
    {
        if (Remnants.Core.Trade.Courier.Blocked(_session.Sim, Trade.TradeUi.Rules) is { } why)
        {
            Toasts.Show("No Radio Mast", why, "ui_post", Palette.Warn);
            return;
        }
        TradingPost.Open(address);
    }

    public void OpenPause()
    {
        if (Pause.Visible) return;
        _speedBeforePause = _session.Speed;
        _session.SetSpeed(0);
        Pause.Open();
    }

    private void ClosePause() => Pause.Close();

    /// <summary>The prep screen for the warned raid; time pauses until it closes.</summary>
    public void OpenPrep()
    {
        if (RaidPrep.Visible || _session.Sim.State.Defense.Raid is not { Stage: Remnants.Core.Raids.RaidStage.Warned }) return;
        _speedBeforePause = _session.Speed == 0 ? _speedBeforePause : _session.Speed;
        _session.SetSpeed(0);
        RaidPrep.Open();
    }

    private void ResumeTime()
    {
        if (_session.Speed == 0) _session.SetSpeed(_speedBeforePause);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false }) return;
        if (Pause.Visible || Settings.Visible || Cybernetics.Visible || GearBench.Visible || RaidPrep.Visible || RaidReport.Visible) return; // the overlays handle their own Esc
        if (Hotkeys.Pressed(e, "pause_menu")) Back();
        else if (Hotkeys.Pressed(e, "toggle_pause")) _session.SetSpeed(_session.Speed == 0 ? Math.Max(1, _speedBeforePause) : Remember());
        else if (Hotkeys.Pressed(e, "speed_1")) _session.SetSpeed(1);
        else if (Hotkeys.Pressed(e, "speed_2")) _session.SetSpeed(2);
        else if (Hotkeys.Pressed(e, "speed_3")) _session.SetSpeed(4);
        else if (Hotkeys.Pressed(e, "speed_4")) _session.SetSpeed(8);
        else if (Hotkeys.Pressed(e, "open_build")) TopBar.EmitBuild();
        else if (Hotkeys.Pressed(e, "open_defend")) TopBar.EmitDefend();
        else if (Hotkeys.Pressed(e, "open_expedition")) Expedition.Toggle();
        else if (Hotkeys.Pressed(e, "open_items")) Inventory.Toggle();
        else if (Hotkeys.Pressed(e, "open_log")) Log.ToggleCollapsed();
        else if (Hotkeys.Pressed(e, "cycle_survivor")) Roster.SelectNext();
        else if (Hotkeys.Pressed(e, "open_power")) TopBar.EmitPower();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private int Remember()
    {
        _speedBeforePause = _session.Speed;
        return 0;
    }

    /// <summary>Esc: close the topmost thing that is open, else open the pause menu.</summary>
    private void Back()
    {
        if (Story.Back()) { }
        else if (WorldMap.Visible) WorldMap.Close();
        else if (Report.Visible) Report.Hide();
        else if (BackRequested?.Invoke() == true) { }
        else if (Expedition.Visible) Expedition.Toggle();
        else if (Inventory.Visible) Inventory.Toggle();
        else if (Repairs.Visible) Repairs.Toggle();
        else if (Pantry.Visible) Pantry.Toggle();
        else if (BuildMenu.Visible) TopBar.EmitBuild();
        else if (Survivor.Visible) Roster.Select(null);
        else OpenPause();
    }
}
