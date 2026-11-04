using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// A small card next to the cursor over a known loot pile or salvage node. A pile: whose it is and what is in it (as
/// last seen when the tile is out of sight). A node: what it gives, about how much a lot yields for the acting unit,
/// how many actions the next lot takes, how loud it is and how many lots are left. Purely visual; the screen feeds it.
/// </summary>
public partial class LootTip : PanelContainer
{
    private readonly Label _title = Widgets.Label("", "Heading");
    private readonly Label _state = Widgets.Label("", "Small");
    private readonly HFlowContainer _chips = new();
    private readonly Label _effect = Widgets.Wrapped("", "Small");

    public LootTip()
    {
        ThemeTypeVariation = "Card";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(260, 0);
        _effect.CustomMinimumSize = new Vector2(244, 0);
        _chips.AddThemeConstantOverride("h_separation", 4);
        _chips.AddThemeConstantOverride("v_separation", 4);
        _chips.MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var col = Widgets.Column(3);
        col.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(col);
        col.AddChild(_title);
        col.AddChild(_state);
        col.AddChild(_chips);
        col.AddChild(_effect);
    }

    public void ShowPile(ScavengePresenter presenter, LootCache pile, Vector2 screen)
    {
        var data = presenter.Sim.Data;
        _title.Text = LootText.PileName(presenter, pile).ToUpperInvariant();
        _title.AddThemeColorOverride("font_color", pile.Kind == LootPileKind.Stash ? Palette.Amber : Palette.Text);
        var kg = pile.Items.Sum(id => data.Items.TryGetValue(id, out var i) ? i.Weight : 0);
        _state.Text = $"{pile.Items.Count} item{(pile.Items.Count == 1 ? "" : "s")} · {kg:0.#} kg" + (presenter.IsVisible(pile.Pos) ? "" : " · as last seen");
        Widgets.Clear(_chips);
        foreach (var chip in LootText.Chips(data, pile.Items)) _chips.AddChild(chip);
        _chips.Visible = true;
        _effect.Text = pile.Kind == LootPileKind.Stash ? "Left here for the next run to this zone." : "Stand next to it to pick it up (F takes everything that fits).";
        Place(screen);
    }

    public void ShowNode(ScavengePresenter presenter, SalvageNode node, Vector2 screen)
    {
        var sim = presenter.Sim;
        if (sim.Data.Salvage.Node(node.Kind) is not { } def) return;
        var unit = presenter.ActingUnit;
        _title.Text = def.Name.ToUpperInvariant();
        _title.AddThemeColorOverride("font_color", node.Depleted ? Palette.TextDim : Palette.Amber);
        if (node.Depleted)
        {
            _state.Text = "Stripped bare.";
            _chips.Visible = false;
            _effect.Text = "";
            Place(screen);
            return;
        }
        var actions = unit is null ? (def.Work - node.Progress) : FieldSalvage.ActionsLeft(def, node, unit);
        _state.Text = $"{node.LotsLeft} lot{(node.LotsLeft == 1 ? "" : "s")} left · {actions} action{(actions == 1 ? "" : "s")} of {def.ApCost} AP for the next"
                      + (node.Progress > 0 ? $" ({node.Progress}/{def.Work} done)" : "");
        Widgets.Clear(_chips);
        foreach (var (id, n) in FieldSalvage.Estimate(sim, node, unit).Where(kv => kv.Value >= 0.05).OrderByDescending(kv => kv.Value))
            _chips.AddChild(Widgets.Chip(LootText.Icon(sim.Data, id), $"~{n:0.#} {ScavengeText.Item(sim.Data, id)}", null, "Expected per lot"));
        _chips.Visible = _chips.GetChildCount() > 0;
        var tools = def.Tools.Count == 0 ? "" : $" Tools: {string.Join(", ", def.Tools.Keys.Select(t => ScavengeText.Item(sim.Data, t)))}.";
        _effect.Text = $"Noise: heard {def.Noise} tiles away.{(def.Skill is { } s ? $" Skill: {s}." : "")}{tools}";
        if (unit is not null && presenter.IsMine(unit))
            _effect.Text += FieldSalvage.ClickedNode(presenter.State, unit, node.Pos) is null
                ? $"\nStand next to it, then click it to salvage ({def.ApCost} AP)."
                : $"\nClick to salvage ({def.ApCost} AP).";
        Place(screen);
    }

    private void Place(Vector2 screen)
    {
        _effect.Visible = _effect.Text.Length > 0;
        Position = screen + new Vector2(18, 18);
        Visible = true;
        Size = Vector2.Zero;
        CallDeferred(Control.MethodName.ResetSize);
    }

    public void HideTip() => Visible = false;
}
