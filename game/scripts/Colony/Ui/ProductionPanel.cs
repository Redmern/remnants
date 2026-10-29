using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The production queue of the selected building (Workshop, Tailor, Kitchen, Armory, Electronics Lab): orders with
/// progress and quality odds (cancel any), and a recipe picker to queue one batch, five, or a repeating order.
/// Locked recipes say why (level, blueprint). Core decides what is allowed.
/// </summary>
public partial class ProductionPanel : PanelContainer
{
    private static readonly (string Label, int Count, bool Repeat)[] Amounts = [("×1", 1, false), ("×5", 5, false), ("repeat", 1, true)];

    private readonly ColonySession _session;
    private readonly RichTextLabel _status = Ui.Rich();
    private readonly VBoxContainer _orders = new();
    private readonly OptionButton _recipes = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true };
    private readonly OptionButton _amount = new() { FocusMode = FocusModeEnum.None };
    private readonly RichTextLabel _recipeInfo = Ui.Rich();
    private readonly List<string> _ids = new();
    private readonly List<(ProgressBar Bar, Label Text, int OrderId)> _rows = new();
    private string _ordersShown = "";
    private string _recipesShown = "";

    public int? BuildingId { get; private set; }

    public ProductionPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(320, 0);
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Production"));
        box.AddChild(_status);
        box.AddChild(_orders);

        box.AddChild(Ui.Heading("Queue a recipe"));
        var row = new HBoxContainer();
        row.AddChild(_recipes);
        foreach (var (label, _, _) in Amounts) _amount.AddItem(label);
        row.AddChild(_amount);
        row.AddChild(Ui.Button("Queue", Queue, 60));
        box.AddChild(row);
        box.AddChild(_recipeInfo);
        _recipes.ItemSelected += _ => Refresh();
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _ordersShown = _recipesShown = "";
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        var recipes = building is null ? [] : sim.Data.RecipesFor(building.DefId);
        Visible = recipes.Count > 0;
        if (!Visible) return;

        RefreshRecipes(sim, building!, recipes);
        RefreshOrders(sim, building!);
        _status.Text = Status(sim, building!);
        _recipeInfo.Text = _recipes.Selected >= 0 && _recipes.Selected < _ids.Count ? RecipeInfo(sim, building!, sim.Data.Recipes[_ids[_recipes.Selected]]) : "";
    }

    private void RefreshRecipes(ColonySim sim, Building building, IReadOnlyList<RecipeDef> recipes)
    {
        var signature = $"{building.Id}:{building.Level}:{string.Join(",", sim.State.KnownBlueprints)}";
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
                CommandError.RecipeLocked => "  [blueprint]",
                _ => "",
            };
            _recipes.AddItem($"{r.Name} → {Output(sim, r)}{lockNote}");
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
                var line = new HBoxContainer();
                var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var text = Ui.Label(size: 13);
                var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), MaxValue = 1 };
                column.AddChild(text);
                column.AddChild(bar);
                line.AddChild(column);
                var cancel = Ui.Button("✕", () => _session.Execute(new CancelOrder(building.Id, orderId)), 30);
                cancel.TooltipText = "Cancel (a batch in progress gives its inputs back)";
                line.AddChild(cancel);
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
        var working = building.Queue.FirstOrDefault(o => o.Paid);
        return sim.CraftStatus(building) switch
        {
            CraftStatus.Idle => "[color=#999]Idle. Queue a recipe and assign workers.[/color]",
            CraftStatus.NoWorkers => $"[color=#{Ui.Hex(Ui.Bad)}]No workers assigned.[/color]",
            CraftStatus.NeedsSkill => $"[color=#{Ui.Hex(Ui.Bad)}]Workers lack the skill for every order.[/color]",
            CraftStatus.WaitingForInputs => $"[color=#{Ui.Hex(Ui.Bad)}]Waiting for materials.[/color]",
            _ when working is not null => $"Working on [b]{sim.Data.Recipes[working.RecipeId].Name}[/b], {rate:0.##} work/h: done in "
                + ColonyText.Duration((int)System.Math.Ceiling((sim.Data.Recipes[working.RecipeId].Work - working.Progress) / System.Math.Max(rate, 1e-6)), sim.Data.Rules),
            _ => $"Starting next hour, {rate:0.##} work/h.",
        };
    }

    private static string Output(ColonySim sim, RecipeDef r) =>
        r.Item is { } item ? $"{r.Amount:0} {sim.Scavenge?.Items.GetValueOrDefault(item)?.Name ?? item}" : $"{r.Amount:0.#} {sim.Data.Resources[r.Resource!].Name}";

    private static string RecipeInfo(ColonySim sim, Building building, RecipeDef r)
    {
        var inputs = ColonyText.Cost(r.Inputs, sim.Data);
        if (r.ItemInputs.Count > 0)
            inputs += ", " + string.Join(", ", r.ItemInputs.Select(kv => $"{kv.Value} {sim.Scavenge?.Items.GetValueOrDefault(kv.Key)?.Name ?? kv.Key}"));
        var afford = sim.State.Stockpile.CanAfford(r.Inputs);
        var lines = new List<string> { $"Needs [color=#{Ui.Hex(afford ? Ui.Good : Ui.Bad)}]{inputs}[/color], {r.Work:0.#} work" };
        if (r.MinSkill > 0) lines.Add($"A worker needs {sim.Data.Jobs[sim.Data.Buildings[building.DefId].Job!].Skill} {r.MinSkill:0}");
        var check = sim.CanQueue(building, r);
        if (!check.Ok) lines.Add($"[color=#{Ui.Hex(Ui.Bad)}]{check.Message}[/color]");
        var odds = sim.QualityChances(building, r).Where(c => c.Chance > 0.005).ToList();
        if (odds.Count > 0 && sim.Scavenge is { } items)
            lines.Add("Quality: " + string.Join("  ", odds.Select(c =>
                $"{items.Rules.Quality.Find(q => q.Id == c.Tier)?.Name ?? c.Tier} {c.Chance * 100:0}%")));
        return string.Join("\n", lines);
    }

    private void Queue()
    {
        if (BuildingId is not { } id || _recipes.Selected < 0 || _recipes.Selected >= _ids.Count) return;
        var (_, count, repeat) = Amounts[System.Math.Max(0, _amount.Selected)];
        _session.Execute(new QueueRecipe(id, _ids[_recipes.Selected], count, repeat));
    }
}
