using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The after-action report of an expedition that came home, as a "what happened" screen: who returned (portrait,
/// health, new injuries with body-part icons, evacuated), who was lost, who was recruited, the loot and any gear
/// lost, as chips.
/// </summary>
public partial class AfterActionDialog : Overlay
{
    private readonly ColonySession _session;
    private readonly Label _zone = Widgets.Label("", "Display");
    private readonly Label _summary = Widgets.Label("", "Muted");
    private readonly VBoxContainer _content = Widgets.Column(10);

    public AfterActionDialog() : this(null!) { }

    public AfterActionDialog(ColonySession session) : base(660)
    {
        _session = session;
        Visible = false;
        Body.AddChild(Title("AFTER-ACTION REPORT"));
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get("ui_expedition", 22), 22, Palette.Rust));
        head.AddChild(_zone);
        head.AddChild(Widgets.Spacer());
        head.AddChild(_summary);
        Body.AddChild(head);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 380),
        };
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_content);
        Body.AddChild(scroll);
        var ok = Widgets.IconButton("stat_check", "Back to the colony", "Close the report", Close, Hotkeys.KeyName("pause_menu"), "Primary");
        ok.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        ok.CustomMinimumSize = new Vector2(200, 38);
        Body.AddChild(ok);
    }

    public new void Hide() => Close();

    public void ShowReport(ExpeditionReport report)
    {
        var sim = _session.Sim;
        var data = sim.Scavenge;
        _zone.Text = data?.Zones.GetValueOrDefault(report.ZoneId)?.Name ?? report.ZoneId;
        _summary.Text = $"Day {report.Tick / sim.Data.Rules.TicksPerDay + 1} · {report.Returned.Count} home · {report.Dead.Count} lost · {report.Recruits.Count} recruited";
        Widgets.Clear(_content);

        _content.AddChild(Widgets.SectionHeader("Came home", "stat_check"));
        if (report.Returned.Count == 0) _content.AddChild(Widgets.Label("Nobody.", "Muted"));
        foreach (var r in report.Returned) _content.AddChild(Returned(sim, r));

        if (report.Dead.Count > 0)
        {
            _content.AddChild(Widgets.SectionHeader("Lost", "stat_dead"));
            var flow = Flow();
            foreach (var name in report.Dead)
                flow.AddChild(Widgets.Chip(Icons.Get("stat_dead", 16), name, Palette.Danger, "Did not come back. Their gear is lost.", Palette.Danger));
            _content.AddChild(flow);
        }
        if (report.Recruits.Count > 0)
        {
            _content.AddChild(Widgets.SectionHeader("Recruited", "stat_recruit"));
            var flow = Flow();
            foreach (var name in report.Recruits)
                flow.AddChild(Widgets.Chip(Icons.Get("stat_recruit", 16), name, Palette.Good, "Joins the colony. Needs a bed before the grace time runs out.", Palette.Good));
            _content.AddChild(flow);
            _content.AddChild(Widgets.Label("Recruits need beds: check the housing.", "Small"));
        }
        _content.AddChild(Widgets.SectionHeader("Loot", "stat_loot"));
        _content.AddChild(Items(sim, report.Loot, Palette.Text, "Nothing brought back."));
        if (report.LostGear.Count > 0)
        {
            _content.AddChild(Widgets.SectionHeader("Gear lost", "stat_warning"));
            _content.AddChild(Items(sim, report.LostGear, Palette.Danger, ""));
        }
        if (report.Vehicle is { } drove)
        {
            _content.AddChild(Widgets.SectionHeader("Vehicle", "ui_vehicle"));
            var condition = sim.State.Vehicles.Find(v => v.Name == drove) is { } v ? $" ({v.Condition:0}/{sim.VehicleStats(v).MaxCondition:0}; repair at the Garage)" : "";
            _content.AddChild(Widgets.Label($"The {drove} brought the party home{condition}.", color: Palette.Text));
        }
        if (report.LostVehicle is { } lost)
        {
            _content.AddChild(Widgets.SectionHeader("Vehicle lost", "stat_warning"));
            _content.AddChild(Widgets.Label($"The {lost} did not come back, with everything in it.", color: Palette.Danger));
        }
        Open();
    }

    private Control Returned(ColonySim sim, ReportSurvivor r)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var row = Widgets.Row(10);
        card.AddChild(row);
        var portrait = new Portrait(56);
        if (sim.State.FindSurvivor(r.SurvivorId) is { } s) portrait.Set(PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
        row.AddChild(portrait);
        var col = Widgets.Column(4);
        col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(col);
        var top = Widgets.Row(8);
        var name = Widgets.Label(r.Name, "", size: 16);
        name.AddThemeFontOverride("font", Fonts.BodyBold);
        top.AddChild(name);
        if (r.Evacuated) top.AddChild(Widgets.Chip(Icons.Get("ui_extract", 14), "evacuated", Palette.Warn, "Pulled out by an evac beacon.", Palette.Warn));
        var hp = new StatBar("stat_hp", 140);
        hp.Set(r.Health / Survivor.MaxHealth, $"{r.Health:0}");
        hp.TooltipText = $"Health {r.Health:0}/{Survivor.MaxHealth:0}";
        top.AddChild(Widgets.Spacer());
        top.AddChild(hp);
        col.AddChild(top);
        var injuries = Flow();
        if (r.NewInjuries.Count == 0) injuries.AddChild(Widgets.Chip(Icons.Get("stat_check", 14), "unhurt", Palette.Good, "No new injuries.", Palette.Good));
        foreach (var line in r.NewInjuries)
        {
            var part = line.Split(' ')[0];
            var lost = line.Contains("lost");
            injuries.AddChild(Widgets.Chip(Icons.BodyPart(part, 16), Spaced(line), lost ? Palette.Danger : Palette.Warn,
                lost ? "Lost for good: a prosthetic can be fitted at the Med Bay." : "Admit to the Med Bay if it is severe.", lost ? Palette.Danger : Palette.Warn));
        }
        col.AddChild(injuries);
        return card;
    }

    private static HFlowContainer Items(ColonySim sim, IDictionary<string, int> items, Color color, string empty)
    {
        var flow = Flow();
        if (items.Count == 0) flow.AddChild(Widgets.Label(empty, "Muted"));
        foreach (var (id, count) in items)
        {
            var def = sim.Scavenge?.Items.GetValueOrDefault(id);
            var icon = def is null ? Icons.Get("item_generic", 16) : Icons.Item(def.DefinedId, def.Kind.ToString(), 16);
            flow.AddChild(Widgets.Chip(icon, $"{def?.Name ?? id} ×{count}", color, def?.Kind.ToString() ?? "", Palette.TextMuted));
        }
        return flow;
    }

    private static HFlowContainer Flow()
    {
        var flow = new HFlowContainer();
        flow.AddThemeConstantOverride("h_separation", 6);
        flow.AddThemeConstantOverride("v_separation", 6);
        return flow;
    }

    private static string Spaced(string text) =>
        string.Concat(text.Select((c, i) => i > 0 && char.IsUpper(c) && char.IsLetter(text[i - 1]) ? " " + char.ToLowerInvariant(c) : c.ToString()));
}
