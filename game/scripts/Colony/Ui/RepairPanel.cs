using System;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The repair queue (TopBar's Repairs, or from defend mode): the orders in the order they are worked — name, HP bar with
/// progress, cost chips (paid or not), cancel — and below them every damaged building and defense not queued yet, each
/// with Queue and its cost, plus Queue all. The header says how fast the colony repairs right now
/// (<see cref="RaidSystem.RepairRate"/>: idle adults on their day shift).
/// </summary>
public partial class RepairPanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly Label _rate = Widgets.Wrapped("", "Small");
    private readonly VBoxContainer _queue = Widgets.Column(6);
    private readonly VBoxContainer _waiting = Widgets.Column(6);
    private readonly Button _queueAll;
    private string _signature = "";

    public RepairPanel(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(420, 0);
        var box = Ui.Panel(this, 8);
        box.AddChild(Ui.TitleRow("Repairs", "ui_repair", Toggle));
        box.AddChild(_rate);
        box.AddChild(Widgets.SectionHeader("Queue", "stat_time"));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) };
        var col = Widgets.Column(8);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddChild(_queue);
        col.AddChild(Widgets.SectionHeader("Damaged", "stat_injured"));
        col.AddChild(_waiting);
        scroll.AddChild(col);
        box.AddChild(scroll);
        _queueAll = Widgets.IconButton("ui_repair", "Queue all repairs", "Queue every damaged building and defense", () => QueueAll(_session), variation: "Primary");
        box.AddChild(_queueAll);
        Visible = false;
    }

    public void Toggle()
    {
        _signature = "";
        Motion.Toggle(this, new Vector2(0, -8));
        Refresh();
    }

    /// <summary>Queues every damaged building and defense that is not queued yet, with one line of feedback.</summary>
    public static void QueueAll(ColonySession session)
    {
        if (session.Sim.Raids is not { } raids) return;
        var targets = RaidText.Unqueued(session.Sim, raids).ToList();
        if (targets.Count == 0)
        {
            session.Notify("Nothing left to queue for repair.");
            return;
        }
        session.ExecuteAll(targets.Select(t => (IColonyCommand)new QueueRepair(t.Target, t.Id)), $"{targets.Count} repair{(targets.Count == 1 ? "" : "s")} queued.");
    }

    public void Refresh()
    {
        if (!Visible || _session.Sim.Raids is not { } raids) return;
        var sim = _session.Sim;
        var rate = RaidSystem.RepairRate(sim, raids);
        _rate.Text = rate > 0
            ? $"Repairing about {rate:0.#} HP an hour (idle adults on their day shift; better with Crafting). Materials are paid when a repair starts."
            : "Nobody is repairing right now: repairs are day work for idle adults. Materials are paid when a repair starts.";
        var unqueued = RaidText.Unqueued(sim, raids).ToList();
        var signature = string.Join(";", sim.State.Defense.Repairs.Select(r => $"{r.Id}:{r.Progress:0}:{r.Paid}"))
                        + "|" + string.Join(";", unqueued.Select(u => $"{u.Target}{u.Id}:{RaidSystem.Missing(sim, raids, u.Target, u.Id):0}"))
                        + "|" + string.Join(",", sim.Data.Resources.Keys.Select(r => (int)sim.State.Stockpile.Get(r)));
        if (signature == _signature) return;
        _signature = signature;

        Widgets.Clear(_queue);
        if (sim.State.Defense.Repairs.Count == 0) _queue.AddChild(Widgets.Label("Nothing queued.", "Muted"));
        var first = true;
        foreach (var order in sim.State.Defense.Repairs)
        {
            var missing = RaidSystem.Missing(sim, raids, order.Target, order.TargetId);
            var (hp, max) = RaidText.Health(sim, raids, order.Target, order.TargetId);
            var status = order.Paid ? (rate > 0 && first ? "repairing" : "paid, waiting") : sim.State.Stockpile.CanAfford(order.Cost) ? "next" : "short of materials";
            first = false;
            var cancel = Widgets.IconButton("ui_close", "", "Cancel this repair (paid materials come back)", () => _session.Execute(new CancelRepair(order.Id)), variation: "Flat");
            _queue.AddChild(Row(sim, raids, order.Target, order.TargetId, hp, max, order.Paid ? null : order.Cost, status, cancel,
                $"{missing:0} HP to go" + (rate > 0 ? $", about {Math.Ceiling(missing / rate):0} h of work" : "")));
        }

        Widgets.Clear(_waiting);
        if (unqueued.Count == 0) _waiting.AddChild(Widgets.Label("Everything else is whole.", "Muted"));
        foreach (var (target, id) in unqueued)
        {
            var (hp, max) = RaidText.Health(sim, raids, target, id);
            var queue = Widgets.IconButton("ui_plus", "Queue", "Add to the repair queue", () => _session.Execute(new QueueRepair(target, id)));
            var wrecked = target == RepairTarget.Building && sim.State.FindBuilding(id) is { } b && RaidSystem.IsRubble(sim, b);
            _waiting.AddChild(Row(sim, raids, target, id, hp, max, RaidSystem.RepairCost(sim, raids, target, id),
                wrecked ? "wrecked" : hp <= 0 ? "destroyed" : "damaged", queue, wrecked ? "Wrecked: nobody can work there until it is repaired." : ""));
        }
        _queueAll.Disabled = unqueued.Count == 0;
    }

    private static Control Row(ColonySim sim, RaidData raids, RepairTarget target, int id, double hp, double max,
        System.Collections.Generic.IReadOnlyDictionary<string, double>? cost, string status, Button action, string tip)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card", TooltipText = tip };
        var col = Widgets.Column(4);
        card.AddChild(col);
        var top = Widgets.Row(6);
        top.AddChild(Icons.Rect(Icons.Get(target == RepairTarget.Building ? "ui_build" : "ui_defend", 16), 16, Palette.Rust));
        var name = Widgets.Label(RaidText.TargetName(sim, raids, target, id));
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        top.AddChild(name);
        var bad = status is "wrecked" or "destroyed" or "short of materials";
        top.AddChild(Widgets.Label(status, "Small", bad ? Palette.Danger : status == "repairing" ? Palette.Good : Palette.TextMuted));
        top.AddChild(action);
        col.AddChild(top);
        var bottom = Widgets.Row(6);
        var bar = new StatBar("stat_hp", 120);
        bar.Set(max <= 0 ? 0 : hp / max, $"{hp:0}/{max:0}");
        bottom.AddChild(bar);
        if (cost is not null)
        {
            var chips = Widgets.CostChips(RaidText.Costs(sim, cost));
            chips.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bottom.AddChild(chips);
        }
        else bottom.AddChild(Widgets.Label("materials paid", "Small", Palette.Good));
        col.AddChild(bottom);
        return card;
    }
}
