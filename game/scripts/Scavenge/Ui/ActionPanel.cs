using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// What the acting unit can do besides moving, as <see cref="ActionButton"/>s with icons, AP-cost pips and hotkeys:
/// pick the attack for map clicks (1–3), use a consumable (4–6, then click a target), salvage a node in reach (I),
/// recruit a stranger (T), extract on an exit (X). Piles are taken from the <see cref="LootPanel"/> (F). Every button
/// sends a Core command through the screen; Core decides whether it works.
/// </summary>
public partial class ActionPanel : PanelContainer
{
    private readonly ScavengePresenter _presenter;
    private readonly Label _hint = Widgets.Wrapped("", "Small");
    private readonly VBoxContainer _content = Widgets.Column(4);
    private readonly List<ActionButton> _buttons = new();

    /// <summary>The attack map clicks on enemies will use.</summary>
    public string? ActionId { get; private set; }

    public event Action<string>? ActionChosen;
    public event Action<string>? UseItemChosen;
    public event Action<IScavengeCommand>? CommandRequested;

    public ActionPanel(ScavengePresenter presenter)
    {
        _presenter = presenter;
        CustomMinimumSize = new Vector2(300, 0);
        var box = UiKit.Panel(this);
        box.AddChild(Widgets.SectionHeader("Actions", "ui_attack"));
        box.AddChild(_hint);
        box.AddChild(_content);
    }

    public void SetAction(string? actionId)
    {
        ActionId = actionId;
        ActionChosen?.Invoke(actionId ?? "");
    }

    /// <summary>Presses the button bound to the key in <paramref name="e"/>, if any is enabled.</summary>
    public bool PressHotkey(InputEvent e)
    {
        var button = _buttons.FirstOrDefault(b => b.Hotkey is { } k && !b.Disabled && Hotkeys.Pressed(e, k));
        if (button is null) return false;
        if (button.ToggleMode) button.ButtonPressed = !button.ButtonPressed;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    public void Refresh(bool busy, string? usingItem)
    {
        Widgets.Clear(_content);
        _buttons.Clear();
        var unit = _presenter.ActingUnit;
        if (unit is null || busy)
        {
            _hint.Text = _presenter.Done ? "The run is over." : "Waiting for the other units to act…";
            return;
        }

        var sim = _presenter.Sim;
        var data = sim.Data;
        var profile = sim.Profile(unit);
        if (ActionId is null || profile.Action(ActionId) is null) ActionId = profile.Actions.FirstOrDefault()?.Id;
        _hint.Text = usingItem is { } item
            ? data.Items[item].Use is { TargetsTile: true } onTile
                ? $"Click a tile within {onTile.Range} for the {ScavengeText.Item(data, item)}"
                  + (onTile.Emp is { } emp ? $": it bursts {emp.Radius} tiles around" : "") + ". Right-click or Esc cancels."
                : $"Click who gets the {ScavengeText.Item(data, item)} (yourself or someone next to you). Right-click or Esc cancels."
            : "Click a lit tile to move, a red target to attack with the chosen action.";

        _content.AddChild(Widgets.Label("ATTACK WITH", "Heading"));
        var n = 0;
        foreach (var a in profile.Actions)
        {
            var icon = a.Pattern == AttackPattern.Melee ? "skill_melee" : "skill_firearms";
            var hotkey = n < 3 ? $"scav_attack_{++n}" : null;
            var button = Add(new ActionButton(icon, a.Name, $"{a.Pattern} {a.Reach} · {a.MinDamage:0}–{a.MaxDamage:0} dmg · {Fmt.Percent(a.Accuracy)}",
                a.ApCost, hotkey, $"{a.Name}: choose it for clicks on targets. Hover a target for its hit chance."
                    + (a.CloseApPenalty > 0 ? $"\n+{a.CloseApPenalty} AP within {a.CloseRange} tiles (scope)." : "")
                    + (a.ArmorPierce > 0 ? $"\nPierces {a.ArmorPierce:0.#} armor." : "")), () => SetAction(a.Id));
            button.ToggleMode = true;
            button.ButtonPressed = a.Id == ActionId;
            button.SetBlocked(a.ApCost > unit.Ap, a.ApCost > unit.Ap);
        }

        var usable = unit.Pack.Distinct().Where(id => data.Items[id].Use is not null).ToList();
        if (usable.Count > 0)
        {
            _content.AddChild(Widgets.Label("USE", "Heading"));
            var i = 0;
            foreach (var id in usable)
            {
                var def = data.Items[id];
                var use = def.Use!;
                var count = unit.Pack.Count(p => p == id);
                var hotkey = i < 6 ? $"scav_item_{++i}" : null;
                var ap = FieldMedicine.ApCost(unit, use);
                var charges = ElectronicWarfare.ChargesLeft(data, unit, id);
                var title = charges is { } c ? $"{ScavengeText.Item(data, id)} · {c} charge{(c == 1 ? "" : "s")}" : $"{ScavengeText.Item(data, id)} ×{count}";
                var button = Add(new ActionButton(ScavengeText.UseIcon(def), title, ScavengeText.UseDetail(use),
                    ap, hotkey, ScavengeText.UseTip(data, def, charges)), () => UseItemChosen?.Invoke(id));
                button.ToggleMode = true;
                button.ButtonPressed = id == usingItem;
                button.SetBlocked(ap > unit.Ap || charges is 0, ap > unit.Ap);
            }
        }

        var nodes = _presenter.NodesInReach(unit).ToList();
        if (nodes.Count > 0)
        {
            _content.AddChild(Widgets.Label("SALVAGE", "Heading"));
            var first = true;
            foreach (var node in nodes)
            {
                if (data.Salvage.Node(node.Kind) is not { } def) continue;
                var nodeId = node.Id;
                var actions = FieldSalvage.ActionsLeft(def, node, unit);
                var yield = FieldSalvage.Estimate(sim, node, unit).OrderByDescending(kv => kv.Value).FirstOrDefault();
                var detail = $"{(yield.Key is null ? "" : $"~{yield.Value:0.#} {ScavengeText.Item(data, yield.Key)} · ")}{actions} action{(actions == 1 ? "" : "s")} · noise {def.Noise}";
                var button = Add(new ActionButton("stat_loot", $"Salvage {def.Name}", detail, def.ApCost, first ? "scav_salvage" : null,
                    $"Salvage the {def.Name.ToLowerInvariant()}: a lot comes off every few actions into your pack (what does not fit stays on the ground). Loud: enemies within {def.Noise} tiles hear it."),
                    () => CommandRequested?.Invoke(new Salvage(_presenter.PlayerId, unit.Id, nodeId)));
                button.SetBlocked(def.ApCost > unit.Ap, def.ApCost > unit.Ap);
                first = false;
            }
        }

        foreach (var npc in _presenter.RecruitableNear(unit))
        {
            var npcId = npc.Id;
            var cost = data.Rules.Turn.RecruitApCost;
            var button = Add(new ActionButton("stat_recruit", $"Recruit {npc.Name}", "they follow you and join the colony", cost, "scav_recruit"),
                () => CommandRequested?.Invoke(new Recruit(_presenter.PlayerId, unit.Id, npcId)));
            button.SetBlocked(cost > unit.Ap, cost > unit.Ap);
        }

        if (_presenter.State.Map.IsExit(unit.Pos))
            Add(new ActionButton("ui_extract", "Extract here", "leave the zone with what you carry", 0, "scav_extract"),
                () => CommandRequested?.Invoke(new Extract(_presenter.PlayerId, unit.Id))).ThemeTypeVariation = "Primary";
    }

    private ActionButton Add(ActionButton button, Action onPressed)
    {
        button.Pressed += onPressed;
        _buttons.Add(button);
        _content.AddChild(button);
        return button;
    }
}
