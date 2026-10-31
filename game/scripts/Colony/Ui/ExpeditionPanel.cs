using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Help;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Plans and launches an expedition as five steps, one at a time, so the planner never shows everything at once:
/// <list type="number">
/// <item>Destination: a known zone (or the World map), what the colony knows of it (the full intel behind an expander),
/// when the party arrives and the weather there.</item>
/// <item>Party: the adults at home as tiles (click to take or leave), readiness warnings, a one-line field profile each.</item>
/// <item>Gear: per picked survivor, the four slots, the pack and the field profile that gear gives; a Gear Bench link.</item>
/// <item>Vehicle &amp; supplies: what each colony vehicle makes of the trip (or on foot), and the rations.</item>
/// <item>Review: one compact summary with every warning (each links to its step), then Launch.</item>
/// </list>
/// A slim summary bar (zone, party, travel, food, fuel, warnings) stays under the steps on every one. Keys while it is
/// open: 1–5 pick a step, Tab / Shift+Tab next / previous, Enter next (Launch on the last). "Repeat last" refills the
/// last launched plan (zone, party, loadouts, vehicle, rations, wait) and goes to Review. While a party is out the panel
/// shows who and where they are and offers to play (or resume) the mission, or to take it online. Core validates the
/// launch. Per tick only the summary and the step on screen refresh; tiles and pickers are rebuilt only when what they
/// list changed.
/// </summary>
public partial class ExpeditionPanel : PanelContainer
{
    private sealed class Loadout
    {
        public Dictionary<EquipSlot, string> Equipment { get; } = new();
        public List<string> Pack { get; } = new();
    }

    /// <summary>The last launched plan, for "Repeat last" (kept for the session, not saved).</summary>
    private sealed record LastPlan(string ZoneId, IReadOnlyList<(int Id, Dictionary<EquipSlot, string> Equipment, List<string> Pack)> Members,
        int? VehicleId, string? Ration, int Wait);

    /// <summary>The last plan per colony (a loaded save or a new game is another sim and starts without one).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ColonySim, LastPlan> Last = new();

    private LastPlan? _last => Last.TryGetValue(_session.Sim, out var plan) ? plan : null;

    /// <summary>Something modal is over the planner (set by the HUD): its keys wait.</summary>
    public Func<bool>? Blocked { get; init; }

    private static readonly EquipSlot[] Slots = [EquipSlot.Weapon, EquipSlot.Body, EquipSlot.Head, EquipSlot.Tool];
    private static readonly string[] StepNames = ["Destination", "Party", "Gear", "Vehicle & supplies", "Review"];
    private const int Destination = 0, Party = 1, Gear = 2, Supplies = 3, Review = 4;
    private const float Width = 640, BodyHeight = 340;

    private readonly ColonySession _session;
    private readonly Dictionary<int, Loadout> _picked = new();
    private int _step;

    private readonly RichTextLabel _away = Ui.Rich();
    private readonly Button _play;
    private readonly Button _online;
    private readonly Control _planner = Widgets.Column(8);
    private readonly Button _repeat;
    private readonly Button[] _tabs = new Button[StepNames.Length];
    private readonly Control[] _steps = new Control[StepNames.Length];
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _launch;
    private readonly Button _launchOnline;
    private readonly Label _blocker = Widgets.Label("", "Small", Palette.Danger);

    // The summary bar.
    private readonly Unchanged _set = new();
    private readonly Label _sumZone = Widgets.Label("", "", Palette.Text, 14);
    private readonly Label _sumParty = Widgets.Label("", "Number", Palette.Text, 15);
    private readonly Label _sumTravel = Widgets.Label("", "", Palette.Text, 14);
    private readonly Label _sumFood = Widgets.Label("", "Number", Palette.Text, 15);
    private readonly Label _sumFuel = Widgets.Label("", "Number", Palette.Text, 15);
    private readonly Control _fuelChip;
    private readonly Button _sumWarnings;

    /// <summary>Asks the screen to switch to the scavenge run.</summary>
    public event Action? PlayPressed;

    /// <summary>Asks the screen to open the world map.</summary>
    public event Action? WorldMapPressed;

    /// <summary>Asks the screen to take the waiting party into an online (co-op or versus) mission.</summary>
    public event Action? OnlinePressed;

    /// <summary>Asks the screen to open the Gear Bench on this item (from the Gear step).</summary>
    public event Action<string>? GearBenchPressed;

    public ExpeditionPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(Width, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this, 8);
        var title = Ui.TitleRow("Expedition", "ui_expedition", Toggle, Hotkeys.KeyName("open_expedition"));
        _repeat = Widgets.IconButton("ui_continue", "Repeat last", "Fill in the last expedition you launched (zone, party, gear, vehicle, rations) and go to Review",
            RepeatLast, variation: "Flat");
        title.AddChild(_repeat);
        title.MoveChild(_repeat, title.GetChildCount() - 2);
        box.AddChild(title);

        // A party out: who, where, and Play / Online once they are there.
        box.AddChild(_away);
        _play = Widgets.IconButton("ui_play", "Play mission", "Go to the zone and play the run", () => PlayPressed?.Invoke(), variation: "Primary");
        _online = Widgets.IconButton("stat_ally", "Online / Co-op",
            "Bring this party to a multiplayer mission (host or join a co-op or versus game); the result comes home here",
            () => OnlinePressed?.Invoke());
        var playRow = Widgets.Row(6);
        foreach (var b in new[] { _play, _online })
        {
            b.CustomMinimumSize = new Vector2(0, 38);
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            playRow.AddChild(b);
        }
        _play.Visible = _online.Visible = false;
        box.AddChild(playRow);

        box.AddChild(_planner);
        // The step strip.
        var strip = Widgets.Row(2);
        var group = new ButtonGroup();
        for (var i = 0; i < StepNames.Length; i++)
        {
            var step = i;
            var tab = new Button
            {
                Text = $"{i + 1}  {StepNames[i]}",
                ThemeTypeVariation = "Tab",
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                TooltipText = Widgets.Tip(StepNames[i], $"{i + 1}"),
                ExpandIcon = false,
            };
            tab.AddThemeFontSizeOverride("font_size", 13);
            tab.Pressed += () => Go(step);
            _tabs[i] = Motion.OnPress(tab);
            strip.AddChild(tab);
        }
        _planner.AddChild(strip);

        // The step bodies: a fixed height so switching steps never moves the summary or the buttons.
        var body = new PanelContainer { ThemeTypeVariation = "Inset", CustomMinimumSize = new Vector2(0, BodyHeight) };
        _planner.AddChild(body);
        // A container sizes the steps (all in one place, one shown); anchors here drifted with the slide-in.
        var stack = new MarginContainer { CustomMinimumSize = new Vector2(0, BodyHeight - 16) };
        body.AddChild(stack);
        _steps[Destination] = DestinationStep();
        _steps[Party] = PartyStep();
        _steps[Gear] = GearStep();
        _steps[Supplies] = SuppliesStep();
        _steps[Review] = ReviewStep();
        foreach (var content in _steps)
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
            content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scroll.AddChild(content);
            stack.AddChild(scroll);
        }

        // The summary bar.
        var bar = Widgets.Row(4);
        bar.AddChild(SummaryChip("ui_world_map", _sumZone, "expeditions", expand: true));
        bar.AddChild(SummaryChip("stat_pop", _sumParty, "expeditions"));
        bar.AddChild(SummaryChip("stat_travel", _sumTravel, "travel"));
        bar.AddChild(SummaryChip("res_food", _sumFood, "rations"));
        _fuelChip = SummaryChip("res_fuel", _sumFuel, "vehicles");
        bar.AddChild(_fuelChip);
        _sumWarnings = Widgets.IconButton("stat_warning", "0", "Warnings: click to go to the first one", JumpToWarning, variation: "Flat");
        _sumWarnings.CustomMinimumSize = new Vector2(52, 30);
        bar.AddChild(_sumWarnings);
        _planner.AddChild(bar);

        // Footer: Back · [why not] · Next / Launch.
        var footer = Widgets.Row(6);
        _back = Widgets.IconButton("ui_chevron_left", "Back", "The step before", () => Go(_step - 1), "Shift+Tab");
        footer.AddChild(_back);
        _blocker.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _blocker.HorizontalAlignment = HorizontalAlignment.Right;
        _blocker.ClipText = true;
        footer.AddChild(_blocker);
        _launchOnline = Widgets.IconButton("stat_ally", "Online",
            "Send the party out as usual (adults only, Town Hall cap, gear and rations from the colony) and take it into a multiplayer mission",
            () => Launch(online: true), variation: "Flat");
        footer.AddChild(_launchOnline);
        _next = Widgets.IconButton("ui_chevron_right", "Next", "The next step", () => Go(_step + 1), "Enter / Tab", "Primary");
        _launch = Widgets.IconButton("ui_expedition", "Launch expedition", "Send the party out", () => Launch(online: false), "Enter", "Primary");
        foreach (var b in new[] { _back, _next, _launch, _launchOnline }) b.CustomMinimumSize = new Vector2(b.CustomMinimumSize.X, 36);
        footer.AddChild(_next);
        footer.AddChild(_launch);
        _planner.AddChild(footer);
        Go(Destination, animate: false);
    }

    private Control SummaryChip(string icon, Label label, string topic, bool expand = false)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Chip", SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.Fill };
        var row = Widgets.Row(4);
        row.AddChild(Icons.Rect(Icons.Get(icon, 14), 14, Palette.TextMuted));
        label.ClipText = expand;
        label.SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.Fill;
        label.CustomMinimumSize = new Vector2(expand ? 60 : 0, 0);
        row.AddChild(label);
        row.AddChild(new InfoTip(topic, HelpContext));
        chip.AddChild(row);
        return chip;
    }

    /// <summary>What the help popovers on this panel read: the colony and the zone being planned.</summary>
    private HelpContext HelpContext() => new()
    {
        Colony = _session.Sim,
        Zone = SelectedZone is { } z ? _session.Sim.Scavenge?.Zones.GetValueOrDefault(z) : null,
        Survivor = _gearFor is { } id ? _session.Sim.State.FindSurvivor(id) : null,
    };

    public void Toggle()
    {
        Motion.Toggle(this);
        Refresh();
    }

    /// <summary>The story mission this party is for (from its briefing), or null for a plain expedition.</summary>
    public string? MissionId { get; private set; }

    /// <summary>Opens the planner for a story mission: its zone, its party size, and Launch sends it on the mission.</summary>
    public void PlanForMission(string missionId)
    {
        if (_session.Sim.Story?.Mission(missionId) is not { Zone: { } zone }) return;
        PlanFor(zone);
        MissionId = missionId;
        _repeat.Visible = false;
        Go(Party);
    }

    /// <summary>Opens the planner on a zone (the world map's "Plan expedition").</summary>
    public void PlanFor(string zoneId)
    {
        MissionId = null;
        _repeatNote = null;
        if (!Visible) Motion.Reveal(this);
        Refresh();
        var index = _zoneIds.IndexOf(zoneId);
        if (index < 0) return;
        _zone.Select(index);
        Go(_picked.Count == 0 ? Party : Review);
    }

    /// <summary>Debug / screenshots: tick these survivors in the party picker.</summary>
    public void Pick(IEnumerable<int> survivorIds)
    {
        foreach (var id in survivorIds) _picked.TryAdd(id, new Loadout());
        _gearFor ??= _picked.Keys.Min();
        Changed();
    }

    /// <summary>Debug / screenshots: pick a vehicle by tier.</summary>
    public void PickVehicle(string tierId)
    {
        RefreshVehicles(_session.Sim);
        var v = _session.Sim.State.Vehicles.Find(x => x.TierId == tierId && !x.Away);
        if (v is null) return;
        _vehicle.Select(_vehicleIds.IndexOf(v.Id));
        Go(Supplies);
    }

    /// <summary>Debug / screenshots: show a step (0–4).</summary>
    public void ShowStep(int step) => Go(step);

    /// <summary>The line icon for an empty equipment slot.</summary>
    public static string SlotIcon(EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => "item_weapon",
        EquipSlot.Body => "item_armor",
        EquipSlot.Head => "body_head",
        _ => "item_tool",
    };

    private void Go(int step, bool animate = true)
    {
        step = Math.Clamp(step, 0, StepNames.Length - 1);
        var from = _step;
        _step = step;
        for (var i = 0; i < _steps.Length; i++)
        {
            var scroll = (Control)_steps[i].GetParent();
            if (i == step)
            {
                if (animate && from != step) Motion.Reveal(scroll, new Vector2(step > from ? 12 : -12, 0));
                else scroll.Visible = true;
            }
            else scroll.Visible = false;
            _tabs[i].SetPressedNoSignal(i == step);
        }
        RefreshStep();
        RefreshSummary();
    }

    /// <summary>Something the player changed: the step on screen and the summary, now (no rebuild of other steps).</summary>
    private void Changed()
    {
        _repeatNote = null;
        RefreshStep(force: true);
        RefreshSummary();
    }

    public void Refresh()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        if (sim.Scavenge is null) return;
        var out_ = RefreshAway(sim);
        _planner.Visible = !out_;
        _repeat.Visible = !out_ && MissionId is null;
        _repeat.Disabled = _last is null;
        if (out_) return;
        RefreshZones(sim);
        // Drop picks who can no longer go (hurt into the Med Bay, pregnant, left).
        var candidates = Candidates(sim).Select(s => s.Id).ToHashSet();
        foreach (var id in _picked.Keys.Where(id => !candidates.Contains(id)).ToList()) _picked.Remove(id);
        if (_gearFor is { } g && !_picked.ContainsKey(g)) _gearFor = _picked.Keys.Cast<int?>().FirstOrDefault();
        ClampLoadouts(sim);
        RefreshStep();
        RefreshSummary();
    }

    /// <summary>A party out: who and where, Play / Online. Returns whether one is out.</summary>
    private bool RefreshAway(ColonySim sim)
    {
        var expedition = sim.State.Expedition;
        _play.Visible = _online.Visible = _away.Visible = expedition is not null;
        if (expedition is null) return false;
        var names = expedition.Members.Select(m => sim.State.FindSurvivor(m.SurvivorId)?.Name ?? "?");
        var phase = expedition.PhaseAt(sim.State.Tick);
        var state = phase switch
        {
            ExpeditionPhase.Outbound => $"on the road, arriving in {expedition.ArrivalTick - sim.State.Tick} h",
            ExpeditionPhase.Returning => $"heading home, back in {expedition.ReturnTick - sim.State.Tick} h",
            _ => expedition.Scavenge is { } run ? $"round {run.Round}" : "in the zone, not started",
        };
        var text = $"[b]{sim.Scavenge!.Zones[expedition.ZoneId].Name}[/b]: {string.Join(", ", names)} ({state}).";
        if (_away.Text != text) _away.Text = text;
        _play.Disabled = phase != ExpeditionPhase.InZone;
        // A run already started locally stays local; a fresh one can go online instead.
        _online.Disabled = phase != ExpeditionPhase.InZone || expedition.Scavenge is not null;
        return true;
    }

    /// <summary>
    /// Takes out of the loadouts what the stores no longer hold (sold, used, or modded at the Gear Bench, which gives the
    /// item a new id), packs first, so Launch never fails on missing items; the Gear step then shows the empty slot.
    /// </summary>
    private void ClampLoadouts(ColonySim sim)
    {
        var used = new Dictionary<string, int>();
        foreach (var l in _picked.Values)
            foreach (var item in l.Equipment.Values.Concat(l.Pack))
                used[item] = used.GetValueOrDefault(item) + 1;
        foreach (var (item, count) in used)
        {
            var extra = count - sim.State.Inventory.Count(item);
            foreach (var l in _picked.Values)
            {
                while (extra > 0 && l.Pack.Remove(item)) extra--;
            }
            foreach (var l in _picked.Values)
                foreach (var slot in l.Equipment.Where(kv => kv.Value == item).Select(kv => kv.Key).ToList())
                    if (extra > 0 && l.Equipment.Remove(slot)) extra--;
        }
    }

    private static IEnumerable<Survivor> Candidates(ColonySim sim) =>
        sim.State.Present.Where(s => !s.IsChild && s.PatientAtBuildingId is null && s.Pregnancy is null).OrderBy(s => s.Id);

    /// <summary>How many of an item are still free, after what the other picked members already take.</summary>
    private int Free(ColonySim sim, string itemId, Loadout except) =>
        sim.State.Inventory.Count(itemId)
        - _picked.Values.Where(l => l != except).Sum(l => l.Equipment.Values.Count(v => v == itemId) + l.Pack.Count(v => v == itemId));

    // ── Warnings, summary, footer ────────────────────────────────────────────────────────────────────────────────────

    private sealed record Warning(string Text, int Step, bool Blocks);

    /// <summary>Everything wrong or worth knowing about the plan, with the step it belongs to; blockers stop the launch.</summary>
    private List<Warning> Warnings(ColonySim sim)
    {
        var list = new List<Warning>();
        if (SelectedZone is not { } zoneId)
        {
            list.Add(new("Pick a destination", Destination, true));
            return list;
        }
        var arrival = sim.PlanArrival(zoneId, WaitFor, SelectedVehicle);
        if (arrival.Blocked)
            list.Add(new((arrival.Travel?.Modifiers ?? []).FirstOrDefault(t => t.Blocks)?.Reason ?? "The route is closed", Destination, true));
        if (arrival.Phase == DayPhase.Night) list.Add(new("Arrives at night: less sight, more Husks", Destination, false));
        var cap = Cap(sim);
        if (_picked.Count == 0) list.Add(new("Pick at least one survivor", Party, true));
        if (_picked.Count > cap) list.Add(new($"Party of {_picked.Count}, room for {cap}", Party, true));
        list.AddRange(Readiness(sim).Select(w => new Warning(w.Text, Party, false)));
        foreach (var (id, loadout) in _picked)
            if (!loadout.Equipment.ContainsKey(EquipSlot.Weapon) && sim.State.FindSurvivor(id) is { } s)
                list.Add(new($"{s.Name} has no weapon", Gear, false));
        if (SelectedVehicle is { } vid && sim.PlanTravel(zoneId, vid).Blocked is { } why) list.Add(new(why, Supplies, true));
        if (_picked.Count > 0 && _bill is { } bill)
        {
            if (!bill.Enough) list.Add(new($"Not enough food: {bill.Covered:0.#} of {_food:0.#}", Supplies, true));
            if (bill.RiskShare > 0) list.Add(new($"{bill.RiskShare * 100:0}% of the rations may make someone sick", Supplies, false));
        }
        return list;
    }

    /// <summary>A story mission sets its own party size and goes on foot; otherwise the vehicle's seats cap the Town Hall's party.</summary>
    private int Cap(ColonySim sim) =>
        MissionId is { } m ? Math.Max(sim.MaxPartySize, sim.Story?.Mission(m)?.MaxParty ?? 0) : sim.MaxPartySizeWith(SelectedVehicle);

    private IReadOnlyList<Remnants.Core.Story.ReadinessWarning> Readiness(ColonySim sim) =>
        MissionId is { } wm && sim.Story?.Mission(wm) is { } wdef
            ? Remnants.Core.Story.MissionReadiness.Check(sim, wdef, Members())
            : _picked.Keys.Select(sim.State.FindSurvivor).OfType<Survivor>().Where(s => s.IsInjured || s.Health < 70)
                .Select(s => new Remnants.Core.Story.ReadinessWarning($"hurt:{s.Id}", Remnants.Core.Story.MissionReadiness.Wound(s))).ToList();

    private List<ExpeditionMember> Members() =>
        _picked.OrderBy(kv => kv.Key).Select(kv => new ExpeditionMember(kv.Key, new(kv.Value.Equipment), new(kv.Value.Pack))).ToList();

    private double _food;
    private Remnants.Core.Colony.RationBill? _bill;
    private List<Warning> _warnings = new();

    private void RefreshSummary()
    {
        var sim = _session.Sim;
        if (sim.Scavenge is not { } data || !_planner.Visible) return;
        var zoneId = SelectedZone;
        var vehicle = SelectedVehicle;
        var trip = zoneId is null ? null : sim.PlanTravel(zoneId, vehicle);
        var arrival = zoneId is null ? null : sim.PlanArrival(zoneId, WaitFor, vehicle);
        var perMember = trip is not null
            ? WorldMap.RationsPerMember(sim.Data, trip.MovingTicks / (double)sim.Data.Rules.TicksPerDay) + (arrival?.ExtraRationsPerMember ?? 0)
            : sim.Data.Rules.Expedition.RationFoodPerMember;
        _food = perMember * _picked.Count;
        var travel = zoneId is null ? 0 : sim.ZoneInfo(zoneId).TravelTicks * 2 + (arrival?.WaitTicks ?? 0);
        _bill = _picked.Count == 0 || sim.Data.Food is null ? null : Remnants.Core.Colony.Rations.Plan(sim, _food, _picked.Count, travel, Rations);
        _warnings = Warnings(sim);

        var zone = zoneId is null ? null : data.Zones[zoneId];
        var info = zoneId is null ? null : sim.ZoneInfo(zoneId);
        _set.Text(_sumZone, zone?.Name ?? "No destination");
        _set.Tooltip(_sumZone, zone is null ? "" : info!.Intel == ZoneIntel.Scouted ? $"{zone.Name}: threat {zone.Threat}, loot tier {zone.LootTier}" : $"{zone.Name}: rumored");
        _set.FontColor(_sumZone, zone is null ? Palette.Danger : info!.Intel == ZoneIntel.Scouted ? ColonyText.ThreatColor(zone.Threat) : Palette.TextMuted);
        var cap = Cap(sim);
        _set.Text(_sumParty, $"{_picked.Count}/{cap}");
        _set.FontColor(_sumParty, _picked.Count == 0 || _picked.Count > cap ? Palette.Danger : Palette.Text);
        _set.Tooltip(_sumParty, $"Party {_picked.Count} of {cap} (Town Hall L{sim.TownHallLevel}" + (vehicle is not null && cap < sim.MaxPartySize ? $", {cap} seats" : "") + ")");
        if (trip is not null && arrival is not null)
        {
            var rules = sim.Data.Rules;
            _set.Text(_sumTravel, $"{ColonyText.Trip(trip.Days)} · D{DayNight.Day(arrival.ArrivalTick, rules)} {DayNight.Hour(arrival.ArrivalTick, rules):00}:00 {arrival.Phase.ToString().ToUpperInvariant()}");
            _set.FontColor(_sumTravel, arrival.Blocked ? Palette.Danger : arrival.Phase == DayPhase.Night ? Palette.Night : Palette.Text);
        }
        else _set.Text(_sumTravel, "—");
        _set.Text(_sumFood, $"{_food:0} ({sim.FoodStock:0})");
        _set.FontColor(_sumFood, _bill is { Enough: false } ? Palette.Danger : Palette.Text);
        _set.Tooltip(_sumFood, $"Rations: {_food:0.#} food for the trip; {sim.FoodStock:0} in the pantry");
        var fuel = vehicle is not null && trip is not null;
        _set.Visible(_fuelChip, fuel);
        if (fuel)
        {
            var have = sim.State.Stockpile.Get(sim.Data.Vehicles.FuelResource);
            _set.Text(_sumFuel, $"{trip!.FuelRoundTrip:0} ({have:0})");
            _set.FontColor(_sumFuel, have + 1e-9 >= trip.FuelRoundTrip ? Palette.Text : Palette.Danger);
            _set.Tooltip(_sumFuel, $"Fuel for the round trip: {trip.FuelRoundTrip:0}; {have:0} in store");
        }
        var count = _warnings.Count;
        var blockers = _warnings.Count(w => w.Blocks);
        if (_sumWarnings.Text != $"{count}") _sumWarnings.Text = $"{count}";
        _sumWarnings.Disabled = count == 0;
        _sumWarnings.Modulate = blockers > 0 ? Palette.Danger : count > 0 ? Palette.Warn : Palette.TextDim;
        _set.Tooltip(_sumWarnings, count == 0 ? "No warnings" : string.Join("\n", _warnings.Select(w => (w.Blocks ? "✖ " : "⚠ ") + w.Text)));

        // Tab status: red with a blocker, amber with a warning, a check when that step is settled.
        for (var i = 0; i < _tabs.Length; i++)
        {
            var mine = _warnings.Where(w => w.Step == i).ToList();
            var settled = i switch { Destination => zoneId is not null, Party => _picked.Count > 0, Review => false, _ => _picked.Count > 0 };
            var (icon, color) = mine.Any(w => w.Blocks) ? ("stat_warning", Palette.Danger)
                : mine.Count > 0 ? ("stat_warning", Palette.Warn)
                : settled ? ("stat_check", Palette.Good) : ("", Palette.TextMuted);
            var texture = icon.Length == 0 ? null : Icons.Get(icon, 14);
            if (_tabs[i].Icon != texture) _tabs[i].Icon = texture;
            if (_tabColors[i] == color) continue;
            _tabColors[i] = color;
            _tabs[i].AddThemeColorOverride("icon_normal_color", color);
            _tabs[i].AddThemeColorOverride("icon_pressed_color", color);
            _tabs[i].AddThemeColorOverride("icon_hover_color", color);
        }

        // Footer: Next until Review; Launch (with why not) on Review.
        var last = _step == Review;
        _back.Disabled = _step == 0;
        _next.Visible = !last;
        _launch.Visible = last;
        _launchOnline.Visible = last && MissionId is null;
        if (!last)
        {
            var next = $"Next: {StepNames[_step + 1]}";
            if (_next.Text != next) _next.Text = next;
        }
        var launchText = MissionId is null ? "Launch expedition" : "Launch mission";
        if (_launch.Text != launchText) _launch.Text = launchText;
        var stop = _warnings.FirstOrDefault(w => w.Blocks);
        _launch.Disabled = _launchOnline.Disabled = stop is not null;
        _set.Text(_blocker, last && stop is not null ? stop.Text : "");
        _set.Tooltip(_launch, stop is null ? "Send the party out (Enter)" : $"Cannot launch: {stop.Text}");
    }

    private readonly Color?[] _tabColors = new Color?[StepNames.Length];

    private void JumpToWarning()
    {
        if (_warnings.OrderByDescending(w => w.Blocks).FirstOrDefault() is { } w) Go(w.Step);
    }

    /// <summary>
    /// The planner's keys, ahead of focus navigation (Tab) and a focused button or dropdown (Enter), but never while
    /// something is typed into, a dropdown's list is open, or a modal sits over the planner.
    /// </summary>
    public override void _Input(InputEvent e)
    {
        if (!Visible || !_planner.Visible || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (Blocked?.Invoke() == true || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        if (_steps.SelectMany(OpenPopups).Any()) return;
        if (key.Keycode is >= Key.Key1 and <= Key.Key5 && !key.CtrlPressed && !key.AltPressed) Go((int)(key.Keycode - Key.Key1));
        else if (key.Keycode == Key.Tab) Go(_step + (key.ShiftPressed ? -1 : 1));
        else if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            if (_step < Review) Go(_step + 1);
            else if (!_launch.Disabled) Launch(online: false);
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    private static IEnumerable<OptionButton> OpenPopups(Node node) =>
        node is OptionButton { } o && o.GetPopup().Visible ? [o] : node.GetChildren().SelectMany(OpenPopups);

    // ── Launch and repeat ────────────────────────────────────────────────────────────────────────────────────────────

    private void Launch(bool online)
    {
        if (SelectedZone is not { } zoneId) return;
        var members = Members();
        IColonyCommand command = MissionId is { } missionId
            ? new Remnants.Core.Story.LaunchStoryMission(missionId, members)
            : new LaunchExpedition(zoneId, members, WaitFor, Rations, SelectedVehicle);
        if (!_session.Execute(command).Ok) return;
        if (MissionId is null)
            Last.AddOrUpdate(_session.Sim, new LastPlan(zoneId, _picked.Select(kv => (kv.Key, new Dictionary<EquipSlot, string>(kv.Value.Equipment), new List<string>(kv.Value.Pack))).ToList(),
                SelectedVehicle, Rations?.Prefer, _wait.Selected));
        _wait.Select(0);
        MissionId = null;
        _repeatNote = null;
        _picked.Clear();
        _gearFor = null;
        Go(Destination, animate: false);
        // A zone on the doorstep is played right away; a far one waits until the party has arrived.
        if (_session.Sim.CurrentExpeditionPhase != ExpeditionPhase.InZone) return;
        if (online) OnlinePressed?.Invoke();
        else PlayPressed?.Invoke();
    }

    /// <summary>Fills in the last launched plan, minus whoever and whatever is no longer here, and opens Review.</summary>
    private void RepeatLast()
    {
        if (_last is not { } plan) return;
        var sim = _session.Sim;
        Refresh();
        var notes = new List<string>();
        var zoneIndex = _zoneIds.IndexOf(plan.ZoneId);
        if (zoneIndex >= 0) _zone.Select(zoneIndex);
        _picked.Clear();
        var candidates = Candidates(sim).Select(s => s.Id).ToHashSet();
        foreach (var (id, equipment, pack) in plan.Members)
        {
            var name = sim.State.FindSurvivor(id)?.Name ?? "someone";
            if (!candidates.Contains(id))
            {
                notes.Add($"{name} cannot go");
                continue;
            }
            var loadout = new Loadout();
            _picked[id] = loadout;
            foreach (var (slot, item) in equipment)
                if (Free(sim, item, loadout) - loadout.Equipment.Values.Count(v => v == item) > 0) loadout.Equipment[slot] = item;
                else notes.Add($"no {sim.Scavenge!.Items[item].Name} left for {name}");
            foreach (var item in pack)
                if (Free(sim, item, loadout) - loadout.Equipment.Values.Count(v => v == item) - loadout.Pack.Count(v => v == item) > 0) loadout.Pack.Add(item);
        }
        RefreshVehicles(sim);
        var vehicleIndex = _vehicleIds.IndexOf(plan.VehicleId);
        if (vehicleIndex < 0 || vehicleIndex < _vehicle.ItemCount && _vehicle.IsItemDisabled(vehicleIndex))
        {
            if (plan.VehicleId is not null) notes.Add("the vehicle is not available: on foot");
            vehicleIndex = 0;
        }
        _vehicle.Select(vehicleIndex);
        _wait.Select(Math.Clamp(plan.Wait, 0, WaitChoices.Length - 1));
        _rationsShown = "";
        RefreshRations(sim);
        _ration.Select(Math.Max(0, _rationIds.IndexOf(plan.Ration)));
        _gearFor = _picked.Keys.Cast<int?>().FirstOrDefault();
        _repeatNote = notes.Count == 0 ? "Same as last time." : "Same as last time, except: " + string.Join("; ", notes) + ".";
        Go(Review);
    }
}
