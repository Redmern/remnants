using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Plans and launches an expedition: zone picker (threat / reward), party picker for the adults at home with each
/// one's gear from the colony inventory and the field profile that gear gives them (movement mode, AP, attacks).
/// While a party is out it shows who and offers to play (or resume) the mission. Core validates the launch.
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
    private readonly VBoxContainer _rows = new();
    private readonly RichTextLabel _inventory = Ui.Rich();
    private readonly RichTextLabel _away = Ui.Rich();
    private readonly Button _launch;
    private readonly Button _play;
    private readonly Control _planner = new VBoxContainer();
    private readonly List<string> _zoneIds = new();
    private readonly Dictionary<int, Loadout> _picked = new();
    private readonly Dictionary<int, Label> _previews = new();
    private string _signature = "";

    /// <summary>Asks the screen to switch to the scavenge run.</summary>
    public event Action? PlayPressed;

    public ExpeditionPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(440, 0);
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Expedition"));
        box.AddChild(_away);
        _play = Ui.Button("Play mission", () => PlayPressed?.Invoke());
        box.AddChild(_play);

        var planner = (VBoxContainer)_planner;
        planner.AddThemeConstantOverride("separation", 6);
        box.AddChild(planner);
        var zoneRow = new HBoxContainer();
        zoneRow.AddChild(Ui.Label("Zone"));
        zoneRow.AddChild(_zone);
        planner.AddChild(zoneRow);
        planner.AddChild(_zoneInfo);
        planner.AddChild(_partyInfo);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 260), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rows);
        planner.AddChild(scroll);
        planner.AddChild(Ui.Heading("Inventory"));
        planner.AddChild(_inventory);
        _launch = Ui.Button("Launch expedition", Launch);
        planner.AddChild(_launch);

        _zone.ItemSelected += _ => RefreshZone();
    }

    public void Toggle()
    {
        Visible = !Visible;
        _signature = "";
        Refresh();
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
        _play.Visible = expedition is not null;
        if (expedition is not null)
        {
            var names = expedition.Members.Select(m => sim.State.FindSurvivor(m.SurvivorId)?.Name ?? "?");
            var state = expedition.Scavenge is { } run ? $"round {run.Round}" : "not started";
            _away.Text = $"[b]{data.Zones[expedition.ZoneId].Name}[/b]: {string.Join(", ", names)} ({state}).";
            _away.Visible = true;
            return;
        }
        _away.Visible = false;

        if (_zoneIds.Count == 0)
        {
            foreach (var zone in data.Zones.Values.OrderBy(z => z.Threat).ThenBy(z => z.Id))
            {
                _zoneIds.Add(zone.Id);
                _zone.AddItem($"{zone.Name}  (threat {zone.Threat}, loot tier {zone.LootTier})");
            }
            _zone.Select(0);
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
        sim.State.Present.Where(s => !s.IsChild && s.PatientAtBuildingId is null).OrderBy(s => s.Id);

    private void RefreshZone()
    {
        var data = _session.Sim.Scavenge!;
        if (_zone.Selected < 0) return;
        var zone = data.Zones[_zoneIds[_zone.Selected]];
        var mix = string.Join(", ", zone.EnemyMix.Select(e => data.Enemies[e.Id].Name));
        var threat = Ui.Hex(zone.Threat >= 3 ? Ui.Bad : zone.Threat == 2 ? new Color(0.95f, 0.75f, 0.4f) : Ui.Good);
        _zoneInfo.Text =
            $"[color=#{threat}]Threat {zone.Threat}[/color] · reward: loot tier {zone.LootTier}, {zone.Caches[0]}–{zone.Caches[1]} caches\n" +
            $"{zone.Width}×{zone.Height} tiles · {zone.Enemies[0]}–{zone.Enemies[1]} enemies ({mix}) · {zone.Npcs[0]}–{zone.Npcs[1]} strangers";
    }

    private void RebuildRows(ColonySim sim, List<Survivor> candidates)
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _previews.Clear();
        if (candidates.Count == 0) _rows.AddChild(Ui.Label("No adults at home and fit to go.", color: Ui.Muted));

        foreach (var s in candidates)
        {
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 2);
            _rows.AddChild(row);

            var pick = new CheckBox
            {
                Text = $"{s.Name}  ·  HP {s.Health:0}  ·  {ColonyText.Body(s.Body, sim.Scavenge).Replace("\n", ", ")}",
                ButtonPressed = _picked.ContainsKey(s.Id),
                FocusMode = FocusModeEnum.None,
            };
            var id = s.Id;
            pick.Toggled += on =>
            {
                if (on) _picked.TryAdd(id, new Loadout());
                else _picked.Remove(id);
                _signature = "";
                Refresh();
            };
            row.AddChild(pick);
            if (!_picked.TryGetValue(s.Id, out var loadout)) continue;

            var gear = new HBoxContainer();
            foreach (var slot in Slots) gear.AddChild(SlotPicker(sim, loadout, slot));
            row.AddChild(gear);
            row.AddChild(PackPicker(sim, loadout));

            var preview = Ui.Label(size: 13, color: Ui.Muted);
            preview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _previews[s.Id] = preview;
            row.AddChild(preview);
            UpdatePreview(sim, s, loadout);
        }
    }

    /// <summary>How many of an item are still free, after what the other picked members already take.</summary>
    private int Free(ColonySim sim, string itemId, Loadout except) =>
        sim.State.Inventory.Count(itemId)
        - _picked.Values.Where(l => l != except).Sum(l => l.Equipment.Values.Count(v => v == itemId) + l.Pack.Count(v => v == itemId));

    private OptionButton SlotPicker(ColonySim sim, Loadout loadout, EquipSlot slot)
    {
        var data = sim.Scavenge!;
        var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true };
        var ids = new List<string?> { null };
        picker.AddItem($"no {slot.ToString().ToLowerInvariant()}");
        var current = loadout.Equipment.GetValueOrDefault(slot);
        foreach (var (itemId, _) in sim.State.Inventory.Counts.Where(kv => data.Items[kv.Key].Slot == slot))
        {
            // What this slot could take: free stock minus what this member already uses elsewhere.
            var available = Free(sim, itemId, loadout) - loadout.Pack.Count(p => p == itemId)
                            - loadout.Equipment.Count(kv => kv.Key != slot && kv.Value == itemId);
            if (available <= 0) continue;
            ids.Add(itemId);
            picker.AddItem($"{data.Items[itemId].Name} ({available - (current == itemId ? 1 : 0)} left)");
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
        var picker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true };
        var ids = new List<string>();
        foreach (var (itemId, _) in sim.State.Inventory.Counts)
        {
            var free = Free(sim, itemId, loadout) - loadout.Equipment.Values.Count(v => v == itemId) - loadout.Pack.Count(v => v == itemId);
            if (free <= 0) continue;
            ids.Add(itemId);
            picker.AddItem($"{data.Items[itemId].Name} ({free} left)");
        }
        picker.Disabled = ids.Count == 0;
        row.AddChild(Ui.Label("Pack", 13, Ui.Muted));
        row.AddChild(picker);
        row.AddChild(Ui.Button("+", () =>
        {
            if (picker.Selected < 0) return;
            loadout.Pack.Add(ids[picker.Selected]);
            _signature = "";
            Refresh();
        }, 30));
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
        if (!_previews.TryGetValue(s.Id, out var label)) return;
        label.Text = ColonyText.Profile(sim.PreviewProfile(s, new(loadout.Equipment), new(loadout.Pack)));
    }

    private void RefreshSummary()
    {
        var sim = _session.Sim;
        var rules = sim.Data.Rules.Expedition;
        var food = rules.RationFoodPerMember * _picked.Count;
        _partyInfo.Text = $"Party {_picked.Count}/{sim.MaxPartySize} (Town Hall L{sim.TownHallLevel}) · rations {food:0} food " +
                          $"(have {sim.State.Stockpile.Get(sim.Data.Rules.Food.ResourceId):0}) · away members don't eat at home";
        _launch.Disabled = _picked.Count == 0;
        var data = sim.Scavenge!;
        _inventory.Text = sim.State.Inventory.Counts.Count == 0
            ? "[color=#999]empty[/color]"
            : string.Join(", ", sim.State.Inventory.Counts.Select(kv => $"{data.Items[kv.Key].Name} {kv.Value}"));
    }

    private void Launch()
    {
        if (_zone.Selected < 0) return;
        var members = _picked.OrderBy(kv => kv.Key)
            .Select(kv => new ExpeditionMember(kv.Key, new(kv.Value.Equipment), new(kv.Value.Pack)))
            .ToList();
        if (!_session.Execute(new LaunchExpedition(_zoneIds[_zone.Selected], members)).Ok) return;
        _picked.Clear();
        _signature = "";
        PlayPressed?.Invoke();
    }
}
