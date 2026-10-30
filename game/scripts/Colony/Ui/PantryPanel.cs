using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The pantry: food days and what eats into them, then every food in stock by group (dishes, preserved, raw) with its
/// count, quality, when the next unit spoils, and spoiled or tainted units. Toggled from the top bar.
/// </summary>
public partial class PantryPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly RichTextLabel _summary = Ui.Rich();
    private readonly VBoxContainer _rows = Widgets.Column(4);
    private string _shown = "";

    public PantryPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(380, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.TitleRow("Pantry", "res_food", Toggle));
        box.AddChild(_summary);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 620) };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_rows);
        box.AddChild(scroll);
    }

    public void Toggle()
    {
        Motion.Toggle(this);
        _shown = "";
        Refresh();
    }

    public void Refresh()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        if (sim.Data.Food is not { } food) return;
        var entries = Pantry.Entries(sim);
        var day = sim.Data.Rules.TicksPerDay;
        var soon = entries.Where(e => e.SpoilsInTicks is { } t && t < day).Sum(e => e.Fresh);
        var cellar = Pantry.CellarRate(sim.State, sim.Data);
        var days = double.IsInfinity(sim.FoodDays) ? "∞" : $"{sim.FoodDays:0.#}";
        _summary.Text = $"[b]{sim.FoodStock:0} food[/b] · {days} days at {sim.FoodPerDay:0.#}/day"
                        + (soon > 0 ? $" · [color=#{Ui.Hex(Palette.Danger)}]{soon} spoiling within a day[/color]" : "")
                        + (cellar < 1 ? $"\nCold Cellar: produce and meat age at {cellar * 100:0}%" : $"\n[color=#{Ui.Hex(Palette.TextMuted)}]No Cold Cellar: everything ages at full speed.[/color]");

        var signature = string.Join(";", entries.Select(e => $"{e.ItemId}:{e.Count}:{e.Spoiled}:{e.Tainted}:{(int)((e.SpoilsInTicks ?? -1) / 2)}"));
        if (signature == _shown) return;
        _shown = signature;
        Widgets.Clear(_rows);
        if (entries.Count == 0) _rows.AddChild(Widgets.Label("The pantry is empty.", "Muted"));
        var groups = entries.GroupBy(e => food.Items[e.ItemId] switch
        {
            { Meal: not null } d when d.Tags.Contains("preserved") => "Preserved",
            { Meal: not null } => "Dishes",
            { FoodValue: <= 0 } => "Cooking supplies",
            _ => "Raw food",
        }).OrderBy(g => g.Key switch { "Dishes" => 0, "Preserved" => 1, "Raw food" => 2, _ => 3 });
        foreach (var group in groups)
        {
            _rows.AddChild(Ui.Heading(group.Key, group.Key == "Dishes" ? "skill_cooking" : group.Key == "Raw food" ? "skill_farming" : "res_food"));
            foreach (var e in group) _rows.AddChild(Row(sim, e));
        }
    }

    private static Control Row(ColonySim sim, PantryEntry e)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var row = Widgets.Row(6);
        card.AddChild(row);
        row.AddChild(Icons.Rect(FoodText.Icon(sim, e.ItemId, 18), 28, FoodText.Tint(sim, e.ItemId, Palette.Amber)));
        var column = Widgets.Column(1);
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var name = Widgets.Label($"{FoodText.ItemName(sim, e.ItemId)}  ×{e.Count}", size: 14);
        column.AddChild(name);
        var def = sim.Data.Food!.Items[e.ItemId];
        name.TooltipText = FoodText.Effects(sim, def);
        name.MouseFilter = MouseFilterEnum.Pass;
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 3);
        if (def.FoodValue > 0) chips.AddChild(Widgets.Chip(null, $"{e.FoodValue:0.#} food", Palette.TextMuted));
        if (def.Meal is not null)
            foreach (var (tier, n) in e.ByQuality.OrderBy(kv => kv.Key))
                chips.AddChild(Widgets.Chip(Icons.Get("stat_quality", 12), $"{FoodText.QualityName(sim, tier)} {n}", FoodText.QualityColor(tier)));
        if (e.Spoiled > 0) chips.AddChild(Widgets.Chip(Icons.Get("stat_warning", 12), $"{e.Spoiled} spoiled", Palette.Danger,
            "Spoiled: only eaten when there is nothing else (it can make people sick), then rots away."));
        if (e.Tainted > 0) chips.AddChild(Widgets.Chip(Icons.Get("buff_sick", 12), $"{e.Tainted} tainted", Palette.Warn,
            "Husk-zone food, or a dish made from it that did not come out well: can make people sick."));
        column.AddChild(chips);
        row.AddChild(column);
        var timer = Widgets.Label(e.Fresh == 0 ? "" : FoodText.SpoilsIn(sim, e.SpoilsInTicks), "Number", FoodText.SpoilColor(sim, e.SpoilsInTicks), 12);
        timer.TooltipText = e.SpoilsInTicks is null ? "Never spoils." : "When the next unit spoils.";
        timer.MouseFilter = MouseFilterEnum.Pass;
        row.AddChild(timer);
        return card;
    }
}
