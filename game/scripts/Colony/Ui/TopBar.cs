using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The colony's top bar, one row at every size (UX audit 4.1): phase, the weather now and one forecast chip ("Rain in
/// 6h", the full forecast in its tooltip), clock and speed; the core resources (food, wood, stone, scrap) as chips with
/// a ▲ / ▼ trend and the rest folded into a "+N" drawer; population, beds and food-days pressure chips and the power grid
/// once there is one (used / supply, battery, tinted on a brownout or a tripped breaker; its button opens the grid
/// overlay), each with an (i); and the actions (Build, Defend, Repairs, Items, Pantry, Expedition, World map, Menu):
/// labelled when the bar has room, else icons with the hotkey in the corner, else a 4×2 grid of icons.
/// </summary>
public partial class TopBar : PanelContainer
{
    /// <summary>The resources always on the bar; the others fold into the "+N" drawer.</summary>
    private static readonly string[] Core = ["food", "wood", "stone", "scrap"];

    private readonly ColonySession _session;
    private readonly Label _day = Widgets.Label("", "Display");
    private readonly Label _hour = Widgets.Label("", "Number");
    private readonly TextureRect _phaseIcon = new() { StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(26, 26) };
    private readonly Label _phase = Widgets.Label("", "Small");
    private DayPhase? _shownPhase;
    private readonly TextureRect _weatherIcon = new() { StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(26, 26) };
    private readonly Label _weather = Widgets.Label("", "Small");
    private readonly Control _weatherBox;
    private readonly Control _weatherRow;
    private readonly PanelContainer _forecast;
    private (string Kind, double Intensity)? _shownWeather;
    private readonly Dictionary<int, Button> _speedButtons = new();
    private readonly Dictionary<string, StockChip> _chips = new();
    private readonly ResourceDrawer _drawer = new();
    private readonly PanelContainer _pop;
    private readonly PanelContainer _beds;
    private readonly PanelContainer _food;
    private readonly ResourceHistory _history = new();
    private readonly Button _defend;
    private readonly Button _repairs;
    private readonly PanelContainer _power;
    private readonly PanelContainer _battery;
    private readonly Button _grid;
    private readonly VSeparator _powerRule = new();
    private readonly GridContainer _actions = new() { SizeFlagsVertical = SizeFlags.ShrinkCenter };
    private readonly List<(Button Button, string Label)> _actionButtons = new();
    private readonly HBoxContainer _row;
    private int _layout = -1;
    private string _fitKey = "";
    private readonly Unchanged _set = new();

    public event Action? BuildPressed;
    public event Action? DefendPressed;
    public event Action? RepairsPressed;
    public event Action? ExpeditionPressed;
    public event Action? ItemsPressed;

    /// <summary>The food-days chip or the pantry button was pressed.</summary>
    public event Action? PantryPressed;
    public event Action? WorldMapPressed;
    public event Action? MenuPressed;

    /// <summary>The grid button (or P): show or hide the power overlay on the town.</summary>
    public event Action? PowerPressed;

    public TopBar(ColonySession session)
    {
        _session = session;
        AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.RustDark), 10, 4));
        var row = Widgets.Row(Space.Sm);
        _row = row;
        AddChild(row);

        // Sun / moon and the phase, then day and hour; the tooltip says what the hour means for the colony.
        var phaseBox = Widgets.Column(-2);
        phaseBox.MouseFilter = MouseFilterEnum.Pass;
        phaseBox.AddChild(_phaseIcon);
        _phase.HorizontalAlignment = HorizontalAlignment.Center;
        _phase.AddThemeFontSizeOverride("font_size", 13);
        phaseBox.AddChild(_phase);
        row.AddChild(phaseBox);

        // The weather now (the tooltip says what it does) and the next change as one chip with the full forecast.
        _weatherBox = Widgets.Column(-2);
        _weatherBox.MouseFilter = MouseFilterEnum.Stop;
        _weatherIcon.MouseFilter = MouseFilterEnum.Ignore;
        _weather.MouseFilter = MouseFilterEnum.Ignore;
        _weatherBox.AddChild(_weatherIcon);
        _weather.HorizontalAlignment = HorizontalAlignment.Center;
        _weather.AddThemeFontSizeOverride("font_size", 13);
        _weatherBox.AddChild(_weather);
        var weatherRow = Widgets.Row(Space.Xs);
        weatherRow.MouseFilter = MouseFilterEnum.Pass;
        weatherRow.AddChild(_weatherBox);
        _forecast = Widgets.Chip(Icons.Get("weather_clear", 16), "");
        _forecast.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _forecast.GetChild(0).AddChild(new InfoTip("weather"));
        weatherRow.AddChild(_forecast);
        weatherRow.Visible = session.Sim.Data.Weather.Enabled;
        _weatherBox.Visible = weatherRow.Visible;
        _weatherRow = weatherRow;
        row.AddChild(weatherRow);

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
                     (2, "ui_fast", "Fast (2×)", "speed_2"), (4, "ui_faster", "Faster (4×)", "speed_3"),
                     (8, "ui_faster", "Fastest (8×): for long waits", "speed_4"),
                 })
        {
            // 4× and 8× share the double-arrow icon; the fastest says its speed so the two tell apart.
            var button = Motion.OnPress(Widgets.IconButton(icon, speed == 8 ? "8×" : "", label, () => _session.SetSpeed(speed), Hotkeys.KeyName(action), "Tab"));
            button.ToggleMode = true;
            button.FocusMode = FocusModeEnum.All;
            button.CustomMinimumSize = new Vector2(30, 30);
            _speedButtons[speed] = button;
            speeds.AddChild(button);
        }
        row.AddChild(speeds);
        row.AddChild(new VSeparator());

        // One fixed row of resources: the core four, then "+N" for the rest (eleven chips used to wrap to two rows).
        var chips = Widgets.Row(Space.Xs);
        chips.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        chips.Alignment = BoxContainer.AlignmentMode.Center;
        foreach (var res in Core.Where(session.Sim.Data.Resources.ContainsKey))
        {
            var chip = new StockChip(res);
            _chips[res] = chip;
            chips.AddChild(chip);
        }
        chips.AddChild(_drawer);
        row.AddChild(chips);
        row.AddChild(new VSeparator());

        _pop = PressureChip("stat_pop", "mood");
        _beds = PressureChip("stat_beds", "housing");
        _food = PressureChip("res_food", "food");
        row.AddChild(_pop);
        row.AddChild(_beds);
        row.AddChild(_food);
        _food.MouseDefaultCursorShape = CursorShape.PointingHand;
        _food.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) PantryPressed?.Invoke();
        };
        _power = PressureChip("stat_power", "power");
        _battery = Widgets.Chip(Icons.Get("stat_battery", 16), "");
        _grid = Widgets.IconButton("ui_power_grid", "", "Power grid overlay: who is fed, and switches", () => PowerPressed?.Invoke(), Hotkeys.KeyName("open_power"));
        _grid.ToggleMode = true;
        _grid.FocusMode = FocusModeEnum.All;
        KeyBadge.Attach(_grid, Hotkeys.KeyName("open_power"));
        row.AddChild(_powerRule);
        row.AddChild(_power);
        row.AddChild(_battery);
        row.AddChild(_grid);
        row.AddChild(new VSeparator());

        _actions.AddThemeConstantOverride("h_separation", 2);
        _actions.AddThemeConstantOverride("v_separation", 2);
        Action("ui_build", "Build", "Build", () => BuildPressed?.Invoke(), "open_build");
        _defend = Action("ui_defend", "Defend", "Defend: walls, posts, lights, traps", () => DefendPressed?.Invoke(), "open_defend");
        _defend.Icon = Icons.Get("ui_defend", 18, "stat_armor");
        _repairs = Action("ui_repair", "Repairs", "Repairs", () => RepairsPressed?.Invoke(), null);
        _repairs.Icon = Icons.Get("ui_repair", 18, "skill_crafting");
        Action("ui_items", "Items", "Items and blueprints", () => ItemsPressed?.Invoke(), "open_items");
        Action("res_food", "Pantry", "Pantry: food, spoilage and food days", () => PantryPressed?.Invoke(), null);
        Action("ui_expedition", "Expedition", "Expedition", () => ExpeditionPressed?.Invoke(), "open_expedition");
        Action("ui_world_map", "Map", "World map", () => WorldMapPressed?.Invoke(), null);
        Action("ui_menu", "Menu", "Menu: save, load, settings", () => MenuPressed?.Invoke(), "pause_menu");
        row.AddChild(_actions);
        Resized += () => Callable.From(FitActions).CallDeferred();
    }

    /// <summary>A top-bar action: icon (and a label when there is room), the hotkey in its tooltip and corner.</summary>
    private Button Action(string icon, string label, string tooltip, Action pressed, string? hotkey)
    {
        var key = hotkey is null ? null : Hotkeys.KeyName(hotkey);
        var button = Widgets.IconButton(icon, "", tooltip, pressed, key);
        button.FocusMode = FocusModeEnum.All;
        KeyBadge.Attach(button, key);
        _actionButtons.Add((button, label));
        _actions.AddChild(button);
        return button;
    }

    /// <summary>A pressure chip (people, beds, food days, power) with its (i) inside.</summary>
    private static PanelContainer PressureChip(string icon, string topic)
    {
        var chip = Widgets.Chip(Icons.Get(icon, 16), "");
        chip.GetChild(0).AddChild(new InfoTip(topic));
        return chip;
    }

    /// <summary>
    /// The richest action layout that keeps the bar on one line: labelled buttons in a row, icons in a row, or icons in
    /// a 4×2 grid. Measured again only when the width or what the bar shows changes.
    /// </summary>
    private void FitActions()
    {
        var width = GetParentAreaSize().X;
        var key = $"{width:0}|{_drawer.Visible}|{_power.Visible}|{_battery.Visible}|{_defend.Visible}|{_weatherRow.Visible}";
        if (key == _fitKey || width <= 0) return;
        _fitKey = key;
        for (var layout = 0; layout < 3; layout++)
        {
            ApplyLayout(layout);
            if (GetCombinedMinimumSize().X <= width) return;
        }
    }

    private void ApplyLayout(int layout)
    {
        if (layout == _layout) return;
        _layout = layout;
        var shown = _actionButtons.Count(b => b.Button.Visible);
        _actions.Columns = layout == 2 ? (shown + 1) / 2 : Math.Max(1, _actionButtons.Count);
        foreach (var (button, label) in _actionButtons)
        {
            var text = layout == 0 ? label : "";
            if (button == _repairs && _repairCount > 0) text = layout == 0 ? $"{label} {_repairCount}" : $"{_repairCount}";
            button.Text = text;
            button.CustomMinimumSize = new Vector2(text.Length == 0 ? 34 : 0, 32);
            foreach (var badge in button.GetChildren().OfType<KeyBadge>()) badge.Visible = layout != 0;
        }
    }

    /// <summary>The power overlay hotkey does what the grid button does.</summary>
    public void EmitPower() => PowerPressed?.Invoke();

    /// <summary>The Build hotkey does what the button does.</summary>
    public void EmitBuild() => BuildPressed?.Invoke();

    /// <summary>The Defend hotkey does what the button does.</summary>
    public void EmitDefend() => DefendPressed?.Invoke();

    /// <summary>Defend and Repairs only with raids loaded; Repairs counts the queue and what is damaged but not queued.</summary>
    private int _repairCount;
    private bool _repairsWarn;

    private void RefreshDefense(ColonySim sim)
    {
        _set.Visible(_defend, sim.Raids is not null);
        _set.Visible(_repairs, sim.Raids is not null);
        if (sim.Raids is not { } raids) return;
        var queued = sim.State.Defense.Repairs.Count;
        var waiting = RaidText.Unqueued(sim, raids).Count();
        if (_repairCount != queued + waiting)
        {
            _repairCount = queued + waiting;
            var layout = _layout;
            _layout = -1;
            ApplyLayout(Math.Max(0, layout));
        }
        _set.Tooltip(_repairs, Widgets.Tip($"Repairs: {queued} queued" + (waiting > 0 ? $", {waiting} damaged and not queued" : ""), null));
        if (_repairsWarn != waiting > 0)
        {
            _repairsWarn = waiting > 0;
            _repairs.AddThemeColorOverride("font_color", _repairsWarn ? Palette.Warn : Palette.Text);
        }
    }

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
        foreach (var c in new Control[] { _phaseIcon, _phase, _day, _hour }) _set.Tooltip(c, tip);
    }

    private void RefreshWeather(ColonySim sim)
    {
        if (!_weatherBox.Visible) return;
        var now = Weather.Current(sim.State, sim.Data);
        if (_shownWeather != (now.Kind, now.Intensity))
        {
            _shownWeather = (now.Kind, now.Intensity);
            var color = WeatherColor(now.Kind);
            _weatherIcon.Texture = Icons.Get($"weather_{now.Kind}", 22, "weather_clear");
            _weatherIcon.Modulate = color;
            _weather.Text = ColonyText.WeatherName(sim, now.Kind, now.Intensity).ToUpperInvariant();
            _weather.AddThemeColorOverride("font_color", color);
        }
        // The forecast and hours left move with the clock: the next change as "Rain in 6h", the rest in the tooltip.
        var tip = ColonyText.WeatherTip(sim);
        _set.Tooltip(_weatherBox, tip);
        var forecast = Weather.Forecast(sim.State, sim.Data);
        var next = forecast.Entries.FirstOrDefault(e => !e.Current && e.Called != now.Kind);
        var rules = sim.Data.Rules;
        var text = next is null ? (forecast.Days > 0 ? "Settled" : "No forecast")
            : $"{ColonyText.WeatherName(sim, next.Called, next.Intensity)} in {ColonyText.Duration((int)Math.Max(1, next.Start - sim.State.Tick), rules)}";
        SetChip(_forecast, text, next is null ? Palette.TextMuted : Palette.Text,
            (next is null ? "No change in sight.\n" : "") + tip + (forecast.Days > 0 ? $"\nForecast: {forecast.Days} day{(forecast.Days == 1 ? "" : "s")} ahead, {forecast.Accuracy * 100:0}% sure." : ""),
            next is null ? "weather_clear" : $"weather_{next.Called}");
        if (next is not null) _set.Modulate(_forecast.GetChild(0).GetChild<TextureRect>(0), WeatherColor(next.Called));
    }

    /// <summary>Calm weather muted, wet weather cool, storms in warning colours.</summary>
    public static Color WeatherColor(string kind) => kind switch
    {
        "clear" => Palette.Warn,
        "rain" or "fog" or "overcast" => Palette.TextMuted,
        "thunderstorm" => Palette.Night,
        "dust_storm" => Palette.Rust,
        _ => Palette.TextMuted,
    };

    public static (string Icon, Color Color) PhaseLook(DayPhase phase) => phase switch
    {
        DayPhase.Dawn => ("phase_dawn", Palette.Rust),
        DayPhase.Day => ("phase_day", Palette.Warn),
        DayPhase.Dusk => ("phase_dusk", Palette.Amber),
        _ => ("phase_night", Palette.Night),
    };

    /// <summary>The clock runs between ticks too (in 10-minute steps), so a slow day does not sit on one hour.</summary>
    public override void _Process(double delta) => RefreshClock();

    private void RefreshClock()
    {
        var sim = _session.Sim;
        var hour = sim.State.Tick % sim.Data.Rules.TicksPerDay;
        var minutes = (int)(_session.TickProgress * 6) * 10;
        var text = $"{hour:00}:{minutes:00}";
        if (_hour.Text != text) _hour.Text = text;
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var rules = sim.Data.Rules;
        _day.Text = $"DAY {sim.State.Tick / rules.TicksPerDay + 1}";
        RefreshClock();
        RefreshPhase(sim);
        RefreshWeather(sim);
        foreach (var (speed, button) in _speedButtons) button.SetPressedNoSignal(speed == _session.Speed);

        _history.Record(sim);
        foreach (var (id, chip) in _chips)
        {
            var amount = sim.State.Stockpile.Get(id);
            var perDay = _history.PerDay(id, amount, sim.State.Tick, rules.TicksPerDay);
            chip.Set(amount, perDay, ResourceTip(sim, id, amount, perDay));
        }
        var folded = new List<string>();
        foreach (var (id, def) in sim.Data.Resources)
        {
            if (_chips.ContainsKey(id)) continue;
            var amount = sim.State.Stockpile.Get(id);
            var perDay = _history.PerDay(id, amount, sim.State.Tick, rules.TicksPerDay);
            var shown = amount >= 0.5 || perDay is { } d && Math.Abs(d) > 0.05;
            if (shown) folded.Add(def.Name);
            _drawer.Set(id, def.Name, amount, perDay, ResourceTip(sim, id, amount, perDay), shown);
        }
        _drawer.SetCount(folded.Count, string.Join(", ", folded));
        RefreshPressure(sim);
        RefreshDefense(sim);
        RefreshPower(sim);
        FitActions();
    }

    /// <summary>Shows the overlay toggle as pressed or not.</summary>
    public void SetPowerOverlay(bool on) => _grid.SetPressedNoSignal(on);

    /// <summary>Supply and demand, battery and trouble; hidden until a generator or battery bank stands (the early game is unchanged).</summary>
    private void RefreshPower(ColonySim sim)
    {
        var nodes = sim.PowerNodes();
        var grid = nodes.Any(n => n.Role is PowerRole.Generator or PowerRole.Storage);
        _power.Visible = _battery.Visible = _grid.Visible = _powerRule.Visible = grid;
        if (!grid) return;
        var p = sim.State.Power;
        var tripped = sim.State.Tick < p.BreakerUntil;
        var color = tripped ? Palette.Danger : p.Brownout ? Palette.Warn : Palette.Text;
        var shed = nodes.Count(n => n.Role is PowerRole.Consumer && n.On && !n.Fed);
        var generators = nodes.Where(n => n.Role == PowerRole.Generator).ToList();
        var tip = new List<string>
        {
            $"Power: {p.Used:0.#} used of {p.Capacity:0.#} available this hour (demand {p.Demand:0.#}).",
            string.Join("\n", generators.Select(g => $"  {sim.Data.Buildings[g.DefId].Name}: {g.Amount:0.#} ({(g.On ? g.Note : "off")})")),
        };
        if (tripped) tip.Add($"BREAKER TRIPPED: every consumer is dark for {p.BreakerUntil - sim.State.Tick} h. Switch something off or add power.");
        else if (p.Brownout) tip.Add($"Brownout: {shed} building{(shed == 1 ? "" : "s")} not fed (lowest priority first).");
        tip.Add("Open the grid overlay (P) to see who is fed and switch buildings.");
        SetChip(_power, (tripped ? "TRIPPED " : "") + $"{p.Used:0.#}/{p.Capacity:0.#}" + (p.Brownout || tripped ? " !" : ""), color,
            string.Join("\n", tip.Where(t => t.Length > 0)), tripped || p.Brownout ? "stat_warning" : "stat_power");

        var cap = sim.PowerStorageCapacity;
        _battery.Visible = cap > 0;
        if (cap <= 0) return;
        var share = Math.Clamp(p.Stored / cap, 0, 1);
        SetChip(_battery, $"{share * 100:0}%", share < 0.15 ? Palette.Warn : Palette.Text,
            $"Batteries: {p.Stored:0.#} of {cap:0.#} stored. Spare solar charges them; they cover the night.");
    }

    private void RefreshPressure(ColonySim sim)
    {
        var people = sim.State.Survivors.Count;
        var away = sim.State.Survivors.Count(s => s.Away);
        SetChip(_pop, away > 0 ? $"{people} ({away} away)" : $"{people}", Palette.Text,
            $"Population: {people}" + (away > 0 ? $", {away} away on an expedition (they eat rations, not colony food)" : ""));

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
            $"Beds: {sim.State.Survivors.Count(s => s.HasBed)} taken of {sim.BedCapacity}\n{bedNote}\nCouples only have children when a bed is free.", bedsBad ? "stat_warning" : "stat_beds");

        var days = sim.FoodDays;
        var foodColor = days < 3 ? Palette.Danger : days < 7 ? Palette.Warn : Palette.Good;
        var daysText = double.IsInfinity(days) ? "∞" : days >= 99 ? "99+d" : $"{days:0.#}d";
        SetChip(_food, daysText + (days < 3 ? " !" : ""), foodColor,
            $"Food days: food lasts about {daysText} at {sim.FoodPerDay:0.#} eaten per day ({sim.FoodStock:0} food in the pantry).\n" +
            "Infants and children are fed first; the hungry lose health. Click for the pantry.",
            days < 3 ? "stat_starving" : "res_food");
    }

    private void SetChip(PanelContainer chip, string text, Color color, string tooltip, string? icon = null)
    {
        var row = chip.GetChild(0);
        var label = row.GetChild<Label>(1);
        _set.Text(label, text);
        _set.FontColor(label, color);
        var rect = row.GetChild<TextureRect>(0);
        if (icon is not null) _set.Texture(rect, Icons.Get(icon, 16));
        _set.Modulate(rect, color == Palette.Text ? Palette.TextMuted : color);
        _set.Tooltip(chip, tooltip);
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
        if (makers.Count == 0)
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
