using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Help;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Story;
using Remnants.Game.Story;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The world map: a stylized top-down chart of the zones the colony knows (rumored zones as hollow "?" markers,
/// scouted ones coloured by threat and sized by reward tier, a cyan ring for Linked presence, a dimmed arc for
/// depletion), dashed roads from the colony labelled with travel days, region names, and the party on the road.
/// Clicking a marker shows its card; "Plan expedition" hands the zone to the <see cref="ExpeditionPanel"/>, which owns
/// the party picker and launch. Reads Core (<c>ColonySim.WorldZones</c>); no rules here.
/// </summary>
public partial class WorldMapPanel : Control
{
    private static readonly Color Ink = new(0.82f, 0.78f, 0.68f);

    private readonly ColonySession _session;
    private readonly WorldMapCanvas _canvas;
    private readonly PanelContainer _card = new() { CustomMinimumSize = new Vector2(380, 0) };
    private readonly Label _cardTitle = Widgets.Label("", "Display", size: 24);
    private readonly Label _cardRegion = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly VBoxContainer _summary = Widgets.Column(6);
    private readonly Expander _about = new("About this zone");
    private readonly Expander _storyBox = new("Story missions");
    private readonly Label _party = Widgets.Wrapped("", "Small", Palette.TextMuted);
    private readonly Button _plan;
    private readonly Button _drone;
    private readonly Unchanged _ui = new();
    private string? _selected;
    private string _cardShown = "";
    private string _storyShown = "";
    private string _canvasShown = "";

    /// <summary>"Plan expedition" for a zone: the screen opens the expedition planner on it.</summary>
    public event Action<string>? PlanPressed;

    /// <summary>A story mission's marker or card entry: the screen opens its briefing.</summary>
    public event Action<string>? MissionPressed;

    public WorldMapPanel(ColonySession session)
    {
        _session = session;
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        _canvas = new WorldMapCanvas(this) { MouseFilter = MouseFilterEnum.Stop };
        _canvas.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_canvas);

        var header = new HBoxContainer { Position = new Vector2(24, 18) };
        header.AddThemeConstantOverride("separation", 12);
        var title = Widgets.Label("WORLD MAP", "Title", Ink);
        header.AddChild(title);
        var keys = Widgets.Label("←/→ or Tab: next zone · Enter: plan", "Small", Palette.TextDim);
        header.AddChild(keys);
        AddChild(header);

        var close = PanelKit.Focusable(Widgets.IconButton("ui_close", "Close", "Close the map", Close, Hotkeys.KeyName("pause_menu")));
        close.SetAnchorsPreset(LayoutPreset.TopRight);
        close.Position = new Vector2(-134, 18);
        AddChild(close);

        // The legend sits on the map's bottom-left, beside what it explains, as chips.
        var legend = Legend();
        legend.SetAnchorsPreset(LayoutPreset.BottomLeft);
        legend.GrowVertical = GrowDirection.Begin;
        legend.OffsetLeft = 24;
        legend.OffsetBottom = -18;
        AddChild(legend);

        // The card is as tall as what it says (no tall empty card); details open below the summary.
        _card.SetAnchorsPreset(LayoutPreset.TopRight);
        _card.GrowHorizontal = GrowDirection.Begin;
        _card.OffsetLeft = -404;
        _card.OffsetRight = -24;
        _card.OffsetTop = 70;
        AddChild(_card);
        var outer = Ui.Panel(_card, 8);
        outer.AddChild(_cardTitle);
        outer.AddChild(_cardRegion);
        var box = Widgets.Column(10);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        outer.AddChild(box);
        box.AddChild(_summary);
        box.AddChild(_about);
        box.AddChild(_storyBox);

        outer.AddChild(_party);
        _plan = PanelKit.Focusable(Widgets.IconButton("ui_expedition", "Plan expedition", "Open the expedition planner on this zone", PlanSelected, "Enter", "Primary"));
        _plan.CustomMinimumSize = new Vector2(0, 36);
        outer.AddChild(_plan);
        var droneRow = Widgets.Row(4);
        _drone = PanelKit.Focusable(Widgets.IconButton("skill_dronepiloting", "Send scout drone", "Fly a scout drone from the stores over this zone: its intel without anyone going", () =>
        {
            if (_selected is not { } id) return;
            // The best pilot at home flies it: less risk, and they train Drone Piloting.
            var pilot = _session.Sim.State.Survivors.Where(s => !s.IsChild && !s.Away).MaxBy(s => s.Get(Remnants.Core.Survivors.Skill.DronePiloting));
            _session.Execute(new SendScoutDrone(id, pilot?.Id));
            Refresh();
        }));
        _drone.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        droneRow.AddChild(_drone);
        droneRow.AddChild(new InfoTip("drones"));
        outer.AddChild(droneRow);
        _droneRow = droneRow;
    }

    private readonly HBoxContainer _droneRow;

    private void PlanSelected()
    {
        if (_selected is not { } id || _plan.Disabled) return;
        Close();
        PlanPressed?.Invoke(id);
    }

    public void Open(string? zoneId = null)
    {
        var opening = !Visible;
        _selected = zoneId ?? _session.Sim.State.Expedition?.ZoneId ?? _selected
            ?? _session.Sim.WorldZones().FirstOrDefault(z => z.Intel != ZoneIntel.Unknown)?.Zone.Id;
        _cardShown = _storyShown = _canvasShown = "";
        if (opening) Motion.Reveal(this, new Vector2(0, 12));
        Refresh();
    }

    public void Close() => Motion.Conceal(this);

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    /// <summary>Arrows / Tab cycle the known zones, Enter plans an expedition to the selected one (while the map is open).</summary>
    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true } key) return;
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        // Not while something opened over the map (a briefing, the pause menu) has the keys.
        if (GetParent() is { } parent)
            for (var i = GetIndex() + 1; i < parent.GetChildCount(); i++)
                if (parent.GetChild(i) is CanvasItem { Visible: true } or CanvasLayer { Visible: true }) return;
        var step = key.Keycode switch
        {
            Key.Right or Key.Down => 1,
            Key.Left or Key.Up => -1,
            Key.Tab => key.ShiftPressed ? -1 : 1,
            _ => 0,
        };
        if (step != 0)
        {
            Cycle(step);
            GetViewport().SetInputAsHandled();
            return;
        }
        // Enter on a focused button presses that button; otherwise it plans.
        if (key.Keycode is Key.Enter or Key.KpEnter && GetViewport().GuiGetFocusOwner() is not BaseButton)
        {
            PlanSelected();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Cycle(int step)
    {
        var zones = _session.Sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown)
            .OrderBy(z => z.Zone.TravelDays).ThenBy(z => z.Zone.Name, StringComparer.Ordinal).Select(z => z.Zone.Id).ToList();
        if (zones.Count == 0) return;
        var at = _selected is null ? -1 : zones.IndexOf(_selected);
        Select(zones[((at < 0 ? (step > 0 ? -1 : 0) : at) + step + zones.Count) % zones.Count]);
    }

    public void Refresh()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        RedrawIfChanged(sim);
        if (_selected is not { } id || sim.ZoneInfo(id) is not { Intel: not ZoneIntel.Unknown } info)
        {
            if (_cardShown == "none") return;
            _cardShown = "none";
            _cardTitle.Text = "Choose a zone";
            _cardRegion.Text = "Click a marker on the map, or use the arrow keys.";
            Widgets.Clear(_summary);
            _about.Visible = _storyBox.Visible = false;
            _plan.Disabled = true;
            _droneRow.Visible = false;
            _party.Visible = false;
            return;
        }
        var vehicles = string.Join(",", sim.State.Vehicles.Where(v => !v.Away).Select(v => $"{v.Id}:{v.Condition:0}"));
        var signature = $"{id}|{info.Intel}|{info.Visits}|{info.Linked}|{Math.Round(info.Depletion * 20)}|{vehicles}|{sim.State.Tick / Math.Max(1, sim.Data.Rules.TicksPerDay / 4)}";
        if (signature != _cardShown)
        {
            _cardShown = signature;
            BuildCard(sim, info);
        }
        RefreshParty(sim);
        RefreshDrone(sim, info);
        RefreshStory(sim, id);
    }

    /// <summary>The canvas redraws when what it shows changes (the party moves, a zone is scouted, the selection), not every tick.</summary>
    private void RedrawIfChanged(ColonySim sim)
    {
        var e = sim.State.Expedition;
        var zones = string.Join(",", sim.WorldZones().Select(z => $"{(int)z.Intel}{Math.Round(z.Depletion * 20)}{(int)z.Linked}"));
        var moving = e is not null || sim.State.Trade.Deliveries.Count > 0 || sim.State.Campaign is not null ? sim.State.Tick : 0;
        var signature = $"{_selected}|{zones}|{moving}|{e?.VehicleId}";
        if (signature == _canvasShown) return;
        _canvasShown = signature;
        _canvas.QueueRedraw();
    }

    /// <summary>The zone card, summary first: threat, loot, Linked and travel as chips; the rest behind "About this zone".</summary>
    private void BuildCard(ColonySim sim, ZoneInfo info)
    {
        var zone = info.Zone;
        _cardTitle.Text = zone.Name;
        _cardRegion.Text = zone.Region.ToUpperInvariant() + (info.Visits > 0 ? $"  ·  visited {info.Visits}×" : "  ·  never visited");
        Widgets.Clear(_summary);
        var scouted = info.Intel == ZoneIntel.Scouted;

        if (scouted)
        {
            var chips = new HFlowContainer();
            chips.AddThemeConstantOverride("h_separation", 6);
            chips.AddThemeConstantOverride("v_separation", 6);
            var threat = Widgets.Row(4);
            threat.AddChild(Widgets.Label("Threat", "Small", Palette.TextMuted));
            threat.AddChild(new Pips(zone.Threat, 4, ColonyText.ThreatColor(zone.Threat)));
            threat.AddChild(new InfoTip("expeditions", () => new HelpContext { Colony = sim, Zone = zone }));
            chips.AddChild(Framed(threat, $"Threat {zone.Threat} of 4: how many and how tough the enemies are."));
            var loot = Widgets.Row(4);
            loot.AddChild(Widgets.Label("Loot", "Small", Palette.TextMuted));
            loot.AddChild(new Pips(zone.LootTier, 4, Palette.Amber));
            loot.AddChild(new InfoTip("loot"));
            chips.AddChild(Framed(loot, $"Loot tier {zone.LootTier} of 4: better gear and more schematics the higher it is."));
            var linked = Widgets.Row(4);
            linked.AddChild(Widgets.Label(Capital(ColonyText.Presence(info.Linked)), "Small", info.Linked == LinkedPresence.None ? Palette.TextMuted : Palette.Network));
            linked.AddChild(new InfoTip("fog"));
            chips.AddChild(Framed(linked, "Linked: the Network's soldiers. More rings on the marker, more of them."));
            if (info.Depletion > 0.01)
                chips.AddChild(Framed(Widgets.Label($"Picked over {Fmt.Percent(info.Depletion)}", "Small", Palette.Warn),
                    "Fewer caches until it recovers: zones refill a little every day."));
            _summary.AddChild(chips);
        }
        else
            _summary.AddChild(Widgets.Wrapped("Rumored, not scouted: threat and rewards are unknown until a party goes there or a drone flies over.", "Small", Palette.TextMuted));

        _summary.AddChild(TravelRow(sim, info));

        // Details: the flavour line, what is there in numbers, with no degenerate "1–1" ranges.
        Widgets.Clear(_about.Body);
        _about.Visible = true;
        if (!string.IsNullOrEmpty(zone.Description)) _about.Body.AddChild(Widgets.Wrapped(zone.Description, "Muted"));
        if (scouted && sim.Scavenge is { } data)
        {
            var mix = string.Join(", ", zone.EnemyMix.Select(e => data.Enemies.GetValueOrDefault(e.Id)?.Name ?? e.Id).Distinct());
            AboutLine($"{Range(zone.Enemies)} enemies{(mix.Length > 0 ? $": {mix}" : "")}");
            AboutLine($"{Range(zone.Caches)} caches to search");
            if (zone.Npcs[1] > 0) AboutLine($"{Range(zone.Npcs)} {(zone.Npcs[1] == 1 ? "stranger" : "strangers")} (may join, trade or fight)");
            if (zone.Pylons[1] > 0) AboutLine($"{Range(zone.Pylons)} {(zone.Pylons[1] == 1 ? "pylon" : "pylons")}: they call reinforcements until destroyed");
        }
        _about.SetTitle(scouted ? "About this zone" : "What is said about it");
    }

    private void AboutLine(string text) => _about.Body.AddChild(Widgets.Wrapped("· " + text, "Small", Palette.Text));

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"3", or "2–4" when the numbers differ.</summary>
    private static string Range(int[] range) => range.Length < 2 || range[0] == range[1] ? $"{range[0]}" : $"{range[0]}–{range[1]}";

    private static PanelContainer Framed(Control content, string tooltip)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Chip", TooltipText = tooltip, MouseFilter = MouseFilterEnum.Stop };
        chip.AddChild(content);
        return chip;
    }

    /// <summary>"On foot 2d · 3 rations each" and, with a vehicle at home, the best one's time and fuel (or why it cannot go).</summary>
    private Control TravelRow(ColonySim sim, ZoneInfo info)
    {
        var col = Widgets.Column(2);
        var zone = info.Zone;
        var head = Widgets.Row(4);
        head.AddChild(Widgets.Label("TRAVEL", "Heading", Palette.TextMuted));
        head.AddChild(new InfoTip("travel", () => new HelpContext { Colony = sim, Zone = zone }));
        head.AddChild(Widgets.Label("· vehicles", "Small", Palette.TextDim));
        head.AddChild(new InfoTip("vehicles"));
        col.AddChild(head);
        var foot = VehicleRules.Plan(sim, zone, null, sim.State.Tick);
        var footText = zone.TravelDays <= 0 ? "On the doorstep" : $"On foot {ColonyText.Duration((int)foot.Ticks, sim.Data.Rules)} each way · {info.RationsPerMember:0.#} food per member";
        col.AddChild(Widgets.Wrapped(foot.Blocked is { } fb ? $"{footText} · {fb}" : footText, "Small", foot.Blocked is null ? Palette.Text : Palette.Warn));
        var best = sim.State.Vehicles.Where(v => !v.Away && !VehicleRules.Broken(sim.Data, v))
            .OrderByDescending(v => sim.VehicleStats(v).Speed).FirstOrDefault();
        if (best is not null && zone.TravelDays > 0)
        {
            var trip = VehicleRules.Plan(sim, zone, best, sim.State.Tick);
            var fuel = sim.Data.Resources.GetValueOrDefault(sim.Data.Vehicles.FuelResource)?.Name ?? "fuel";
            var text = trip.Blocked is { } why ? $"By {best.Name}: {why}"
                : $"By {best.Name} {ColonyText.Duration((int)trip.Ticks, sim.Data.Rules)} · {trip.FuelRoundTrip:0} {fuel.ToLowerInvariant()} there and back";
            var line = Widgets.Wrapped(text, "Small", trip.Blocked is null ? Palette.Text : Palette.Warn);
            line.TooltipText = string.Join("\n", trip.Terms.Select(t => $"{t.Label}: speed ×{t.SpeedMultiplier:0.##}"));
            line.MouseFilter = MouseFilterEnum.Stop;
            col.AddChild(line);
        }
        return col;
    }

    /// <summary>Where the party is and when it is back: the reason Plan is off, said where it is seen.</summary>
    private void RefreshParty(ColonySim sim)
    {
        var e = sim.State.Expedition;
        _ui.Visible(_party, e is not null);
        _plan.Disabled = e is not null || _selected is null;
        if (e is null)
        {
            if (_plan.Text != "Plan expedition") _plan.Text = "Plan expedition";
            return;
        }
        var zone = sim.Scavenge?.Zones.GetValueOrDefault(e.ZoneId)?.Name ?? e.ZoneId;
        var tick = sim.State.Tick;
        var rules = sim.Data.Rules;
        var text = e.PhaseAt(tick) switch
        {
            ExpeditionPhase.Outbound => $"The party is on the road to {zone}: there in {ColonyText.Duration((int)Math.Max(0, e.ArrivalTick - tick), rules)}.",
            ExpeditionPhase.InZone => $"The party is in {zone}: play it from the Expedition panel.",
            _ => $"The party is heading home from {zone}: back in {ColonyText.Duration((int)Math.Max(0, e.ReturnTick - tick), rules)}.",
        };
        _ui.Text(_party, text + " One party at a time.");
        if (_plan.Text != "A party is already out") _plan.Text = "A party is already out";
    }

    /// <summary>The scout drone button: what a flight there would cost and risk, or why it cannot go.</summary>
    private void RefreshDrone(ColonySim sim, ZoneInfo info)
    {
        var scavenge = sim.Scavenge!;
        var rules = scavenge.Rules.World.DroneScout;
        var flying = sim.State.DroneFlights.FirstOrDefault(f => f.ZoneId == info.Zone.Id);
        var item = WorldMap.DroneItem(sim.State, scavenge);
        _ui.Visible(_droneRow, info.Intel != ZoneIntel.Scouted || flying is not null);
        if (!_droneRow.Visible) return;
        var pilot = sim.State.Survivors.Where(s => !s.IsChild && !s.Away).MaxBy(s => s.Get(Remnants.Core.Survivors.Skill.DronePiloting));
        var risk = WorldMap.DroneLossChance(scavenge, info.Zone, pilot);
        var days = WorldMap.DroneLegTicks(sim.Data, scavenge, info.Zone) / (double)sim.Data.Rules.TicksPerDay;
        var (disabled, text) = flying is not null ? (true, flying.Arrived ? "Scout drone flying home" : "Scout drone on its way")
            : info.Zone.TravelDays > rules.MaxTravelDays ? (true, "Beyond a drone's battery")
            : item is null ? (true, "No scout drone in the stores")
            : (false, $"Send scout drone · {days:0.#}d · {risk * 100:0}% risk");
        if (_drone.Disabled != disabled) _drone.Disabled = disabled;
        if (_drone.Text != text) _drone.Text = text;
        _ui.Tooltip(_drone, $"Reveals this zone's intel in {days:0.#} days without anyone going. It may be shot down over dangerous zones "
                            + $"({risk * 100:0}% here{(pilot is null ? "" : $", flown by {pilot.Name}")}); otherwise it is back, recharged, "
                            + $"{rules.RechargeDays:0.#} days after the flight home.");
    }

    /// <summary>
    /// The zone card's story section: missions there, their threat, the schematics they pay and what they need. Rebuilt only
    /// when the zone or a mission's status changes.
    /// </summary>
    private void RefreshStory(ColonySim sim, string zoneId)
    {
        var missions = Campaign.Visible(sim).Where(m => m.Def.Zone == zoneId && m.Def.Type == MissionType.Field && !m.Def.Perimeter).ToList();
        var later = sim.Story?.Missions.Where(m => m.Zone == zoneId && m.Type == MissionType.Field && !m.Perimeter
                                                    && sim.State.Campaign?.Progress(m.Id).Status == MissionStatus.Locked).Take(2).ToList() ?? [];
        var signature = $"{zoneId}|" + string.Join(",", missions.Select(m => $"{m.Def.Id}:{m.Progress.Status}:{m.Progress.Outcome}"))
                        + "|" + string.Join(",", later.Select(m => m.Id));
        if (signature == _storyShown) return;
        _storyShown = signature;
        var box = _storyBox.Body;
        Widgets.Clear(box);
        _storyBox.Visible = missions.Count > 0 || later.Count > 0;
        if (!_storyBox.Visible) return;
        var open = missions.Count(m => m.Progress.Status == MissionStatus.Available);
        _storyBox.SetTitle(open > 0 ? $"Story missions ({open} open)" : $"Story missions ({missions.Count + later.Count})");
        // An open mission is something to do here: show it without a click.
        if (open > 0) _storyBox.SetExpanded(true);
        foreach (var (def, p) in missions)
        {
            var row = Widgets.Row(6);
            row.AddChild(Icons.Rect(Icons.Get(def.Main ? "story_main" : "story_side", 16), 16, p.Status == MissionStatus.Done ? Palette.TextDim : Palette.Amber));
            var state = p.Status switch { MissionStatus.Done => p.Outcome == "failed" ? " · failed" : " · done", MissionStatus.Failed => " · back later", _ => "" };
            var name = Widgets.Wrapped($"{def.Name} · threat {def.Threat}{state}", "Small");
            row.AddChild(name);
            if (p.Status == MissionStatus.Available)
                row.AddChild(PanelKit.Focusable(Widgets.IconButton("ui_info", "Briefing", "Read the briefing", () => MissionPressed?.Invoke(def.Id), variation: "Flat")));
            box.AddChild(row);
            var schematics = def.Rewards.Where(e => e.Kind == EffectKind.Schematic).Select(e => Campaign.SchematicName(e.Id)).ToList();
            if (schematics.Count > 0 && p.Status != MissionStatus.Done)
                box.AddChild(Widgets.Label($"   pays: {string.Join(", ", schematics)}", "Small", Palette.Good));
        }
        foreach (var def in later)
            box.AddChild(Widgets.Wrapped($"◇ {def.Name} · needs {def.NeedsText}", "Small", Palette.TextDim));
    }

    /// <summary>The legend as a chip row on the map: each mark as the map draws it, with a tip where a rule hides behind it.</summary>
    private static Control Legend()
    {
        var row = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(720, 0) };
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddThemeConstantOverride("v_separation", 6);
        Control Chip(string glyph, Color color, string text, string tooltip, string? topic = null)
        {
            var inner = Widgets.Row(4);
            inner.AddChild(Widgets.Label(glyph, "Number", color));
            inner.AddChild(Widgets.Label(text, "Small", Palette.TextMuted));
            if (topic is not null) inner.AddChild(new InfoTip(topic));
            return Framed(inner, tooltip);
        }
        row.AddChild(Chip("?", Ink, "rumored", "A rumor: not scouted, threat and rewards unknown."));
        var threat = Widgets.Row(3);
        threat.AddChild(Widgets.Label("Threat", "Small", Palette.TextMuted));
        for (var t = 1; t <= 4; t++) threat.AddChild(Widgets.Label($"● {t}", "Number", ColonyText.ThreatColor(t)));
        threat.AddChild(new InfoTip("expeditions"));
        row.AddChild(Framed(threat, "Marker colour: the zone's threat, 1 (green) to 4."));
        row.AddChild(Chip("◆", Palette.Amber, "loot tier · bigger = richer", "Diamonds on a marker and its size: the loot tier.", "loot"));
        row.AddChild(Chip("○", Palette.Network, "Linked seen", "Cyan rings: Linked presence, more rings for more.", "fog"));
        var picked = Widgets.Row(4);
        picked.AddChild(new LegendMark());
        picked.AddChild(Widgets.Label("picked over", "Small", Palette.TextMuted));
        row.AddChild(Framed(picked, "A dark arc on the marker: how much of the zone has been looted. It refills slowly."));
        row.AddChild(Chip("◆", Palette.Amber, "story mission", "Gold diamond: a main story mission, hollow: a side mission. Click it for the briefing."));
        row.AddChild(Chip("●", Palette.Amber, "party · vehicle", "The party on its road; a party that drives shows its vehicle with condition.", "vehicles"));
        return row;
    }

    internal ColonySim Sim => _session.Sim;

    internal string? Selected => _selected;

    internal void OpenMission(string missionId) => MissionPressed?.Invoke(missionId);

    internal void Select(string zoneId)
    {
        if (_selected == zoneId) return;
        _selected = zoneId;
        Refresh();
    }
}

/// <summary>The legend's "picked over" mark, drawn like the map draws it: a marker with a dark arc eating into it.</summary>
public partial class LegendMark : Control
{
    public LegendMark()
    {
        CustomMinimumSize = new Vector2(16, 16);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var c = Size / 2;
        DrawCircle(c, 7, Palette.Good.Darkened(0.35f));
        DrawCircle(c, 5, Palette.Good);
        DrawArc(c, 5.5f, -Mathf.Pi / 2, Mathf.Pi * 0.6f, 16, new Color(0, 0, 0, 0.6f), 3f, true);
    }
}

/// <summary>The world map's drawing surface; normalized zone positions are mapped into the area left of the card.</summary>
public partial class WorldMapCanvas : Control
{
    private readonly WorldMapPanel _owner;

    public WorldMapCanvas() : this(null!) { }

    public WorldMapCanvas(WorldMapPanel owner) => _owner = owner;

    private static readonly Color Sea = new(0.06f, 0.06f, 0.055f);
    private static readonly Color Land = new(0.15f, 0.14f, 0.12f);
    private static readonly Color LandEdge = new(0.3f, 0.26f, 0.2f);
    private static readonly Color River = new(0.17f, 0.21f, 0.22f);
    private static readonly Color Ink = new(0.82f, 0.78f, 0.68f);
    private static readonly Color Network = Palette.Network;

    private const float MarkerBase = 11f;

    private Rect2 Area()
    {
        var size = Size;
        return new Rect2(60, 90, Math.Max(200, size.X - 404 - 24 - 90), Math.Max(200, size.Y - 90 - 60));
    }

    private Vector2 At(double[] pos)
    {
        var area = Area();
        return area.Position + new Vector2((float)pos[0] * area.Size.X, (float)pos[1] * area.Size.Y);
    }

    private static float Radius(ZoneDef zone) => MarkerBase + zone.LootTier * 3.5f;

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        var pin = StoryPins().Where(p => p.At.DistanceTo(click.Position) < 14).Select(p => p.Id).FirstOrDefault();
        if (pin is not null && _owner.Sim.State.Campaign?.Progress(pin).Status == MissionStatus.Available)
        {
            _owner.OpenMission(pin);
            AcceptEvent();
            return;
        }
        var hit = _owner.Sim.WorldZones()
            .Where(z => z.Intel != ZoneIntel.Unknown)
            .Select(z => (z.Zone.Id, Distance: At(z.Zone.MapPos).DistanceTo(click.Position) - Radius(z.Zone)))
            .Where(z => z.Distance < 10)
            .OrderBy(z => z.Distance)
            .FirstOrDefault();
        if (hit.Id is not null) _owner.Select(hit.Id);
        AcceptEvent();
    }

    public override void _Draw()
    {
        var sim = _owner.Sim;
        var data = sim.Scavenge!;
        var font = GetThemeDefaultFont();
        var area = Area();

        DrawRect(new Rect2(Vector2.Zero, Size), Sea);
        DrawLand(area);
        DrawGrid(area);

        var zones = sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown).ToList();
        var colony = At(data.Rules.World.ColonyPos);

        // Region names behind everything else, at the middle of the region's known zones.
        foreach (var region in zones.GroupBy(z => z.Zone.Region).Where(g => g.Key.Length > 0))
        {
            // Above the region's highest marker, centred over its markers.
            var points = region.Select(z => At(z.Zone.MapPos)).ToList();
            var x = (points.Min(p => p.X) + points.Max(p => p.X)) / 2;
            var y = points.Min(p => p.Y) - 52;
            var text = string.Join(" ", region.Key.ToUpperInvariant().ToCharArray());
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 15).X;
            // Outlined so a name over the river or a road still reads.
            DrawStringOutline(font, new Vector2(x - width / 2, y), text, HorizontalAlignment.Left, -1, 15, 4, new Color(Sea, 0.9f));
            DrawString(font, new Vector2(x - width / 2, y), text, HorizontalAlignment.Left, -1, 15, new Color(Ink, 0.62f));
        }

        // Roads: dashed from the colony, labelled with the travel time.
        foreach (var info in zones)
        {
            var to = At(info.Zone.MapPos);
            if (info.Zone.TravelDays <= 0 && to.DistanceTo(colony) < 40) continue;
            var color = new Color(ColonyText.ThreatColor(info.Zone.Threat), info.Intel == ZoneIntel.Scouted ? 0.55f : 0.3f);
            DrawDashedLine(colony, to, color, 2f, 9f);
            if (info.Zone.TravelDays <= 0) continue;
            // The label sits beside the road halfway along its open stretch (between the colony's ring and the marker's), so
            // labels of roads that meet at the colony do not pile up there and none sits on a ring.
            var length = colony.DistanceTo(to);
            var start = 26f;
            var end = length - Radius(info.Zone) - 12;
            if (end - start < 24) continue;
            var dir = (to - colony) / length;
            var along = colony + dir * ((start + end) / 2);
            var side = dir.Orthogonal() * 12;
            var label = $"{info.Zone.TravelDays:0.#}d";
            var lw = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
            var pos = along + side + new Vector2(-lw / 2, 4);
            DrawStringOutline(font, pos, label, HorizontalAlignment.Left, -1, 13, 4, new Color(Sea, 0.85f));
            DrawString(font, pos, label, HorizontalAlignment.Left, -1, 13, new Color(Ink, 0.85f));
        }

        DrawColony(colony, font);
        foreach (var info in zones) DrawZone(info, font, info.Zone.Id == _owner.Selected);
        DrawParty(sim, data, colony);
        DrawCouriers(sim, colony, font, area);
        DrawStory(sim, colony, font);
    }

    /// <summary>Open story missions as pins on their zone's edge: (mission id, where, main?, new?).</summary>
    private List<(string Id, Vector2 At, bool Main, bool New, string Name)> StoryPins()
    {
        var sim = _owner.Sim;
        var pins = new List<(string, Vector2, bool, bool, string)>();
        if (sim.Scavenge is not { } data) return pins;
        var slot = new Dictionary<string, int>();
        foreach (var (def, p) in Campaign.Visible(sim).Where(m => m.Def.Type == MissionType.Field && m.Progress.Status is MissionStatus.Available or MissionStatus.Active))
        {
            var at = def.Perimeter ? At(data.Rules.World.ColonyPos) + new Vector2(30, -26)
                : def.Zone is { } z && data.Zones.TryGetValue(z, out var zone) ? At(zone.MapPos) + new Vector2(Radius(zone) + 10, -Radius(zone) - 6) : Vector2.Zero;
            if (at == Vector2.Zero) continue;
            var key = def.Zone ?? "";
            var n = slot.GetValueOrDefault(key);
            slot[key] = n + 1;
            pins.Add((def.Id, at + new Vector2(0, n * 22), def.Main, p.AvailableTick >= sim.State.Tick - sim.Data.Rules.TicksPerDay, def.Name));
        }
        return pins;
    }

    /// <summary>
    /// Story marks: a gold diamond (main) or hollow one (side) per open mission, pulsing while new; radio waves at the
    /// colony while someone is calling; the caravan's truck on its road with the days until it arrives.
    /// </summary>
    private void DrawStory(ColonySim sim, Vector2 colony, Font font)
    {
        _pulsing = false;
        if (sim.State.Campaign is not { } campaign || sim.Story is null) return;
        var pulse = GameSettings.ReduceMotion ? 1f : 0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() / 260f);
        var pins = StoryPins();
        _pulsing = pins.Any(p => p.New);
        foreach (var (_, at, main, isNew, name) in pins)
        {
            var size = isNew ? 12 + 2 * pulse : 12;
            if (StoryArt.Marker(main ? "marker_mission_main" : "marker_mission_side") is { } tex)
                DrawTextureRect(tex, new Rect2(at - new Vector2(size, size) * 1.4f, new Vector2(size, size) * 2.8f), false);
            else
            {
                Vector2[] diamond = [at + new Vector2(0, -size), at + new Vector2(size, 0), at + new Vector2(0, size), at + new Vector2(-size, 0)];
                if (main) DrawColoredPolygon(diamond, Palette.Amber);
                DrawPolyline([.. diamond, diamond[0]], main ? Palette.Amber.Darkened(0.4f) : Palette.Amber, 2f, true);
            }
            DrawString(font, at + new Vector2(14, 5), name, HorizontalAlignment.Left, -1, 13, Palette.Amber);
        }
        if (Campaign.Waiting(sim).Any())
        {
            var at = colony + new Vector2(-30, -24);
            if (StoryArt.Marker("marker_radio_contact") is { } wave) DrawTextureRect(wave, new Rect2(at - new Vector2(14, 14), new Vector2(28, 28)), false);
            else
                for (var i = 0; i < 3; i++)
                    DrawArc(at, 5 + i * 5 + pulse * 2, -Mathf.Pi * 0.8f, -Mathf.Pi * 0.2f, 12, new Color(Palette.Amber, 0.9f - i * 0.25f), 2f, true);
        }
        // The caravan on its road (the next caravan call not yet in).
        if (campaign.Flag("caravan_open") > 0 && campaign.Inbox.FirstOrDefault(c => !c.Taken && c.Tick > sim.State.Tick
                                                                                  && sim.Story.Dialogues.GetValueOrDefault(c.DialogueId)?.Caller == "marrow") is { } coming)
        {
            var days = (coming.Tick - sim.State.Tick) / (double)sim.Data.Rules.TicksPerDay;
            var from = colony + new Vector2(-260, 120);
            var at = from.Lerp(colony, (float)Math.Clamp(1 - days / 4, 0.1, 0.9));
            DrawDashedLine(from, colony, new Color(Ink, 0.35f), 2f, 6f);
            if (StoryArt.Marker("marker_caravan") is { } truck) DrawTextureRect(truck, new Rect2(at - new Vector2(16, 16), new Vector2(32, 32)), false);
            else DrawRect(new Rect2(at - new Vector2(10, 6), new Vector2(20, 12)), Ink);
            DrawString(font, at + new Vector2(-30, 28), $"Long Haul · {days:0.#}d", HorizontalAlignment.Left, -1, 13, Ink);
        }
    }

    private bool _pulsing;

    /// <summary>Redraws every frame only while a new story pin pulses; otherwise the panel asks when something changed.</summary>
    public override void _Process(double delta)
    {
        if (_pulsing && IsVisibleInTree() && !GameSettings.ReduceMotion) QueueRedraw();
    }

    /// <summary>
    /// Trade couriers on their way in from another colony (off the map's east edge, which is not on this map), with who
    /// sent them and the days left.
    /// </summary>
    private void DrawCouriers(ColonySim sim, Vector2 colony, Font font, Rect2 area)
    {
        var deliveries = sim.State.Trade.Deliveries;
        for (var i = 0; i < deliveries.Count; i++)
        {
            var d = deliveries[i];
            var from = new Vector2(area.End.X - 20, area.Position.Y + 40 + i * 46);
            var span = Math.Max(1, d.ArriveTick - d.DepartTick);
            var progress = Mathf.Clamp((sim.State.Tick - d.DepartTick) / (float)span, 0, 1);
            DrawDashedLine(from, colony, new Color(Palette.Good, 0.5f), 2f, 6f);
            var at = from.Lerp(colony, progress);
            DrawCircle(at, 11, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            DrawTextureRect(Icons.Get("ui_courier", 18), new Rect2(at - new Vector2(9, 9), new Vector2(18, 18)), false, Palette.Good);
            var days = Math.Max(0, d.ArriveTick - sim.State.Tick) / (double)sim.Data.Rules.TicksPerDay;
            var label = $"Courier from {d.From} · {days:0.#}d";
            var width = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
            DrawString(font, at + new Vector2(-width / 2, -16), label, HorizontalAlignment.Left, -1, 13, Ink);
        }
    }

    /// <summary>A fixed, hand-shaped landmass with a river through it: the same picture every time.</summary>
    private void DrawLand(Rect2 area)
    {
        Vector2 P(float x, float y) => area.Position + new Vector2(x * area.Size.X, y * area.Size.Y);
        var outline = new[]
        {
            P(-0.04f, 0.08f), P(0.18f, -0.03f), P(0.42f, 0.04f), P(0.63f, -0.02f), P(0.86f, 0.0f), P(1.04f, 0.07f),
            P(1.02f, 0.42f), P(1.05f, 0.78f), P(0.9f, 1.03f), P(0.6f, 0.96f), P(0.38f, 1.05f), P(0.1f, 0.98f),
            P(-0.05f, 0.82f), P(-0.02f, 0.45f),
        };
        DrawColoredPolygon(outline, Land);
        DrawPolyline([.. outline, outline[0]], LandEdge, 3f, true);

        var river = new[] { P(0.0f, 0.3f), P(0.14f, 0.36f), P(0.25f, 0.44f), P(0.36f, 0.4f), P(0.5f, 0.62f), P(0.58f, 0.78f), P(0.7f, 0.86f), P(0.82f, 1.0f) };
        DrawPolyline(river, River, 6f, true);
        var lake = new List<Vector2>();
        for (var i = 0; i < 20; i++)
        {
            var a = i / 20f * Mathf.Tau;
            lake.Add(P(0.3f, 0.27f) + new Vector2(Mathf.Cos(a) * 38, Mathf.Sin(a) * 22));
        }
        DrawColoredPolygon([.. lake], River);
    }

    private void DrawGrid(Rect2 area)
    {
        var color = new Color(Ink, 0.05f);
        for (var i = 0; i <= 10; i++)
        {
            var x = area.Position.X + area.Size.X * i / 10f;
            var y = area.Position.Y + area.Size.Y * i / 10f;
            DrawLine(new Vector2(x, area.Position.Y - 20), new Vector2(x, area.End.Y + 20), color, 1f);
            DrawLine(new Vector2(area.Position.X - 20, y), new Vector2(area.End.X + 20, y), color, 1f);
        }
    }

    private void DrawColony(Vector2 at, Font font)
    {
        DrawCircle(at, 20, new Color(Ink, 0.12f));
        DrawRect(new Rect2(at + new Vector2(-9, -4), new Vector2(18, 12)), Ink);
        DrawColoredPolygon([at + new Vector2(-12, -4), at + new Vector2(0, -15), at + new Vector2(12, -4)], Ink);
        DrawString(font, at + new Vector2(-24, 30), "COLONY", HorizontalAlignment.Left, -1, 13, Ink);
    }

    private void DrawZone(ZoneInfo info, Font font, bool selected)
    {
        var zone = info.Zone;
        var at = At(zone.MapPos);
        var r = Radius(zone);
        var color = ColonyText.ThreatColor(zone.Threat);
        if (selected) DrawArc(at, r + 9, 0, Mathf.Tau, 40, Ink, 2f, true);

        if (info.Intel == ZoneIntel.Rumored)
        {
            DrawCircle(at, r, new Color(Sea, 0.85f));
            DrawArc(at, r, 0, Mathf.Tau, 32, new Color(Ink, 0.6f), 2f, true);
            DrawString(font, at + new Vector2(-5, 7), "?", HorizontalAlignment.Left, -1, 20, Ink);
        }
        else
        {
            DrawCircle(at, r, color.Darkened(0.35f));
            DrawCircle(at, r - 4, color);
            // Depletion: a dark wedge eating into the marker.
            if (info.Depletion > 0.01)
                DrawArc(at, r - 2, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * (float)info.Depletion, 32, new Color(0, 0, 0, 0.55f), 4f, true);
            var tier = new string('◆', zone.LootTier);
            var w = font.GetStringSize(tier, HorizontalAlignment.Left, -1, 11).X;
            DrawString(font, at + new Vector2(-w / 2, 4), tier, HorizontalAlignment.Left, -1, 11, new Color(0.1f, 0.1f, 0.1f));
            if (info.Linked != LinkedPresence.None)
            {
                var rings = (int)info.Linked;
                for (var i = 0; i < rings; i++)
                    DrawArc(at, r + 4 + i * 4, 0, Mathf.Tau, 40, new Color(Network, 0.85f - i * 0.2f), 1.5f, true);
            }
        }

        var name = zone.Name;
        var width = font.GetStringSize(name, HorizontalAlignment.Left, -1, 14).X;
        DrawString(font, at + new Vector2(-width / 2, r + 24), name, HorizontalAlignment.Left, -1, 14, Ink);
    }

    /// <summary>
    /// The party as a marker on its road: walking out, in the zone, or walking home. A party in a vehicle shows as the
    /// vehicle's token (its icon and tier) with its name and condition under it.
    /// </summary>
    private void DrawParty(ColonySim sim, ScavengeData data, Vector2 colony)
    {
        if (sim.State.Expedition is not { } e || !data.Zones.TryGetValue(e.ZoneId, out var zone)) return;
        var target = At(zone.MapPos);
        var tick = sim.State.Tick;
        var progress = e.PhaseAt(tick) switch
        {
            ExpeditionPhase.Outbound => e.TravelTicks > 0 ? (tick - e.LaunchedTick) / (float)e.TravelTicks : 1f,
            ExpeditionPhase.Returning => (e.ReturnTravelTicks > 0 ? e.ReturnTravelTicks : e.TravelTicks) is var back and > 0
                ? 1f - (tick - (e.ReturnTick - back)) / (float)back : 0f,
            _ => 1f,
        };
        var at = colony.Lerp(target, Mathf.Clamp(progress, 0, 1)) + new Vector2(0, -Radius(zone) - 10) * (progress >= 1 ? 1 : 0);
        // Keep the token off every marker (it covered the zone it passed, and the click went to it).
        foreach (var info in sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown))
        {
            var centre = At(info.Zone.MapPos);
            var clear = Radius(info.Zone) + 16;
            var away = at - centre;
            if (away.Length() < clear) at = centre + (away.Length() < 1e-3 ? new Vector2(0, -1) : away.Normalized()) * clear;
        }
        if (e.VehicleId is { } vid && sim.FindVehicle(vid) is { } vehicle)
        {
            var stats = sim.VehicleStats(vehicle);
            var font = ThemeDB.FallbackFont;
            DrawCircle(at, 13, new Color(0.12f, 0.1f, 0.08f));
            DrawArc(at, 13, 0, Mathf.Tau, 28, Hud.Palette.Amber, 2.5f, true);
            DrawTextureRect(Hud.Icons.Get("ui_vehicle", 18), new Rect2(at - new Vector2(9, 9), new Vector2(18, 18)), false, Hud.Palette.Amber);
            var label = $"T{stats.Tier.Tier} {vehicle.Name} {vehicle.Condition:0}/{stats.MaxCondition:0}";
            var width = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13).X;
            DrawString(font, at + new Vector2(-width / 2, -18), label, HorizontalAlignment.Left, -1, 13, Hud.Palette.Gauge(vehicle.Condition / stats.MaxCondition));
            return;
        }
        DrawCircle(at, 7, new Color(0.95f, 0.9f, 0.7f));
        DrawArc(at, 7, 0, Mathf.Tau, 20, new Color(0.1f, 0.1f, 0.1f), 2f, true);
    }
}
