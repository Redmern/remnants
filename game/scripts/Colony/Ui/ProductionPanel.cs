using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The production queue of the selected building (Workshop, Tailor, Kitchen, Armory, Electronics Lab): orders with
/// progress and quality odds (cancel any), and a recipe picker to queue one batch, five, or a repeating order.
/// Locked recipes say why (level, schematic); Decrypt orders appear while their chip is in the stores. Core decides what is allowed.
/// At a cooking building (Campfire, Kitchen, Smokehouse) the picker is a recipe book instead: every dish as a card, known or
/// locked (level, schematic), with ingredient chips (stock, need, and when the soonest of it spoils), what the dish does and its buffs.
/// <para>A bench whose recipes have groups (the Electronics Lab: Components, Devices, Upgrades, Disassembly) shows them as
/// tabs; ungrouped recipes there go under Other. Recipes that need power carry a badge, disassembly shows its yields and
/// research chance, and a building on the grid shows whether it is fed and what that gives. The Electronics Lab also
/// lists the charging bench (drained devices home from the field, with progress and ETA).</para>
/// </summary>
public partial class ProductionPanel : PanelContainer
{
    private static readonly (string Label, int Count, bool Repeat)[] Amounts = [("×1", 1, false), ("×5", 5, false), ("repeat", 1, true)];

    private readonly ColonySession _session;
    private readonly RichTextLabel _status = Ui.Rich();
    private readonly VBoxContainer _orders = new();
    private readonly OptionButton _recipes = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true, FitToLongestItem = false };
    private readonly OptionButton _amount = new() { FocusMode = FocusModeEnum.None };
    private readonly RichTextLabel _recipeInfo = Ui.Rich();
    private readonly List<string> _ids = new();
    private readonly HBoxContainer _groupTabs = Widgets.Row(2);
    private readonly Dictionary<string, Button> _groupButtons = new();
    private string? _group;
    private readonly Label _power = Widgets.Wrapped("", "Small");
    private readonly VBoxContainer _bench = Widgets.Column(4);
    private readonly VBoxContainer _benchRows = Widgets.Column(3);
    private readonly Label _benchNote = Widgets.Wrapped("", "Small");
    private string _benchShown = "";
    private readonly List<(ProgressBar Bar, Label Text, int OrderId)> _rows = new();
    private string _ordersShown = "";
    private string _recipesShown = "";

    private readonly Button _cyber;
    private readonly Label _title;
    private readonly VBoxContainer _book = Widgets.Column(6);
    private string _bookShown = "";
    private string? _selected;
    private readonly Button _bench;

    public int? BuildingId { get; private set; }

    /// <summary>The recipe book (cooking buildings), e.g. to scroll it into view.</summary>
    public Control Book => _book;

    /// <summary>"Cybernetics" pressed (shown where prosthetics are made).</summary>
    public event System.Action? CyberneticsPressed;

    /// <summary>"Gear Bench" pressed (shown at a bench building: gear_mods.json rules.benches).</summary>
    public event System.Action? GearBenchPressed;

    public ProductionPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(320, 0);
        Visible = false;
        var box = Ui.Panel(this);
        _cyber = Widgets.IconButton("body_prosthetic", "Cybernetics", "The cybernetics tree: which limbs can be made here and what they need",
            () => CyberneticsPressed?.Invoke(), variation: "Flat");
        _cyber.CustomMinimumSize = new Vector2(0, 26);
        _bench = Widgets.IconButton("bld_armory", "Gear Bench", "Fit and remove gear mods, fit Mk kits, compare builds",
            () => GearBenchPressed?.Invoke(), variation: "Flat");
        _bench.CustomMinimumSize = new Vector2(0, 26);
        var buttons = Widgets.Row(4);
        buttons.AddChild(_bench);
        buttons.AddChild(_cyber);
        var header = Widgets.SectionHeader("Production", "skill_crafting", buttons);
        _title = header.GetChildren().OfType<Label>().First();
        box.AddChild(header);
        box.AddChild(_power);
        box.AddChild(_status);
        box.AddChild(_orders);

        box.AddChild(_book);
        box.AddChild(Ui.Heading("Queue a recipe", "ui_plus"));
        box.AddChild(_groupTabs);
        var row = new HBoxContainer();
        row.AddChild(_recipes);
        foreach (var (label, _, _) in Amounts) _amount.AddItem(label);
        row.AddChild(_amount);
        row.AddChild(Widgets.IconButton("ui_plus", "Queue", "Add to the queue", Queue, variation: "Primary"));
        box.AddChild(row);
        box.AddChild(_recipeInfo);
        _recipes.ItemSelected += _ => Refresh();

        _bench.AddChild(Widgets.SectionHeader("Charging bench", "stat_battery"));
        _bench.AddChild(_benchNote);
        _bench.AddChild(_benchRows);
        box.AddChild(_bench);
    }

    /// <summary>The recipe groups of a bench (ungrouped ones as "Other" when others are grouped); empty when none are.</summary>
    private static List<string> Groups(IReadOnlyList<RecipeDef> recipes)
    {
        if (recipes.All(r => r.Group is null)) return [];
        var order = new List<string> { "Components", "Devices", "Upgrades", "Disassembly" };
        return recipes.Select(GroupOf).Distinct().OrderBy(g => order.IndexOf(g) is var i and >= 0 ? i : order.Count).ToList();
    }

    private static string GroupOf(RecipeDef r) => r.Group ?? "Other";

    /// <summary>Switches the recipe picker to a group's tab (the tabs themselves and debug captures).</summary>
    public void ShowGroup(string group)
    {
        _group = group;
        _recipesShown = "";
        Refresh();
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _ordersShown = _recipesShown = _bookShown = "";
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        var recipes = building is null ? [] : sim.Data.RecipesFor(building.DefId);
        Visible = recipes.Count > 0;
        if (!Visible) return;
        _bench.Visible = sim.Scavenge?.GearMods is { } gear && gear.Def.Rules.Benches.Contains(building!.DefId);
        _cyber.Visible = sim.Data.Cybernetics is { } cyber && recipes.Any(r => cyber.Node(r.Item) is not null);

        var cooking = IsCooking(sim, building!);
        _title.Text = (cooking ? ColonyText.BuildingName(sim, building!) : "Production").ToUpperInvariant();
        _recipes.Visible = !cooking;
        _book.Visible = cooking;
        if (cooking) RefreshBook(sim, building!, recipes);
        else
        {
            RefreshGroups(recipes);
            RefreshRecipes(sim, building!, _group is null ? recipes : recipes.Where(r => GroupOf(r) == _group).ToList());
        }
        RefreshOrders(sim, building!);
        _status.Text = Status(sim, building!);
        RefreshPower(sim, building!);
        RefreshBench(sim, building!);
        var current = Current;
        _recipeInfo.Text = current is not null && sim.Data.Recipes.TryGetValue(current, out var r) ? RecipeInfo(sim, building!, r) : "";
    }

    private string? Current => _book.Visible ? _selected : _recipes.Selected >= 0 && _recipes.Selected < _ids.Count ? _ids[_recipes.Selected] : null;

    private static bool IsCooking(ColonySim sim, Building building) =>
        sim.Data.Food is not null && sim.Data.Buildings[building.DefId].Job == "cooking";

    /// <summary>The recipe book: one card per dish, the selected one highlighted; locked ones say what they wait on.</summary>
    private void RefreshBook(ColonySim sim, Building building, IReadOnlyList<RecipeDef> recipes)
    {
        var stock = string.Join(",", sim.State.Food.Lots.Select(kv => $"{kv.Key}:{kv.Value.Where(l => !l.Spoiled).Sum(l => l.Count)}"));
        var signature = $"{building.Id}:{building.Level}:{string.Join(",", sim.State.Schematics.Known)}:{stock}:{_selected}:{sim.Hour}";
        if (signature == _bookShown) return;
        _bookShown = signature;
        if (_selected is null || recipes.All(r => r.Id != _selected))
            _selected = recipes.FirstOrDefault(r => sim.CanQueue(building, r).Ok)?.Id ?? recipes.FirstOrDefault()?.Id;
        Widgets.Clear(_book);
        _book.AddChild(Ui.Heading("Recipe book", "skill_cooking"));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var r in recipes) grid.AddChild(RecipeCard(sim, building, r));
        _book.AddChild(grid);
    }

    private Control RecipeCard(ColonySim sim, Building building, RecipeDef r)
    {
        var check = sim.CanQueue(building, r);
        var known = check.Error is not (CommandError.RecipeLocked or CommandError.RecipeLevelTooLow);
        var card = new PanelContainer
        {
            ThemeTypeVariation = r.Id == _selected ? "CardSelected" : "Card",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(150, 0),
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = Tooltip(sim, r),
        };
        if (!known) card.Modulate = new Color(1, 1, 1, 0.6f);
        var column = Widgets.Column(3);
        column.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(column);
        var head = Widgets.Row(4);
        var own = known && r.Item is { } dish && sim.Data.Food?.IsFood(dish) == true;
        head.AddChild(Icons.Rect(known ? OutputIcon(sim, r) : Icons.Get("stat_lock", 18), own ? 24 : 18,
            own ? FoodText.Tint(sim, r.Item!, Palette.Amber) : known ? Palette.Amber : Palette.TextMuted));
        var name = Widgets.Label(r.Name, "Heading", size: 14);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        head.AddChild(name);
        head.AddChild(Widgets.Label($"×{r.Amount:0}", "Number", Palette.TextMuted, 12));
        column.AddChild(head);
        if (!known)
            column.AddChild(Widgets.Wrapped(check.Error == CommandError.RecipeLevelTooLow ? $"Unknown: needs {sim.Data.Buildings[building.DefId].Name} L{r.MinLevel}" : check.Message,
                "Small", Palette.Danger));
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 3);
        chips.AddThemeConstantOverride("v_separation", 3);
        foreach (var (key, need) in r.Ingredients)
        {
            var have = Pantry.Available(sim.State, sim.Data, key);
            var soon = FoodText.SoonestSpoil(sim, key);
            var text = $"{FoodText.KeyName(sim, key)} {have}/{need}";
            var tip = $"{FoodText.KeyName(sim, key)}: {have} fresh in the pantry, {need} per batch" + (soon is { } t ? $"; the soonest spoils in {FoodText.SpoilsIn(sim, t)}" : "");
            // A tag shows the icon of a food it stands for (any meat → game meat).
            var shown = key.StartsWith(FoodData.TagPrefix)
                ? sim.Data.Food!.Items.Keys.FirstOrDefault(i => sim.Data.Food.Matches(i, key)) ?? key
                : key;
            var chip = Widgets.Chip(FoodText.Icon(sim, shown, 14), soon is { } s && s < 3 * sim.Data.Rules.TicksPerDay ? $"{text} · {FoodText.SpoilsIn(sim, s)}" : text,
                have >= need ? Palette.Text : Palette.Danger, tip, FoodText.Tint(sim, shown, Palette.TextMuted));
            chips.AddChild(chip);
        }
        foreach (var (res, amount) in r.Inputs)
            chips.AddChild(Widgets.Chip(Icons.Resource(res, 14), $"{sim.Data.Resources[res].Name} {amount:0.#}",
                sim.State.Stockpile.Has(res, amount) ? Palette.Text : Palette.Danger));
        column.AddChild(chips);
        if (r.Id == _selected && r.Item is { } item && sim.Data.Food?.Items.GetValueOrDefault(item) is { } def)
            column.AddChild(Widgets.Wrapped(FoodText.Effects(sim, def), "Small", Palette.TextMuted));
        var id = r.Id;
        card.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            _selected = id;
            _bookShown = "";
            Refresh();
        };
        return card;
    }

    private static string Tooltip(ColonySim sim, RecipeDef r)
    {
        if (r.Item is not { } item || sim.Data.Food?.Items.GetValueOrDefault(item)?.Meal is not { } meal) return r.Name;
        var buffs = meal.Buffs.Keys.Append(meal.FieldBuff).OfType<string>().Distinct()
            .Select(b => sim.Data.Food!.Buffs.GetValueOrDefault(b)).OfType<BuffDef>().Select(FoodText.BuffTip);
        return string.Join("\n\n", new[] { $"{r.Name}: {FoodText.Effects(sim, sim.Data.Food!.Items[item])}" }.Concat(buffs));
    }

    private void RefreshGroups(IReadOnlyList<RecipeDef> recipes)
    {
        var groups = Groups(recipes);
        _groupTabs.Visible = groups.Count > 0;
        if (groups.Count == 0)
        {
            _group = null;
            return;
        }
        if (_group is null || !groups.Contains(_group)) _group = groups[0];
        if (!groups.SequenceEqual(_groupButtons.Keys))
        {
            Widgets.Clear(_groupTabs);
            _groupButtons.Clear();
            foreach (var g in groups)
            {
                var group = g;
                var count = recipes.Count(r => GroupOf(r) == g);
                var tab = Widgets.TextButton(g, () => ShowGroup(group), $"{g}: {count} recipe{(count == 1 ? "" : "s")}", "Tab");
                tab.ToggleMode = true;
                tab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                tab.AddThemeFontSizeOverride("font_size", 13);
                _groupButtons[g] = tab;
                _groupTabs.AddChild(tab);
            }
        }
        foreach (var (g, tab) in _groupButtons) tab.SetPressedNoSignal(g == _group);
    }

    /// <summary>"Powered: +25% crafting speed" / "Unpowered …", for buildings that draw from the grid.</summary>
    private void RefreshPower(ColonySim sim, Building building)
    {
        var nodes = sim.PowerNodes();
        var node = nodes.FirstOrDefault(n => n.BuildingId == building.Id && n.Role == PowerRole.Consumer);
        _power.Visible = node is not null;
        if (node is null) return;
        var grid = nodes.Any(n => n.Role is PowerRole.Generator or PowerRole.Storage);
        var (text, color) = !node.On ? ("Switched off the grid: no bonus, tier 3 parts wait.", Palette.TextMuted)
            : sim.IsPowered(building) ? ($"Powered: {node.Note}.", Palette.Good)
            : !grid ? ($"Unpowered: no generator yet. Power gives {node.Note}.", Palette.TextMuted)
            : !PowerText.Idle(sim, node) ? ($"Unpowered: the grid cannot feed its {node.Amount:0.#} draw. Power gives {node.Note}.", Palette.Warn)
            : ($"Idle: it draws only while someone is on shift. Power gives {node.Note}.", Palette.TextMuted);
        _power.Text = text;
        _power.AddThemeColorOverride("font_color", color);
    }

    /// <summary>The charging bench: drained devices in arrival order with progress and ETA (only at the Electronics Lab).</summary>
    private void RefreshBench(ColonySim sim, Building building)
    {
        var e = sim.Data.Electronics;
        _bench.Visible = e is not null && building.DefId == e.Disassembly.Building;
        if (e is null || !_bench.Visible) return;
        var charging = sim.State.Charging;
        var fed = sim.State.Power.Charging;
        var atOnce = System.Math.Min(charging.Count, e.Charging.MaxAtOnce);
        _benchNote.Text = charging.Count == 0 ? "Nothing to charge. Reusable devices that come home flat wait here."
            : fed ? $"On the grid: {atOnce} charging at once, {e.Charging.PoweredRate / System.Math.Max(e.Charging.UnpoweredRate, 1e-6):0.#}× the trickle rate."
            : $"Off the grid: trickle charging, {atOnce} at once. A fed grid charges far faster.";
        _benchNote.AddThemeColorOverride("font_color", charging.Count > 0 && !fed ? Palette.Warn : Palette.TextMuted);
        var signature = string.Join(";", charging.Select(c => $"{c.ItemId}:{c.Done:0.##}/{c.Work:0.##}")) + fed;
        if (signature == _benchShown) return;
        _benchShown = signature;
        Widgets.Clear(_benchRows);
        for (var index = 0; index < charging.Count; index++)
        {
            var item = charging[index];
            var def = sim.Scavenge?.Items.GetValueOrDefault(item.ItemId);
            var active = index < e.Charging.MaxAtOnce;
            var row = Widgets.Row(8);
            row.AddChild(Icons.Rect(def is null ? Icons.Get("item_tool", 18) : Icons.Item(def.DefinedId, def.Kind.ToString(), 18), 18,
                active ? Palette.Amber : Palette.TextDim));
            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            col.AddThemeConstantOverride("separation", 1);
            var eta = Power.ChargeEta(sim.State, sim.Data, item);
            var when = eta is { } t ? $"ready in {ColonyText.Duration(t, sim.Data.Rules)}" : "not charging";
            col.AddChild(Ui.Label($"{def?.Name ?? item.ItemId}  ·  {(active ? when : "queued, " + when)}", 13));
            var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5), MaxValue = 1, Value = item.Work > 0 ? item.Done / item.Work : 1 };
            bar.AddThemeStyleboxOverride("fill", HudTheme.Box(fed ? Palette.Good : Palette.Warn, radius: 2, padX: 0, padY: 0));
            col.AddChild(bar);
            row.AddChild(col);
            _benchRows.AddChild(row);
        }
    }

    private void RefreshRecipes(ColonySim sim, Building building, IReadOnlyList<RecipeDef> recipes)
    {
        // Decrypt orders only show while their chip is in the stores and the schematic is still unknown.
        recipes = recipes.Where(r => r.Learns is not { } learns
            || sim.State.Inventory.Has(learns) && !Remnants.Core.Schematics.Schematics.Has(sim, learns)).ToList();
        var chips = recipes.Where(r => r.Learns is not null).Select(r => $"{r.Learns}:{sim.State.Inventory.Count(r.Learns!)}");
        var signature = $"{building.Id}:{building.Level}:{_group}:{string.Join(",", sim.State.Schematics.Known)}:{string.Join(",", chips)}";
        if (signature == _recipesShown) return;
        _recipesShown = signature;
        var selected = _recipes.Selected >= 0 && _recipes.Selected < _ids.Count ? _ids[_recipes.Selected] : null;
        _ids.Clear();
        _recipes.Clear();
        foreach (var r in recipes)
        {
            var check = sim.CanQueue(building, r);
            _ids.Add(r.Id);
            var lockNote = check.Error switch
            {
                CommandError.RecipeLevelTooLow => $"  [L{r.MinLevel}]",
                CommandError.RecipeLocked => "  [schematic]",
                _ => "",
            };
            _recipes.AddIconItem(OutputIcon(sim, r), r.Learns is null ? $"{r.Name} → {Output(sim, r)}{(r.NeedsPower ? "  [needs power]" : "")}{lockNote}" : $"{r.Name}{lockNote}");
            _recipes.SetItemDisabled(_recipes.ItemCount - 1, !check.Ok);
        }
        var index = selected is null ? -1 : _ids.IndexOf(selected);
        if (index < 0) index = Enumerable.Range(0, _ids.Count).FirstOrDefault(i => !_recipes.IsItemDisabled(i));
        _recipes.Select(index);
    }

    private void RefreshOrders(ColonySim sim, Building building)
    {
        var signature = string.Join(";", building.Queue.Select(o => $"{o.Id}/{o.Remaining}"));
        if (signature != _ordersShown)
        {
            _ordersShown = signature;
            foreach (var child in _orders.GetChildren()) child.QueueFree();
            _rows.Clear();
            if (building.Queue.Count == 0)
                _orders.AddChild(Ui.Label("Queue empty.", color: Ui.Muted));
            foreach (var order in building.Queue)
            {
                var orderId = order.Id;
                var line = new PanelContainer { ThemeTypeVariation = order.Paid ? "CardSelected" : "Card" };
                var inner = Widgets.Row(8);
                line.AddChild(inner);
                inner.AddChild(Icons.Rect(OutputIcon(sim, sim.Data.Recipes[order.RecipeId]), 22, order.Paid ? Palette.Amber : Palette.TextMuted));
                var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var text = Ui.Label(size: 14);
                var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), MaxValue = 1 };
                bar.AddThemeStyleboxOverride("fill", HudTheme.Box(Palette.Amber, radius: 2, padX: 0, padY: 0));
                column.AddChild(text);
                column.AddChild(bar);
                inner.AddChild(column);
                if (sim.Data.Recipes[order.RecipeId].NeedsPower) inner.AddChild(PowerBadge(sim.IsPowered(building)));
                var cancel = Widgets.IconButton("ui_close", "", "Cancel (a batch in progress gives its inputs back)",
                    () => _session.Execute(new CancelOrder(building.Id, orderId)), variation: "Flat");
                inner.AddChild(cancel);
                _orders.AddChild(line);
                _rows.Add((bar, text, orderId));
            }
        }

        foreach (var (bar, text, orderId) in _rows)
        {
            if (building.Queue.Find(o => o.Id == orderId) is not { } order) continue;
            var recipe = sim.Data.Recipes[order.RecipeId];
            bar.Value = order.Progress / recipe.Work;
            var amount = order.Repeat ? "repeat" : order.Remaining > 1 ? $"×{order.Remaining}" : "";
            text.Text = $"{recipe.Name} {amount}  {(order.Paid ? $"{order.Progress:0.#}/{recipe.Work:0.#}" : "not started")}";
        }
    }

    private static string Status(ColonySim sim, Building building)
    {
        var rate = sim.CraftEffortPerTick(building);
        var average = sim.CraftEffortPerHourAveraged(building);
        var working = building.Queue.FirstOrDefault(o => o.Paid);
        return sim.CraftStatus(building) switch
        {
            CraftStatus.Idle => "[color=#999]Idle. Queue a recipe and assign workers.[/color]",
            CraftStatus.NoWorkers => $"[color=#{Ui.Hex(Ui.Bad)}]No workers assigned.[/color]",
            CraftStatus.NeedsSkill => NeedsSkill(sim, building),
            CraftStatus.WaitingForInputs => $"[color=#{Ui.Hex(Ui.Bad)}]Waiting for materials.[/color]",
            CraftStatus.NeedsPower => $"[color=#{Ui.Hex(Palette.Warn)}]Needs power: the next order is tier 3 and waits for the grid.[/color] "
                + "Workers practise meanwhile. Build a generator, or switch something else off.",
            _ when working is not null => $"Working on [b]{sim.Data.Recipes[working.RecipeId].Name}[/b], {rate:0.##} work/h on shift: done in "
                + ColonyText.Duration((int)System.Math.Ceiling((sim.Data.Recipes[working.RecipeId].Work - working.Progress) / System.Math.Max(average, 1e-6)), sim.Data.Rules),
            _ => $"Starting next hour, {rate:0.##} work/h.",
        };
    }

    /// <summary>Which skill is missing, how far off the best worker is, that they practise meanwhile, and what else to do.</summary>
    private static string NeedsSkill(ColonySim sim, Building building)
    {
        if (sim.SkillGap(building) is not { } gap) return $"[color=#{Ui.Hex(Ui.Bad)}]Workers lack the skill for every order.[/color]";
        var eta = gap.Ticks is { } t && t < int.MaxValue ? $", about {ColonyText.Duration(t, sim.Data.Rules)}" : "";
        var easy = sim.Data.RecipesFor(building.DefId)
            .Where(r => r.MinSkill <= gap.Best + 1e-9 && sim.CanQueue(building, r).Ok)
            .Select(r => r.Name).FirstOrDefault();
        var hint = easy is null ? "" : $" Or queue {easy} first: making it trains the skill too.";
        return $"[color=#{Ui.Hex(Ui.Bad)}]Needs {gap.Skill} {gap.Needed:0} (best worker {gap.Best:0}).[/color] "
            + $"Workers are practising{eta}.{hint}";
    }

    private static string Output(ColonySim sim, RecipeDef r)
    {
        string Name(string id) => sim.Scavenge?.Items.GetValueOrDefault(id)?.Name ?? id;
        if (r.Learns is { } learns) return $"the {sim.Data.Schematics.All[learns].Name} schematic";
        if (r.Item is not { } item) return $"{r.Amount:0.#} {sim.Data.Resources[r.Resource!].Name}";
        var main = $"{r.Amount:0} {Name(item)}";
        return r.Byproducts.Count == 0 ? main : main + ", " + string.Join(", ", r.Byproducts.Select(kv => $"{kv.Value} {Name(kv.Key)}"));
    }

    /// <summary>A small "needs power" chip, green when the building is fed.</summary>
    private static Control PowerBadge(bool powered)
    {
        var chip = Widgets.Chip(Icons.Get("stat_power", 14), powered ? "power" : "no power", powered ? Palette.Good : Palette.Warn,
            powered ? "Tier 3: needs power, and has it." : "Tier 3: only progresses while the building is powered.", powered ? Palette.Good : Palette.Warn);
        chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return chip;
    }

    private static string RecipeInfo(ColonySim sim, Building building, RecipeDef r)
    {
        var inputs = ColonyText.Cost(r.Inputs, sim.Data);
        if (r.ItemInputs.Count > 0)
            inputs += ", " + string.Join(", ", r.ItemInputs.Select(kv => $"{kv.Value} {sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}"));
        if (r.Ingredients.Count > 0)
            inputs = string.Join(", ", r.Ingredients.Select(kv => $"{kv.Value} {FoodText.KeyName(sim, kv.Key)}").Prepend(r.Inputs.Count > 0 ? inputs : null).OfType<string>());
        var afford = sim.State.Stockpile.CanAfford(r.Inputs) && CraftingIngredients(sim, r);
        var lines = new List<string> { $"Needs [color=#{Ui.Hex(afford ? Ui.Good : Ui.Bad)}]{inputs}[/color], {r.Work:0.#} work" };
        if (r.Group == "Disassembly")
        {
            lines.Add($"Yields [color=#{Ui.Hex(Palette.Amber)}]{Output(sim, r)}[/color]; skill and Circuit Tinkerer may add one more of each.");
            if (r.ResearchChance > 0) lines.Add($"{r.ResearchChance * 100:0}% chance of Research Data per batch.");
        }
        if (r.NeedsPower)
            lines.Add(sim.IsPowered(building) ? $"[color=#{Ui.Hex(Ui.Good)}]Needs power: the bench is powered.[/color]"
                : $"[color=#{Ui.Hex(Palette.Warn)}]Needs power: it waits until the bench is on a fed grid.[/color]");
        if (r.MinSkill > 0) lines.Add($"A worker needs {sim.Data.Jobs[sim.Data.Buildings[building.DefId].Job!].Skill} {r.MinSkill:0}");
        var check = sim.CanQueue(building, r);
        if (!check.Ok) lines.Add($"[color=#{Ui.Hex(Ui.Bad)}]{check.Message}[/color]");
        var odds = sim.QualityChances(building, r).Where(c => c.Chance > 0.005).ToList();
        if (odds.Count > 0 && sim.Scavenge is { } items)
            lines.Add((IsCooking(sim, building) ? "Cook quality: " : "Quality: ") + string.Join("  ", odds.Select(c =>
                $"{items.Rules.Quality.Find(q => q.Id == c.Tier)?.Name ?? c.Tier} {c.Chance * 100:0}%")));
        if (IsCooking(sim, building) && r.Ingredients.Keys.Any(k => sim.Data.Food!.Items.Values.Any(i => i.Tainted && sim.Data.Food.Matches(i.Id, k))))
            lines.Add($"[color=#{Ui.Hex(Palette.Warn)}]Husk-zone meat may stay tainted unless the dish comes out well.[/color]");
        return string.Join("\n", lines);
    }

    private static bool CraftingIngredients(ColonySim sim, RecipeDef r) =>
        r.Ingredients.All(kv => Pantry.Available(sim.State, sim.Data, kv.Key) >= kv.Value);

    private void Queue()
    {
        if (BuildingId is not { } id || Current is not { } recipe) return;
        var (_, count, repeat) = Amounts[System.Math.Max(0, _amount.Selected)];
        _session.Execute(new QueueRecipe(id, recipe, count, repeat));
    }

    private static Texture2D OutputIcon(ColonySim sim, RecipeDef r) =>
        r.Learns is not null ? Icons.Get("item_schematic", 18, "item_blueprint")
        : r.Item is { } food && sim.Data.Food?.IsFood(food) == true ? FoodText.Icon(sim, food, 18)
        : r.Item is { } item && sim.Scavenge?.Items.GetValueOrDefault(item) is { } def
            ? Icons.Item(def.DefinedId, def.Kind.ToString(), 18)
            : Icons.Resource(r.Resource ?? "", 18);
}
