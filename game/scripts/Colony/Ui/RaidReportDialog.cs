using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The after-raid report, in the after-action style, popped when a new <see cref="DefenseState.LastReport"/> appears:
/// the outcome headline, defense against attack (the strength breakdown in the tooltip), who stood, the traps that
/// sprang, the damage (destroyed marked), what was stolen or trampled, the hurt, the dead and the taken, the loot, a
/// schematic found and the mood change, with "Queue all repairs".
/// </summary>
public partial class RaidReportDialog : Overlay
{
    private readonly ColonySession _session;
    private readonly Label _headline = Widgets.Label("", "Title");
    private readonly Label _who = Widgets.Label("", "Display");
    private readonly Label _summary = Widgets.Label("", "Muted");
    private readonly StrengthBar _bar = new();
    private readonly Label _numbers = Widgets.Wrapped("", "Small");
    private readonly VBoxContainer _content = Widgets.Column(10);
    private readonly Button _repairs;

    public event System.Action? RepairsQueued;

    public RaidReportDialog() : this(null!) { }

    public RaidReportDialog(ColonySession session) : base(700)
    {
        _session = session;
        Visible = false;
        Body.AddChild(Title("RAID REPORT"));
        _headline.HorizontalAlignment = HorizontalAlignment.Center;
        Body.AddChild(_headline);
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get("stat_threat", 22), 22, Palette.Rust));
        head.AddChild(_who);
        head.AddChild(Widgets.Spacer());
        head.AddChild(_summary);
        Body.AddChild(head);
        _bar.CustomMinimumSize = new Vector2(0, 24);
        Body.AddChild(_bar);
        Body.AddChild(_numbers);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 340) };
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_content);
        Body.AddChild(scroll);
        var buttons = Widgets.Row(8);
        _repairs = Widgets.IconButton("ui_repair", "Queue all repairs", "Queue every damaged building and defense for repair", () =>
        {
            RepairPanel.QueueAll(_session);
            RepairsQueued?.Invoke();
            Refresh();
        });
        buttons.AddChild(_repairs);
        buttons.AddChild(Widgets.Spacer());
        var ok = Widgets.IconButton("stat_check", "Back to the colony", "Close the report", Close, Hotkeys.KeyName("pause_menu"), "Primary");
        ok.CustomMinimumSize = new Vector2(200, 38);
        buttons.AddChild(ok);
        Body.AddChild(buttons);
    }

    private void Refresh()
    {
        if (_session.Sim.Raids is { } raids) _repairs.Disabled = !RaidText.Unqueued(_session.Sim, raids).Any();
    }

    public void ShowReport(RaidReport report)
    {
        var sim = _session.Sim;
        var raids = sim.Raids;
        var name = raids?.Def.Threats.GetValueOrDefault(report.Kind)?.Name ?? report.Kind.ToString();
        _headline.Text = report.Repelled ? "RAID REPELLED" : "THEY BROKE THROUGH";
        _headline.AddThemeColorOverride("font_color", report.Repelled ? Palette.Good : Palette.Danger);
        _who.Text = $"{name} ×{report.Size} from the {RaidText.Side(report.Approach)}";
        _summary.Text = $"Day {report.Tick / sim.Data.Rules.TicksPerDay + 1}, {report.Tick % sim.Data.Rules.TicksPerDay:00}:00 · {report.AttackersKilled} of them down";
        _bar.Set(report.Attack + report.Defense <= 0 ? 1 : report.Defense / (report.Attack + report.Defense));
        _bar.TooltipText = string.Join("\n", report.Breakdown.Select(l => $"{l.Line}: {(l.Value < 0 ? "−" : "+")}{System.Math.Abs(l.Value):0.#}"));
        _numbers.Text = $"Defense {report.Defense:0} against attack {report.Attack:0} after traps and walls. Hover the bar for the breakdown.";
        Widgets.Clear(_content);

        Section("Stood their ground", "stat_pop", report.Defenders, n => Chip("stance_overwatch", n, Palette.Text, "Defended the colony."), "Nobody stood.");
        if (report.TrapsSprung.Count > 0)
            Section("Traps sprung", "def_cat_traps", report.TrapsSprung, n => Chip("def_cat_traps", n, Palette.Amber, "Sprang on them. Re-arm the ones that re-arm."), "");

        _content.AddChild(Widgets.SectionHeader("Damage", "ui_repair"));
        var damage = Flow();
        if (report.Damaged.Count == 0) damage.AddChild(Widgets.Label("Nothing damaged.", "Muted"));
        foreach (var d in report.Damaged)
            damage.AddChild(Chip(d.Destroyed ? "stat_dead" : "stat_injured", d.Destroyed ? $"{d.Name} −{d.Damage:0} · destroyed" : $"{d.Name} −{d.Damage:0}",
                d.Destroyed ? Palette.Danger : Palette.Warn, d.Destroyed ? "Down: it does nothing until repaired." : "Damaged: queue a repair."));
        _content.AddChild(damage);

        if (report.Stolen.Count > 0)
        {
            var verb = report.Kind == ThreatKind.Husk ? "Trampled" : "Stolen";
            _content.AddChild(Widgets.SectionHeader(verb, "stat_loot"));
            var flow = Flow();
            foreach (var (res, amount) in report.Stolen)
                flow.AddChild(Widgets.Chip(Icons.Resource(res, 16), $"−{Fmt.Amount(amount)} {sim.Data.Resources.GetValueOrDefault(res)?.Name ?? res}", Palette.Danger,
                    $"{verb} from the stockpile.", Palette.Danger));
            _content.AddChild(flow);
        }
        if (report.Injured.Count > 0)
            Section("Hurt", "stat_injured", report.Injured, i => Chip("stat_injured", $"{i.Name}: {i.Injury}", Palette.Warn, "See the Med Bay."), "");
        if (report.Dead.Count > 0) Section("Killed", "stat_dead", report.Dead, n => Chip("stat_dead", n, Palette.Danger, "Fell defending the colony."), "");
        if (report.Taken.Count > 0) Section("Taken", "stat_linked", report.Taken, n => Chip("stat_linked", n, Palette.Network, "Carried off by the Linked."), "");

        _content.AddChild(Widgets.SectionHeader("Left behind", "stat_loot"));
        var loot = Flow();
        if (report.Loot.Count == 0 && report.Schematic is null) loot.AddChild(Widgets.Label("Nothing worth taking.", "Muted"));
        foreach (var (id, count) in report.Loot)
        {
            var item = sim.Scavenge?.Items.GetValueOrDefault(id);
            var res = sim.Data.Resources.GetValueOrDefault(id);
            var icon = item is not null ? Icons.Item(item.DefinedId, item.Kind.ToString(), 16) : res is not null ? Icons.Resource(id, 16) : Icons.Get("item_generic", 16);
            loot.AddChild(Widgets.Chip(icon, $"{item?.Name ?? res?.Name ?? id} ×{count}", Palette.Text, "Picked off the fallen.", Palette.TextMuted));
        }
        if (report.Schematic is { } schematic)
            loot.AddChild(Chip("item_blueprint", $"Schematic: {schematic}", Palette.Good, "A new defense to build."));
        _content.AddChild(loot);

        if (System.Math.Abs(report.MoodChange) > 0.05)
        {
            var good = report.MoodChange > 0;
            _content.AddChild(Widgets.SectionHeader("Morale", "stat_mood"));
            var mood = Flow();
            mood.AddChild(Chip("stat_mood", $"mood {Fmt.Delta(report.MoodChange)}", good ? Palette.Good : Palette.Danger,
                good ? "Everyone is proud they held." : "The colony is shaken."));
            _content.AddChild(mood);
        }
        Refresh();
        Open();
    }

    private void Section<T>(string title, string icon, IEnumerable<T> items, System.Func<T, Control> chip, string empty)
    {
        _content.AddChild(Widgets.SectionHeader(title, icon));
        var flow = Flow();
        var any = false;
        foreach (var item in items)
        {
            any = true;
            flow.AddChild(chip(item));
        }
        if (!any && empty.Length > 0) flow.AddChild(Widgets.Label(empty, "Muted"));
        _content.AddChild(flow);
    }

    private static PanelContainer Chip(string icon, string text, Color color, string tip) =>
        Widgets.Chip(Icons.Get(icon, 16, "stat_warning"), text, color, tip, color == Palette.Text ? Palette.TextMuted : color);

    private static HFlowContainer Flow()
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        return flow;
    }
}
