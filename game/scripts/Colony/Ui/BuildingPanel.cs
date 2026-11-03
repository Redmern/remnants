using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Help;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The selected building, one panel with a pinned header and tabs (UX audit 6.2). The header never scrolls away: icon,
/// name, level pips, status chips (power fed / not fed / off, raid damage, lightning damage, night shift), the workers as
/// <see cref="WorkerSlots"/> ("+" opens the picker, right-click a portrait unassigns, a survivor card dropped on the
/// panel is assigned) and the one output line. Tabs: <b>Overview</b> (what it does, raid and lightning damage with their
/// repair, the Night Shift toggle and Floodlight, the Power switch), <b>Upgrade</b> (the next level's gain, named cost
/// chips, what blocks it with a link) and one tab per building-specific panel the HUD hosts here (<see cref="AddExtra"/>:
/// crops, the queue, patients, research, radio, study). Everything is built once and updated in place. Core decides
/// whether an upgrade, a night shift, a Floodlight, a repair or a switch goes through.
/// </summary>
public partial class BuildingPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Unchanged _set = new();

    // Header (pinned)
    private readonly TextureRect _icon = Icons.Rect(Icons.Get("ui_build", 26), 26, Palette.Rust);
    private readonly Label _name = Widgets.Label("", "Display");
    private readonly Label _category = Widgets.Label("", "Small");
    private readonly Pips _levels = new();
    private readonly Label _levelText = Widgets.Label("", "Small");
    private readonly HFlowContainer _chips = new();
    private readonly PanelContainer _powerChip;
    private readonly PanelContainer _damageChip;
    private readonly PanelContainer _lightningChip;
    private readonly PanelContainer _nightChip;
    private readonly HBoxContainer _workersHead;
    private readonly Label _workersLabel = Widgets.Label("", "Heading");
    private readonly WorkerSlots _workers;
    private readonly Label _output = Widgets.Wrapped("", "");
    private readonly HBoxContainer _outputRow;

    // Tabs
    private readonly TabStrip _tabs = new();
    private readonly ScrollContainer _scroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
    private readonly VBoxContainer _pages = Widgets.Column(0);
    private readonly List<Control> _pageList = new();
    private readonly int _overviewTab, _upgradeTab;
    private readonly List<(PanelContainer Panel, int Tab)> _extras = new();
    private bool _pickTab;

    // Overview
    private readonly Label _purpose = Widgets.Wrapped("", "Muted");
    private readonly VBoxContainer _damageBox = Widgets.Column(Space.Xs);
    private readonly Label _damageTitle = Widgets.Label("", "Heading");
    private readonly StatBar _damageHp = new("stat_hp");
    private readonly Label _damageStatus = Widgets.Wrapped("", "Small");
    private readonly Button _damageCancel;
    private readonly Button _damageRepair;
    private readonly CostRow _damageCost = new();
    private readonly VBoxContainer _nightBox = Widgets.Column(Space.Xs);
    private readonly Button _nightShift;
    private readonly Label _nightNote = Widgets.Wrapped("", "Small");
    private readonly HBoxContainer _floodRow = Widgets.Row(Space.Sm);
    private readonly CostRow _floodCost = new();
    private readonly Button _flood;
    private readonly VBoxContainer _repairBox = Widgets.Column(Space.Xs);
    private readonly Label _repairNote = Widgets.Wrapped("", "Small", Palette.Warn);
    private readonly CostRow _repairCost = new();
    private readonly Button _repair;
    private readonly VBoxContainer _powerBox = Widgets.Column(Space.Xs);
    private readonly Label _powerLines = Widgets.Wrapped("", "Small");
    private readonly Button _powerSwitch;

    // Upgrade
    private readonly Label _upgradeTitle = Widgets.Label("", "Heading");
    private readonly VBoxContainer _gains = Widgets.Column(2);
    private readonly CostRow _costs = new(named: true);
    private readonly Label _blocker = Widgets.Wrapped("", "Small");
    private readonly Button _blockerLink;
    private readonly Button _upgrade;
    private readonly Label _upgradeWhy = Widgets.Wrapped("", "Small");
    private readonly Label _buildTime = Widgets.Wrapped("", "Small", Palette.TextMuted);
    private readonly Control _upgradeBody;

    // Construction (a hosted tab while the building is built or upgraded)
    private readonly ConstructionSitePanel _site;

    public int? BuildingId { get; private set; }

    public event Action? Closed;

    /// <summary>The worker slots (debug: the smoke run clicks "+").</summary>
    public WorkerSlots Workers => _workers;

    /// <summary>The Construction tab (debug: the smoke run assigns builders through it).</summary>
    public ConstructionSitePanel Site => _site;

    /// <summary>"Open survivor" on a worker's portrait.</summary>
    public event Action<int>? SurvivorRequested;

    /// <summary>A blocker's "Open archive" link (the schematic an upgrade needs).</summary>
    public event Action? ArchiveRequested;

    /// <summary>The tallest the panel may grow (the HUD's room in the right column); the tab body scrolls past it.</summary>
    public Func<float>? MaxHeight { get; set; }

    /// <summary>A survivor's fit for a job: Core's effectiveness (the job's skill and attributes, weighted) and its parts.</summary>
    public static SlotFit JobFit(Remnants.Core.Data.JobDef job, Survivor s)
    {
        var parts = job.Attributes.Select(kv => $"{kv.Key} {s.Get(kv.Key):0}").Prepend($"{job.Skill} {s.Get(job.Skill):0}");
        return new SlotFit(JobOutput.Effectiveness(job, s), $"{job.Name} fit: {string.Join(", ", parts)}");
    }

    public BuildingPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(PanelWidth.Side, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var box = Ui.Panel(this, Space.Sm);

        // ── Header: name and level, status chips, workers, output ──
        var head = Widgets.Row(Space.Sm);
        head.AddChild(_icon);
        var names = Widgets.Column(0);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        names.AddChild(_name);
        var sub = Widgets.Row(Space.Sm);
        sub.AddChild(_category);
        _levels.MouseFilter = MouseFilterEnum.Stop;
        sub.AddChild(_levels);
        sub.AddChild(_levelText);
        names.AddChild(sub);
        head.AddChild(names);
        var close = Widgets.IconButton("ui_close", "", "Deselect", () => Closed?.Invoke(), "Esc", "Flat");
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        head.AddChild(close);
        box.AddChild(head);

        _chips.AddThemeConstantOverride("h_separation", Space.Xs);
        _chips.AddThemeConstantOverride("v_separation", Space.Xs);
        _powerChip = StatusChip("stat_power", "power", () => ShowTab(_overviewTab, _powerBox));
        _damageChip = StatusChip("ui_repair", "raids", () => ShowTab(_overviewTab, _damageBox));
        _lightningChip = StatusChip("weather_lightning", "weather", () => ShowTab(_overviewTab, _repairBox));
        _nightChip = StatusChip("ui_night_shift", "night_shift", () => ShowTab(_overviewTab, _nightBox));
        box.AddChild(_chips);

        _workersHead = InfoTip.Beside(_workersLabel, "jobs_output", WorkerContext);
        box.AddChild(_workersHead);
        _workers = new WorkerSlots(session)
        {
            Title = sim => BuildingId is { } b && sim.State.FindBuilding(b) is { } at ? $"Assign to {ColonyText.BuildingName(sim, at)}" : "Assign",
            Candidate = (sim, s) => !s.IsInfant && (BuildingId is not { } here || !s.Jobs.Contains(here)),
            Fit = (sim, s) => BuildingId is { } b && sim.State.FindBuilding(b) is { } at && sim.Data.Buildings[at.DefId].Job is { } job
                ? JobFit(sim.Data.Jobs[job], s) : null,
            // A pick adds this job (their others stay, effort splits); shift-click moves them here alone.
            Assign = s => JobPicks.Add(s.Id, BuildingId ?? -1),
            Move = s => JobPicks.Move(s.Id, BuildingId ?? -1),
            Unassign = s => JobPicks.Leave(s.Id, BuildingId ?? -1),
            FreeTip = "Free slot: click to pick who works here (they keep their other jobs).\nOr drag a survivor's card onto this panel.",
        };
        _workers.SurvivorRequested += id => SurvivorRequested?.Invoke(id);
        box.AddChild(_workers);
        _output.MouseFilter = MouseFilterEnum.Stop;
        _outputRow = InfoTip.Beside(_output, "production", WorkerContext);
        _output.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(_outputRow);

        // ── Tabs ──
        _overviewTab = _tabs.Add("Overview", "What it does, damage, night shift and power");
        _upgradeTab = _tabs.Add("Upgrade", "The next level: what it adds and costs");
        _tabs.Changed += OnTab;
        box.AddChild(_tabs);
        _scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _pages.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_pages);
        box.AddChild(_scroll);

        // Overview page
        var overview = Widgets.Column(Space.Md);
        overview.AddChild(_purpose);

        // Raid damage: shown only when the building is hurt; a wrecked one cannot be worked until repaired.
        _damageBox.AddChild(_damageTitle);
        _damageBox.AddChild(_damageHp);
        _damageBox.AddChild(_damageStatus);
        var damageRow = Widgets.Row(Space.Sm);
        _damageRepair = Widgets.IconButton("ui_repair", "Queue repair", "Idle adults work the repair queue by day; materials are paid when it starts.", QueueRepair);
        _damageRepair.Icon = Icons.Get("ui_repair", 18, "skill_crafting");
        _damageRepair.FocusMode = FocusModeEnum.All;
        damageRow.AddChild(_damageRepair);
        _damageCancel = Widgets.IconButton("ui_close", "Cancel repair", "Take it out of the repair queue", CancelRepair);
        _damageCancel.FocusMode = FocusModeEnum.All;
        damageRow.AddChild(_damageCancel);
        _damageCost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        damageRow.AddChild(_damageCost);
        _damageBox.AddChild(damageRow);
        overview.AddChild(_damageBox);

        // Struck by lightning: lower output until repaired.
        _repairBox.AddChild(Widgets.SectionHeader("Lightning damage", "weather_lightning", new InfoTip("weather")));
        _repairBox.AddChild(_repairNote);
        var repairRow = Widgets.Row(Space.Sm);
        _repair = Widgets.IconButton("ui_build", "Repair", "", Repair, variation: "Primary");
        _repair.FocusMode = FocusModeEnum.All;
        repairRow.AddChild(_repair);
        repairRow.AddChild(_repairCost);
        _repairBox.AddChild(repairRow);
        overview.AddChild(_repairBox);

        _nightBox.AddChild(Widgets.SectionHeader("Night", "phase_night", new InfoTip("night_shift", WorkerContext)));
        var nightRow = Widgets.Row(Space.Sm);
        _nightShift = Motion.OnPress(Widgets.IconButton("ui_night_shift", "Night shift", "", ToggleNightShift));
        _nightShift.ToggleMode = true;
        _nightShift.FocusMode = FocusModeEnum.All;
        nightRow.AddChild(_nightShift);
        _nightNote.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nightRow.AddChild(_nightNote);
        _nightBox.AddChild(nightRow);
        _flood = Widgets.IconButton("bld_floodlight", "Fit Floodlight", "", FitFloodlight);
        _flood.FocusMode = FocusModeEnum.All;
        _floodRow.AddChild(_flood);
        _floodCost.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _floodRow.AddChild(_floodCost);
        _nightBox.AddChild(_floodRow);
        overview.AddChild(_nightBox);

        _powerBox.AddChild(Widgets.SectionHeader("Power", "stat_power", new InfoTip("power")));
        _powerBox.AddChild(_powerLines);
        // A switch, not a call to action: flat, saying the state it is in.
        _powerSwitch = Motion.OnPress(Widgets.IconButton("stat_power", "On the grid", "", TogglePower, variation: "Flat"));
        _powerSwitch.ToggleMode = true;
        _powerSwitch.FocusMode = FocusModeEnum.All;
        _powerSwitch.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _powerBox.AddChild(_powerSwitch);
        overview.AddChild(_powerBox);
        AddPage(overview);

        // Upgrade page
        var upgrade = Widgets.Column(Space.Sm);
        upgrade.AddChild(_upgradeTitle);
        var body = Widgets.Column(Space.Sm);
        _upgradeBody = body;
        body.AddChild(_gains);
        body.AddChild(Widgets.Label("COSTS", "Heading"));
        body.AddChild(_costs);
        body.AddChild(InfoTip.Beside(_buildTime, "upgrade_time"));
        var blockerRow = Widgets.Row(Space.Sm);
        _blocker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        blockerRow.AddChild(_blocker);
        _blockerLink = Widgets.IconButton("item_schematic", "Open archive", "Items → Schematics: where schematics are kept and researched", () => ArchiveRequested?.Invoke(), variation: "Flat");
        _blockerLink.FocusMode = FocusModeEnum.All;
        _blockerLink.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        blockerRow.AddChild(_blockerLink);
        body.AddChild(blockerRow);
        _upgrade = Widgets.IconButton("ui_chevron_up", "Upgrade", "Upgrade this building", Upgrade, variation: "Primary");
        _upgrade.FocusMode = FocusModeEnum.All;
        body.AddChild(_upgrade);
        body.AddChild(_upgradeWhy);
        upgrade.AddChild(body);
        AddPage(upgrade);

        // Construction: the first hosted tab, so a building under construction opens on it.
        _site = new ConstructionSitePanel(session);
        _site.SurvivorRequested += id => SurvivorRequested?.Invoke(id);
        AddExtra(_site, "Construction", "The site: materials hauled, work, builders, pause, priority and cancel");
        Visible = false;
        Fit.OnParentResized(this, FitHeight);
    }

    /// <summary>A small chip in the header (power, damage, night): click jumps to its section; an (i) on hover through the tooltip.</summary>
    private PanelContainer StatusChip(string icon, string topic, Action onClick)
    {
        var chip = Widgets.Chip(Icons.Get(icon, 16), "");
        chip.MouseDefaultCursorShape = CursorShape.PointingHand;
        chip.GetChild(0).AddChild(new InfoTip(topic, WorkerContext));
        chip.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) onClick();
        };
        chip.Visible = false;
        _chips.AddChild(chip);
        return chip;
    }

    private void AddPage(Control page)
    {
        page.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.Visible = _pageList.Count == _tabs.Selected;
        _pageList.Add(page);
        _pages.AddChild(page);
    }

    /// <summary>
    /// Hosts a building-specific panel (crops, queue, patients...) under its own tab, shown while the panel says it
    /// applies (its own <see cref="CanvasItem.Visible"/>, set by its Refresh). It loses its frame: the building panel is its frame.
    /// </summary>
    public void AddExtra(PanelContainer panel, string title, string tooltip)
    {
        var tab = _tabs.Add(title, tooltip);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var host = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        host.AddChild(panel);
        AddPage(host);
        _extras.Add((panel, tab));
        _tabs.SetShown(tab, false);
        panel.VisibilityChanged += SyncExtras;
    }

    /// <summary>True when <paramref name="control"/> lives in one of this panel's tabs.</summary>
    public bool Hosts(Control control) => _pages.IsAncestorOf(control);

    /// <summary>Opens the tab holding <paramref name="control"/> and scrolls it into view (debug captures of long panels).</summary>
    public void Reveal(Control control)
    {
        var page = _pageList.FindIndex(p => p == control || p.IsAncestorOf(control));
        if (page >= 0) _tabs.Select(page);
        _scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, control);
    }

    private void ShowTab(int tab, Control? section = null)
    {
        _tabs.Select(tab);
        if (section is not null) _scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, section);
    }

    private void OnTab(int tab)
    {
        for (var i = 0; i < _pageList.Count; i++)
        {
            if (i == tab) Motion.Reveal(_pageList[i]);
            else _pageList[i].Visible = false;
        }
        _scroll.ScrollVertical = 0;
        FitHeight();
    }

    private void SyncExtras()
    {
        foreach (var (panel, tab) in _extras) _tabs.SetShown(tab, panel.Visible);
    }

    /// <summary>A new building opens on its own work (the first hosted tab that applies), else on Overview.</summary>
    private void PickTab()
    {
        if (!_pickTab) return;
        _pickTab = false;
        SyncExtras();
        var extra = _extras.FirstOrDefault(x => x.Panel.Visible);
        _tabs.Select(extra.Panel is not null ? extra.Tab : _overviewTab);
        OnTab(_tabs.Selected);
    }

    /// <summary>Survivor-card drops: assign the dropped survivor here (Core decides; a refusal shows in the log).</summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        BuildingId is { } id && SurvivorCard.DraggedId(data) is not null
        && (_session.Sim.Site(id) is not null && _tabs.Selected == _extras[0].Tab
            || _session.Sim.State.FindBuilding(id) is { } b && _session.Sim.Data.Buildings[b.DefId].Job is not null);

    /// <summary>A card dropped on the Construction tab makes a builder; anywhere else, a worker.</summary>
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (BuildingId is not { } id || SurvivorCard.DraggedId(data) is not { } who) return;
        if (_session.Sim.Site(id) is not null && (_tabs.Selected == _extras[0].Tab || _session.Sim.State.FindBuilding(id) is null))
            _session.Execute(new AssignBuilder(who, id));
        else _session.Execute(JobPicks.Add(who, id));
    }

    private HelpContext WorkerContext()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        return new HelpContext
        {
            Colony = sim,
            Building = building,
            Survivor = building is null ? null : sim.State.WorkersAt(building.Id).OrderBy(s => s.Id).FirstOrDefault(),
        };
    }

    public void Show(int? buildingId)
    {
        var changed = buildingId != BuildingId;
        BuildingId = buildingId;
        _workers.Reset();
        _site.Show(buildingId);
        if (buildingId is null) Visible = false;
        else if (_session.Sim.State.FindBuilding(buildingId.Value) is not null || _session.Sim.Site(buildingId.Value) is not null)
        {
            Motion.Reveal(this, new Vector2(12, 0));
            if (changed)
            {
                _pickTab = true;
                CallDeferred(MethodName.PickTab);
            }
        }
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        var site = BuildingId is { } sid ? sim.Site(sid) : null;
        _site.Refresh();
        // A new building's site has only the header and the Construction tab; a standing building has them all.
        _tabs.SetShown(_overviewTab, building is not null);
        _tabs.SetShown(_upgradeTab, building is not null);
        if (building is null && site is not null)
        {
            var planned = sim.Data.Buildings[site.DefId];
            _set.Texture(_icon, Icons.Building(planned.Id, planned.Category.ToString(), 26));
            _set.Text(_name, planned.Name);
            _set.Text(_category, planned.Category.ToString());
            _levels.Set(0, planned.MaxLevel);
            _set.Text(_levelText, "under construction");
            _set.Tooltip(_levels, "Not built yet: see the Construction tab.");
            _set.Tooltip(_levelText, "Not built yet: see the Construction tab.");
            _set.Visible(_workersHead, false);
            _set.Visible(_workers, false);
            _set.Visible(_outputRow, false);
            _set.Visible(_chips, false);
            if (_tabs.Selected != _extras[0].Tab) _tabs.Select(_extras[0].Tab);
            FitHeight();
            return;
        }
        if (building is null)
        {
            Visible = false;
            return;
        }

        var def = sim.Data.Buildings[building.DefId];
        var level = def.Level(building.Level);
        _set.Texture(_icon, Icons.Building(def.Id, def.Category.ToString(), 26));
        _set.Text(_name, def.Name);
        _set.Text(_category, def.Category.ToString());
        _levels.Set(building.Level, def.MaxLevel);
        _set.Text(_levelText, $"L{building.Level}/{def.MaxLevel}");
        var levelTip = $"Level {building.Level} of {def.MaxLevel}." + (building.Level < def.MaxLevel ? " Upgrade for more workers and output (Upgrade tab)." : " Fully upgraded.");
        _set.Tooltip(_levels, levelTip);
        _set.Tooltip(_levelText, levelTip);
        _set.Text(_purpose, BuildMenu.Purpose(sim, def));

        RefreshDamage(sim, building);
        RefreshWorkers(sim, building, def, level.WorkerSlots);
        _set.Text(_output, OutputText(sim, building, def, level, out var tip));
        _set.Tooltip(_output, tip);
        _set.Visible(_outputRow, _output.Text.Length > 0);
        RefreshNight(sim, building, def);
        RefreshRepair(sim, building, def);
        RefreshPower(sim, building);
        RefreshUpgrade(sim, building, def);
        _set.Visible(_chips, _chips.GetChildren().OfType<Control>().Any(c => c.Visible));
        FitHeight();
    }

    /// <summary>
    /// The panel is as tall as its content up to <see cref="MaxHeight"/>; past that the tab body scrolls and the header
    /// stays put. Set only when it changes, so a steady panel never relayouts.
    /// </summary>
    private void FitHeight()
    {
        var page = _tabs.Selected < _pageList.Count ? _pageList[_tabs.Selected] : null;
        var want = page?.GetCombinedMinimumSize().Y ?? 0;
        var chrome = GetCombinedMinimumSize().Y - _scroll.CustomMinimumSize.Y;
        var room = MaxHeight?.Invoke() is { } max and > 0 ? max - chrome : want;
        var height = Mathf.Round(Math.Max(60, Math.Min(want, room)));
        if (Math.Abs(_scroll.CustomMinimumSize.Y - height) >= 1) _scroll.CustomMinimumSize = new Vector2(0, height);
    }

    private void SetChip(PanelContainer chip, bool shown, string text = "", Color? color = null, string tooltip = "")
    {
        _set.Visible(chip, shown);
        if (!shown) return;
        var row = chip.GetChild(0);
        var label = row.GetChild<Label>(1);
        var c = color ?? Palette.Text;
        _set.Text(label, text);
        _set.FontColor(label, c);
        _set.Modulate(row.GetChild<TextureRect>(0), c == Palette.Text ? Palette.TextMuted : c);
        _set.Tooltip(chip, tooltip + "\nClick for the details.");
    }

    private void RefreshDamage(ColonySim sim, Building building)
    {
        var raids = sim.Raids;
        var damage = raids is null ? 0 : Remnants.Core.Raids.RaidSystem.Damage(sim.State, building);
        _set.Visible(_damageBox, damage > 0);
        if (raids is null || damage <= 0)
        {
            SetChip(_damageChip, false);
            return;
        }
        var max = Remnants.Core.Raids.RaidSystem.BuildingHp(raids, building);
        var wrecked = Remnants.Core.Raids.RaidSystem.IsRubble(sim, building);
        var color = wrecked ? Palette.Danger : Palette.Warn;
        SetChip(_damageChip, true, wrecked ? "Wrecked" : $"Damaged {max - damage:0}/{max:0}", color,
            wrecked ? "Wrecked in a raid: nobody can work here until it is repaired." : "Damaged in a raid: it still works; repair it before the next raid.");
        _set.Text(_damageTitle, wrecked ? "WRECKED: REPAIR IT" : "DAMAGED IN A RAID");
        _set.FontColor(_damageTitle, color);
        _set.Tooltip(_damageTitle, wrecked ? "Nobody can work or be assigned here until it is repaired." : "It still works; repair it before the next raid.");
        _damageHp.Set((max - damage) / max, $"{max - damage:0}/{max:0}");
        var order = sim.State.Defense.Repairs.FirstOrDefault(r => r.Target == Remnants.Core.Raids.RepairTarget.Building && r.TargetId == building.Id);
        var cost = Remnants.Core.Raids.RaidSystem.RepairCost(sim, raids, Remnants.Core.Raids.RepairTarget.Building, building.Id);
        _set.Visible(_damageStatus, order is not null);
        _set.Visible(_damageCancel, order is not null);
        _set.Visible(_damageRepair, order is null);
        _set.Visible(_damageCost, order is null);
        if (order is not null)
        {
            var place = sim.State.Defense.Repairs.IndexOf(order) + 1;
            _set.Text(_damageStatus, order.Paid ? $"Being repaired ({order.Progress:0} HP done)" : $"Repair queued (#{place})");
            _set.FontColor(_damageStatus, Palette.Good);
            return;
        }
        SetText(_damageRepair, wrecked ? "Repair" : "Queue repair");
        _damageRepair.ThemeTypeVariation = wrecked ? "Primary" : "";
        _damageCost.Set(RaidText.Costs(sim, cost));
    }

    private void QueueRepair()
    {
        if (BuildingId is { } id) _session.Execute(new Remnants.Core.Raids.QueueRepair(Remnants.Core.Raids.RepairTarget.Building, id));
    }

    private void CancelRepair()
    {
        if (BuildingId is not { } id) return;
        var order = _session.Sim.State.Defense.Repairs.FirstOrDefault(r => r.Target == Remnants.Core.Raids.RepairTarget.Building && r.TargetId == id);
        if (order is not null) _session.Execute(new Remnants.Core.Raids.CancelRepair(order.Id));
    }

    private void RefreshNight(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def)
    {
        var rules = sim.Data.Rules.DayNight;
        var shown = rules.Enabled && def.Job is not null;
        _set.Visible(_nightBox, shown);
        SetChip(_nightChip, shown && building.NightShift, "Night shift", Palette.Night, "Workers here keep going at night, at lower output and lost sleep.");
        if (!shown) return;
        var lit = sim.CanRunNightShift(building);
        var cut = (int)Math.Round((1 - rules.NightShift.OutputMultiplier) * 100);
        _nightShift.SetPressedNoSignal(building.NightShift);
        SetText(_nightShift, building.NightShift ? "On" : "Off");
        _nightShift.Disabled = !lit && !building.NightShift;
        _set.Tooltip(_nightShift, ColonyText.NightShiftTip(sim, building));
        _set.Text(_nightNote, !lit ? $"Too dark: {def.Name} works outdoors. Fit a Floodlight or reach level {rules.LitMinLevel}."
            : building.NightShift ? $"Working nights at −{cut}% output; workers lose sleep and mood."
            : $"Workers sleep. On: −{cut}% output at night, costs mood and sleep.");
        _set.FontColor(_nightNote, !lit ? Palette.Warn : building.NightShift ? Palette.Night : Palette.TextMuted);

        var needFlood = def.Outdoor && !lit;
        _set.Visible(_floodRow, needFlood);
        if (!needFlood) return;
        var cost = rules.FloodlightCost;
        _floodCost.Set(cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key))));
        _set.Tooltip(_flood, $"A lamp mast that lights {def.Name} for a night shift ({ColonyText.Cost(cost, sim.Data)}).");
    }

    private void RefreshRepair(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def)
    {
        _set.Visible(_repairBox, building.Damaged);
        var rules = sim.Data.Weather.Repair;
        SetChip(_lightningChip, building.Damaged, $"Struck: {rules.DamagedOutput * 100:0}% output", Palette.Warn,
            $"Lightning struck the {def.Name}: it works at {rules.DamagedOutput * 100:0}% until repaired.");
        if (!building.Damaged) return;
        _set.Text(_repairNote, $"Lightning struck the {def.Name}: it works at {rules.DamagedOutput * 100:0}% until repaired.");
        _repairCost.Set(rules.Cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key))));
        _repair.Disabled = !_repairCost.Affordable;
        _set.Tooltip(_repair, $"Repair the {def.Name} ({ColonyText.Cost(rules.Cost, sim.Data)}).");
    }

    private void Repair()
    {
        if (BuildingId is { } id) _session.Execute(new RepairBuilding(id));
    }

    private void RefreshPower(ColonySim sim, Building building)
    {
        var nodes = sim.PowerNodes().Where(n => n.BuildingId == building.Id).ToList();
        _set.Visible(_powerBox, nodes.Count > 0);
        if (nodes.Count == 0)
        {
            SetChip(_powerChip, false);
            return;
        }
        var on = nodes[0].On;
        var lines = nodes.Select(n => PowerText.Line(sim, n)).ToList();
        if (sim.Data.Buildings[building.DefId].Category == Remnants.Core.Data.BuildingCategory.Power && sim.State.Power is var p)
            lines.Add($"Grid: {p.Used:0.#} used of {p.Capacity:0.#}" + (sim.PowerStorageCapacity > 0 ? $", batteries {p.Stored:0.#}/{sim.PowerStorageCapacity:0.#}" : ""));
        _set.Text(_powerLines, string.Join("\n", lines));
        var unfed = nodes.Any(n => n.Role == PowerRole.Consumer && n.On && !n.Fed && !PowerText.Idle(sim, n));
        _set.FontColor(_powerLines, !on ? Palette.TextMuted : unfed ? Palette.Warn : Palette.Text);
        var main = nodes[0];
        var (chip, color) = main.Role switch
        {
            _ when !on => ("Power off", Palette.TextDim),
            PowerRole.Generator => ($"Makes {main.Amount:0.#}", Palette.Warn),
            PowerRole.Storage => ($"Battery {sim.State.Power.Stored:0}/{sim.PowerStorageCapacity:0}", Palette.Warn),
            _ when PowerText.Idle(sim, main) => ("Power idle", Palette.TextMuted),
            _ => main.Fed ? ("Fed", Palette.Good) : ("Not fed", Palette.Danger),
        };
        SetChip(_powerChip, true, chip, color, string.Join("\n", lines));
        _powerSwitch.SetPressedNoSignal(on);
        SetText(_powerSwitch, on ? "Switch: on the grid" : "Switch: off");
        _set.Tooltip(_powerSwitch, on
            ? "Switch it off: a consumer stops drawing (and loses its bonus), a generator stays cold and burns no fuel."
            : "Switch it back on the grid.");
    }

    private void TogglePower()
    {
        if (BuildingId is not { } id) return;
        var on = _session.Sim.PowerNodes().FirstOrDefault(n => n.BuildingId == id)?.On ?? true;
        _session.Execute(new SetPower(id, !on));
    }

    private void ToggleNightShift()
    {
        if (BuildingId is { } id && _session.Sim.State.FindBuilding(id) is { } b) _session.Execute(new SetNightShift(id, !b.NightShift));
    }

    private void FitFloodlight()
    {
        if (BuildingId is { } id) _session.Execute(new InstallFloodlight(id));
    }

    private void RefreshWorkers(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def, int slots)
    {
        var workers = sim.State.WorkersAt(building.Id).OrderBy(s => s.Id).ToList();
        _set.Visible(_workersHead, def.Job is not null);
        _set.Visible(_workers, def.Job is not null);
        if (def.Job is null) return;
        _set.Text(_workersLabel, $"WORKERS {workers.Count}/{slots}");
        _set.Tooltip(_workersLabel, $"{workers.Count} of {slots} slots filled. Each worker adds output by their {sim.Data.Jobs[def.Job].Skill} skill and attributes.");
        _workers.WorkerTip = w => $"{w.Name}: {ColonyText.Job(sim, w)}\n{JobFit(sim.Data.Jobs[def.Job], w).Detail}";
        _workers.Set(workers, slots);
    }

    private static string OutputText(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def, Remnants.Core.Data.BuildingLevelDef level, out string tip)
    {
        tip = "";
        if (def.Produces is { } res)
        {
            var name = sim.Data.Resources[res].Name;
            var total = sim.OutputPerDay(building);
            tip = $"{def.BaseOutput:0.##} {name}/h per worker at full skill × level {Fmt.Factor(level.OutputMultiplier)}, on shift\n" +
                  string.Join("\n", sim.State.WorkersAt(building.Id).Select(w =>
                      $"  {w.Name}: {JobOutput.PerTick(def, building.Level, sim.Data.Jobs[def.Job!], w, sim.Data.Rules.Output, sim.Data, WorkTools.Quality(sim.State, sim.Data, sim.Data.Jobs[def.Job!], w)) * Schedule.ShiftHoursPerDay(w, building, def, sim.Data.Rules):0.#}/day")) +
                  "\nPer day counts the shift hours (and the Night Shift at its rate). Open a worker's Overview for the full formula.";
            return $"Makes about {total:0.#} {name} per day";
        }
        if (Harvesting.Current(sim.Data, building) is { } harvest)
        {
            tip = $"{harvest.Name}: {harvest.Description}\n" + string.Join("\n", sim.State.WorkersAt(building.Id).Select(w =>
                $"  {w.Name}: {JobOutput.PerTick(def, building.Level, sim.Data.Jobs[def.Job!], w, sim.Data.Rules.Output, sim.Data) * Schedule.ShiftHoursPerDay(w, building, def, sim.Data.Rules) * harvest.Yield:0.#} items/day"));
            return $"{harvest.Name}: about {sim.OutputPerDay(building):0.#} food per day";
        }
        if (def.Id == sim.Data.Food?.Rules.CellarBuilding)
            return $"Produce and meat age at {Pantry.CellarRate(sim.State, sim.Data) * 100:0}% speed";
        if (level.Beds > 0) return $"Sleeps {level.Beds}";
        if (level.Patients > 0) return $"{level.Patients} patient beds";
        return "";
    }

    private void RefreshUpgrade(ColonySim sim, Building building, Remnants.Core.Data.BuildingDef def)
    {
        var next = building.Level + 1;
        _tabs.SetTitle(_upgradeTab, next > def.MaxLevel ? "Upgrade" : $"Upgrade · L{next}");
        if (sim.Site(building.Id) is { } upgrading)
        {
            // Shown on the Construction tab; the building keeps working at its level meanwhile.
            _set.Text(_upgradeTitle, $"UPGRADING TO LEVEL {upgrading.Level}: see the Construction tab");
            _set.Visible(_upgradeBody, false);
            return;
        }
        if (next > def.MaxLevel)
        {
            _set.Text(_upgradeTitle, "MAX LEVEL REACHED");
            _set.Visible(_upgradeBody, false);
            return;
        }
        _set.Visible(_upgradeBody, true);
        var current = def.Level(building.Level);
        var nextLevel = def.Level(next);
        _set.Text(_upgradeTitle, $"LEVEL {building.Level} → {next}");
        var gains = new List<string>();
        if (nextLevel.WorkerSlots > current.WorkerSlots) gains.Add($"Workers {current.WorkerSlots} → {nextLevel.WorkerSlots}");
        if (nextLevel.OutputMultiplier > current.OutputMultiplier) gains.Add($"Output {Fmt.Factor(current.OutputMultiplier)} → {Fmt.Factor(nextLevel.OutputMultiplier)}");
        if (nextLevel.Beds > current.Beds) gains.Add($"Beds {current.Beds} → {nextLevel.Beds}");
        if (nextLevel.Patients > current.Patients) gains.Add($"Patient beds {current.Patients} → {nextLevel.Patients}");
        if (def.Outdoor && building.Level < sim.Data.Rules.DayNight.LitMinLevel && next >= sim.Data.Rules.DayNight.LitMinLevel) gains.Add("Lit for night shifts");
        if (gains.Count == 0) gains.Add("A sturdier building");
        SetGains(gains);

        var cost = nextLevel.Cost;
        _costs.Set(cost.Select(kv => (kv.Key, sim.Data.Resources[kv.Key].Name, kv.Value, sim.State.Stockpile.Get(kv.Key))));
        _set.Visible(_buildTime.GetParent<Control>(), sim.Data.Rules.Construction.Enabled);
        _set.Text(_buildTime, $"Build time {SiteText.Estimate(sim.BuildHours(def, next), sim.Data.Rules)} with two builders; it keeps working at level {building.Level} meanwhile.");

        var cap = sim.MaxUpgradeLevel(def);
        var capped = def.Id != sim.Data.Rules.TownHallBuildingId && next > cap;
        var gate = Remnants.Core.Schematics.Schematics.LevelUnlocked(sim, def.Id, next) ? null : sim.Data.Schematics.GateOfBuildingLevel(def.Id, next);
        var gateName = gate is null ? null : sim.Data.Schematics.All[gate].Name;
        _set.Visible(_blocker, def.Id != sim.Data.Rules.TownHallBuildingId || gate is not null);
        _set.Visible(_blockerLink, gateName is not null);
        _set.Text(_blocker, gateName is not null ? $"Needs the {gateName} schematic."
            : capped ? $"Town Hall level {sim.TownHallLevel} allows up to level {cap}: upgrade the Town Hall first."
            : $"Town Hall level {sim.TownHallLevel} allows up to level {cap}.");
        _set.FontColor(_blocker, capped || gate is not null ? Palette.Danger : Palette.TextMuted);
        // Core decides; a refused upgrade explains itself in the log. The button only signals what is likely.
        _upgrade.Disabled = capped || gate is not null || !_costs.Affordable;
        SetText(_upgrade, $"Upgrade to level {next}");
        var why = gateName is not null ? $"Locked: needs the {gateName} schematic."
            : capped ? "Locked: needs a higher Town Hall level."
            : !_costs.Affordable ? "Not enough resources yet (see the red costs)." : "";
        // A lock is already said in red above the button; only the shortfall needs saying under it.
        _set.Text(_upgradeWhy, why);
        _set.FontColor(_upgradeWhy, Palette.Danger);
        _set.Visible(_upgradeWhy, why.Length > 0 && gateName is null && !capped);
        _set.Tooltip(_upgrade, why.Length > 0 ? why : $"Upgrade to level {next}.");
    }

    private void SetGains(List<string> gains)
    {
        while (_gains.GetChildCount() < gains.Count)
        {
            var row = Widgets.Row(Space.Sm);
            row.AddChild(Icons.Rect(Icons.Get("ui_chevron_up", 14), 14, Palette.Good));
            row.AddChild(Widgets.Label(""));
            _gains.AddChild(row);
        }
        for (var i = 0; i < _gains.GetChildCount(); i++)
        {
            var row = _gains.GetChild<HBoxContainer>(i);
            _set.Visible(row, i < gains.Count);
            if (i < gains.Count) _set.Text(row.GetChild<Label>(1), gains[i]);
        }
    }

    private static void SetText(Button button, string text)
    {
        if (button.Text != text) button.Text = text;
    }

    private void Upgrade()
    {
        if (BuildingId is { } id) _session.Execute(new Upgrade(id));
    }
}
