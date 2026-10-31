using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Remnants.Game.Hud;
using Side = Remnants.Core.Raids.Side;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The selected placed defense: name, side and status chips (destroyed, sprung, idle without upkeep, gate shut); its HP
/// bar; what it does; the actions with their costs — repair (queues it), re-arm a sprung trap, upgrade into the next
/// tier, rotate, remove (refund shown, asks first); and for posts and towers the guard slots (portraits, take off duty),
/// an assign dropdown of the adults who can stand guard and the night-watch toggle. Core decides; refused commands
/// explain themselves in the log.
/// </summary>
public partial class DefensePanel : PanelContainer
{
    private readonly ColonySession _session;
    private readonly TextureRect _icon = Icons.Rect(Icons.Get("ui_defend", 26), 26, Palette.Rust);
    private readonly Label _name = Widgets.Label("", "Display");
    private readonly Label _sub = Widgets.Label("", "Small");
    private readonly HFlowContainer _status = new();
    private readonly StatBar _hp = new("stat_hp");
    private readonly Label _stats = Widgets.Wrapped("", "Small");
    private readonly VBoxContainer _actions = Widgets.Column(6);
    private readonly VBoxContainer _guardBox = Widgets.Column(6);
    private readonly Label _guardTitle = Widgets.Label("", "Heading");
    private readonly WorkerSlots _guards;
    private readonly Button _night;
    private readonly Label _nightNote = Widgets.Wrapped("", "Small");
    private string _signature = "";

    public int? DefenseId { get; private set; }

    /// <summary>"Open survivor" on a guard's portrait.</summary>
    public event Action<int>? SurvivorRequested;

    /// <summary>A guard's fit, for sorting the picker: their better fighting skill and their Perception, averaged.</summary>
    private static SlotFit GuardFit(Remnants.Core.Survivors.Survivor s)
    {
        double firearms = s.Get(Remnants.Core.Survivors.Skill.Firearms), melee = s.Get(Remnants.Core.Survivors.Skill.Melee);
        var perception = s.Get(Remnants.Core.Survivors.AttributeKind.Perception);
        return new SlotFit((Math.Max(firearms, melee) + perception) / 200,
            $"Guard fit: Firearms {firearms:0}, Melee {melee:0}, Perception {perception:0}");
    }

    public event Action? Closed;

    public DefensePanel(ColonySession session)
    {
        _session = session;
        var box = Ui.Panel(this, 8);
        var head = Widgets.Row(8);
        head.AddChild(_icon);
        var names = Widgets.Column(0);
        names.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _name.AddThemeFontSizeOverride("font_size", 20);
        _name.ClipText = true;
        names.AddChild(_name);
        names.AddChild(_sub);
        head.AddChild(names);
        var close = Widgets.IconButton("ui_close", "", "Deselect", () => Closed?.Invoke(), "Esc", "Flat");
        close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        head.AddChild(close);
        box.AddChild(head);
        _status.AddThemeConstantOverride("h_separation", 4);
        box.AddChild(_status);
        box.AddChild(_hp);
        box.AddChild(_stats);
        box.AddChild(_actions);

        _guardBox.AddChild(Widgets.SectionHeader("Guards", "stance_overwatch"));
        _guardBox.AddChild(_guardTitle);
        _guards = new WorkerSlots(session, 44)
        {
            Title = sim => DefenseId is { } id && sim.State.Defense.Find(id) is { } d && sim.Raids?.Defense(d.DefId) is { } def
                ? $"Guard the {def.Name}" : "Guard",
            Candidate = (sim, s) => !s.IsChild && Guards.PostOf(sim.State, s.Id)?.Id != DefenseId,
            Fit = (_, s) => GuardFit(s),
            Assign = s => new AssignGuard(s.Id, DefenseId),
            Unassign = s => new AssignGuard(s.Id, null),
            FreeTip = "Free guard slot: click to post someone here (takes them off their job).",
        };
        _guards.SurvivorRequested += id => SurvivorRequested?.Invoke(id);
        _guardBox.AddChild(_guards);
        _night = Widgets.IconButton("phase_night", "Night watch", "", ToggleNight);
        _night.ToggleMode = true;
        _guardBox.AddChild(_night);
        _guardBox.AddChild(_nightNote);
        box.AddChild(_guardBox);
        Visible = false;
    }

    public void Show(int? defenseId)
    {
        DefenseId = defenseId;
        _signature = "";
        _guards.Reset();
        if (defenseId is null) Visible = false;
        else Motion.Reveal(this, new Vector2(12, 0));
        Refresh();
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        if (DefenseId is not { } id || sim.Raids is not { } raids || sim.State.Defense.Find(id) is not { } d || !raids.Defenses.TryGetValue(d.DefId, out var def))
        {
            Visible = false;
            return;
        }
        var grid = DefenseGrid.Of(sim, raids);
        _icon.Texture = Icons.Get(RaidText.Icon(def), 26, RaidText.IconFallback(def), "ui_defend");
        _name.Text = def.Name;
        var faces = def.Kind is DefenseKind.Wall or DefenseKind.Gate or DefenseKind.Barricade ? "runs across" : "faces";
        _sub.Text = $"{def.Kind} · {RaidText.Side(grid.SideOf(d.X, d.Y))} side · {faces} {RaidText.Side(d.Facing)}";
        _hp.Set(d.Hp / def.Hp, $"{d.Hp:0}/{def.Hp:0}");
        _hp.TooltipText = $"HP {d.Hp:0} of {def.Hp:0}. Walls soak in proportion to the HP left.";
        _stats.Text = RaidText.Stats(def);
        _stats.TooltipText = def.Description;

        // Rebuilt only when something the buttons show changed (costs met, state, guards).
        var guards = sim.State.Defense.Guards.Where(kv => kv.Value == d.Id).Select(kv => kv.Key).OrderBy(k => k).ToList();
        var signature = $"{d.DefId}|{d.Hp:0}|{d.Sprung}|{d.Closed}|{d.NightWatch}|{sim.State.Defense.Unpaid.Contains(d.Id)}|{string.Join(",", guards)}|"
                        + $"{sim.State.Defense.Repairs.FirstOrDefault(r => r.Target == RepairTarget.Defense && r.TargetId == d.Id)?.Progress:0}|"
                        + string.Join(",", sim.Data.Resources.Keys.Select(r => (int)sim.State.Stockpile.Get(r))) + $"|{sim.TownHallLevel}|{sim.State.Survivors.Count}";
        if (signature == _signature) return;
        _signature = signature;
        RefreshStatus(sim, d, def);
        RefreshActions(sim, raids, d, def);
        RefreshGuards(sim, raids, d, def, guards);
    }

    private void RefreshStatus(ColonySim sim, PlacedDefense d, DefenseDef def)
    {
        Widgets.Clear(_status);
        void Chip(string icon, string text, Color color, string tip) => _status.AddChild(Widgets.Chip(Icons.Get(icon, 14), text, color, tip, color));
        if (d.Hp <= 0) Chip("stat_dead", "destroyed", Palette.Danger, "Knocked down: it does nothing until repaired.");
        else if (d.Hp < def.Hp) Chip("stat_injured", "damaged", Palette.Warn, "Repair it to bring it back to full strength.");
        if (d.Sprung) Chip("stat_warning", "sprung", Palette.Warn, "Sprung in the last raid: re-arm it.");
        if (sim.State.Defense.Unpaid.Contains(d.Id)) Chip("stat_warning", "idle: no upkeep", Palette.Warn, $"Upkeep {ColonyText.Cost(def.Upkeep, sim.Data)} a day was not paid: it does nothing today.");
        if (d.Closed) Chip("stat_lock", "shut", Palette.Text, "The gate is shut for the raid.");
        if (d.Hp > 0 && !d.Sprung && !sim.State.Defense.Unpaid.Contains(d.Id)) Chip("stat_check", "working", Palette.Good, "Standing and ready.");
        if (def.Upkeep.Count > 0) Chip("stat_time", $"upkeep {ColonyText.Cost(def.Upkeep, sim.Data)}/day", Palette.TextMuted, "Paid at midnight from the stockpile.");
    }

    private void RefreshActions(ColonySim sim, RaidData raids, PlacedDefense d, DefenseDef def)
    {
        Widgets.Clear(_actions);
        HBoxContainer Row(Button button, IReadOnlyDictionary<string, double>? cost)
        {
            var row = Widgets.Row(6);
            button.CustomMinimumSize = new Vector2(118, 30);
            row.AddChild(button);
            if (cost is not null)
            {
                var chips = Widgets.CostChips(RaidText.Costs(sim, cost));
                chips.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                row.AddChild(chips);
            }
            _actions.AddChild(row);
            return row;
        }

        var missing = RaidSystem.Missing(sim, raids, RepairTarget.Defense, d.Id);
        var order = sim.State.Defense.Repairs.FirstOrDefault(r => r.Target == RepairTarget.Defense && r.TargetId == d.Id);
        if (order is not null)
        {
            var row = Row(Widgets.IconButton("ui_close", "Cancel repair", "Take it out of the repair queue (paid materials come back)",
                () => _session.Execute(new CancelRepair(order.Id))), null);
            var bar = new StatBar(null, 0);
            var total = order.Progress + missing;
            bar.Set(total <= 0 ? 1 : order.Progress / total, order.Paid ? "repairing" : "waiting", Palette.Amber);
            bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(bar);
        }
        else if (missing > 1e-9)
            Row(Widgets.IconButton("ui_repair", "Repair", $"Queue the repair ({missing:0} HP). Idle adults work the queue by day; materials are paid when it starts.",
                () => _session.Execute(new QueueRepair(RepairTarget.Defense, d.Id))), RaidSystem.RepairCost(sim, raids, RepairTarget.Defense, d.Id));

        if (def.IsTrap && def.Rearmable && d.Sprung)
            Row(Widgets.IconButton("ui_continue", "Re-arm", "Set the trap again", () => _session.Execute(new RearmTrap(d.Id)), variation: "Primary"),
                DefenseHandlers.Cost(sim, def, def.RearmCost));

        if (def.UpgradesTo is { } nextId && raids.Defenses.TryGetValue(nextId, out var next))
        {
            var why = RaidText.LockReason(sim, raids, next);
            var up = Widgets.IconButton("ui_chevron_up", "Upgrade", why is null
                ? $"Into a {next.Name}: {RaidText.Stats(next)}. You pay the difference in cost." : $"{next.Name}: {why}.",
                () => _session.Execute(new UpgradeDefense(d.Id)));
            up.Disabled = why is not null;
            var row = Row(up, why is null ? DefenseHandlers.UpgradeCost(def, next) : null);
            if (why is not null) row.AddChild(Widgets.Label(why, "Small", Palette.Danger));
        }

        var tools = Widgets.Row(6);
        tools.AddChild(Widgets.IconButton("ui_rotate", "Rotate", "Turn it a quarter (free)", () =>
            _session.Execute(new RotateDefense(d.Id, Next(d.Facing)))));
        var refund = def.Cost.ToDictionary(kv => kv.Key, kv => Math.Floor(kv.Value * raids.Def.Repair.RemoveRefund * (d.Hp / def.Hp)));
        var refundText = refund.Values.Sum() > 0 ? $"You get back {ColonyText.Cost(refund.Where(kv => kv.Value > 0).ToDictionary(), sim.Data)}." : "Nothing comes back.";
        tools.AddChild(Widgets.IconButton("ui_close", "Remove", $"Tear it down. {refundText}", () =>
            ConfirmDialog.Ask(this, "Remove", $"Tear down the {def.Name}? {refundText} Its guards go off duty.", "Remove",
                () => _session.Execute(new RemoveDefense(d.Id)))));
        _actions.AddChild(tools);
    }

    private static Side Next(Side side) => side switch { Side.South => Side.West, Side.West => Side.North, Side.North => Side.East, _ => Side.South };

    private void RefreshGuards(ColonySim sim, RaidData raids, PlacedDefense d, DefenseDef def, List<int> guards)
    {
        _guardBox.Visible = def.GuardSlots > 0;
        if (!_guardBox.Visible) return;
        _guardTitle.Text = $"ON DUTY {guards.Count}/{def.GuardSlots}";
        var onDuty = guards.Select(sim.State.FindSurvivor).OfType<Remnants.Core.Survivors.Survivor>().ToList();
        _guards.WorkerTip = g => $"{g.Name}\nGuards here{(d.NightWatch ? " on the night watch" : " by day")}.";
        _guards.Set(onDuty, def.GuardSlots);

        _night.SetPressedNoSignal(d.NightWatch);
        _night.Text = d.NightWatch ? "Night watch: ON" : "Night watch: off";
        _night.TooltipText = "Guards here stand watch from dusk to dawn and sleep in the day. Night work costs sleep and mood.";
        _nightNote.Text = d.NightWatch ? "They watch the dark hours: most raids come at night." : "Day watch: the post is empty at night.";
        _nightNote.AddThemeColorOverride("font_color", d.NightWatch ? Palette.Night : Palette.TextMuted);
    }

    private void ToggleNight()
    {
        if (DefenseId is { } id && _session.Sim.State.Defense.Find(id) is { } d) _session.Execute(new SetNightWatch(id, !d.NightWatch));
    }
}
