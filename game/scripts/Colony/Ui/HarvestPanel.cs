using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// What the selected Farm, Greenhouse or Hunting Lodge works on (data/food.json harvests): the current crop with its
/// standing crop, when it comes in and the weather on it, and a card per option to switch to (yield, growing time,
/// how long the food keeps). Switching loses the standing crop, so it asks first when there is one. Core decides.
/// </summary>
public partial class HarvestPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly RichTextLabel _current = Ui.Rich();
    private readonly GridContainer _options = new() { Columns = 2 };
    private readonly Label _title;
    private string _shown = "";

    public int? BuildingId { get; private set; }

    public HarvestPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(320, 0);
        Visible = false;
        var box = Ui.Panel(this);
        var header = Widgets.SectionHeader("Crops", "skill_farming");
        _title = header.GetChildren().OfType<Label>().First();
        box.AddChild(header);
        box.AddChild(_current);
        box.AddChild(Ui.Heading("Plant or hunt", "ui_plus"));
        _options.AddThemeConstantOverride("h_separation", 6);
        _options.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(_options);
    }

    public void Show(int? buildingId)
    {
        BuildingId = buildingId;
        _shown = "";
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        var building = BuildingId is { } id ? sim.State.FindBuilding(id) : null;
        var harvest = building is null ? null : Harvesting.Current(sim.Data, building);
        Visible = harvest is not null;
        if (!Visible) return;
        var def = sim.Data.Buildings[building!.DefId];
        _title.Text = (def.Job == "hunting" ? "Hunting" : "Crops").ToUpperInvariant();
        _current.Text = Current(sim, building, harvest!);

        var signature = $"{building.Id}:{building.Level}:{harvest!.Id}";
        if (signature == _shown) return;
        _shown = signature;
        Widgets.Clear(_options);
        foreach (var option in Harvesting.Options(sim.Data, building.DefId)) _options.AddChild(Card(sim, building, option, option.Id == harvest.Id));
    }

    private static string Current(ColonySim sim, Building building, HarvestDef harvest)
    {
        var lines = new System.Collections.Generic.List<string> { $"[b]{harvest.Name}[/b]  ·  about {sim.OutputPerDay(building):0.#} food/day with these workers" };
        var wait = Harvesting.TicksUntilReady(sim.State, building);
        var standing = string.Join(", ", building.HarvestProgress.Where(kv => kv.Value >= 0.05)
            .Select(kv => $"{kv.Value:0.#} {FoodText.ItemName(sim, kv.Key)}"));
        lines.Add(wait > 0
            ? $"[color=#{Ui.Hex(Palette.Warn)}]Growing: first harvest in {ColonyText.Duration((int)wait, sim.Data.Rules)}[/color]" + (standing.Length > 0 ? $" ({standing} standing)" : "")
            : $"[color=#{Ui.Hex(Palette.Good)}]Coming in[/color] as it grows" + (standing.Length > 0 ? $": {standing} on the way" : ""));
        var weather = Harvesting.Modifiers(sim.State, sim.Data, building, harvest);
        if (weather.Count > 0)
            lines.Add("Weather: " + string.Join(", ", weather.Select(m => $"{m.Reason} ×{m.Multiplier:0.##}")));
        else if (!harvest.Weather || !sim.Data.Buildings[building.DefId].Outdoor)
            lines.Add($"[color=#{Ui.Hex(Palette.TextMuted)}]Not affected by the weather.[/color]");
        return string.Join("\n", lines);
    }

    private Control Card(ColonySim sim, Building building, HarvestDef h, bool current)
    {
        var locked = h.MinLevel > building.Level;
        var card = new PanelContainer
        {
            ThemeTypeVariation = current ? "CardSelected" : "Card",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = $"{h.Name}: {h.Description}" + (current ? "" : "\nClick to switch. The standing crop is lost."),
        };
        if (locked) card.Modulate = new Color(1, 1, 1, 0.6f);
        var column = Widgets.Column(3);
        column.MouseFilter = MouseFilterEnum.Ignore;
        card.AddChild(column);
        var head = Widgets.Row(4);
        foreach (var item in h.Outputs.Keys.Take(3)) head.AddChild(Icons.Rect(FoodText.Icon(sim, item, 16), 20, FoodText.Tint(sim, item, current ? Palette.Amber : Palette.TextMuted)));
        var name = Widgets.Label(h.Name, "Heading", size: 14);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        if (current) head.AddChild(Icons.Rect(Icons.Get("stat_check", 14), 14, Palette.Good));
        column.AddChild(head);
        var food = sim.Data.Food!;
        var keeps = h.Outputs.Keys.Select(i => food.Items[i].ShelfDays).Where(d => d > 0).DefaultIfEmpty(0).Min();
        var value = h.Yield * h.Outputs.Sum(kv => kv.Value * food.Items[kv.Key].FoodValue);
        var facts = $"yield ×{h.Yield:0.##} · {value:0.##} food per effort" + (h.GrowDays > 0 ? $" · grows {h.GrowDays:0.#}d" : " · no wait")
                    + (keeps > 0 ? $" · keeps {keeps:0}d" : "");
        if (h.Byproducts.Count > 0) facts += " · +" + string.Join(", ", h.Byproducts.Keys.Select(r => sim.Data.Resources[r].Name));
        column.AddChild(Widgets.Wrapped(facts, "Small", Palette.TextMuted));
        if (h.Outputs.Keys.Any(i => food.Items[i].Tainted)) column.AddChild(Widgets.Wrapped("Can make people sick unless cooked well.", "Small", Palette.Warn));
        if (locked) column.AddChild(Widgets.Wrapped($"Needs level {h.MinLevel}", "Small", Palette.Danger));
        var id = h.Id;
        card.GuiInput += e =>
        {
            if (current || locked || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            void Switch() => _session.Execute(new SetHarvest(building.Id, id));
            if (building.HarvestProgress.Values.Sum() >= 1)
                ConfirmDialog.Ask(GetTree().Root, $"Switch to {h.Name}?",
                    $"The standing crop ({building.HarvestProgress.Values.Sum():0} on the way) is lost" + (h.GrowDays > 0 ? $", and the first {h.Name} harvest takes {h.GrowDays:0.#} days." : "."),
                    "Switch", Switch);
            else Switch();
        };
        return card;
    }
}
