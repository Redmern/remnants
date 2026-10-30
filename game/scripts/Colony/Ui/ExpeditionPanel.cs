using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Plans and launches an expedition: zone picker (known zones only, with what the colony knows: threat / reward /
/// Linked presence / travel), a World map button, party picker for the adults at home with each one's gear from the
/// colony inventory and the field profile that gear gives them (movement mode, AP, attacks). While a party is out it
/// shows who and where they are (on the road, in the zone, heading home) and offers to play (or resume) the mission
/// once they have arrived, or to take the party online (Online / Co-op: the multiplayer menu brings exactly these
/// survivors and their gear; launched under the same rules, rations included). Core validates the launch.
/// </summary>
public partial class ExpeditionPanel : PanelContainer
{
    private sealed class Loadout
    {
        public Dictionary<EquipSlot, string> Equipment { get; } = new();
        public List<string> Pack { get; } = new();
    }

    private static readonly EquipSlot[] Slots = [EquipSlot.Weapon, EquipSlot.Body, EquipSlot.Tool];

    private readonly ColonySession _session;
    private readonly OptionButton _zone = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
    private readonly RichTextLabel _zoneInfo = Ui.Rich();
    private readonly Label _partyInfo = Ui.Label(color: Ui.Muted);
    private readonly OptionButton _wait = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
    private readonly TextureRect _arrivalIcon = new() { StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(22, 22) };
    private readonly Label _arrival = Ui.Label();
    private readonly Label _arrivalNote = Ui.Label(color: Ui.Muted);

    /// <summary>The wait choices, in the order of <see cref="_wait"/>: go now, or wait for a phase.</summary>
    private static readonly DayPhase?[] WaitChoices = [null, DayPhase.Dawn, DayPhase.Day, DayPhase.Dusk, DayPhase.Night];
    private readonly VBoxContainer _rows = new();
    private readonly MarginContainer _inventory = new();
    private readonly RichTextLabel _away = Ui.Rich();
    private readonly Button _launch;
    private readonly Button _play;
    private readonly Button _online;
    private readonly Button _launchOnline;
    private readonly Control _planner = new VBoxContainer();
    private readonly List<string> _zoneIds = new();
    private readonly Dictionary<int, Loadout> _picked = new();
    private readonly Dictionary<int, MarginContainer> _previews = new();
    private string _signature = "";
    private string _inventoryShown = "";

    /// <summary>Asks the screen to switch to the scavenge run.</summary>
    public event Action? PlayPressed;

    /// <summary>Asks the screen to open the world map.</summary>
    public event Action? WorldMapPressed;

    /// <summary>Asks the screen to take the waiting party into an online (co-op or versus) mission.</summary>
    public event Action? OnlinePressed;

    public ExpeditionPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(560, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this, 8);
        box.AddChild(Ui.TitleRow("Expedition", "ui_expedition", Toggle, Hotkeys.KeyName("open_expedition")));
        box.AddChild(_away);
        _play = Widgets.IconButton("ui_play", "Play mission", "Go to the zone and play the run", () => PlayPressed?.Invoke(), variation: "Primary");
        _play.CustomMinimumSize = new Vector2(0, 38);
        _online = Widgets.IconButton("stat_ally", "Online / Co-op",
            "Bring this party to a multiplayer mission (host or join a co-op or versus game); the result comes home here",
            () => OnlinePressed?.Invoke());
        _online.CustomMinimumSize = new Vector2(0, 38);
        var playRow = Widgets.Row(6);
        _play.SizeFlagsHorizontal = _online.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        playRow.AddChild(_play);
        playRow.AddChild(_online);
        box.AddChild(playRow);

        var planner = (VBoxContainer)_planner;
        planner.AddThemeConstantOverride("separation", 8);
        box.AddChild(planner);
        planner.AddChild(Widgets.SectionHeader("Destination", "ui_world_map"));
        var zoneRow = Widgets.Row(6);
        zoneRow.AddChild(_zone);
        zoneRow.AddChild(Widgets.IconButton("ui_world_map", "World map", "Open the world map", () => WorldMapPressed?.Invoke()));
        planner.AddChild(zoneRow);
        planner.AddChild(_zoneInfo);
        planner.AddChild(Widgets.SectionHeader("Arrival", "stat_time"));
        var arrivalRow = Widgets.Row(6);
        arrivalRow.AddChild(_arrivalIcon);
        _arrival.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        arrivalRow.AddChild(_arrival);
        foreach (var choice in WaitChoices) _wait.AddItem(choice is { } p ? $"Wait for {p.ToString().ToLowerInvariant()}" : "Go now");
        _wait.TooltipText = "Wait outside the zone for another time of day. Each day waited costs extra rations per member.";
        _wait.CustomMinimumSize = new Vector2(150, 0);
        arrivalRow.AddChild(_wait);
        planner.AddChild(arrivalRow);
        _arrivalNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        planner.AddChild(_arrivalNote);
        planner.AddChild(Widgets.SectionHeader("Party", "stat_pop"));
        _partyInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        planner.AddChild(_partyInfo);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);
        planner.AddChild(scroll);
        planner.AddChild(Widgets.SectionHeader("Colony items", "ui_items"));
        planner.AddChild(_inventory);
        _launch = Widgets.IconButton("ui_expedition", "Launch expedition", "Send the party out", () => Launch(online: false), variation: "Primary");
        _launch.CustomMinimumSize = new Vector2(0, 38);
        _launchOnline = Widgets.IconButton("stat_ally", "Launch online / co-op",
            "Send the party out as usual (adults only, Town Hall cap, gear and rations from the colony) and take it into a multiplayer mission",
            () => Launch(online: true));
        _launchOnline.CustomMinimumSize = new Vector2(0, 38);
        var launchRow = Widgets.Row(6);
        _launch.SizeFlagsHorizontal = _launchOnline.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        launchRow.AddChild(_launch);
        launchRow.AddChild(_launchOnline);
        planner.AddChild(launchRow);

        _zone.ItemSelected += _ => RefreshZone();
        _wait.ItemSelected += _ => RefreshSummary();
    }

    public void Toggle()
    {
        Motion.Toggle(this);
        _signature = "";
        Refresh();
    }

    /// <summary>Opens the planner on a zone (the world map's "Plan expedition").</summary>
    public void PlanFor(string zoneId)
    {
        Visible = true;
        _signature = "";
        Refresh();
        var index = _zoneIds.IndexOf(zoneId);
        if (index < 0) return;
        _zone.Select(index);
        RefreshZone();
        RefreshSummary();
    }

    /// <summary>Debug / screenshots: tick these survivors in the party picker.</summary>
    public void Pick(IEnumerable<int> survivorIds)
    {
        foreach (var id in survivorIds) _picked.TryAdd(id, new Loadout());
        _signature = "";
        Refresh();
    }

    public void Refresh()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        if (sim.Scavenge is not { } data) return;

        var expedition = sim.State.Expedition;
        _planner.Visible = expedition is null;
        _play.Visible = _online.Visible = expedition is not null;
        if (expedition is not null)
        {
            var names = expedition.Members.Select(m => sim.State.FindSurvivor(m.SurvivorId)?.Name ?? "?");
            var phase = expedition.PhaseAt(sim.State.Tick);
            var state = phase switch
            {
                ExpeditionPhase.Outbound => $"on the road, arriving in {expedition.ArrivalTick - sim.State.Tick} h",
                ExpeditionPhase.Returning => $"heading home, back in {expedition.ReturnTick - sim.State.Tick} h",
                _ => expedition.Scavenge is { } run ? $"round {run.Round}" : "in the zone, not started",
            };
            _away.Text = $"[b]{data.Zones[expedition.ZoneId].Name}[/b]: {string.Join(", ", names)} ({state}).";
            _away.Visible = true;
            _play.Disabled = phase != ExpeditionPhase.InZone;
            // A run already started locally stays local; a fresh one can go online instead.
            _online.Disabled = phase != ExpeditionPhase.InZone || expedition.Scavenge is not null;
            return;
        }
        _away.Visible = false;

        // Only zones the colony knows of; the list grows as visits reveal more.
        var known = sim.WorldZones().Where(z => z.Intel != ZoneIntel.Unknown).ToList();
        if (!known.Select(z => z.Zone.Id).SequenceEqual(_zoneIds))
        {
            var selected = _zone.Selected >= 0 && _zone.Selected < _zoneIds.Count ? _zoneIds[_zone.Selected] : null;
            _zoneIds.Clear();
            _zone.Clear();
            foreach (var info in known)
            {
                _zoneIds.Add(info.Zone.Id);
                var threat = info.Intel == ZoneIntel.Scouted ? $"threat {info.Zone.Threat}, loot tier {info.Zone.LootTier}" : "rumored";
                _zone.AddItem($"{info.Zone.Name}  ({threat}, {ColonyText.Travel(info.Zone.TravelDays)})");
            }
            _zone.Select(Math.Max(0, selected is null ? 0 : _zoneIds.IndexOf(selected)));
        }

        var candidates = Candidates(sim).ToList();
        var signature = string.Join(",", candidates.Select(s => s.Id)) + "|" + string.Join(",", sim.State.Inventory.Counts.Select(kv => $"{kv.Key}:{kv.Value}"));
        if (signature != _signature)
        {
            _signature = signature;
            foreach (var id in _picked.Keys.Where(id => candidates.All(s => s.Id != id)).ToList()) _picked.Remove(id);
            RebuildRows(sim, candidates);
        }
        RefreshZone();
        RefreshSummary();
    }

    private static IEnumerable<Survivor> Candidates(ColonySim sim) =>
        sim.State.Present.Where(s => !s.IsChild && s.PatientAtBuildingId is null && s.Pregnancy is null).OrderBy(s => s.Id);

    private string? SelectedZone => _zone.Selected >= 0 && _zone.Selected < _zoneIds.Count ? _zoneIds[_zone.Selected] : null;

    private void RefreshZone()
    {
        if (SelectedZone is not { } id) return;
        _zoneInfo.Text = ColonyText.ZoneSummary(_session.Sim, _session.Sim.ZoneInfo(id));
        RefreshSummary();
    }

    private void RebuildRows(ColonySim sim, List<Survivor> candidates)
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _previews.Clear();
        if (candidates.Count == 0) _rows.AddChild(Ui.Label("No adults at home and fit to go.", color: Ui.Muted));

        foreach (var s in candidates)
        {
            var picked = _picked.TryGetValue(s.Id, out var loadout);
            var card = new PanelContainer { ThemeTypeVariation = picked ? "CardSelected" : "Card" };
            var row = Widgets.Column(4);
            card.AddChild(row);
            _rows.AddChild(card);

            var head = Widgets.Row(8);
            var portrait = new Portrait(40) { Selected = picked };
            portrait.Set(PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
            head.AddChild(portrait);
            var pick = new CheckBox
            {
                Text = s.Name,
                ButtonPressed = picked,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = picked ? "Leave at home" : "Take on the expedition",
            };
            var id = s.Id;
            pick.Toggled += on =>
            {
                if (on) _picked.TryAdd(id, new Loadout());
                else _picked.Remove(id);
                _signature = "";
                Refresh();
            };
            head.AddChild(pick);
            var hp = new StatBar("stat_hp", 90);
            hp.Set(s.Health / Survivor.MaxHealth, $"{s.Health:0}");
            hp.TooltipText = $"Health {s.Health:0}/{Survivor.MaxHealth:0}";
            head.AddChild(hp);
            var body = ColonyText.Body(s.Body, sim.Scavenge);
            var bodyIcon = Icons.Rect(Icons.Get(body == "Healthy" ? "body_full" : "stat_injured", 18), 18, body == "Healthy" ? Palette.Good : Palette.Warn);
            bodyIcon.TooltipText = body;
            bodyIcon.MouseFilter = MouseFilterEnum.Stop;
            head.AddChild(bodyIcon);
            row.AddChild(head);
            if (!picked) continue;

            var gear = Widgets.Row(4);
            foreach (var slot in Slots) gear.AddChild(SlotPicker(sim, loadout!, slot));
            row.AddChild(gear);
            row.AddChild(PackPicker(sim, loadout!));

            var preview = new MarginContainer();
            _previews[s.Id] = preview;
            row.AddChild(preview);
            UpdatePreview(sim, s, loadout!);
        }
    }

    /// <summary>How many of an item are still free, after what the other picked members already take.</summary>
    private int Free(ColonySim sim, string itemId, Loadout except) =>
        sim.State.Inventory.Count(itemId)
        - _picked.Values.Where(l => l != except).Sum(l => l.Equipment.Values.Count(v => v == itemId) + l.Pack.Count(v => v == itemId));

    private OptionButton SlotPicker(ColonySim sim, Loadout loadout, EquipSlot slot)
    {
        var data = sim.Scavenge!;
        var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
        var ids = new List<string?> { null };
        picker.AddIconItem(Icons.Get(slot switch { EquipSlot.Weapon => "item_weapon", EquipSlot.Body => "item_armor", _ => "item_tool" }, 16), $"no {slot.ToString().ToLowerInvariant()}");
        var current = loadout.Equipment.GetValueOrDefault(slot);
        foreach (var (itemId, _) in sim.State.Inventory.Counts.Where(kv => data.Items[kv.Key].Slot == slot))
        {
            // What this slot could take: free stock minus what this member already uses elsewhere.
            var available = Free(sim, itemId, loadout) - loadout.Pack.Count(p => p == itemId)
                            - loadout.Equipment.Count(kv => kv.Key != slot && kv.Value == itemId);
            if (available <= 0) continue;
            ids.Add(itemId);
            picker.AddIconItem(Icons.Item(data.Items[itemId].DefinedId, data.Items[itemId].Kind.ToString(), 16), $"{data.Items[itemId].Name} ({available - (current == itemId ? 1 : 0)} left)");
        }
        picker.Select(Math.Max(0, ids.IndexOf(current)));
        picker.ItemSelected += index =>
        {
            if (ids[(int)index] is { } chosen) loadout.Equipment[slot] = chosen;
            else loadout.Equipment.Remove(slot);
            _signature = "";
            Refresh();
        };
        return picker;
    }

    private Control PackPicker(ColonySim sim, Loadout loadout)
    {
        var data = sim.Scavenge!;
        var row = new HBoxContainer();
        var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
        var ids = new List<string>();
        foreach (var (itemId, _) in sim.State.Inventory.Counts)
        {
            var free = Free(sim, itemId, loadout) - loadout.Equipment.Values.Count(v => v == itemId) - loadout.Pack.Count(v => v == itemId);
            if (free <= 0) continue;
            ids.Add(itemId);
            picker.AddIconItem(Icons.Item(data.Items[itemId].DefinedId, data.Items[itemId].Kind.ToString(), 16), $"{data.Items[itemId].Name} ({free} left)");
        }
        picker.Disabled = ids.Count == 0;
        row.AddChild(Ui.Label("Pack", 13, Ui.Muted));
        row.AddChild(picker);
        row.AddChild(Widgets.IconButton("ui_plus", "", "Put one in the pack", () =>
        {
            if (picker.Selected < 0) return;
            loadout.Pack.Add(ids[picker.Selected]);
            _signature = "";
            Refresh();
        }));
        var contents = loadout.Pack.Count == 0 ? "empty" : string.Join(", ", loadout.Pack.GroupBy(p => p).Select(g => $"{data.Items[g.Key].Name}×{g.Count()}"));
        row.AddChild(Ui.Label(contents, 13));
        if (loadout.Pack.Count > 0)
            row.AddChild(Ui.Button("Clear", () =>
            {
                loadout.Pack.Clear();
                _signature = "";
                Refresh();
            }, 50));
        return row;
    }

    private void UpdatePreview(ColonySim sim, Survivor s, Loadout loadout)
    {
        if (!_previews.TryGetValue(s.Id, out var box)) return;
        Widgets.Clear(box);
        box.AddChild(ProfileChips.Build(sim.PreviewProfile(s, new(loadout.Equipment), new(loadout.Pack))));
    }

    private void RefreshSummary()
    {
        var sim = _session.Sim;
        var perMember = SelectedZone is { } zoneId ? sim.ZoneInfo(zoneId).RationsPerMember : sim.Data.Rules.Expedition.RationFoodPerMember;
        if (SelectedZone is { } zid) perMember += RefreshArrival(sim, zid);
        var food = perMember * _picked.Count;
        _partyInfo.Text = $"Party {_picked.Count}/{sim.MaxPartySize} (Town Hall L{sim.TownHallLevel}) · rations {food:0} food " +
                          $"(have {sim.FoodStock:0}) · away members don't eat at home";
        _launch.Disabled = _launchOnline.Disabled = _picked.Count == 0;
        var data = sim.Scavenge!;
        var signature = string.Join(",", sim.State.Inventory.Counts.Select(kv => $"{kv.Key}:{kv.Value}"));
        if (signature == _inventoryShown) return;
        _inventoryShown = signature;
        Widgets.Clear(_inventory);
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 4);
        flow.AddThemeConstantOverride("v_separation", 4);
        if (sim.State.Inventory.Counts.Count == 0) flow.AddChild(Widgets.Label("No items.", "Muted"));
        foreach (var (itemId, count) in sim.State.Inventory.Counts)
        {
            var def = data.Items[itemId];
            flow.AddChild(Widgets.Chip(Icons.Item(def.DefinedId, def.Kind.ToString(), 14), $"{def.Name} {count}", null, def.Kind.ToString()));
        }
        _inventory.AddChild(flow);
    }

    private DayPhase? WaitFor => WaitChoices[Math.Clamp(_wait.Selected, 0, WaitChoices.Length - 1)];

    /// <summary>When the party would arrive and what that phase means in the field; returns the wait's extra food per member.</summary>
    private float RefreshArrival(ColonySim sim, string zoneId)
    {
        var plan = sim.PlanArrival(zoneId, WaitFor);
        var rules = sim.Data.Rules;
        var (icon, color) = TopBar.PhaseLook(plan.Phase);
        _arrivalIcon.Texture = Icons.Get(icon, 20, "stat_time");
        _arrivalIcon.Modulate = color;
        _arrival.Text = $"Arrives day {DayNight.Day(plan.ArrivalTick, rules)}, {DayNight.Hour(plan.ArrivalTick, rules):00}:00 · {plan.Phase.ToString().ToUpperInvariant()}";
        _arrival.AddThemeColorOverride("font_color", color);
        var wait = plan.WaitTicks > 0 ? $"Waits {plan.WaitTicks} h outside: +{plan.WaitRationsPerMember:0} food each. " : "";
        _arrivalNote.Text = wait + plan.Phase switch
        {
            DayPhase.Night => "Night: your people see much less and are harder to spot — but not by the Linked's sensors or cameras. More Husks, and they hear further. Flares light further; muzzle flashes give shooters away.",
            DayPhase.Dawn or DayPhase.Dusk => "Half light: sight a little shorter, a little easier to slip by Husks.",
            _ => "Daylight: everyone sees well.",
        };
        _arrivalNote.AddThemeColorOverride("font_color", plan.Phase == DayPhase.Night ? Palette.Night : Ui.Muted);
        return (float)plan.WaitRationsPerMember;
    }

    private void Launch(bool online)
    {
        if (SelectedZone is not { } zoneId) return;
        var members = _picked.OrderBy(kv => kv.Key)
            .Select(kv => new ExpeditionMember(kv.Key, new(kv.Value.Equipment), new(kv.Value.Pack)))
            .ToList();
        if (!_session.Execute(new LaunchExpedition(zoneId, members, WaitFor)).Ok) return;
        _wait.Select(0);
        _picked.Clear();
        _signature = "";
        // A zone on the doorstep is played right away; a far one waits until the party has arrived.
        if (_session.Sim.CurrentExpeditionPhase != ExpeditionPhase.InZone) return;
        if (online) OnlinePressed?.Invoke();
        else PlayPressed?.Invoke();
    }
}
