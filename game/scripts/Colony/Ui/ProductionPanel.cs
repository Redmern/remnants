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
    private readonly List<(ProgressBar Bar, Label Text, int OrderId)> _rows = new();
    private string _ordersShown = "";
    private string _recipesShown = "";

    private readonly Button _cyber;
    private readonly Label _title;
    private readonly VBoxContainer _book = Widgets.Column(6);
    private string _bookShown = "";
    private string? _selected;

    public int? BuildingId { get; private set; }

    /// <summary>The recipe book (cooking buildings), e.g. to scroll it into view.</summary>
    public Control Book => _book;

    /// <summary>"Cybernetics" pressed (shown where prosthetics are made).</summary>
    public event System.Action? CyberneticsPressed;

    public ProductionPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(320, 0);
        Visible = false;
        var box = Ui.Panel(this);
        _cyber = Widgets.IconButton("body_prosthetic", "Cybernetics", "The cybernetics tree: which limbs can be made here and what they need",
            () => CyberneticsPressed?.Invoke(), variation: "Flat");
        _cyber.CustomMinimumSize = new Vector2(0, 26);
        var header = Widgets.SectionHeader("Production", "skill_crafting", _cyber);
        _title = header.GetChildren().OfType<Label>().First();
        box.AddChild(header);
        box.AddChild(_status);
        box.AddChild(_orders);

        box.AddChild(_book);
        box.AddChild(Ui.Heading("Queue a recipe", "ui_plus"));
        var row = new HBoxContainer();
        row.AddChild(_recipes);
        foreach (var (label, _, _) in Amounts) _amount.AddItem(label);
        row.AddChild(_amount);
        row.AddChild(Widgets.IconButton("ui_plus", "Queue", "Add to the queue", Queue, variation: "Primary"));
        box.AddChild(row);
        box.AddChild(_recipeInfo);
        _recipes.ItemSelected += _ => Refresh();
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
        _cyber.Visible = sim.Data.Cybernetics is { } cyber && recipes.Any(r => cyber.Node(r.Item) is not null);

        var cooking = IsCooking(sim, building!);
        _title.Text = (cooking ? ColonyText.BuildingName(sim, building!) : "Production").ToUpperInvariant();
        _recipes.Visible = !cooking;
        _book.Visible = cooking;
        if (cooking) RefreshBook(sim, building!, recipes);
        else RefreshRecipes(sim, building!, recipes);
        RefreshOrders(sim, building!);
        _status.Text = Status(sim, building!);
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
        head.AddChild(Icons.Rect(known ? OutputIcon(sim, r) : Icons.Get("stat_lock", 18), 18, known ? Palette.Amber : Palette.TextMuted));
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
            var icon = key.StartsWith(FoodData.TagPrefix) ? Icons.Get("item_food", 14) : FoodText.Icon(sim, key, 14);
            var chip = Widgets.Chip(icon, soon is { } s && s < 3 * sim.Data.Rules.TicksPerDay ? $"{text} · {FoodText.SpoilsIn(sim, s)}" : text,
                have >= need ? Palette.Text : Palette.Danger, tip);
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

    private void RefreshRecipes(ColonySim sim, Building building, IReadOnlyList<RecipeDef> recipes)
    {
        // Decrypt orders only show while their chip is in the stores and the schematic is still unknown.
        recipes = recipes.Where(r => r.Learns is not { } learns
            || sim.State.Inventory.Has(learns) && !Remnants.Core.Schematics.Schematics.Has(sim, learns)).ToList();
        var chips = recipes.Where(r => r.Learns is not null).Select(r => $"{r.Learns}:{sim.State.Inventory.Count(r.Learns!)}");
        var signature = $"{building.Id}:{building.Level}:{string.Join(",", sim.State.Schematics.Known)}:{string.Join(",", chips)}";
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
            _recipes.AddIconItem(OutputIcon(sim, r), r.Learns is null ? $"{r.Name} → {Output(sim, r)}{lockNote}" : $"{r.Name}{lockNote}");
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

    private static string Output(ColonySim sim, RecipeDef r) =>
        r.Learns is { } learns ? $"the {sim.Data.Schematics.All[learns].Name} schematic"
        : r.Item is { } item ? $"{r.Amount:0} {sim.Scavenge?.Items.GetValueOrDefault(item)?.Name ?? item}" : $"{r.Amount:0.#} {sim.Data.Resources[r.Resource!].Name}";

    private static string RecipeInfo(ColonySim sim, Building building, RecipeDef r)
    {
        var inputs = ColonyText.Cost(r.Inputs, sim.Data);
        if (r.ItemInputs.Count > 0)
            inputs += ", " + string.Join(", ", r.ItemInputs.Select(kv => $"{kv.Value} {sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}"));
        if (r.Ingredients.Count > 0)
            inputs = string.Join(", ", r.Ingredients.Select(kv => $"{kv.Value} {FoodText.KeyName(sim, kv.Key)}").Prepend(r.Inputs.Count > 0 ? inputs : null).OfType<string>());
        var afford = sim.State.Stockpile.CanAfford(r.Inputs) && CraftingIngredients(sim, r);
        var lines = new List<string> { $"Needs [color=#{Ui.Hex(afford ? Ui.Good : Ui.Bad)}]{inputs}[/color], {r.Work:0.#} work" };
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
        : r.Item is { } item && sim.Scavenge?.Items.GetValueOrDefault(item) is { } def
            ? Icons.Item(def.DefinedId, def.Kind.ToString(), 18)
            : Icons.Resource(r.Resource ?? "", 18);
}
