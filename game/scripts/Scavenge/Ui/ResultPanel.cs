using System;
using System.Linq;
using System.Text;
using Godot;
using Remnants.Core.Scavenge;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>End-of-run summary from Core's <see cref="ExtractionResult"/>: who came home and how, recruits, the dead, loot.</summary>
public partial class ResultPanel : CenterContainer
{
    private readonly Label _title = UiKit.Label(size: 26);
    private readonly RichTextLabel _body = UiKit.Rich(scroll: true);

    public event Action? ContinuePressed;

    public ResultPanel()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 440) };
        var box = UiKit.Panel(panel, 10);
        AddChild(panel);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_title);
        box.AddChild(_body);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddChild(UiKit.Button("Continue", () => ContinuePressed?.Invoke(), 160));
        box.AddChild(row);
    }

    public void Present(ScavengeData data, ExtractionResult result)
    {
        var wiped = result.Returned.Count == 0;
        _title.Text = wiped ? "No one came back" : result.Deaths.Count > 0 ? "Extraction — with losses" : "Extraction complete";
        _title.AddThemeColorOverride("font_color", wiped ? UiKit.Bad : UiKit.Good);

        var b = new StringBuilder();
        var bad = UiKit.Hex(UiKit.Bad);
        if (!result.Complete) b.Append($"[color=#{bad}]Left early: the run is not finished.[/color]\n\n");

        b.Append("[color=#aaa]RETURNED[/color]\n");
        if (result.Returned.Count == 0) b.Append("nobody\n");
        foreach (var r in result.Returned)
        {
            var hurt = r.Survivor.Body.Parts.Where(kv => kv.Value.Condition is not (Remnants.Core.Survivors.PartCondition.Healthy
                or Remnants.Core.Survivors.PartCondition.Prosthetic)).Select(kv => ScavengeText.Part(kv.Key).ToLowerInvariant()).ToList();
            b.Append($"{r.Survivor.Name}  ·  {r.Survivor.Health:0} HP{(r.Evacuated ? "  ·  evacuated" : "")}");
            if (hurt.Count > 0) b.Append($"  ·  [color=#{bad}]hurt: {string.Join(", ", hurt)}[/color]");
            b.Append('\n');
        }

        if (result.Recruits.Count > 0)
        {
            b.Append("\n[color=#aaa]RECRUITS[/color]\n");
            foreach (var r in result.Recruits) b.Append($"[color=#{UiKit.Hex(UiKit.Good)}]{r.Survivor.Name}[/color] joins the colony\n");
        }

        if (result.Deaths.Count > 0)
        {
            b.Append("\n[color=#aaa]LOST[/color]\n");
            foreach (var d in result.Deaths) b.Append($"[color=#{bad}]{d.Name}[/color]\n");
        }

        b.Append("\n[color=#aaa]LOOT BROUGHT HOME[/color]\n");
        b.Append(result.Loot.Count == 0 ? "nothing" : string.Join(", ", result.Loot.Select(kv => $"{ScavengeText.Item(data, kv.Key)} x{kv.Value}")));
        _body.Text = b.ToString();
        Visible = true;
    }
}
