using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony.Ui;

/// <summary>Recipe picker and batch progress for a production building (Workshop).</summary>
public partial class WorkshopPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly OptionButton _recipes = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None, ClipText = true };
    private readonly RichTextLabel _info = Ui.Rich();
    private readonly List<string?> _ids = new();
    private int? _shownFor;

    public int? BuildingId { get; private set; }

    public WorkshopPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Recipe"));
        var row = new HBoxContainer();
        row.AddChild(_recipes);
        row.AddChild(Ui.Button("Set", Set, 48));
        box.AddChild(row);
        box.AddChild(_info);
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _shownFor = null;
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        var def = building is null ? null : sim.Data.Buildings[building.DefId];
        Visible = def is { Recipes.Count: > 0 };
        if (!Visible) return;

        if (_shownFor != building!.Id)
        {
            _shownFor = building.Id;
            _ids.Clear();
            _recipes.Clear();
            _ids.Add(null);
            _recipes.AddItem("nothing");
            foreach (var r in def!.Recipes)
            {
                _ids.Add(r.Id);
                var output = r.Item is { } item ? sim.Scavenge?.Items.GetValueOrDefault(item)?.Name ?? item : sim.Data.Resources[r.Resource!].Name;
                _recipes.AddItem($"{r.Name} → {r.Amount:0.#} {output}  ({ColonyText.Cost(r.Inputs, sim.Data)}{(r.MinLevel > 1 ? $", L{r.MinLevel}" : "")})");
            }
            _recipes.Select(_ids.IndexOf(building.RecipeId));
        }

        var recipe = def!.Recipes.Find(r => r.Id == building.RecipeId);
        if (recipe is null)
        {
            _info.Text = "[color=#999]Idle. Pick a recipe and assign workers.[/color]";
            return;
        }
        var rate = sim.CraftEffortPerTick(building);
        var left = recipe.Work - building.CraftProgress;
        var eta = rate > 0 ? ColonyText.Duration((int)System.Math.Ceiling(left / rate), sim.Data.Rules) : "no workers";
        var status = building.CraftPaid ? $"{building.CraftProgress:0.#}/{recipe.Work:0.#} work, done in {eta}"
            : sim.State.Stockpile.CanAfford(recipe.Inputs) ? "starting next hour" : $"[color=#{Ui.Hex(Ui.Bad)}]waiting for {ColonyText.Cost(recipe.Inputs, sim.Data)}[/color]";
        _info.Text = $"Making [b]{recipe.Name}[/b]: {status}";
    }

    private void Set()
    {
        if (BuildingId is { } id && _recipes.Selected >= 0) _session.Execute(new SetRecipe(id, _ids[_recipes.Selected]));
    }
}
