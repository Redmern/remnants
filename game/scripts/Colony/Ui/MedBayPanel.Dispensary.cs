using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Med Bay's dispensary: the medical supplies in stock (by item, best quality noted) and the auto-treatment order —
/// which treatments the bay gives on its own, first first; switch one off or move it up. Its recipes and queue are the
/// generic production panel next to this one (the Med Bay's dispensary recipes, by level).
/// </summary>
public partial class MedBayPanel
{
    private readonly HFlowContainer _stock = new();
    private readonly VBoxContainer _priorities = Widgets.Column(2);

    /// <summary>The bottom of the dispensary section, its auto-treat order (to scroll it into view).</summary>
    public Control Dispensary => _priorities;
    private string _dispensarySignature = "";

    private void BuildDispensary(VBoxContainer box)
    {
        box.AddChild(Ui.Heading("Dispensary", "res_medicine"));
        _stock.AddThemeConstantOverride("h_separation", 4);
        _stock.AddThemeConstantOverride("v_separation", 4);
        box.AddChild(_stock);
        var reset = Widgets.IconButton("ui_continue", "", "Back to the default order, everything on",
            () => _session.Execute(new SetCarePriorities(null)), variation: "Flat");
        reset.CustomMinimumSize = new Vector2(0, 26);
        box.AddChild(Widgets.SectionHeader("Auto-treat", "job_patient", reset));
        box.AddChild(_priorities);
    }

    private void RefreshDispensary(ColonySim sim)
    {
        if (sim.Data.Medicine is not { } med || sim.Scavenge is not { } items) return;
        var supplies = sim.State.Inventory.Counts
            .Where(kv => items.Items.TryGetValue(kv.Key, out var d) && d.Medical && kv.Value > 0)
            .GroupBy(kv => items.Items[kv.Key].DefinedId)
            .OrderBy(g => g.Key, System.StringComparer.Ordinal)
            .Select(g => (Id: g.Key, Count: g.Sum(kv => kv.Value),
                Best: g.Select(kv => items.Items[kv.Key]).MaxBy(d => items.QualityOf(d)?.StatMultiplier ?? 1)!))
            .ToList();
        var order = Medicine.Priorities(sim.State, sim.Data);
        var all = med.Def.MedBay.DefaultPriorities;
        var signature = string.Join(",", supplies.Select(s => $"{s.Id}:{s.Count}:{s.Best.Id}")) + "|" + string.Join(",", order);
        if (signature == _dispensarySignature) return;
        _dispensarySignature = signature;

        Widgets.Clear(_stock);
        if (supplies.Count == 0) _stock.AddChild(Ui.Label("No medical supplies in stock.", color: Ui.Muted));
        foreach (var (id, count, best) in supplies)
        {
            var quality = best.Quality is not null && items.QualityOf(best) is { } tier ? $" ({tier.Name})" : "";
            _stock.AddChild(Widgets.Chip(Icons.Item(id, best.Kind.ToString()), $"{items.Items[id].Name} ×{count}", tooltip: MedicalText.ItemTip(items, best) + (quality == "" ? "" : $"\nBest in stock{quality}")));
        }

        Widgets.Clear(_priorities);
        // Enabled ones in their order, then the switched-off ones.
        foreach (var p in order.Concat(all.Where(p => !order.Contains(p))))
        {
            var on = order.Contains(p);
            var row = Widgets.Row(4);
            var rank = on ? $"{order.ToList().IndexOf(p) + 1}." : "–";
            var toggle = Widgets.IconButton(on ? "ui_checkbox_on" : "ui_checkbox_off", $"{rank} {MedicalText.Priority(p)}", MedicalText.PriorityTip(p),
                () => _session.Execute(new SetCarePriorities(on ? order.Where(x => x != p).ToList() : [.. order, p])), variation: "Flat");
            toggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            toggle.Alignment = HorizontalAlignment.Left;
            toggle.CustomMinimumSize = new Vector2(0, 26);
            if (!on) toggle.Modulate = Palette.TextDim;
            row.AddChild(toggle);
            var index = order.ToList().IndexOf(p);
            var up = Widgets.IconButton("ui_chevron_up", "", "Treat this sooner", () =>
            {
                var list = order.ToList();
                (list[index - 1], list[index]) = (list[index], list[index - 1]);
                _session.Execute(new SetCarePriorities(list));
            }, variation: "Flat");
            up.CustomMinimumSize = new Vector2(26, 26);
            up.Disabled = !on || index <= 0;
            row.AddChild(up);
            _priorities.AddChild(row);
        }
    }

    /// <summary>"· infected, fever" for a patient's line (nothing when they carry no condition).</summary>
    private static string ConditionTags(ColonySim sim, Remnants.Core.Survivors.Survivor s) =>
        MedicalText.Tags(sim.Data.Medicine, s.Body) is { Count: > 0 } tags ? $" · {string.Join(", ", tags)}" : "";
}
