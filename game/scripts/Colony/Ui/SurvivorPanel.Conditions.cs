using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The Body tab's conditions (data/medicine.json): each infection, fracture, sickness, dependency or painkiller with its
/// severity, what helps, and a button per stocked item that would treat it now (a <see cref="TreatSurvivor"/> override of
/// the Med Bay's own choice). Pain from severe injuries shows as its own line.
/// </summary>
public partial class SurvivorPanel
{
    private readonly VBoxContainer _conditions = Widgets.Column(Space.Sm);
    private Control _conditionsHeader = null!;
    private string _conditionSignature = "";

    private void BuildConditions(VBoxContainer box)
    {
        _conditionsHeader = Widgets.SectionHeader("Conditions", "cond_infection", new Hud.Help.InfoTip("medicine", Context));
        box.AddChild(_conditionsHeader);
        box.AddChild(_conditions);
    }

    private void RefreshConditions(ColonySim sim, Survivor s)
    {
        if (sim.Data.Medicine is not { } med || sim.Scavenge is not { } items)
        {
            _conditionsHeader.Visible = _conditions.Visible = false;
            return;
        }
        var treatments = sim.State.Inventory.Counts.Keys
            .Where(id => items.Items.TryGetValue(id, out var d) && d.Use is { } use && use.Treats && use.StimAp == 0 && use.ReviveHp == 0
                         && Treatment.Helps(med, s.Body, use))
            .OrderBy(id => id, System.StringComparer.Ordinal).ToList();
        var pain = Treatment.InPain(med, s.Body);
        var signature = $"{s.Id}|{pain}|" + string.Join(",", s.Body.Conditions.Select(c => $"{c.Id}:{c.Part}:{c.Severity:0.00}:{c.Treated}:{c.Remaining}")) +
                        "|" + string.Join(",", treatments.Select(t => $"{t}:{sim.State.Inventory.Count(t)}"));
        if (signature == _conditionSignature) return;
        _conditionSignature = signature;
        Widgets.Clear(_conditions);

        var any = false;
        foreach (var c in s.Body.Conditions)
        {
            any = true;
            var def = med.Condition(c.Id);
            AddConditionRow(MedicalText.Icon(def), MedicalText.Line(med, c), Medicine.Suggestion(med, c), MedicalText.Tint(def, c), def?.Description ?? "",
                treatments.Where(t => items.Items[t].Use is { } u && (u.Cures.Contains(c.Id) || c.Id == med.Fracture.Condition && u.Splint)).ToList(), s, sim);
        }
        if (pain)
        {
            any = true;
            AddConditionRow("stat_injured", "In pain and worn out",
                $"A severe injury: −{med.Pain.OutputPenalty * 100:0}% output, −{med.Pain.FieldApPenalty} AP in the field. Painkillers dull it.",
                Palette.Warn, "", treatments.Where(t => items.Items[t].Use!.PainRelief > 0).ToList(), s, sim);
        }
        var undressed = Treatment.Undressed(s.Body, Dressing.Clean).Where(p => s.Body[p].Dressing == Dressing.None).ToList();
        if (undressed.Count > 0)
        {
            any = true;
            AddConditionRow("stat_warning", $"Open wound{(undressed.Count > 1 ? "s" : "")}: {string.Join(", ", undressed.Select(p => CyberText.Part(p).ToLowerInvariant()))}",
                "Dirty wounds can get infected. A bandage helps; antiseptic or a clean dressing stops it.", Palette.Warn, "",
                treatments.Where(t => items.Items[t].Use!.Dress != Dressing.None).ToList(), s, sim);
        }
        if (!any) _conditions.AddChild(Widgets.Label("No conditions.", "Muted"));
    }

    private void AddConditionRow(string icon, string line, string suggestion, Color tint, string tooltip, List<string> items, Survivor s, ColonySim sim)
    {
        var row = Widgets.Row(Space.Sm);
        var mark = Icons.Rect(Icons.Get(icon, 20), 20, tint);
        mark.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(mark);
        var col = Widgets.Column(2);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var text = Widgets.Wrapped(line, "", tint);
        text.TooltipText = tooltip;
        text.MouseFilter = MouseFilterEnum.Stop;
        col.AddChild(text);
        var hint = Widgets.Label(suggestion, "Small", Palette.TextMuted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(hint);
        row.AddChild(col);
        // The treatments under the text, so a long item name never squeezes the description.
        var actions = new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation", Space.Xs);
        actions.AddThemeConstantOverride("v_separation", Space.Xs);
        col.AddChild(actions);
        foreach (var id in items.Take(2))
        {
            var item = sim.Scavenge!.Items[id];
            var survivorId = s.Id;
            // A real button that says what it does ("Give Crude Antibiotics"), not a link-like flat one.
            var button = Widgets.IconButton("ui_use", $"Give {item.Name}", MedicalText.ItemTip(sim.Scavenge, item) + $"\nIn stock: {sim.State.Inventory.Count(id)}",
                () => _session.Execute(new TreatSurvivor(survivorId, id)));
            button.FocusMode = FocusModeEnum.All;
            button.CustomMinimumSize = new Vector2(0, 28);
            button.Disabled = s.Away;
            actions.AddChild(button);
        }
        _conditions.AddChild(row);
    }
}
