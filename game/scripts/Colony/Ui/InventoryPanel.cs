using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The colony's items by kind (with their quality), and blueprints: those studied, and those in stock with a Study
/// button (Core checks the study building). Toggled from the top bar.
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly VBoxContainer _items = Widgets.Column(4);
    private string _itemsShown = "";
    private readonly RichTextLabel _known = Ui.Rich();
    private readonly VBoxContainer _study = new();
    private string _studyShown = "";

    public InventoryPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(340, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.TitleRow("Items", "ui_items", Toggle, Hotkeys.KeyName("open_items")));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 320) };
        _items.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_items);
        box.AddChild(scroll);
        box.AddChild(Ui.Heading("Blueprints", "item_blueprint"));
        box.AddChild(_known);
        box.AddChild(_study);
    }

    public void Toggle()
    {
        Motion.Toggle(this);
        Refresh();
    }

    public void Refresh()
    {
        if (!Visible || _session.Sim.Scavenge is not { } data) return;
        var state = _session.Sim.State;

        RefreshItems(data, state.Inventory);

        var unlocks = _session.Sim.Data.Recipes.Values.Where(r => r.Blueprint is not null).ToLookup(r => r.Blueprint!);
        string Unlocks(string bp) => string.Join(", ", unlocks[bp].Select(r => r.Name));
        _known.Text = state.KnownBlueprints.Count == 0
            ? "[color=#999]None studied. Find them in caches in riskier zones.[/color]"
            : string.Join("\n", state.KnownBlueprints.Select(bp => $"{Palette.Paint("✓", Palette.Good)} {data.Items[bp].Name} → {Unlocks(bp)}"));

        var unstudied = state.Inventory.Counts.Keys.Where(id => data.Items[id].Kind == ItemKind.Blueprint).ToList();
        var signature = string.Join(",", unstudied.Select(id => $"{id}:{state.Inventory.Count(id)}"));
        if (signature == _studyShown) return;
        _studyShown = signature;
        foreach (var child in _study.GetChildren()) child.QueueFree();
        foreach (var id in unstudied)
        {
            var bp = data.Items[id];
            var row = new HBoxContainer();
            var at = _session.Sim.Data.Buildings[bp.Blueprint!.StudyAt].Name;
            var label = Ui.Label($"{bp.Name} ×{state.Inventory.Count(id)}", 13);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.TooltipText = $"Studied at {at} L{bp.Blueprint.MinLevel}; unlocks {Unlocks(id)}.";
            label.MouseFilter = MouseFilterEnum.Pass;
            row.AddChild(label);
            row.AddChild(Widgets.IconButton("attr_intellect", "Study", $"Study at the {at} (level {bp.Blueprint.MinLevel}+)", () => _session.Execute(new StudyBlueprint(id))));
            _study.AddChild(row);
        }
    }

    /// <summary>Items grouped by kind: icon, name, quality (stars and colour) and count.</summary>
    private void RefreshItems(Remnants.Core.Scavenge.ScavengeData data, ItemInventory inventory)
    {
        var signature = string.Join(",", inventory.Counts.Select(kv => $"{kv.Key}:{kv.Value}"));
        if (signature == _itemsShown) return;
        _itemsShown = signature;
        Widgets.Clear(_items);
        var groups = inventory.Counts
            .Select(kv => (Def: data.Items[kv.Key], Count: kv.Value))
            .Where(e => e.Def.Kind != ItemKind.Blueprint)
            .GroupBy(e => e.Def.Kind)
            .OrderBy(g => g.Key)
            .ToList();
        if (groups.Count == 0) _items.AddChild(Widgets.Label("No items yet. Craft them or bring them back from expeditions.", "Muted"));
        foreach (var group in groups)
        {
            _items.AddChild(Widgets.SectionHeader(group.Key.ToString(), $"item_{group.Key.ToString().ToLowerInvariant()}"));
            foreach (var (def, count) in group.OrderBy(e => e.Def.DefinedId).ThenBy(e => e.Def.Id))
            {
                var row = Widgets.Row(6);
                row.AddChild(Icons.Rect(Icons.Item(def.DefinedId, def.Kind.ToString(), 18), 18, Palette.TextMuted));
                var (stars, color, tier) = def.Quality switch
                {
                    "fine" => (3, Palette.Good, "Fine"),
                    "crude" => (1, Palette.TextMuted, "Crude"),
                    _ => (def.BaseId is null ? 0 : 2, Palette.Text, "Standard"),
                };
                var name = Widgets.Label(def.Name, "", color);
                name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                name.ClipText = true;
                row.AddChild(name);
                if (stars > 0)
                {
                    var quality = new Pips(stars, 3, color) { TooltipText = $"Quality: {tier}" };
                    row.AddChild(quality);
                }
                row.AddChild(Widgets.Label($"×{count}", "Number"));
                row.TooltipText = $"{def.Name} · {def.Kind}{(stars > 0 ? $" · {tier} quality" : "")}";
                _items.AddChild(row);
            }
        }
    }
}
