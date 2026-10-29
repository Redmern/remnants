using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony.Ui;

/// <summary>After-action report of an expedition that came home.</summary>
public partial class AfterActionDialog : AcceptDialog
{
    private readonly RichTextLabel _text = Ui.Rich(scroll: true);

    public AfterActionDialog()
    {
        Title = "After-action report";
        MinSize = new Vector2I(460, 360);
        _text.CustomMinimumSize = new Vector2(440, 300);
        AddChild(_text);
    }

    public void ShowReport(ColonySim sim, ExpeditionReport report)
    {
        var data = sim.Scavenge;
        string Item(string id) => data?.Items.GetValueOrDefault(id)?.Name ?? id;
        string Items(IDictionary<string, int> items) =>
            items.Count == 0 ? "nothing" : string.Join(", ", items.Select(kv => $"{Item(kv.Key)} ×{kv.Value}"));

        var zone = data?.Zones.GetValueOrDefault(report.ZoneId)?.Name ?? report.ZoneId;
        var lines = new List<string> { $"[b]{zone}[/b]" };
        lines.Add($"[color=#{Ui.Hex(Ui.Good)}]Came home[/color]");
        if (report.Returned.Count == 0) lines.Add("  nobody");
        foreach (var r in report.Returned)
        {
            var injuries = r.NewInjuries.Count == 0 ? "unhurt" : string.Join(", ", r.NewInjuries);
            lines.Add($"  {r.Name}{(r.Evacuated ? " (evacuated)" : "")}: HP {r.Health:0}, {injuries}");
        }
        if (report.Dead.Count > 0)
        {
            lines.Add($"[color=#{Ui.Hex(Ui.Bad)}]Lost[/color]");
            lines.AddRange(report.Dead.Select(n => $"  {n}"));
        }
        if (report.Recruits.Count > 0) lines.Add($"Recruited: {string.Join(", ", report.Recruits)} (they need beds)");
        lines.Add($"Loot: {Items(report.Loot)}");
        if (report.LostGear.Count > 0) lines.Add($"Gear lost: {Items(report.LostGear)}");
        _text.Text = string.Join("\n", lines);
        PopupCentered();
    }
}
