using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The colony's top bar: clock and speed, a chip per resource (amount, change over the last day, a tooltip that
/// explains it: what makes it and, for food, what is eaten), population, beds and food pressure, and the Build /
/// Items / Expedition / World map / Menu buttons with their hotkeys.
/// </summary>
public partial class TopBar : PanelContainer
{
    /// <summary>Resources always shown; others appear once the colony has some.</summary>
    private static readonly HashSet<string> Staples = ["wood", "stone", "scrap", "food", "medicine"];

    private readonly ColonySession _session;
    private readonly Label _day = Widgets.Label("", "Display");
    private readonly Label _hour = Widgets.Label("", "Number");
    private readonly TextureRect _phaseIcon = new() { StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(26, 26) };
    private readonly Label _phase = Widgets.Label("", "Small");
    private DayPhase? _shownPhase;
    private readonly Dictionary<int, Button> _speedButtons = new();
    private readonly Dictionary<string, ResourceChip> _chips = new();
    private readonly PanelContainer _pop;
    private readonly PanelContainer _beds;
    private readonly PanelContainer _food;
    private readonly ResourceHistory _history = new();

    public event Action? BuildPressed;
    public event Action? ExpeditionPressed;
    public event Action? ItemsPressed;
    public event Action? WorldMapPressed;
    public event Action? MenuPressed;

    public TopBar(ColonySession session)
    {
        _session = session;
        AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.RustDark), 10, 4));
        var row = Widgets.Row(10);
        AddChild(row);

        // Sun / moon and the phase, then day and hour; the tooltip says what the hour means for the colony.
        var phaseBox = Widgets.Column(-2);
        phaseBox.MouseFilter = MouseFilterEnum.Pass;
        phaseBox.AddChild(_phaseIcon);
        _phase.HorizontalAlignment = HorizontalAlignment.Center;
        _phase.AddThemeFontSizeOverride("font_size", 11);
        phaseBox.AddChild(_phase);
        row.AddChild(phaseBox);

        var clock = Widgets.Column(-4);
        _day.AddThemeFontSizeOverride("font_size", 18);
        _hour.AddThemeColorOverride("font_color", Palette.TextMuted);
        _hour.AddThemeFontSizeOverride("font_size", 14);
        clock.AddChild(_day);
        clock.AddChild(_hour);
        clock.CustomMinimumSize = new Vector2(64, 0);
        row.AddChild(clock);

        var speeds = Widgets.Row(2);
        foreach (var (speed, icon, label, action) in new[]
                 {
                     (0, "ui_pause", "Pause", "toggle_pause"), (1, "ui_play", "Normal speed", "speed_1"),
                     (2, "ui_fast", "Fast (2×)", "speed_2"), (4, "ui_faster", "Fastest (4×)", "speed_3"),
                 })
        {
            var button = Widgets.IconButton(icon, "", label, () => _session.SetSpeed(speed), Hotkeys.KeyName(action), "Tab");
            button.ToggleMode = true;
            button.CustomMinimumSize = new Vector2(30, 30);
            _speedButtons[speed] = button;
            speeds.AddChild(button);
        }
        row.AddChild(speeds);
        row.AddChild(new VSeparator());

        var chips = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = FlowContainer.AlignmentMode.Center };
        chips.AddThemeConstantOverride("h_separation", 4);
        chips.AddThemeConstantOverride("v_separation", 4);
        foreach (var res in session.Sim.Data.Resources.Keys)
        {
            var chip = new ResourceChip(res);
            _chips[res] = chip;
            chips.AddChild(chip);
        }
        row.AddChild(chips);
        row.AddChild(new VSeparator());

        _pop = Widgets.Chip(Icons.Get("stat_pop", 16), "");
        _beds = Widgets.Chip(Icons.Get("stat_beds", 16), "");
        _food = Widgets.Chip(Icons.Get("res_food", 16), "");
        row.AddChild(_pop);
        row.AddChild(_beds);
        row.AddChild(_food);
        row.AddChild(new VSeparator());

        row.AddChild(Widgets.IconButton("ui_build", "", "Build", () => BuildPressed?.Invoke(), Hotkeys.KeyName("open_build")));
        row.AddChild(Widgets.IconButton("ui_items", "", "Items and blueprints", () => ItemsPressed?.Invoke(), Hotkeys.KeyName("open_items")));
        row.AddChild(Widgets.IconButton("ui_expedition", "", "Expedition", () => ExpeditionPressed?.Invoke(), Hotkeys.KeyName("open_expedition")));
        row.AddChild(Widgets.IconButton("ui_world_map", "", "World map", () => WorldMapPressed?.Invoke()));
        row.AddChild(Widgets.IconButton("ui_menu", "", "Menu: save, load, settings", () => MenuPressed?.Invoke(), Hotkeys.KeyName("pause_menu")));
    }

    /// <summary>The Build hotkey does what the button does.</summary>
    public void EmitBuild() => BuildPressed?.Invoke();

    private void RefreshPhase(ColonySim sim)
    {
        var phase = sim.Phase;
        if (phase != _shownPhase)
        {
            _shownPhase = phase;
            var (icon, color) = PhaseLook(phase);
            _phaseIcon.Texture = Icons.Get(icon, 22, "stat_day");
            _phaseIcon.Modulate = color;
            _phase.Text = phase.ToString().ToUpperInvariant();
            _phase.AddThemeColorOverride("font_color", color);
        }
        var tip = ColonyText.PhaseTip(sim);
        _phaseIcon.TooltipText = _phase.TooltipText = _day.TooltipText = _hour.TooltipText = tip;
    }

    public static (string Icon, Color Color) PhaseLook(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => ("phase_dawn", Palette.Rust),
        DayPhase.Day => ("phase_day", Palette.Warn),
        DayPhase.Dusk => ("phase_dusk", Palette.Amber),
        _ => ("phase_night", Palette.Night),
    };

    public void Refresh()
    {
        var sim = _session.Sim;
        var rules = sim.Data.Rules;
        _day.Text = $"DAY {sim.State.Tick / rules.TicksPerDay + 1}";
        _hour.Text = $"{sim.State.Tick % rules.TicksPerDay:00}:00";
        RefreshPhase(sim);
        foreach (var (speed, button) in _speedButtons) button.SetPressedNoSignal(speed == _session.Speed);

        _history.Record(sim);
        foreach (var (id, chip) in _chips)
        {
            var amount = sim.State.Stockpile.Get(id);
            var perDay = _history.PerDay(id, amount, sim.State.Tick, rules.TicksPerDay);
            chip.Visible = Staples.Contains(id) || amount >= 0.5 || perDay is { } d && Math.Abs(d) > 0.05;
            chip.Set(amount, perDay, ResourceTip(sim, id, amount, perDay));
        }
        RefreshPressure(sim);
    }

    private void RefreshPressure(ColonySim sim)
    {
        var people = sim.State.Survivors.Count;
        var away = sim.State.Survivors.Count(s => s.Away);
        SetChip(_pop, away > 0 ? $"{people} ({away} away)" : $"{people}", Palette.Text,
            $"Population {people}" + (away > 0 ? $", {away} away on an expedition (they eat rations, not colony food)" : ""));

        var homeless = sim.State.Survivors.Count(s => !s.HasBed);
        var expecting = sim.State.Survivors.Count(s => s.Pregnancy is not null);
        var free = Bonds.FreeBedsForBabies(sim.State, sim.Data);
        var bedsBad = homeless > 0 || free < 0;
        var bedsTight = free == 0;
        var bedNote = homeless > 0 ? $"{homeless} without a bed: they leave when their grace time runs out."
            : free < 0 ? $"Beds short for {expecting} on the way."
            : free == 0 ? "Beds full: no room for babies, and recruits will have nowhere to sleep."
            : $"{free} bed{(free == 1 ? "" : "s")} free.";
        SetChip(_beds, $"{sim.State.Survivors.Count(s => s.HasBed)}/{sim.BedCapacity}{(bedsBad ? " !" : "")}",
            bedsBad ? Palette.Danger : bedsTight ? Palette.Warn : Palette.Text,
            $"Beds taken / beds\n{bedNote}\nCouples only have children when a bed is free.", bedsBad ? "stat_warning" : "stat_beds");

        var days = sim.FoodDays;
        var foodColor = days < 3 ? Palette.Danger : days < 7 ? Palette.Warn : Palette.Good;
        var daysText = double.IsInfinity(days) ? "∞" : days >= 99 ? "99+d" : $"{days:0.#}d";
        SetChip(_food, daysText + (days < 3 ? " !" : ""), foodColor,
            $"Food lasts about {daysText} at {sim.FoodPerDay:0.#} eaten per day.\nInfants and children are fed first; the hungry lose health.",
            days < 3 ? "stat_starving" : "res_food");
    }

    private static void SetChip(PanelContainer chip, string text, Color color, string tooltip, string? icon = null)
    {
        var row = chip.GetChild(0);
        var label = row.GetChild<Label>(1);
        label.Text = text;
        label.AddThemeColorOverride("font_color", color);
        var rect = row.GetChild<TextureRect>(0);
        if (icon is not null) rect.Texture = Icons.Get(icon, 16);
        rect.Modulate = color == Palette.Text ? Palette.TextMuted : color;
        chip.TooltipText = tooltip;
    }

    /// <summary>"Wood 114 — last day +12. Made by Woodcutter L1: 2 workers, about 14/day."</summary>
    private static string ResourceTip(ColonySim sim, string id, double amount, double? perDay)
    {
        var lines = new List<string> { $"{sim.Data.Resources[id].Name}: {Fmt.Amount(amount)}" };
        if (perDay is { } d) lines.Add($"Change over the last day: {Fmt.Delta(d)}");
        var makers = sim.State.Buildings.Where(b => sim.Data.Buildings[b.DefId].Produces == id).ToList();
        foreach (var b in makers)
        {
            var workers = sim.State.WorkersAt(b.Id).Count();
            lines.Add($"Made by {ColonyText.BuildingName(sim, b)}: {workers} worker{(workers == 1 ? "" : "s")}, about {sim.OutputPerDay(b):0.#}/day");
        }
        if (id == sim.Data.Rules.Food.ResourceId) lines.Add($"Eaten: about {sim.FoodPerDay:0.#}/day by the people at home");
        if (makers.Count == 0 && id != sim.Data.Rules.Food.ResourceId)
            lines.Add(sim.Data.Recipes.Values.Any(r => r.Resource == id) ? "Crafted in a production building." : "Found by scavenging and spent on building.");
        return string.Join("\n", lines);
    }

    /// <summary>Stockpile snapshots by tick, for the per-day change. Resets when time runs backwards (a load).</summary>
    private sealed class ResourceHistory
    {
        private readonly SortedDictionary<long, Dictionary<string, double>> _byTick = new();

        public void Record(ColonySim sim)
        {
            var tick = sim.State.Tick;
            if (_byTick.Count > 0 && _byTick.Keys.Last() > tick) _byTick.Clear();
            if (_byTick.ContainsKey(tick)) return;
            _byTick[tick] = sim.Data.Resources.Keys.ToDictionary(r => r, r => sim.State.Stockpile.Get(r));
            while (_byTick.Count > 0 && _byTick.Keys.First() < tick - sim.Data.Rules.TicksPerDay) _byTick.Remove(_byTick.Keys.First());
        }

        /// <summary>The change since the oldest snapshot within a day, scaled to a day; null with under a few hours of history.</summary>
        public double? PerDay(string id, double now, long tick, int ticksPerDay)
        {
            if (_byTick.Count == 0) return null;
            var (oldTick, old) = _byTick.First();
            var span = tick - oldTick;
            if (span < 4) return null;
            return (now - old.GetValueOrDefault(id)) * ticksPerDay / span;
        }
    }
}
