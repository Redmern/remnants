using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>The five steps of the <see cref="ExpeditionPanel"/>: each built once, refreshed in place while it is on screen.</summary>
public partial class ExpeditionPanel
{
    // ── 1. Destination ───────────────────────────────────────────────────────────────────────────────────────────────

    private readonly OptionButton _zone = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
    private readonly List<string> _zoneIds = new();
    private readonly Label _zoneHead = Widgets.Label("", "Display", null, 17);
    private readonly Label _zoneLine = Widgets.Wrapped("", "Muted");
    private readonly RichTextLabel _zoneIntel = Ui.Rich();
    private Button _intelToggle = null!;
    private readonly OptionButton _wait = new() { FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(150, 0) };
    private readonly TextureRect _arrivalIcon = new() { StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(22, 22) };
    private readonly Label _arrival = Ui.Label();
    private readonly Label _arrivalNote = Widgets.Wrapped("", "Small");
    private readonly Label _weatherNote = Widgets.Wrapped("", "Small");

    /// <summary>The wait choices, in the order of <see cref="_wait"/>: go now, or wait for a phase.</summary>
    private static readonly DayPhase?[] WaitChoices = [null, DayPhase.Dawn, DayPhase.Day, DayPhase.Dusk, DayPhase.Night];

    private string? SelectedZone => _zone.Selected >= 0 && _zone.Selected < _zoneIds.Count ? _zoneIds[_zone.Selected] : null;
    private DayPhase? WaitFor => WaitChoices[Math.Clamp(_wait.Selected, 0, WaitChoices.Length - 1)];

    private Control DestinationStep()
    {
        var box = Widgets.Column(8);
        var row = Widgets.Row(6);
        row.AddChild(_zone);
        row.AddChild(Widgets.IconButton("ui_world_map", "World map", "Pick the destination on the world map", () => WorldMapPressed?.Invoke()));
        box.AddChild(row);
        var head = Widgets.Row(6);
        _zoneHead.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_zoneHead);
        head.AddChild(new InfoTip("expeditions", HelpContext));
        box.AddChild(head);
        box.AddChild(_zoneLine);
        _intelToggle = Widgets.TextButton("Intel ▸", ToggleIntel, "What the colony knows: enemies, strangers, caches, how picked-over it is", "Flat");
        _intelToggle.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        box.AddChild(Motion.OnPress(_intelToggle));
        _zoneIntel.Visible = false;
        box.AddChild(_zoneIntel);

        box.AddChild(Widgets.SectionHeader("Arrival", "stat_time", new InfoTip("travel", HelpContext)));
        var arrival = Widgets.Row(6);
        arrival.AddChild(_arrivalIcon);
        _arrival.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        arrival.AddChild(_arrival);
        foreach (var choice in WaitChoices) _wait.AddItem(choice is { } p ? $"Wait for {p.ToString().ToLowerInvariant()}" : "Go now");
        _wait.TooltipText = "Wait outside the zone for another time of day. Each day waited costs extra rations per member.";
        arrival.AddChild(_wait);
        box.AddChild(arrival);
        box.AddChild(InfoTip.Beside(_arrivalNote, "stealth", HelpContext));
        box.AddChild(InfoTip.Beside(_weatherNote, "weather", HelpContext));
        _zone.ItemSelected += _ => Changed();
        _wait.ItemSelected += _ => Changed();
        return box;
    }

    private bool _intelOpen;

    private void ToggleIntel()
    {
        _intelOpen = !_intelOpen;
        if (_intelOpen) Motion.Reveal(_zoneIntel);
        else Motion.Conceal(_zoneIntel);
        _intelToggle.Text = _intelOpen ? "Intel ▾" : "Intel ▸";
    }

    /// <summary>The known zones; the list grows as visits reveal more.</summary>
    private void RefreshZones(ColonySim sim)
    {
        var known = sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown).ToList();
        if (known.Select(z => z.Zone.Id).SequenceEqual(_zoneIds)) return;
        var selected = SelectedZone;
        _zoneIds.Clear();
        _zone.Clear();
        foreach (var info in known)
        {
            _zoneIds.Add(info.Zone.Id);
            _zone.AddItem($"{info.Zone.Name}  ·  {ColonyText.Travel(info.Zone.TravelDays)}");
        }
        _zone.Select(Math.Max(0, selected is null ? 0 : _zoneIds.IndexOf(selected)));
    }

    private void RefreshDestination(ColonySim sim)
    {
        if (SelectedZone is not { } id) return;
        var info = sim.ZoneInfo(id);
        var zone = info.Zone;
        var scouted = info.Intel == ZoneIntel.Scouted;
        _set.Text(_zoneHead, scouted ? $"{zone.Name} · threat {zone.Threat} · loot tier {zone.LootTier}" : $"{zone.Name} · rumored");
        _set.FontColor(_zoneHead, scouted ? ColonyText.ThreatColor(zone.Threat) : Palette.TextMuted);
        _set.Text(_zoneLine, scouted ? $"{ColonyText.Presence(info.Linked)} · {zone.Region}" : $"{zone.Region} · not scouted: threat and rewards unknown");
        var intel = ColonyText.ZoneSummary(sim, info);
        if (_zoneIntel.Text != intel) _zoneIntel.Text = intel;

        var plan = sim.PlanArrival(id, WaitFor, SelectedVehicle);
        var rules = sim.Data.Rules;
        var (icon, color) = TopBar.PhaseLook(plan.Phase);
        var texture = Icons.Get(icon, 20, "stat_time");
        if (_arrivalIcon.Texture != texture) _arrivalIcon.Texture = texture;
        _set.Modulate(_arrivalIcon, color);
        _set.Text(_arrival, $"Arrives day {DayNight.Day(plan.ArrivalTick, rules)}, {DayNight.Hour(plan.ArrivalTick, rules):00}:00 · {plan.Phase.ToString().ToUpperInvariant()}");
        _set.FontColor(_arrival, color);
        // One short line; the long version is in the stealth help.
        var phase = plan.Phase switch
        {
            DayPhase.Night => "Night: less sight for everyone, more Husks that hear further.",
            DayPhase.Dawn or DayPhase.Dusk => "Half light: shorter sight, easier to slip by Husks.",
            _ => "Daylight: everyone sees well.",
        };
        var wait = plan.WaitTicks > 0 ? $" Waits {plan.WaitTicks} h outside: +{plan.WaitRationsPerMember:0} food each." : "";
        _set.Text(_arrivalNote, phase + wait);
        _set.FontColor(_arrivalNote, plan.Phase == DayPhase.Night ? Palette.Night : Palette.TextMuted);

        var weather = new List<string>();
        foreach (var term in (plan.Travel?.Modifiers ?? []).Where(t => t.Blocks || t.Source == "weather"))
            weather.Add(term.Blocks ? $"{term.Reason}: wait for it to pass." : term.Reason + (plan.DelayRationsPerMember > 0 ? $" (+{plan.DelayRationsPerMember:0} food each)." : "."));
        var there = Weather.SpellAt(sim.State, sim.Data, Weather.RegionOf(sim.Data, sim.Scavenge!.Zones[id]), sim.State.Tick);
        if (sim.Data.Weather.Enabled && there.Kind != "clear")
            weather.Add($"{ColonyText.WeatherName(sim, there.Kind, there.Intensity)} over {zone.Region} right now.");
        _set.Text(_weatherNote, weather.Count == 0 ? $"Clear over {zone.Region}." : string.Join(" ", weather));
        _set.FontColor(_weatherNote, plan.Blocked ? Palette.Danger : Palette.TextMuted);
    }

    // ── 2. Party ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A survivor tile: portrait, name, health, body; click to take or leave.</summary>
    private sealed class Tile(PanelContainer card, Portrait portrait, Label name, StatBar hp, TextureRect body)
    {
        public PanelContainer Card { get; } = card;
        public Portrait Portrait { get; } = portrait;
        public Label Name { get; } = name;
        public StatBar Hp { get; } = hp;
        public TextureRect Body { get; } = body;
    }

    private readonly Label _partyInfo = Widgets.Label("", "Muted");
    private readonly GridContainer _tiles = new() { Columns = 2 };
    private readonly Dictionary<int, Tile> _tileById = new();
    private readonly Label _noAdults = Widgets.Label("No adults at home and fit to go.", "Muted");
    private readonly Label _readiness = Widgets.Wrapped("", "", Palette.Warn);
    private readonly RichTextLabel _profiles = Ui.Rich();
    private string _tileIds = "";

    private Control PartyStep()
    {
        var box = Widgets.Column(8);
        box.AddChild(InfoTip.Beside(_partyInfo, "expeditions", HelpContext));
        _tiles.AddThemeConstantOverride("h_separation", 6);
        _tiles.AddThemeConstantOverride("v_separation", 6);
        _tiles.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(_tiles);
        box.AddChild(_noAdults);
        box.AddChild(InfoTip.Beside(_readiness, "injuries", HelpContext));
        box.AddChild(Widgets.SectionHeader("In the field", "ui_move", new InfoTip("controls", HelpContext)));
        box.AddChild(_profiles);
        return box;
    }

    private void RefreshParty(ColonySim sim)
    {
        var candidates = Candidates(sim).ToList();
        var ids = string.Join(",", candidates.Select(s => s.Id));
        if (ids != _tileIds)
        {
            // Who can go changed: rebuild the tiles (rare: someone came home, was hurt, grew up).
            _tileIds = ids;
            Widgets.Clear(_tiles);
            _tileById.Clear();
            foreach (var s in candidates) _tileById[s.Id] = MakeTile(s);
            var i = 0;
            foreach (var tile in _tileById.Values) Motion.Enter(tile.Card, i++);
        }
        _noAdults.Visible = candidates.Count == 0;
        foreach (var s in candidates)
        {
            var tile = _tileById[s.Id];
            var picked = _picked.ContainsKey(s.Id);
            var variation = picked ? "CardSelected" : "Card";
            if (tile.Card.ThemeTypeVariation != variation) tile.Card.ThemeTypeVariation = variation;
            if (tile.Portrait.Selected != picked) tile.Portrait.Selected = picked;
            tile.Hp.Set(s.Health / Survivor.MaxHealth, $"{s.Health:0}");
            var body = ColonyText.Body(s.Body, sim.Scavenge);
            _set.Tooltip(tile.Body, body);
            _set.Modulate(tile.Body, body == "Healthy" ? Palette.Good : Palette.Warn);
            _set.Tooltip(tile.Card, (picked ? "Leave at home" : "Take on the expedition") + $"\n{s.Name}: health {s.Health:0}/{Survivor.MaxHealth:0}\n{body}");
        }

        var cap = Cap(sim);
        var mission = MissionId is { } mid && sim.Story?.Mission(mid) is { } md ? $"Mission: {md.Name} · " : "";
        _set.Text(_partyInfo, $"{mission}{_picked.Count} of {cap} picked · Town Hall L{sim.TownHallLevel}" +
                              (MissionId is null && cap < sim.MaxPartySize ? $" · {cap} seats in the vehicle" : "") + " · click to take or leave");
        var warnings = Readiness(sim);
        _set.Text(_readiness, string.Join("\n", warnings.Select(w => "⚠ " + w.Text)));
        _set.Visible(_readiness.GetParent<CanvasItem>(), warnings.Count > 0);

        var lines = _picked.OrderBy(kv => kv.Key).Select(kv =>
        {
            var s = sim.State.FindSurvivor(kv.Key)!;
            var p = sim.PreviewProfile(s, new(kv.Value.Equipment), new(kv.Value.Pack));
            var move = string.Join(" / ", p.Movement.Modes.Select(m => $"{m.Pattern} {m.Range}"));
            var best = p.Actions.OrderByDescending(a => a.MaxDamage).FirstOrDefault();
            return $"[b]{s.Name}[/b]  move {move} · {p.MaxAp} AP · sight {p.Sight:0.#}" + (best is null ? "" : $" · {best.Name} {best.MinDamage:0}–{best.MaxDamage:0}");
        }).ToList();
        var text = lines.Count == 0 ? $"[color=#{Ui.Hex(Ui.Muted)}]Pick someone to see what they can do out there.[/color]" : string.Join("\n", lines);
        if (_profiles.Text != text) _profiles.Text = text;
    }

    private Tile MakeTile(Survivor s)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card", SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand, FocusMode = FocusModeEnum.All };
        var row = Widgets.Row(8);
        row.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(row);
        var portrait = new Portrait(36) { MouseFilter = MouseFilterEnum.Ignore };
        portrait.Set(PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
        row.AddChild(portrait);
        var text = Widgets.Column(0);
        text.MouseFilter = MouseFilterEnum.Ignore;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var name = Widgets.Label(s.Name, "", null, 15);
        name.AddThemeFontOverride("font", Fonts.BodyBold);
        name.ClipText = true;
        text.AddChild(name);
        var hp = new StatBar("stat_hp", 80) { MouseFilter = MouseFilterEnum.Ignore };
        text.AddChild(hp);
        row.AddChild(text);
        var body = Icons.Rect(Icons.Get("body_full", 18), 18, Palette.Good);
        body.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(body);
        var id = s.Id;
        // Act on press: picking people is the planner's most common click.
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } or InputEventKey { Pressed: true, Keycode: Key.Space })
            {
                card.AcceptEvent();
                TogglePick(id);
            }
        };
        _tiles.AddChild(card);
        return new Tile(card, portrait, name, hp, body);
    }

    private void TogglePick(int id)
    {
        if (!_picked.Remove(id))
        {
            _picked[id] = new Loadout();
            _gearFor ??= id;
        }
        else if (_gearFor == id) _gearFor = _picked.Keys.Cast<int?>().FirstOrDefault();
        Changed();
    }

    // ── 3. Gear ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private int? _gearFor;
    private readonly HBoxContainer _gearWho = Widgets.Row(4);
    private readonly VBoxContainer _gearBox = Widgets.Column(6);
    private readonly Label _gearEmpty = Widgets.Label("Pick a party first (step 2).", "Muted");
    private string _gearWhoShown = "", _gearShown = "";

    private Control GearStep()
    {
        var box = Widgets.Column(8);
        box.AddChild(_gearEmpty);
        box.AddChild(_gearWho);
        box.AddChild(_gearBox);
        return box;
    }

    private void RefreshGear(ColonySim sim, bool force)
    {
        _gearEmpty.Visible = _picked.Count == 0;
        // Who: one toggle per picked survivor.
        var who = string.Join(",", _picked.Keys.OrderBy(k => k)) + $"|{_gearFor}";
        if (who != _gearWhoShown)
        {
            _gearWhoShown = who;
            Widgets.Clear(_gearWho);
            foreach (var id in _picked.Keys.OrderBy(k => k))
            {
                var s = sim.State.FindSurvivor(id);
                if (s is null) continue;
                var target = id;
                var b = Widgets.TextButton(s.Name, () =>
                {
                    _gearFor = target;
                    Changed();
                }, $"{s.Name}'s gear", id == _gearFor ? "Primary" : "");
                _gearWho.AddChild(Motion.OnPress(b));
            }
        }
        // What: rebuilt when the survivor, their loadout or the colony's stock changed (not every tick).
        var loadout = _gearFor is { } gid ? _picked.GetValueOrDefault(gid) : null;
        var signature = loadout is null ? "" :
            $"{_gearFor}|{string.Join(",", loadout.Equipment.Select(kv => $"{kv.Key}={kv.Value}"))}|{string.Join(",", loadout.Pack)}|"
            + string.Join(",", _picked.Where(kv => kv.Key != _gearFor).SelectMany(kv => kv.Value.Equipment.Values.Concat(kv.Value.Pack)))
            + "|" + string.Join(",", sim.State.Inventory.Counts.Select(kv => $"{kv.Key}:{kv.Value}"));
        if (!force && signature == _gearShown) return;
        _gearShown = signature;
        Widgets.Clear(_gearBox);
        if (loadout is null || sim.State.FindSurvivor(_gearFor!.Value) is not { } survivor) return;
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var slot in Slots) grid.AddChild(SlotCell(sim, loadout, slot));
        _gearBox.AddChild(Widgets.SectionHeader($"{survivor.Name}'s loadout", "item_weapon", new InfoTip("gear_mods", HelpContext)));
        _gearBox.AddChild(grid);
        _gearBox.AddChild(PackPicker(sim, loadout));
        _gearBox.AddChild(Widgets.SectionHeader("Field profile with this gear", "ui_move", new InfoTip("hit_chance", HelpContext)));
        _gearBox.AddChild(ProfileChips.Build(sim.PreviewProfile(survivor, new(loadout.Equipment), new(loadout.Pack))));
    }

    /// <summary>A slot: its picker (with how many are left) and, for a weapon or armour, a link to the Gear Bench.</summary>
    private Control SlotCell(ColonySim sim, Loadout loadout, EquipSlot slot)
    {
        var data = sim.Scavenge!;
        var cell = Widgets.Column(2);
        cell.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var head = Widgets.Row(4);
        head.AddChild(Icons.Rect(Icons.Get(SlotIcon(slot), 14), 14, Palette.TextMuted));
        head.AddChild(Widgets.Label(slot.ToString().ToUpperInvariant(), "Heading"));
        head.AddChild(Widgets.Spacer());
        var current = loadout.Equipment.GetValueOrDefault(slot);
        if (current is not null && slot is EquipSlot.Weapon or EquipSlot.Body or EquipSlot.Head)
        {
            var item = current;
            var bench = Widgets.TextButton("Gear Bench ›", () => GearBenchPressed?.Invoke(item), "Mod this at the Gear Bench", "Flat");
            bench.CustomMinimumSize = new Vector2(0, 22);
            bench.AddThemeFontSizeOverride("font_size", 12);
            head.AddChild(bench);
        }
        cell.AddChild(head);
        var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
        var ids = new List<string?> { null };
        picker.AddIconItem(Icons.Get(SlotIcon(slot), 16), $"no {slot.ToString().ToLowerInvariant()}");
        foreach (var (itemId, _) in sim.State.Inventory.Counts.Where(kv => data.Items[kv.Key].Slot == slot))
        {
            // What this slot could take: free stock minus what this member already uses elsewhere.
            var available = Free(sim, itemId, loadout) - loadout.Pack.Count(p => p == itemId)
                            - loadout.Equipment.Count(kv => kv.Key != slot && kv.Value == itemId);
            if (available <= 0) continue;
            ids.Add(itemId);
            picker.AddIconItem(Icons.Item(data.Items[itemId].DefinedId, data.Items[itemId].Kind.ToString(), 16),
                $"{data.Items[itemId].Name} ({available - (current == itemId ? 1 : 0)} left)");
        }
        picker.Select(Math.Max(0, ids.IndexOf(current)));
        if (current is not null) picker.TooltipText = GearText.Line(data, data.Items[current]);
        picker.ItemSelected += index =>
        {
            if (ids[(int)index] is { } chosen) loadout.Equipment[slot] = chosen;
            else loadout.Equipment.Remove(slot);
            Changed();
        };
        cell.AddChild(picker);
        return cell;
    }

    private Control PackPicker(ColonySim sim, Loadout loadout)
    {
        var data = sim.Scavenge!;
        var box = Widgets.Column(4);
        box.AddChild(Widgets.SectionHeader("Pack", "stat_load", new InfoTip("loot", HelpContext)));
        var row = Widgets.Row(4);
        var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
        var ids = new List<string>();
        foreach (var (itemId, _) in sim.State.Inventory.Counts)
        {
            var free = Free(sim, itemId, loadout) - loadout.Equipment.Values.Count(v => v == itemId) - loadout.Pack.Count(v => v == itemId);
            if (free <= 0) continue;
            ids.Add(itemId);
            picker.AddIconItem(Icons.Item(data.Items[itemId].DefinedId, data.Items[itemId].Kind.ToString(), 16), $"{data.Items[itemId].Name} ({free} left)");
        }
        picker.Disabled = ids.Count == 0;
        row.AddChild(picker);
        row.AddChild(Widgets.IconButton("ui_plus", "Add", "Put one in the pack", () =>
        {
            if (picker.Selected < 0) return;
            loadout.Pack.Add(ids[picker.Selected]);
            Changed();
        }));
        box.AddChild(row);
        // The pack as chips: click one to take it back out.
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        if (loadout.Pack.Count == 0) flow.AddChild(Widgets.Label("Empty.", "Muted"));
        foreach (var group in loadout.Pack.GroupBy(p => p))
        {
            var item = group.Key;
            var def = data.Items[item];
            var chip = Widgets.TextButton($"{def.Name} ×{group.Count()}  ✕", () =>
            {
                loadout.Pack.Remove(item);
                Changed();
            }, "Take one out", "Flat");
            chip.Icon = Icons.Item(def.DefinedId, def.Kind.ToString(), 14);
            chip.CustomMinimumSize = new Vector2(0, 26);
            flow.AddChild(chip);
        }
        box.AddChild(flow);
        return box;
    }

    // ── 4. Vehicle & supplies ────────────────────────────────────────────────────────────────────────────────────────

    private readonly OptionButton _vehicle = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true, FitToLongestItem = false };
    private readonly RichTextLabel _vehicleInfo = Ui.Rich();
    private readonly List<int?> _vehicleIds = new();
    private readonly Control _vehicleSection = Widgets.Column(6);
    private readonly OptionButton _ration = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All, ClipText = true };
    private readonly RichTextLabel _rationInfo = Ui.Rich();
    private readonly List<string?> _rationIds = new();
    private string _rationsShown = "";

    /// <summary>The chosen vehicle; none for a story mission, which goes on foot.</summary>
    private int? SelectedVehicle =>
        MissionId is null && _vehicle.Selected >= 0 && _vehicle.Selected < _vehicleIds.Count ? _vehicleIds[_vehicle.Selected] : null;

    private RationChoice? Rations => _ration.Selected > 0 && _ration.Selected < _rationIds.Count ? new RationChoice(_rationIds[_ration.Selected]) : null;

    private Control SuppliesStep()
    {
        var box = Widgets.Column(8);
        var vehicle = (VBoxContainer)_vehicleSection;
        vehicle.AddChild(Widgets.SectionHeader("Vehicle", "ui_vehicle", new InfoTip("vehicles", HelpContext)));
        _vehicle.TooltipText = "Drive a colony vehicle: faster on the road and fewer rations, but it burns fuel, its seats cap the party and it is loud on arrival. It parks at the zone's edge as an extraction point and a stash.";
        vehicle.AddChild(_vehicle);
        vehicle.AddChild(_vehicleInfo);
        box.AddChild(vehicle);
        box.AddChild(Widgets.SectionHeader("Rations", "res_food", new InfoTip("rations", HelpContext)));
        _ration.TooltipText = "What to pack first; the rest is filled from the pantry (food that keeps until they are home first).";
        box.AddChild(_ration);
        box.AddChild(_rationInfo);
        _vehicle.ItemSelected += _ => Changed();
        _ration.ItemSelected += _ => Changed();
        return box;
    }

    /// <summary>The picker: on foot, then every vehicle at home (broken ones listed but disabled).</summary>
    private void RefreshVehicles(ColonySim sim)
    {
        var home = sim.State.Vehicles.Where(v => !v.Away).OrderByDescending(v => sim.Data.VehicleTiers[v.TierId].Tier).ThenBy(v => v.Id).ToList();
        var ids = new List<int?> { null };
        ids.AddRange(home.Select(v => (int?)v.Id));
        var labels = new List<string> { "On foot" };
        labels.AddRange(home.Select(v => $"{v.Name}  ({sim.Data.VehicleTiers[v.TierId].Name}, {v.Condition:0}/{sim.VehicleStats(v).MaxCondition:0})"));
        if (ids.SequenceEqual(_vehicleIds) && Enumerable.Range(0, labels.Count).All(i => i < _vehicle.ItemCount && _vehicle.GetItemText(i) == labels[i])) return;
        var selected = SelectedVehicle;
        _vehicleIds.Clear();
        _vehicleIds.AddRange(ids);
        _vehicle.Clear();
        for (var i = 0; i < ids.Count; i++)
        {
            _vehicle.AddIconItem(Icons.Get(i == 0 ? "ui_move" : "ui_vehicle", 16, "ui_expedition"), labels[i]);
            if (ids[i] is { } vid && sim.FindVehicle(vid) is { } v && VehicleRules.Broken(sim.Data, v)) _vehicle.SetItemDisabled(i, true);
        }
        _vehicle.Select(Math.Max(0, _vehicleIds.IndexOf(selected)));
    }

    private void RefreshSupplies(ColonySim sim)
    {
        RefreshVehicles(sim);
        _set.Visible(_vehicleSection, MissionId is null);
        if (SelectedZone is { } zoneId) RefreshVehicleInfo(sim, zoneId);
        RefreshRations(sim);
    }

    /// <summary>The trip by the chosen vehicle: days each way (vs on foot), fuel, seats, cargo, noise; the speed terms in the tooltip.</summary>
    private void RefreshVehicleInfo(ColonySim sim, string zoneId)
    {
        var walk = sim.PlanTravel(zoneId);
        string text;
        if (SelectedVehicle is not { } id || sim.FindVehicle(id) is not { } v)
        {
            text = $"On foot: [b]{ColonyText.Trip(walk.Days)}[/b] each way, no fuel, no stash at the edge.";
            _vehicleInfo.TooltipText = "";
        }
        else
        {
            var plan = sim.PlanTravel(zoneId, id);
            var stats = sim.VehicleStats(v);
            var have = sim.State.Stockpile.Get(sim.Data.Vehicles.FuelResource);
            var lines = new List<string>();
            if (plan.Blocked is { } why) lines.Add($"[color=#{Ui.Hex(Ui.Bad)}]{why}[/color]");
            var faster = walk.Days > 0 ? $" (on foot {ColonyText.Trip(walk.Days)})" : "";
            lines.Add($"[b]{ColonyText.Trip(plan.Days)}[/b] each way{faster} · fuel [color=#{Ui.Hex(have + 1e-9 >= plan.FuelRoundTrip ? Ui.Good : Ui.Bad)}]{plan.FuelRoundTrip:0}[/color] (have {have:0})");
            lines.Add($"Seats {(stats.Seats is { } s ? s.ToString() : "any")} · cargo {stats.CargoKg:0} kg · noise {stats.Noise * 100:0}% more Husks{(stats.SmokeLauncher ? " (smoke: none)" : "")} · condition {v.Condition:0}/{stats.MaxCondition:0}");
            text = string.Join("\n", lines);
            _vehicleInfo.TooltipText = plan.Terms.Count == 0 ? "" : "Speed: " + string.Join(" · ", plan.Terms.Select(t => $"{t.Label} ×{t.SpeedMultiplier:0.##}"));
        }
        if (_vehicleInfo.Text != text) _vehicleInfo.Text = text;
    }

    /// <summary>
    /// The ration picker: "Pack automatically" plus every food that can go on the road, then what the pantry would pack
    /// for this party: items, weight, whether any of it is risky, and the road buff a good meal gives.
    /// </summary>
    private void RefreshRations(ColonySim sim)
    {
        if (sim.Data.Food is not { } foodData) return;
        var options = Pantry.Entries(sim).Where(e => e.Fresh > 0 && foodData.Items[e.ItemId] is { FoodValue: > 0 } d && (d.Edible || d.Meal is not null))
            .OrderBy(e => foodData.Items[e.ItemId].Meal?.FieldBuff is null ? 1 : 0).ThenBy(e => e.ItemId).ToList();
        var signature = string.Join(",", options.Select(e => $"{e.ItemId}:{e.Fresh}"));
        if (signature != _rationsShown)
        {
            _rationsShown = signature;
            var keep = Rations?.Prefer;
            _ration.Clear();
            _rationIds.Clear();
            _ration.AddItem("Pack automatically");
            _rationIds.Add(null);
            foreach (var e in options)
            {
                var def = foodData.Items[e.ItemId];
                var road = def.Meal?.FieldBuff is { } fb ? $" · {FoodText.BuffName(sim, fb)}" : "";
                _ration.AddIconItem(FoodText.Icon(sim, e.ItemId, 16), $"{FoodText.ItemName(sim, e.ItemId)} ×{e.Fresh} ({def.FoodValue:0.#} food each){road}");
                _rationIds.Add(e.ItemId);
            }
            _ration.Select(Math.Max(0, _rationIds.IndexOf(keep)));
        }
        string text;
        if (_picked.Count == 0 || _bill is not { } bill) text = $"[color=#{Ui.Hex(Ui.Muted)}]Pick a party to see what it packs.[/color]";
        else
        {
            var items = string.Join(", ", bill.Items.Select(kv => $"{kv.Value} {FoodText.ItemName(sim, kv.Key)}"));
            var lines = new List<string>
            {
                bill.Enough ? $"Packs {items} · {bill.Weight:0.#} kg" : $"[color=#{Ui.Hex(Ui.Bad)}]Not enough food: {bill.Covered:0.#} of {_food:0.#}.[/color]",
            };
            if (bill.FieldBuff is { } buff && foodData.Buffs.GetValueOrDefault(buff) is { } b)
                lines.Add($"[color=#{Ui.Hex(Palette.Good)}]{b.Name}[/color]: {b.Description}");
            if (bill.RiskShare > 0)
                lines.Add($"[color=#{Ui.Hex(Palette.Warn)}]{bill.RiskShare * 100:0}% of it is spoiled, tainted or will spoil on the road: someone may get sick.[/color]");
            text = string.Join("\n", lines);
        }
        if (_rationInfo.Text != text) _rationInfo.Text = text;
    }

    // ── 5. Review ────────────────────────────────────────────────────────────────────────────────────────────────────

    private readonly RichTextLabel _review = Ui.Rich();
    private string? _repeatNote;

    private Control ReviewStep()
    {
        var box = Widgets.Column(8);
        _review.MetaClicked += meta =>
        {
            if (meta.AsString() is { } link && link.StartsWith("step:") && int.TryParse(link[5..], out var step)) Go(step);
        };
        box.AddChild(_review);
        return box;
    }

    private void RefreshReview(ColonySim sim)
    {
        var data = sim.Scavenge!;
        var muted = Ui.Hex(Ui.Muted);
        string Link(int step, string text) => $"[url=step:{step}]{text}[/url]";
        var lines = new List<string>();
        if (_repeatNote is { } note) lines.Add($"[color=#{muted}][i]{note}[/i][/color]\n");
        if (SelectedZone is { } zoneId)
        {
            var trip = sim.PlanTravel(zoneId, SelectedVehicle);
            var arrival = sim.PlanArrival(zoneId, WaitFor, SelectedVehicle);
            var rules = sim.Data.Rules;
            var by = SelectedVehicle is { } vid && sim.FindVehicle(vid) is { } v ? $"by {v.Name}" : "on foot";
            lines.Add($"[b]{Link(Destination, data.Zones[zoneId].Name)}[/b] · {ColonyText.Trip(trip.Days)} each way {by} · arrives day {DayNight.Day(arrival.ArrivalTick, rules)}, " +
                      $"{DayNight.Hour(arrival.ArrivalTick, rules):00}:00 {arrival.Phase.ToString().ToUpperInvariant()}");
        }
        var party = _picked.OrderBy(kv => kv.Key).Select(kv =>
        {
            var s = sim.State.FindSurvivor(kv.Key);
            var gear = string.Join(", ", Slots.Where(kv.Value.Equipment.ContainsKey).Select(slot => data.Items[kv.Value.Equipment[slot]].Name));
            return $"{s?.Name ?? "?"}" + (gear.Length > 0 ? $" [color=#{muted}]({gear})[/color]" : $" [color=#{muted}](no gear)[/color]");
        }).ToList();
        lines.Add($"[b]{Link(Party, "Party")}[/b]  " + (party.Count == 0 ? $"[color=#{Ui.Hex(Ui.Bad)}]nobody yet[/color]" : string.Join(" · ", party)));
        var cost = $"{_food:0.#} food of {sim.FoodStock:0}";
        if (SelectedVehicle is { } fv && SelectedZone is { } fz)
            cost += $" · {sim.PlanTravel(fz, fv).FuelRoundTrip:0} fuel of {sim.State.Stockpile.Get(sim.Data.Vehicles.FuelResource):0}";
        lines.Add($"[b]{Link(Supplies, "Cost")}[/b]  {cost}");
        if (_warnings.Count > 0)
        {
            lines.Add("");
            foreach (var w in _warnings.OrderByDescending(w => w.Blocks))
                lines.Add($"[color=#{Ui.Hex(w.Blocks ? Palette.Danger : Palette.Warn)}]{(w.Blocks ? "✖" : "⚠")} {Link(w.Step, w.Text)}[/color]");
        }
        else lines.Add($"\n[color=#{Ui.Hex(Palette.Good)}]Ready to go.[/color]");
        var text = string.Join("\n", lines);
        if (_review.Text != text) _review.Text = text;
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Refreshes the step on screen only (others refresh when shown).</summary>
    private void RefreshStep(bool force = false)
    {
        var sim = _session.Sim;
        if (sim.Scavenge is null || !_planner.Visible) return;
        RefreshZones(sim);
        // The ration bill feeds the summary and the review; keep the vehicle list current for both.
        RefreshVehicles(sim);
        switch (_step)
        {
            case Destination: RefreshDestination(sim); break;
            case Party: RefreshParty(sim); break;
            case Gear: RefreshGear(sim, force); break;
            case Supplies: RefreshSupplies(sim); break;
            case Review: RefreshReview(sim); break;
        }
    }
}
