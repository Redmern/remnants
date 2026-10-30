using System;
using Godot;
using Remnants.Core.Scavenge;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// Defenses against the fog for the acting unit: Overwatch (react to the first hostile that shows itself), Brace (no
/// ambush bonus against it, extra cover) and Lookout (more sight and detection this round). Costs come from Core's
/// rules; Core decides whether it works. Kept separate from <see cref="ActionPanel"/> so it can be restyled alone.
/// </summary>
public partial class StancePanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly VBoxContainer _content = new();

    public event Action<IScavengeCommand>? CommandRequested;

    public StancePanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(260, 0);
        var box = UiKit.Panel(this);
        box.AddChild(UiKit.Heading("Stance"));
        _content.AddThemeConstantOverride("separation", 4);
        box.AddChild(_content);
    }

    public void Refresh(bool busy)
    {
        foreach (var child in _content.GetChildren()) child.QueueFree();
        var unit = _presenter.ActingUnit;
        Visible = unit is not null && !busy;
        if (unit is null || busy) return;

        var fog = _presenter.Sim.Data.Rules.Fog;
        var pid = _presenter.PlayerId;
        Add($"Overwatch  ·  {fog.Overwatch.ApCost} AP, ends turn",
            "Fire at the first hostile that moves or reveals itself in sight and reach (once).",
            fog.Overwatch.ApCost, new SetStance(pid, unit.Id, Stance.Overwatch), unit);
        Add($"Brace  ·  {fog.Brace.ApCost} AP, ends turn",
            $"No ambush bonus against {unit.Name}, +{fog.Brace.CoverBonus * 100:0}% cover; {fog.Brace.NextTurnApPenalty} AP less next turn.",
            fog.Brace.ApCost, new SetStance(pid, unit.Id, Stance.Brace), unit);
        var lookout = Add($"Lookout  ·  {fog.Lookout.ApCost} AP",
            $"+{fog.Lookout.SightBonus:0} sight and sharper eyes until {unit.Name}'s next turn.",
            fog.Lookout.ApCost, new Lookout(pid, unit.Id), unit);
        if (unit.Lookout)
        {
            lookout.Disabled = true;
            lookout.Text = "On lookout";
        }
    }

    private Button Add(string text, string tooltip, int cost, IScavengeCommand command, Unit unit)
    {
        var button = UiKit.Button(text, () => CommandRequested?.Invoke(command));
        button.TooltipText = tooltip;
        button.Disabled = cost > unit.Ap;
        _content.AddChild(button);
        return button;
    }
}
