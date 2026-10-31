using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Help;
using Remnants.Game.Hud;
using Remnants.Game.Hud.Help;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The production queue of the selected building (Workshop, Tailor, Kitchen, Armory, Electronics Lab, Garage), one task per
/// tab: <b>Queue</b> (the orders, the active one with its bar, the rest one line each with a state chip: Repeat, ×N, Next,
/// Waiting for materials, Not started; "Again" repeats the last order queued here; the Gear Bench and Cybernetics buttons),
/// <b>Recipes</b> (group tabs at the Electronics Lab, one row per recipe with a quick "+" that queues the stepper's count,
/// and the picked recipe's needs, work and time, quality odds, power and research chance with their help tips, a −/+
/// batch stepper, Repeat and Queue) and, at the Electronics Lab, <b>Charging</b> (drained devices with progress and ETA).
/// At a cooking building (Campfire, Kitchen, Smokehouse) the recipes tab is the recipe book: each dish one line (food
/// value, buffs, "can make ×N"), the picked one opened to its ingredient chips (stock / need, the soonest spoil) and
/// effects, locked dishes folded under "Locked (N)". Rows are kept by id and updated in place, so nothing rebuilds while
/// the colony ticks. Core decides what is allowed.
/// </summary>
public partial class ProductionPanel : PanelContainer
{
    /// <summary>The last order the player queued per building, for "Again" (shared by every production panel).</summary>
    private static readonly Dictionary<int, (string Recipe, int Count, bool Repeat)> Last = new();

    private readonly ColonySession _session;
    private readonly Unchanged _set = new();
    private readonly Label _title;
    private readonly Button _cyber, _gearBench;
    private readonly HBoxContainer _powerRow;
    private readonly Label _power = Widgets.Wrapped("", "Small");
    private readonly HBoxContainer _statusRow;
    private readonly RichTextLabel _status = Ui.Rich();
    private readonly PanelTabs _tabs = new();

    // Queue tab.
    private readonly VBoxContainer _orders = Widgets.Column(4);
    private readonly KeyedRows<int> _orderRows;
    private readonly Dictionary<int, OrderRow> _orderRefs = new();
    private readonly Label _empty = Widgets.Label("Queue empty. Pick something under Recipes.", "Muted");
    private readonly Button _again;

    // Recipes tab.
    private readonly VBoxContainer _recipesPage = Widgets.Column(6);
    private readonly HFlowContainer _groupTabs = new();
    private readonly Dictionary<string, Button> _groupButtons = new();
    private string? _group;
    private readonly VBoxContainer _recipeList = Widgets.Column(3);
    private readonly KeyedRows<string> _recipeRows;
    private readonly Dictionary<string, RecipeRow> _recipeRefs = new();
    private string _recipesShown = "";
    private readonly VBoxContainer _detail = Widgets.Column(4);
    private string _detailShown = "";
    private readonly Stepper _count = new(1, 50, 1, "Batches to queue (Shift: ±5, wheel works too)");
    private readonly CheckBox _repeat = PanelKit.Focusable(new CheckBox { Text = "Repeat", TooltipText = "Keep making it: the order starts again when a batch is done" });
    private readonly Button _queueButton;
    private string? _selected;

    // Recipe book (cooking).
    private readonly VBoxContainer _book = Widgets.Column(4);
    private readonly VBoxContainer _dishes = Widgets.Column(3);
    private readonly KeyedRows<string> _dishRows;
    private readonly Dictionary<string, DishRow> _dishRefs = new();
    private readonly Expander _locked = new("Locked");
    private string _bookShown = "";

    // Charging tab.
    private readonly VBoxContainer _bench = Widgets.Column(4);
    private readonly VBoxContainer _benchList = Widgets.Column(3);
    private readonly KeyedRows<string> _benchRows;
    private readonly Dictionary<string, ChargeRow> _benchRefs = new();
    private readonly Label _benchNote = Widgets.Wrapped("", "Small");

    private sealed record OrderRow(PanelContainer Card, TextureRect Icon, Label Name, PanelContainer Amount, PanelContainer State, Label Progress, ProgressBar Bar, PanelContainer? Power);
    private sealed record RecipeRow(PanelContainer Card, Label Name, Label Note, Button Add);
    private sealed record DishRow(PanelContainer Card, Label Summary, Label CanMake, VBoxContainer Body, List<System.Action> Update);
    private sealed record ChargeRow(TextureRect Icon, Label Text, ProgressBar Bar);

    public int? BuildingId { get; private set; }

    /// <summary>The recipe book (cooking buildings), e.g. to scroll it into view.</summary>
    public Control Book => _book;

    /// <summary>"Cybernetics" pressed (shown where prosthetics are made).</summary>
    public event System.Action? CyberneticsPressed;

    /// <summary>"Gear Bench" pressed (shown at a bench building: gear_mods.json rules.benches).</summary>
    public event System.Action? GearBenchPressed;

    /// <param name="embedded">Inside another panel (the Garage's Build tab): no frame of its own.</param>
    public ProductionPanel(ColonySession session, bool embedded = false)
    {
        _session = session;
        CustomMinimumSize = new Vector2(embedded ? 0 : 320, 0);
        if (embedded) AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        Visible = false;
        var box = Ui.Panel(this);

        var header = Widgets.SectionHeader("Production", "skill_crafting");
        _title = header.GetChildren().OfType<Label>().First();
        header.AddChild(new InfoTip("crafting", BuildingContext));
        box.AddChild(header);
        _powerRow = InfoTip.Beside(_power, "power", BuildingContext);
        _power.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(_powerRow);
        _statusRow = InfoTip.Beside(_status, "production", BuildingContext);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(_statusRow);
        box.AddChild(_tabs);

        // Queue tab: "Again" and the bench buttons on top, then the orders.
        var queuePage = Widgets.Column(6);
        var tools = new HFlowContainer();
        tools.AddThemeConstantOverride("h_separation", 4);
        tools.AddThemeConstantOverride("v_separation", 4);
        _again = PanelKit.Focusable(Widgets.IconButton("ui_plus", "Again", "Queue the last order again", QueueAgain));
        _gearBench = PanelKit.Focusable(Widgets.IconButton("bld_armory", "Gear Bench", "Fit and remove gear mods, fit Mk kits, compare builds",
            () => GearBenchPressed?.Invoke()));
        _cyber = PanelKit.Focusable(Widgets.IconButton("body_prosthetic", "Cybernetics", "The cybernetics tree: which limbs can be made here and what they need",
            () => CyberneticsPressed?.Invoke()));
        tools.AddChild(_again);
        tools.AddChild(_gearBench);
        tools.AddChild(_cyber);
        queuePage.AddChild(tools);
        _orders.AddChild(_empty);
        _orderRows = new KeyedRows<int>(_orders, offset: 1);
        queuePage.AddChild(_orders);
        _tabs.Add("queue", "Queue", queuePage, "The orders, in the order they are worked");

        // Recipes tab: group tabs, the list, the picked recipe and the queue row.
        _groupTabs.Visible = false;
        _groupTabs.AddThemeConstantOverride("h_separation", 2);
        _groupTabs.AddThemeConstantOverride("v_separation", 2);
        _recipesPage.AddChild(_groupTabs);
        _recipeRows = new KeyedRows<string>(_recipeList);
        _recipesPage.AddChild(_recipeList);
        var detailCard = new PanelContainer { ThemeTypeVariation = "Inset" };
        var detailCol = Widgets.Column(6);
        detailCard.AddChild(detailCol);
        detailCol.AddChild(_detail);
        var queueRow = Widgets.Row(6);
        queueRow.AddChild(_count);
        queueRow.AddChild(_repeat);
        queueRow.AddChild(Widgets.Spacer());
        _queueButton = PanelKit.Focusable(Widgets.IconButton("ui_plus", "Queue", "Add to the queue", Queue, variation: "Primary"));
        queueRow.AddChild(_queueButton);
        detailCol.AddChild(queueRow);
        _repeat.Toggled += on => _count.Modulate = on ? new Color(1, 1, 1, 0.45f) : Colors.White;

        // The recipe book shares the tab at cooking buildings.
        _book.AddChild(PanelKit.Header("Recipe book", "skill_cooking", "spoilage"));
        _book.AddChild(Widgets.Label("stock / per batch · soonest spoil", "Small", Palette.TextDim));
        _dishRows = new KeyedRows<string>(_dishes);
        _book.AddChild(_dishes);
        _book.AddChild(_locked);
        _book.Visible = false;
        _recipesPage.AddChild(_book);
        _recipesPage.AddChild(detailCard);
        _tabs.Add("recipes", "Recipes", _recipesPage, "Everything this building can make");

        // Charging tab (Electronics Lab only).
        _bench.AddChild(PanelKit.Header("Charging bench", "stat_battery", "electronics"));
        _bench.AddChild(_benchNote);
        _benchRows = new KeyedRows<string>(_benchList);
        _bench.AddChild(_benchList);
        _tabs.Add("charging", "Charging", _bench, "Reusable devices that came home flat");
        _tabs.SetTabVisible("charging", false);
        _tabs.Changed += _ => Refresh();
    }

    private HelpContext BuildingContext() => new()
    {
        Colony = _session.Sim,
        Building = BuildingId is { } id ? _session.Sim.State.FindBuilding(id) : null,
    };

    /// <summary>The recipe groups of a bench (ungrouped ones as "Other" when others are grouped); empty when none are.</summary>
    private static List<string> Groups(IReadOnlyList<RecipeDef> recipes)
    {
        if (recipes.All(r => r.Group is null)) return [];
        var order = new List<string> { "Components", "Devices", "Upgrades", "Disassembly" };
        return recipes.Select(GroupOf).Distinct().OrderBy(g => order.IndexOf(g) is var i and >= 0 ? i : order.Count).ToList();
    }

    private static string GroupOf(RecipeDef r) => r.Group ?? "Other";

    /// <summary>Switches to the Recipes tab on a group (the group tabs themselves and debug captures).</summary>
    public void ShowGroup(string group)
    {
        _group = group;
        _recipesShown = "";
        _tabs.Select("recipes");
        Refresh();
    }

    public void Show(int? buildingId)
    {
        var changed = BuildingId != buildingId;
        BuildingId = buildingId;
        _recipesShown = _bookShown = _detailShown = "";
        if (changed)
        {
            _orderRows.Reset();
            _orderRefs.Clear();
            _selected = null;
            _group = null;
            _count.Value = 1;
            _repeat.ButtonPressed = false;
            // An empty queue opens on the recipes: that is what there is to do.
            var b = buildingId is { } id ? _session.Sim.State.FindBuilding(id) : null;
            _tabs.Select(DebugCapture.Value("--production-tab") is { Length: > 0 } tab ? tab : b is { Queue.Count: > 0 } ? "queue" : "recipes");
        }
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        var recipes = building is null ? [] : sim.Data.RecipesFor(building.DefId);
        _set.Visible(this, recipes.Count > 0);
        if (!Visible || building is null) return;
        _set.Visible(_gearBench, sim.Scavenge?.GearMods is { } gear && gear.Def.Rules.Benches.Contains(building.DefId));
        _set.Visible(_cyber, sim.Data.Cybernetics is { } cyber && recipes.Any(r => cyber.Node(r.Item) is not null));

        var cooking = IsCooking(sim, building);
        _set.Text(_title, (cooking ? ColonyText.BuildingName(sim, building) : "Production").ToUpperInvariant());
        _tabs.SetLabel("recipes", cooking ? "Recipe book" : "Recipes");
        _tabs.SetLabel("queue", building.Queue.Count > 0 ? $"Queue {building.Queue.Count}" : "Queue");
        var e = sim.Data.Electronics;
        var lab = e is not null && building.DefId == e.Disassembly.Building;
        _tabs.SetTabVisible("charging", lab);
        if (lab) _tabs.SetLabel("charging", sim.State.Charging.Count > 0 ? $"Charging {sim.State.Charging.Count}" : "Charging");

        _status.Text = Status(sim, building);
        RefreshPower(sim, building);
        switch (_tabs.Selected)
        {
            case "queue":
                RefreshOrders(sim, building);
                break;
            case "recipes":
                _set.Visible(_book, cooking);
                _set.Visible(_recipeList, !cooking);
                if (cooking)
                {
                    _set.Visible(_groupTabs, false);
                    RefreshBook(sim, building, recipes);
                }
                else
                {
                    RefreshGroups(recipes);
                    RefreshRecipes(sim, building, _group is null ? recipes : recipes.Where(r => GroupOf(r) == _group).ToList());
                }
                RefreshDetail(sim, building);
                break;
            case "charging":
                if (lab) RefreshBench(sim, e!);
                break;
        }
    }

    private static bool IsCooking(ColonySim sim, Building building) =>
        sim.Data.Food is not null && sim.Data.Buildings[building.DefId].Job == "cooking";

    // ---- Queue -------------------------------------------------------------------------------------------------------

    private void RefreshOrders(ColonySim sim, Building building)
    {
        _set.Visible(_empty, building.Queue.Count == 0);
        if (Last.TryGetValue(building.Id, out var last) && sim.Data.Recipes.TryGetValue(last.Recipe, out var lastRecipe))
        {
            _set.Visible(_again, true);
            _again.Text = $"Again: {lastRecipe.Name} {(last.Repeat ? "↻" : $"×{last.Count}")}";
            _again.Disabled = !sim.CanQueue(building, lastRecipe).Ok;
        }
        else _set.Visible(_again, false);

        var status = sim.CraftStatus(building);
        var next = building.Queue.FirstOrDefault(o => o.Paid) ?? building.Queue.FirstOrDefault();
        var keys = building.Queue.Select(o => o.Id).ToList();
        foreach (var gone in _orderRefs.Keys.Except(keys).ToList()) _orderRefs.Remove(gone);
        _orderRows.Sync<PanelContainer>(keys, orderId => MakeOrderRow(sim, building, orderId), (orderId, _) =>
        {
            if (building.Queue.Find(o => o.Id == orderId) is not { } order || !_orderRefs.TryGetValue(orderId, out var row)) return;
            var recipe = sim.Data.Recipes[order.RecipeId];
            var active = order.Paid;
            if (row.Card.ThemeTypeVariation != (active ? "CardSelected" : "Card")) row.Card.ThemeTypeVariation = active ? "CardSelected" : "Card";
            _set.Modulate(row.Icon, active ? Palette.Amber : Palette.TextMuted);
            _set.Text(row.Name, recipe.Name);
            var amount = order.Repeat ? "Repeat" : order.Remaining > 1 ? $"×{order.Remaining}" : "";
            SetChip(row.Amount, amount, order.Repeat ? Palette.Amber : Palette.TextMuted,
                order.Repeat ? "Starts again after each batch: cancel it to stop" : $"{order.Remaining} batches left");
            var (state, color, tip) = active ? ("", Palette.Text, "")
                : order == next ? status switch
                {
                    CraftStatus.WaitingForInputs => ("Waiting for materials", Palette.Danger, "Its inputs are not in the stores yet"),
                    CraftStatus.NoWorkers => ("No workers", Palette.Danger, "Assign someone to this building"),
                    CraftStatus.NeedsSkill => ("Needs skill", Palette.Danger, "No worker has the skill yet; they practise meanwhile"),
                    CraftStatus.NeedsPower => ("Needs power", Palette.Warn, "Tier 3: waits for a fed grid"),
                    _ => ("Next", Palette.TextMuted, "Starts when the workers are on shift"),
                }
                : ("Not started", Palette.TextDim, "Waits for the orders above it");
            SetChip(row.State, state, color, tip);
            _set.Visible(row.Bar, active);
            _set.Visible(row.Progress, active);
            if (active)
            {
                Ui.Glide(row.Bar, order.Progress / recipe.Work, _session);
                _set.Text(row.Progress, $"{order.Progress:0.#}/{recipe.Work:0.#}");
            }
            if (row.Power is { } power) SetChip(power, sim.IsPowered(building) ? "power" : "no power", sim.IsPowered(building) ? Palette.Good : Palette.Warn);
        });
    }

    private PanelContainer MakeOrderRow(ColonySim sim, Building building, int orderId)
    {
        var order = building.Queue.Find(o => o.Id == orderId)!;
        var recipe = sim.Data.Recipes[order.RecipeId];
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var inner = Widgets.Row(8);
        card.AddChild(inner);
        var icon = Icons.Rect(OutputIcon(sim, recipe), 22, Palette.TextMuted);
        inner.AddChild(icon);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 2);
        var line = Widgets.Row(6);
        var name = Widgets.Label(recipe.Name);
        name.ClipText = true;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(name);
        var amount = PanelKit.StateChip("", Palette.TextMuted);
        var state = PanelKit.StateChip("", Palette.TextMuted);
        line.AddChild(amount);
        line.AddChild(state);
        var progress = Widgets.Label("", "Number", Palette.TextMuted);
        line.AddChild(progress);
        column.AddChild(line);
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), MaxValue = 1 };
        bar.AddThemeStyleboxOverride("fill", HudTheme.Box(Palette.Amber, radius: 2, padX: 0, padY: 0));
        column.AddChild(bar);
        inner.AddChild(column);
        var power = recipe.NeedsPower ? PowerBadge(sim.IsPowered(building)) : null;
        if (power is not null) inner.AddChild(power);
        var buildingId = building.Id;
        var cancel = PanelKit.Focusable(Widgets.IconButton("ui_close", "", "Cancel (a batch in progress gives its inputs back)",
            () => _session.Execute(new CancelOrder(buildingId, orderId)), variation: "Flat"));
        inner.AddChild(cancel);
        _orderRefs[orderId] = new OrderRow(card, icon, name, amount, state, progress, bar, power);
        return card;
    }

    private void SetChip(PanelContainer chip, string text, Color color, string? tip = null)
    {
        _set.Visible(chip, text.Length > 0);
        if (text.Length == 0) return;
        var label = PanelKit.ChipText(chip);
        _set.Text(label, text);
        _set.FontColor(label, color);
        if (tip is not null) _set.Tooltip(chip, tip);
    }

    private void QueueAgain()
    {
        if (BuildingId is not { } id || !Last.TryGetValue(id, out var last)) return;
        _session.Execute(new QueueRecipe(id, last.Recipe, last.Count, last.Repeat));
        Refresh();
    }

    // ---- Recipes -----------------------------------------------------------------------------------------------------

    private void RefreshGroups(IReadOnlyList<RecipeDef> recipes)
    {
        var groups = Groups(recipes);
        _set.Visible(_groupTabs, groups.Count > 0);
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
                var tab = PanelKit.Focusable(Motion.OnPress(Widgets.TextButton(g, () => ShowGroup(group), $"{g}: {count} recipe{(count == 1 ? "" : "s")}", "Tab")));
                tab.ToggleMode = true;
                tab.CustomMinimumSize = new Vector2(0, 26);
                _groupButtons[g] = tab;
                _groupTabs.AddChild(tab);
            }
        }
        foreach (var (g, tab) in _groupButtons) tab.SetPressedNoSignal(g == _group);
    }

    private void RefreshRecipes(ColonySim sim, Building building, IReadOnlyList<RecipeDef> recipes)
    {
        // Decrypt orders only show while their chip is in the stores and the schematic is still unknown.
        recipes = recipes.Where(r => r.Learns is not { } learns
            || sim.State.Inventory.Has(learns) && !Remnants.Core.Schematics.Schematics.Has(sim, learns)).ToList();
        var chips = recipes.Where(r => r.Learns is not null).Select(r => $"{r.Learns}:{sim.State.Inventory.Count(r.Learns!)}");
        var signature = $"{building.Id}:{building.Level}:{_group}:{string.Join(",", sim.State.Schematics.Known)}:{string.Join(",", chips)}";
        var checks = recipes.ToDictionary(r => r.Id, r => sim.CanQueue(building, r));
        if (signature != _recipesShown)
        {
            _recipesShown = signature;
            if (_selected is null || recipes.All(r => r.Id != _selected))
                _selected = recipes.FirstOrDefault(r => checks[r.Id].Ok)?.Id ?? recipes.FirstOrDefault()?.Id;
            // Known recipes first, then the locked ones, each in data order.
            var keys = recipes.OrderBy(r => checks[r.Id].Ok ? 0 : 1).Select(r => r.Id).ToList();
            foreach (var gone in _recipeRefs.Keys.Except(keys).ToList()) _recipeRefs.Remove(gone);
            _recipeRows.Sync<PanelContainer>(keys, id => MakeRecipeRow(sim, building, sim.Data.Recipes[id]));
        }
        foreach (var r in recipes)
        {
            if (!_recipeRefs.TryGetValue(r.Id, out var row)) continue;
            var check = checks[r.Id];
            var picked = r.Id == _selected;
            if (row.Card.ThemeTypeVariation != (picked ? "CardSelected" : "Card")) row.Card.ThemeTypeVariation = picked ? "CardSelected" : "Card";
            var afford = sim.State.Stockpile.CanAfford(r.Inputs) && r.ItemInputs.All(kv => sim.State.Inventory.Count(kv.Key) >= kv.Value);
            _set.FontColor(row.Name, !check.Ok ? Palette.TextDim : afford ? Palette.Text : Palette.TextMuted);
            var note = check.Error switch
            {
                CommandError.RecipeLevelTooLow => $"L{r.MinLevel}",
                CommandError.RecipeLocked => "schematic",
                _ when check.Ok && !afford => "short",
                _ => r.NeedsPower ? "power" : "",
            };
            _set.Text(row.Note, note);
            _set.FontColor(row.Note, check.Ok && !afford ? Palette.Warn : Palette.TextDim);
            _set.Tooltip(row.Note, check.Ok ? afford ? r.NeedsPower ? "Tier 3: progresses only while powered" : "" : "Some inputs are not in the stores: it waits for them" : check.Message);
            row.Add.Disabled = !check.Ok;
            _set.Tooltip(row.Add, check.Ok ? $"Queue {(_repeat.ButtonPressed ? "on repeat" : $"×{_count.Value}")}" : check.Message);
        }
    }

    private PanelContainer MakeRecipeRow(ColonySim sim, Building building, RecipeDef r)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card", TooltipText = Tooltip(sim, r) };
        var row = Widgets.Row(6);
        row.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(row);
        row.AddChild(Icons.Rect(OutputIcon(sim, r), 18, Palette.Amber));
        var name = Widgets.Label(r.Learns is null && (r.Amount > 1.001 || r.Byproducts.Count > 0) ? $"{r.Name} → {Output(sim, r)}" : r.Name);
        name.ClipText = true;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.MouseFilter = MouseFilterEnum.Ignore;
        row.AddChild(name);
        var note = Widgets.Label("", "Small", Palette.TextDim);
        note.MouseFilter = MouseFilterEnum.Stop;
        row.AddChild(note);
        var id = r.Id;
        var add = PanelKit.Focusable(Widgets.IconButton("ui_plus", "", "Queue", () =>
        {
            _selected = id;
            Queue();
        }, variation: "Flat"));
        add.CustomMinimumSize = new Vector2(28, 26);
        row.AddChild(add);
        PanelKit.Clickable(card, () => Select(id));
        _recipeRefs[r.Id] = new RecipeRow(card, name, note, add);
        return card;
    }

    private void Select(string recipeId)
    {
        if (_selected == recipeId) return;
        _selected = recipeId;
        _detailShown = "";
        Refresh();
    }

    /// <summary>The picked recipe: what it makes, needs, its work and time, quality odds, power, research chance, why not.</summary>
    private void RefreshDetail(ColonySim sim, Building building)
    {
        var r = _selected is { } id && sim.Data.Recipes.TryGetValue(id, out var found) && found.Building == building.DefId ? found : null;
        var check = r is null ? null : sim.CanQueue(building, r);
        _queueButton.Disabled = check is not { Ok: true };
        _queueButton.TooltipText = check is { Ok: false } ? check.Message : "Add to the queue";
        if (r is null)
        {
            if (_detailShown == "none") return;
            _detailShown = "none";
            Widgets.Clear(_detail);
            _detail.AddChild(Widgets.Label("Pick a recipe.", "Muted"));
            return;
        }
        var cooking = IsCooking(sim, building);
        var afford = sim.State.Stockpile.CanAfford(r.Inputs) && CraftingIngredients(sim, r) && r.ItemInputs.All(kv => sim.State.Inventory.Count(kv.Key) >= kv.Value);
        var odds = sim.QualityChances(building, r).Where(c => c.Chance > 0.005).ToList();
        var average = sim.CraftEffortPerHourAveraged(building);
        var signature = $"{r.Id}|{afford}|{check!.Ok}|{sim.IsPowered(building)}|{string.Join(",", odds.Select(o => $"{o.Tier}{o.Chance:0.00}"))}|{average:0.0}";
        if (signature == _detailShown) return;
        _detailShown = signature;
        Widgets.Clear(_detail);

        var head = Widgets.Row(6);
        head.AddChild(Icons.Rect(OutputIcon(sim, r), 20, Palette.Amber));
        var title = Widgets.Label(r.Name, "Heading");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.ClipText = true;
        head.AddChild(title);
        if (r.Learns is null) head.AddChild(Widgets.Label(Output(sim, r), "Small", Palette.TextMuted));
        _detail.AddChild(head);

        var needs = Ui.Rich();
        needs.Text = $"Needs [color=#{Ui.Hex(afford ? Ui.Good : Ui.Bad)}]{Inputs(sim, r)}[/color]";
        _detail.AddChild(needs);
        var hours = average > 1e-6 ? ColonyText.Duration((int)System.Math.Ceiling(r.Work / average), sim.Data.Rules) : "no one on shift";
        _detail.AddChild(PanelKit.Tip($"Work {r.Work:0.#} · ≈ {hours} a batch with this crew", cooking ? "cooking" : "crafting", context: BuildingContext));
        if (r.MinSkill > 0)
            _detail.AddChild(PanelKit.Tip($"A worker needs {sim.Data.Jobs[sim.Data.Buildings[building.DefId].Job!].Skill} {r.MinSkill:0}", "skills"));
        if (odds.Count > 0 && sim.Scavenge is { } items)
            _detail.AddChild(PanelKit.Tip((cooking ? "Cook quality: " : "Quality: ") + string.Join("  ", odds.Select(c =>
                $"{items.Rules.Quality.Find(q => q.Id == c.Tier)?.Name ?? c.Tier} {c.Chance * 100:0}%")), cooking ? "cooking" : "crafting", "Small", Palette.Text, BuildingContext));
        if (r.Group == "Disassembly")
        {
            _detail.AddChild(Widgets.Wrapped($"Yields {Output(sim, r)}; skill and Circuit Tinkerer may add one more of each.", "Small", Palette.Amber));
            if (r.ResearchChance > 0) _detail.AddChild(PanelKit.Tip($"{r.ResearchChance * 100:0}% chance of Research Data per batch", "research"));
        }
        if (r.NeedsPower)
            _detail.AddChild(PanelKit.Tip(sim.IsPowered(building) ? "Needs power: the bench is powered." : "Needs power: waits until the bench is on a fed grid.",
                "power", "Small", sim.IsPowered(building) ? Palette.Good : Palette.Warn, BuildingContext));
        if (cooking && r.Item is { } dish && sim.Data.Food?.Items.GetValueOrDefault(dish) is { } food)
            _detail.AddChild(PanelKit.Tip(FoodText.Effects(sim, food), "food_buffs"));
        if (cooking && r.Ingredients.Keys.Any(k => sim.Data.Food!.Items.Values.Any(i => i.Tainted && sim.Data.Food.Matches(i.Id, k))))
            _detail.AddChild(Widgets.Wrapped("Husk-zone meat may stay tainted unless the dish comes out well.", "Small", Palette.Warn));
        if (!check.Ok) _detail.AddChild(Widgets.Wrapped(check.Message, "Small", Palette.Danger));
    }

    private static string Inputs(ColonySim sim, RecipeDef r)
    {
        var inputs = ColonyText.Cost(r.Inputs, sim.Data);
        if (r.ItemInputs.Count > 0)
            inputs += ", " + string.Join(", ", r.ItemInputs.Select(kv => $"{kv.Value} {sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}"));
        if (r.Ingredients.Count > 0)
            inputs = string.Join(", ", r.Ingredients.Select(kv => $"{kv.Value} {FoodText.KeyName(sim, kv.Key)}").Prepend(r.Inputs.Count > 0 ? inputs : null).OfType<string>());
        return inputs;
    }

    private static string Tooltip(ColonySim sim, RecipeDef r)
    {
        if (r.Item is not { } item || sim.Data.Food?.Items.GetValueOrDefault(item)?.Meal is not { } meal) return r.Name;
        var buffs = meal.Buffs.Keys.Append(meal.FieldBuff).OfType<string>().Distinct()
            .Select(b => sim.Data.Food!.Buffs.GetValueOrDefault(b)).OfType<BuffDef>().Select(FoodText.BuffTip);
        return string.Join("\n\n", new[] { $"{r.Name}: {FoodText.Effects(sim, sim.Data.Food!.Items[item])}" }.Concat(buffs));
    }

    // ---- Recipe book -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The recipe book: one line per known dish (the picked one open), locked ones folded away. Built when the known set
    /// changes; stock, "can make" and spoil times update in place.
    /// </summary>
    private void RefreshBook(ColonySim sim, Building building, IReadOnlyList<RecipeDef> recipes)
    {
        var checks = recipes.ToDictionary(r => r.Id, r => sim.CanQueue(building, r));
        bool Known(RecipeDef r) => checks[r.Id].Error is not (CommandError.RecipeLocked or CommandError.RecipeLevelTooLow);
        var known = recipes.Where(Known).ToList();
        var locked = recipes.Where(r => !Known(r)).ToList();
        var signature = $"{building.Id}:{building.Level}:{string.Join(",", known.Select(r => r.Id))}";
        if (_selected is null || known.All(r => r.Id != _selected))
            _selected = known.FirstOrDefault(r => checks[r.Id].Ok)?.Id ?? known.FirstOrDefault()?.Id;
        if (signature != _bookShown)
        {
            _bookShown = signature;
            _locked.SetTitle($"Locked ({locked.Count})");
            _locked.Visible = locked.Count > 0;
            Widgets.Clear(_locked.Body);
            foreach (var r in locked)
            {
                var row = Widgets.Row(6);
                row.AddChild(Icons.Rect(Icons.Get("stat_lock", 16), 16, Palette.TextDim));
                var why = checks[r.Id].Error == CommandError.RecipeLevelTooLow ? $"needs {sim.Data.Buildings[building.DefId].Name} L{r.MinLevel}" : checks[r.Id].Message;
                row.AddChild(Widgets.Wrapped($"{r.Name}: {why}", "Small", Palette.TextMuted));
                _locked.Body.AddChild(row);
            }
            foreach (var gone in _dishRefs.Keys.Except(known.Select(r => r.Id)).ToList()) _dishRefs.Remove(gone);
            _dishRows.Sync<PanelContainer>(known.Select(r => r.Id).ToList(), id => MakeDish(sim, sim.Data.Recipes[id]));
        }
        foreach (var r in known)
        {
            if (!_dishRefs.TryGetValue(r.Id, out var row)) continue;
            var picked = r.Id == _selected;
            if (row.Card.ThemeTypeVariation != (picked ? "CardSelected" : "Card")) row.Card.ThemeTypeVariation = picked ? "CardSelected" : "Card";
            if (row.Body.Visible != picked)
            {
                if (picked) Motion.Reveal(row.Body);
                else row.Body.Visible = false;
            }
            var batches = Batches(sim, r);
            _set.Text(row.CanMake, batches > 0 ? $"can make ×{batches}" : "short");
            _set.FontColor(row.CanMake, batches > 0 ? Palette.Good : Palette.Danger);
            if (picked) foreach (var update in row.Update) update();
        }
    }

    /// <summary>Whole batches the stores and pantry cover now.</summary>
    private static int Batches(ColonySim sim, RecipeDef r)
    {
        var n = int.MaxValue;
        foreach (var (key, need) in r.Ingredients)
            if (need > 0) n = System.Math.Min(n, Pantry.Available(sim.State, sim.Data, key) / need);
        foreach (var (res, amount) in r.Inputs)
            if (amount > 0) n = System.Math.Min(n, (int)(sim.State.Stockpile.Get(res) / amount));
        return n == int.MaxValue ? 99 : System.Math.Min(n, 99);
    }

    private PanelContainer MakeDish(ColonySim sim, RecipeDef r)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card", TooltipText = Tooltip(sim, r) };
        var column = Widgets.Column(4);
        column.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(column);
        var head = Widgets.Row(6);
        head.MouseFilter = MouseFilterEnum.Ignore;
        var own = r.Item is { } dish && sim.Data.Food?.IsFood(dish) == true;
        head.AddChild(Icons.Rect(OutputIcon(sim, r), 20, own ? FoodText.Tint(sim, r.Item!, Palette.Amber) : Palette.Amber));
        var name = Widgets.Label(r.Amount > 1 ? $"{r.Name} ×{r.Amount:0}" : r.Name);
        name.ClipText = true;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        var food = r.Item is { } item ? sim.Data.Food?.Items.GetValueOrDefault(item) : null;
        var summary = Widgets.Label(food is null ? "" : $"food {food.FoodValue:0.#}", "Small", Palette.TextMuted);
        head.AddChild(summary);
        if (food?.Meal is { } meal)
            foreach (var buff in meal.Buffs.Keys.Append(meal.FieldBuff).OfType<string>().Distinct().Take(3))
            {
                var icon = Icons.Rect(Icons.Get(FoodText.BuffIcon(buff), 14), 14, Palette.Amber);
                icon.TooltipText = sim.Data.Food!.Buffs.GetValueOrDefault(buff) is { } def ? FoodText.BuffTip(def) : buff;
                icon.MouseFilter = MouseFilterEnum.Stop;
                head.AddChild(icon);
            }
        var canMake = Widgets.Label("", "Small", Palette.Good);
        canMake.CustomMinimumSize = new Vector2(84, 0);
        canMake.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(canMake);
        column.AddChild(head);

        // The opened card: ingredient chips (updated in place) and the dish's effects.
        var body = Widgets.Column(4);
        body.Visible = false;
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 3);
        chips.AddThemeConstantOverride("v_separation", 3);
        var updates = new List<System.Action>();
        foreach (var (key, need) in r.Ingredients)
        {
            // A tag shows the icon of a food it stands for (any meat → game meat).
            var shown = key.StartsWith(FoodData.TagPrefix)
                ? sim.Data.Food!.Items.Keys.FirstOrDefault(i => sim.Data.Food.Matches(i, key)) ?? key
                : key;
            var chip = Widgets.Chip(FoodText.Icon(sim, shown, 14), "", Palette.Text, "", FoodText.Tint(sim, shown, Palette.TextMuted));
            var label = PanelKit.ChipText(chip);
            var k = key;
            var n = need;
            updates.Add(() =>
            {
                var s = _session.Sim;
                var have = Pantry.Available(s.State, s.Data, k);
                var soon = FoodText.SoonestSpoil(s, k);
                var text = $"{FoodText.KeyName(s, k)} {have}/{n}";
                _set.Text(label, soon is { } t && t < 3 * s.Data.Rules.TicksPerDay ? $"{text} · {FoodText.SpoilsIn(s, t)}" : text);
                _set.FontColor(label, have >= n ? Palette.Text : Palette.Danger);
                _set.Tooltip(chip, $"{FoodText.KeyName(s, k)}: {have} fresh in the pantry, {n} per batch" + (soon is { } t2 ? $"; the soonest spoils in {FoodText.SpoilsIn(s, t2)}" : ""));
            });
            chips.AddChild(chip);
        }
        foreach (var (res, amount) in r.Inputs)
        {
            var chip = Widgets.Chip(Icons.Resource(res, 14), $"{sim.Data.Resources[res].Name} {amount:0.#}");
            var label = PanelKit.ChipText(chip);
            var id = res;
            var a = amount;
            updates.Add(() => _set.FontColor(label, _session.Sim.State.Stockpile.Has(id, a) ? Palette.Text : Palette.Danger));
            chips.AddChild(chip);
        }
        body.AddChild(chips);
        column.AddChild(body);
        var recipeId = r.Id;
        PanelKit.Clickable(card, () => Select(recipeId));
        _dishRefs[r.Id] = new DishRow(card, summary, canMake, body, updates);
        return card;
    }

    // ---- Charging ----------------------------------------------------------------------------------------------------

    /// <summary>The charging bench: drained devices in arrival order with progress and ETA (only at the Electronics Lab).</summary>
    private void RefreshBench(ColonySim sim, ElectronicsDef e)
    {
        var charging = sim.State.Charging;
        var fed = sim.State.Power.Charging;
        var atOnce = System.Math.Min(charging.Count, e.Charging.MaxAtOnce);
        _set.Text(_benchNote, charging.Count == 0 ? "Nothing to charge. Reusable devices that come home flat wait here."
            : fed ? $"On the grid: {atOnce} charging at once, {e.Charging.PoweredRate / System.Math.Max(e.Charging.UnpoweredRate, 1e-6):0.#}× the trickle rate."
            : $"Off the grid: trickle charging, {atOnce} at once. A fed grid charges far faster.");
        _set.FontColor(_benchNote, charging.Count > 0 && !fed ? Palette.Warn : Palette.TextMuted);
        var keys = charging.Select((c, i) => $"{i}:{c.ItemId}").ToList();
        foreach (var gone in _benchRefs.Keys.Except(keys).ToList()) _benchRefs.Remove(gone);
        _benchRows.Sync<HBoxContainer>(keys, key => MakeChargeRow(sim, key), (key, _) =>
        {
            if (!_benchRefs.TryGetValue(key, out var row)) return;
            var index = int.Parse(key[..key.IndexOf(':')]);
            var item = charging[index];
            var active = index < e.Charging.MaxAtOnce;
            _set.Modulate(row.Icon, active ? Palette.Amber : Palette.TextDim);
            var eta = Power.ChargeEta(sim.State, sim.Data, item);
            var when = eta is { } t ? $"ready in {ColonyText.Duration(t, sim.Data.Rules)}" : "not charging";
            var name = sim.Scavenge?.Items.GetValueOrDefault(item.ItemId)?.Name ?? item.ItemId;
            _set.Text(row.Text, $"{name}  ·  {(active ? when : "queued, " + when)}");
            Ui.Glide(row.Bar, item.Work > 0 ? item.Done / item.Work : 1, _session);
            row.Bar.AddThemeStyleboxOverride("fill", fed ? FedFill : TrickleFill);
        });
    }

    private static readonly StyleBox FedFill = HudTheme.Box(Palette.Good, radius: 2, padX: 0, padY: 0);
    private static readonly StyleBox TrickleFill = HudTheme.Box(Palette.Warn, radius: 2, padX: 0, padY: 0);

    private HBoxContainer MakeChargeRow(ColonySim sim, string key)
    {
        var itemId = key[(key.IndexOf(':') + 1)..];
        var def = sim.Scavenge?.Items.GetValueOrDefault(itemId);
        var row = Widgets.Row(8);
        var icon = Icons.Rect(def is null ? Icons.Get("item_tool", 18) : Icons.Item(def.DefinedId, def.Kind.ToString(), 18), 18, Palette.TextDim);
        row.AddChild(icon);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 1);
        var text = Widgets.Label("", "Small", Palette.Text);
        col.AddChild(text);
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5), MaxValue = 1 };
        col.AddChild(bar);
        row.AddChild(col);
        _benchRefs[key] = new ChargeRow(icon, text, bar);
        return row;
    }

    // ---- Shared ------------------------------------------------------------------------------------------------------

    /// <summary>"Powered: +25% crafting speed" / "Unpowered …", for buildings that draw from the grid.</summary>
    private void RefreshPower(ColonySim sim, Building building)
    {
        var nodes = sim.PowerNodes();
        var node = nodes.FirstOrDefault(n => n.BuildingId == building.Id && n.Role == PowerRole.Consumer);
        _set.Visible(_powerRow, node is not null);
        if (node is null) return;
        var grid = nodes.Any(n => n.Role is PowerRole.Generator or PowerRole.Storage);
        var (text, color) = !node.On ? ("Switched off the grid: no bonus, tier 3 parts wait.", Palette.TextMuted)
            : sim.IsPowered(building) ? ($"Powered: {node.Note}.", Palette.Good)
            : !grid ? ($"Unpowered: no generator yet. Power gives {node.Note}.", Palette.TextMuted)
            : !PowerText.Idle(sim, node) ? ($"Unpowered: the grid cannot feed its {node.Amount:0.#} draw. Power gives {node.Note}.", Palette.Warn)
            : ($"Idle: it draws only while someone is on shift. Power gives {node.Note}.", Palette.TextMuted);
        _set.Text(_power, text);
        _set.FontColor(_power, color);
    }

    private static string Status(ColonySim sim, Building building)
    {
        var rate = sim.CraftEffortPerTick(building);
        var average = sim.CraftEffortPerHourAveraged(building);
        var working = building.Queue.FirstOrDefault(o => o.Paid);
        return sim.CraftStatus(building) switch
        {
            CraftStatus.Idle => $"[color=#{Ui.Hex(Palette.TextMuted)}]Idle. Queue a recipe and assign workers.[/color]",
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
        if (r.Vehicle is { } tier) return $"a {sim.Data.VehicleTiers[tier].Name}";
        if (r.Item is not { } item) return $"{r.Amount:0.#} {sim.Data.Resources[r.Resource!].Name}";
        var main = $"{r.Amount:0} {Name(item)}";
        return r.Byproducts.Count == 0 ? main : main + ", " + string.Join(", ", r.Byproducts.Select(kv => $"{kv.Value} {Name(kv.Key)}"));
    }

    /// <summary>A small "needs power" chip, green when the building is fed.</summary>
    private static PanelContainer PowerBadge(bool powered)
    {
        var chip = Widgets.Chip(Icons.Get("stat_power", 14), powered ? "power" : "no power", powered ? Palette.Good : Palette.Warn,
            "Tier 3: only progresses while the building is powered.", powered ? Palette.Good : Palette.Warn);
        chip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return chip;
    }

    private static bool CraftingIngredients(ColonySim sim, RecipeDef r) =>
        r.Ingredients.All(kv => Pantry.Available(sim.State, sim.Data, kv.Key) >= kv.Value);

    private void Queue()
    {
        if (BuildingId is not { } id || _selected is not { } recipe) return;
        var repeat = _repeat.ButtonPressed;
        var count = repeat ? 1 : _count.Value;
        if (_session.Execute(new QueueRecipe(id, recipe, count, repeat)).Ok) Last[id] = (recipe, count, repeat);
        Refresh();
    }

    private static Texture2D OutputIcon(ColonySim sim, RecipeDef r) =>
        r.Learns is not null ? Icons.Get("item_schematic", 18, "item_blueprint")
        : r.Item is { } food && sim.Data.Food?.IsFood(food) == true ? FoodText.Icon(sim, food, 18)
        : r.Item is { } item && sim.Scavenge?.Items.GetValueOrDefault(item) is { } def
            ? Icons.Item(def.DefinedId, def.Kind.ToString(), 18)
            : r.Vehicle is not null ? Icons.Get("ui_vehicle", 18) : Icons.Resource(r.Resource ?? "", 18);
}
