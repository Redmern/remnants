using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Med Bay's dispensary tab: the medical supplies in stock as a plain list (by item, best quality noted; the tooltip
/// says what each does) and the auto-treatment order — which treatments the bay gives on its own, first first; switch
/// one off or move it up. Its recipes and queue are the generic production panel next to this one.
/// </summary>
public partial class MedBayPanel
{
    private readonly VBoxContainer _stock = Widgets.Column(2);
    private readonly KeyedRows<string> _stockRows;
    private readonly Label _noStock = Widgets.Label("No medical supplies in stock.", "Muted");
    private readonly VBoxContainer _priorities = Widgets.Column(2);
    private readonly KeyedRows<TreatPriority> _priorityRows;

    /// <summary>The dispensary's auto-treat order (opens the Dispensary tab, e.g. to scroll it into view).</summary>
    public Control Dispensary
    {
        get
        {
            _tabs.Select("dispensary");
            return _priorities;
        }
    }

    private string _dispensarySignature = "";

    private VBoxContainer BuildDispensary()
    {
        var box = Widgets.Column(8);
        box.AddChild(PanelKit.Header("In stock", "res_medicine", "injuries"));
        _stock.AddChild(_noStock);
        box.AddChild(_stock);
        var reset = PanelKit.Focusable(Widgets.IconButton("ui_continue", "Reset", "Back to the default order, everything on",
            () => _session.Execute(new SetCarePriorities(null)), variation: "Flat"));
        reset.CustomMinimumSize = new Vector2(0, 26);
        box.AddChild(PanelKit.Header("Auto-treat order", "job_patient", "med_bay", reset));
        box.AddChild(Widgets.Wrapped("The bay works down this list on its own, top first. Untick a treatment to save its supplies.", "Small", Palette.TextMuted));
        box.AddChild(_priorities);
        return box;
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
        var order = Medicine.Priorities(sim.State, sim.Data).ToList();
        var all = med.Def.MedBay.DefaultPriorities;
        var signature = string.Join(",", supplies.Select(s => $"{s.Id}:{s.Count}:{s.Best.Id}")) + "|" + string.Join(",", order);
        if (signature == _dispensarySignature) return;
        _dispensarySignature = signature;

        _set.Visible(_noStock, supplies.Count == 0);
        _stockRows.Sync(supplies.Select(s => s.Id).ToList(), id =>
        {
            var row = Widgets.Row(6);
            row.MouseFilter = MouseFilterEnum.Stop;
            row.AddChild(Icons.Rect(Icons.Item(id, items.Items[id].Kind.ToString(), 16), 16, Palette.TextMuted));
            var name = Widgets.Label(items.Items[id].Name);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            row.AddChild(name);
            row.AddChild(Widgets.Label("", "Small", Palette.TextMuted));
            row.AddChild(Widgets.Label("", "Number"));
            return row;
        }, (id, row) =>
        {
            var (_, count, best) = supplies.Find(s => s.Id == id);
            var quality = best.Quality is not null && items.QualityOf(best) is { } tier ? tier.Name : "";
            _set.Text(row.GetChild<Label>(2), quality);
            _set.Text(row.GetChild<Label>(3), $"×{count}");
            _set.Tooltip(row, MedicalText.ItemTip(items, best) + (quality == "" ? "" : $"\nBest in stock: {quality}"));
        });

        // Enabled ones in their order, then the switched-off ones.
        var shown = order.Concat(all.Where(p => !order.Contains(p))).ToList();
        _priorityRows.Sync(shown, PriorityRow, (p, row) =>
        {
            var on = order.Contains(p);
            var index = order.IndexOf(p);
            var toggle = row.GetChild<Button>(0);
            var text = $"{(on ? $"{index + 1}." : "–")} {MedicalText.Priority(p)}";
            if (toggle.Text != text) toggle.Text = text;
            toggle.Icon = Icons.Get(on ? "ui_checkbox_on" : "ui_checkbox_off", 18);
            _set.Modulate(toggle, on ? Colors.White : Palette.TextDim);
            row.GetChild<Button>(1).Disabled = !on || index <= 0;
            row.GetChild<Button>(2).Disabled = !on || index >= order.Count - 1;
        });
    }

    private HBoxContainer PriorityRow(TreatPriority p)
    {
        var row = Widgets.Row(4);
        List<TreatPriority> Order() => Medicine.Priorities(_session.Sim.State, _session.Sim.Data).ToList();
        var toggle = PanelKit.Focusable(Motion.OnPress(Widgets.IconButton("ui_checkbox_off", MedicalText.Priority(p), MedicalText.PriorityTip(p), () =>
        {
            var order = Order();
            _session.Execute(new SetCarePriorities(order.Contains(p) ? order.Where(x => x != p).ToList() : [.. order, p]));
        }, variation: "Flat")));
        toggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        toggle.Alignment = HorizontalAlignment.Left;
        toggle.CustomMinimumSize = new Vector2(0, 26);
        row.AddChild(toggle);
        void Move(int by)
        {
            var list = Order();
            var i = list.IndexOf(p);
            if (i < 0 || i + by < 0 || i + by >= list.Count) return;
            (list[i + by], list[i]) = (list[i], list[i + by]);
            _session.Execute(new SetCarePriorities(list));
        }
        var up = PanelKit.Focusable(Widgets.IconButton("ui_chevron_up", "", "Treat this sooner", () => Move(-1), variation: "Flat"));
        up.CustomMinimumSize = new Vector2(26, 26);
        row.AddChild(up);
        var down = PanelKit.Focusable(Widgets.IconButton("ui_chevron_down", "", "Treat this later", () => Move(1), variation: "Flat"));
        down.CustomMinimumSize = new Vector2(26, 26);
        row.AddChild(down);
        return row;
    }

    /// <summary>"· infected, fever" for a patient's line (nothing when they carry no condition).</summary>
    private static string ConditionTags(ColonySim sim, Remnants.Core.Survivors.Survivor s) =>
        MedicalText.Tags(sim.Data.Medicine, s.Body) is { Count: > 0 } tags ? $" · {string.Join(", ", tags)}" : "";
}
