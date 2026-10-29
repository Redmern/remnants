using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Items;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The colony's items by kind (with their quality), and blueprints: those studied, and those in stock with a Study
/// button (Core checks the study building). Toggled from the top bar.
/// </summary>
public partial class InventoryPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly RichTextLabel _items = Ui.Rich();
    private readonly RichTextLabel _known = Ui.Rich();
    private readonly VBoxContainer _study = new();
    private string _studyShown = "";

    public InventoryPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(300, 0);
        SizeFlagsVertical = SizeFlags.ShrinkBegin;
        Visible = false;
        var box = Ui.Panel(this);
        box.AddChild(Ui.Heading("Items"));
        box.AddChild(_items);
        box.AddChild(Ui.Heading("Blueprints"));
        box.AddChild(_known);
        box.AddChild(_study);
    }

    public void Toggle()
    {
        Visible = !Visible;
        Refresh();
    }

    public void Refresh()
    {
        if (!Visible || _session.Sim.Scavenge is not { } data) return;
        var state = _session.Sim.State;

        var groups = state.Inventory.Counts
            .Select(kv => (Def: data.Items[kv.Key], Count: kv.Value))
            .Where(e => e.Def.Kind != ItemKind.Blueprint)
            .GroupBy(e => e.Def.Kind)
            .OrderBy(g => g.Key);
        var text = new List<string>();
        foreach (var group in groups)
        {
            text.Add($"[b]{group.Key}[/b]");
            foreach (var (def, count) in group.OrderBy(e => e.Def.DefinedId).ThenBy(e => e.Def.Id))
            {
                var color = def.Quality switch { "fine" => Ui.Good, "crude" => Ui.Muted, _ => HudTheme.Text };
                text.Add($"  [color=#{Ui.Hex(color)}]{def.Name}[/color] ×{count}");
            }
        }
        _items.Text = text.Count == 0 ? "[color=#999]No items.[/color]" : string.Join("\n", text);

        var unlocks = _session.Sim.Data.Recipes.Values.Where(r => r.Blueprint is not null).ToLookup(r => r.Blueprint!);
        string Unlocks(string bp) => string.Join(", ", unlocks[bp].Select(r => r.Name));
        _known.Text = state.KnownBlueprints.Count == 0
            ? "[color=#999]None studied. Find them in caches in riskier zones.[/color]"
            : string.Join("\n", state.KnownBlueprints.Select(bp => $"[color=#{Ui.Hex(Ui.Good)}]✓[/color] {data.Items[bp].Name} → {Unlocks(bp)}"));

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
            row.AddChild(Ui.Button("Study", () => _session.Execute(new StudyBlueprint(id)), 60));
            _study.AddChild(row);
        }
    }
}
