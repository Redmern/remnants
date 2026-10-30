using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Raids;
using Remnants.Core.Survivors;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The prep screen for a warned raid (the banner's Prepare). Left: the defenders checklist (survivor cards; guards are
/// locked in, the away, patients and children cannot stand), shelter the children, shut the gates, recall the party
/// (only while <see cref="DefenseHandlers.CanRecall"/>). Right: where the warning came from
/// (<see cref="RaidWarning.Sources"/>) and the odds from <see cref="AutoResolve.Assess"/> — defense against attack as a bar
/// with the verdict and the breakdown. Then "Auto-resolve on arrival" (the default) and a disabled "Fight it" for the
/// tactical battle to come. Every toggle is a command; the preview re-reads Core.
/// </summary>
public partial class RaidPrepDialog : Overlay
{
    private readonly ColonySession _session;
    private readonly Label _headline = Widgets.Label("", "Display", Palette.Danger);
    private readonly Label _when = Widgets.Label("", "Muted");
    private readonly VBoxContainer _defenders = Widgets.Column(4);
    private readonly Label _defNote = Widgets.Wrapped("", "Small");
    private readonly Button _shelter;
    private readonly Button _gates;
    private readonly Button _recall;
    private readonly VBoxContainer _sources = Widgets.Column(2);
    private readonly StrengthBar _bar = new();
    private readonly Label _verdict = Widgets.Label("", "Display");
    private readonly Label _numbers = Widgets.Wrapped("", "Small");
    private readonly VBoxContainer _breakdown = Widgets.Column(2);
    private string _signature = "";

    public RaidPrepDialog() : this(null!) { }

    public RaidPrepDialog(ColonySession session) : base(900)
    {
        _session = session;
        Visible = false;
        Body.AddChild(Title("PREPARE FOR THE RAID"));
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get("stat_threat", 24), 24, Palette.Danger));
        head.AddChild(_headline);
        head.AddChild(Widgets.Spacer());
        head.AddChild(_when);
        Body.AddChild(head);

        var columns = Widgets.Row(16);
        Body.AddChild(columns);

        var left = Widgets.Column(8);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsStretchRatio = 1.1f;
        columns.AddChild(left);
        left.AddChild(Widgets.SectionHeader("Defenders", "stat_pop"));
        left.AddChild(_defNote);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 250) };
        _defenders.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_defenders);
        left.AddChild(scroll);
        left.AddChild(Widgets.SectionHeader("Orders", "ui_defend"));
        _shelter = Toggle("stat_pregnant", "Shelter the children", () => _session.Execute(new ShelterChildren(!Raid()!.ChildrenSheltered)));
        _gates = Toggle("stat_lock", "Shut the gates", () => _session.Execute(new CloseGates(!Raid()!.GatesClosed)));
        left.AddChild(_shelter);
        left.AddChild(_gates);
        _recall = Widgets.IconButton("ui_expedition", "Recall the party", "", () => _session.Execute(new RecallExpedition()));
        left.AddChild(_recall);

        var right = Widgets.Column(8);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(right);
        right.AddChild(Widgets.SectionHeader("Warning", "stat_sight"));
        right.AddChild(_sources);
        right.AddChild(Widgets.SectionHeader("Odds", "ui_attack"));
        right.AddChild(_verdict);
        _bar.CustomMinimumSize = new Vector2(0, 26);
        right.AddChild(_bar);
        right.AddChild(_numbers);
        var bscroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 150) };
        _breakdown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bscroll.AddChild(_breakdown);
        right.AddChild(bscroll);

        var buttons = Widgets.Row(8);
        buttons.AddChild(Widgets.Spacer());
        var fight = Widgets.IconButton("ui_attack", "Fight it (coming soon)", "Fighting the raid on the tactical map comes in a later update.", () => { });
        fight.Disabled = true;
        buttons.AddChild(fight);
        var auto = Widgets.IconButton("stat_check", "Auto-resolve on arrival", "The defenders handle it when the raid arrives; you get a report.", AutoResolveOnArrival, Hotkeys.KeyName("pause_menu"), "Primary");
        auto.CustomMinimumSize = new Vector2(240, 40);
        buttons.AddChild(auto);
        Body.AddChild(buttons);
    }

    private static Button Toggle(string icon, string text, Action onPressed)
    {
        var b = Widgets.IconButton(icon, text, "", onPressed);
        b.ToggleMode = true;
        b.Alignment = HorizontalAlignment.Left;
        return b;
    }

    private Raid? Raid() => _session.Sim.State.Defense.Raid is { Stage: RaidStage.Warned } r ? r : null;

    protected override void OnOpened()
    {
        _signature = "";
        Refresh();
    }

    private void AutoResolveOnArrival()
    {
        if (Raid() is { Fight: true }) _session.Execute(new ChooseFight(false));
        Close();
    }

    public void Refresh()
    {
        if (!Visible) return;
        var sim = _session.Sim;
        if (sim.Raids is not { } raids || Raid() is not { } raid)
        {
            Close();  // it arrived (or was never there)
            return;
        }
        var signature = $"{sim.State.Tick}|{string.Join(",", raid.Defenders)}|{raid.ChildrenSheltered}|{raid.GatesClosed}|{sim.State.Expedition?.Id}|{sim.State.Defense.Guards.Count}";
        if (signature == _signature) return;
        _signature = signature;
        var threat = raids.Threat(raid.Kind);
        _headline.Text = $"{threat.Name} from the {RaidText.Side(raid.Approach)}, {RaidText.Estimate(raid)} strong";
        _when.Text = $"Arriving {RaidText.Arrival(sim, raid)}";
        RefreshDefenders(sim, raid);
        RefreshOrders(sim, raids, raid);
        RefreshOdds(sim, raids, raid);
    }

    // ---- defenders ----

    private static bool Able(Survivor s) => !s.Away && !s.IsChild && s.PatientAtBuildingId is null;

    private void RefreshDefenders(ColonySim sim, Raid raid)
    {
        var guards = sim.State.Defense.Guards.Keys.ToHashSet();
        var able = sim.State.Survivors.Where(Able).Select(s => s.Id).ToList();
        var chosen = raid.Defenders.Count == 0 ? able.ToHashSet() : raid.Defenders.Concat(guards).Where(able.Contains).ToHashSet();
        _defNote.Text = raid.Defenders.Count == 0
            ? $"Everyone able stands ({able.Count}). Untick someone to keep them out of it; guards always fight."
            : $"{chosen.Count} of {able.Count} stand. Guards always fight.";
        Widgets.Clear(_defenders);
        foreach (var s in sim.State.Survivors.Where(s => !s.IsInfant).OrderBy(s => !Able(s)).ThenBy(s => !guards.Contains(s.Id)).ThenBy(s => s.Name))
        {
            var isGuard = guards.Contains(s.Id);
            var on = chosen.Contains(s.Id);
            var why = s.IsChild ? "child" : s.Away ? "away" : s.PatientAtBuildingId is not null ? "patient" : null;
            var card = new PanelContainer { ThemeTypeVariation = on ? "CardSelected" : "Card" };
            var row = Widgets.Row(8);
            card.AddChild(row);
            var portrait = new Portrait(36) { Dim = why is not null };
            portrait.Set(PortraitStudio.For(GetTree(), s), PortraitStudio.Backdrop(s.Id));
            row.AddChild(portrait);
            var col = Widgets.Column(0);
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            col.AddChild(Widgets.Label(s.Name, "", why is null ? Palette.Text : Palette.TextDim));
            var combat = Math.Max(s.Get(Skill.Firearms), s.Get(Skill.Melee));
            col.AddChild(Widgets.Label(why ?? $"combat {combat:0} · health {s.Health:0}", "Small"));
            row.AddChild(col);
            if (isGuard) row.AddChild(Widgets.Chip(Icons.Get("stance_overwatch", 14), "guard", Palette.Amber, "On guard duty: always defends.", Palette.Amber));
            if (s.IsInjured) row.AddChild(Widgets.Chip(Icons.Get("stat_injured", 14), "hurt", Palette.Warn, "Injured: fights weaker.", Palette.Warn));
            var tick = Widgets.IconButton(on ? "ui_checkbox_on" : "ui_checkbox_off", "", why is not null ? $"Cannot defend: {why}."
                : isGuard ? "Guards always defend." : on ? "Keep out of the fight" : "Have them defend", () => ToggleDefender(sim, raid, s.Id, able, chosen), variation: "Flat");
            tick.Disabled = why is not null || isGuard;
            row.AddChild(tick);
            _defenders.AddChild(card);
        }
    }

    private void ToggleDefender(ColonySim sim, Raid raid, int id, List<int> able, HashSet<int> chosen)
    {
        var next = new HashSet<int>(chosen);
        if (!next.Remove(id)) next.Add(id);
        if (next.Count == 0)
        {
            _session.Notify("Someone has to stand: with nobody picked, everyone able defends.", true);
            return;
        }
        // Everyone picked is the default (empty list): new adults join it too.
        _session.Execute(new SetDefenders(able.All(next.Contains) ? [] : next.OrderBy(x => x).ToList()));
    }

    // ---- orders ----

    private void RefreshOrders(ColonySim sim, RaidData raids, Raid raid)
    {
        var children = sim.State.Survivors.Count(s => s.IsChild && !s.Away);
        _shelter.SetPressedNoSignal(raid.ChildrenSheltered);
        _shelter.Icon = Icons.Get(raid.ChildrenSheltered ? "ui_checkbox_on" : "ui_checkbox_off", 18);
        _shelter.Text = children == 0 ? "Shelter the children (none here)" : $"Shelter the children ({children})";
        _shelter.TooltipText = "In the shelter they are safe but frightened (a little mood). Out, each is a risk if they get in.";
        var gates = sim.State.Defense.Defenses.Count(d => raids.Defenses.GetValueOrDefault(d.DefId)?.Kind == DefenseKind.Gate);
        _gates.SetPressedNoSignal(raid.GatesClosed);
        _gates.Icon = Icons.Get(raid.GatesClosed ? "ui_checkbox_on" : "ui_checkbox_off", 18);
        _gates.Text = gates == 0 ? "Shut the gates (no gates built)" : $"Shut the gates ({gates})";
        _gates.Disabled = gates == 0;
        _gates.TooltipText = $"Shut gates soak like walls; an open gate on their side loses {Fmt.Percent(raids.Def.Strength.OpenGateLoss)} of the walls' soak. They reopen after.";

        var canRecall = DefenseHandlers.CanRecall(sim.State, raid);
        _recall.Visible = sim.State.Expedition is not null;
        _recall.Disabled = !canRecall;
        _recall.TooltipText = canRecall
            ? "Turn the party back: they are still on the road out and can be home before the raid."
            : sim.State.Expedition is null ? "No party is out." : "Too far out (or already in the zone) to make it back in time.";
    }

    // ---- odds ----

    private void RefreshOdds(ColonySim sim, RaidData raids, Raid raid)
    {
        Widgets.Clear(_sources);
        var lead = RaidWarning.LeadHours(sim, raids);
        foreach (var s in RaidWarning.Sources(sim, raids))
        {
            var row = Widgets.Row(6);
            var name = Widgets.Label(s.Name, "Small");
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(Widgets.Label($"+{s.Hours:0.#} h", "Number", Palette.Good));
            _sources.AddChild(row);
        }
        var total = Widgets.Label($"Lead time now: {lead} h (this raid was seen {raid.ArrivalTick - raid.WarnTick} h out). More watch, better estimates.", "Small", Palette.TextMuted);
        total.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sources.AddChild(total);

        var a = AutoResolve.Assess(sim, raids, raid);
        var share = a.Attack + a.Defense <= 0 ? 1 : a.Defense / (a.Attack + a.Defense);
        _bar.Set(share);
        _verdict.Text = a.Holds ? (a.Pressure < 0.35 ? "THEY SHOULD HOLD EASILY" : "THEY SHOULD HOLD") : a.Pressure > 0.65 ? "THEY WILL BREAK THROUGH" : "TOO CLOSE TO CALL";
        _verdict.AddThemeColorOverride("font_color", a.Holds ? (a.Pressure < 0.35 ? Palette.Good : Palette.Warn) : Palette.Danger);
        _numbers.Text = $"Defense {a.Defense:0} against attack {a.Attack:0} (of {a.RawAttack:0}: traps take {a.TrapDamage:0}, walls soak the rest down). " +
                        "Based on the watch's guess at their numbers; the real raid may be bigger or smaller.";
        Widgets.Clear(_breakdown);
        foreach (var line in a.Breakdown)
        {
            var row = Widgets.Row(6);
            var name = Widgets.Label(line.Line, "Small");
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.ClipText = true;
            row.AddChild(name);
            row.AddChild(Widgets.Label(line.Value < 0 ? $"−{-line.Value:0.#}" : $"+{line.Value:0.#}", "Number", line.Value < 0 ? Palette.Danger : Palette.Good));
            _breakdown.AddChild(row);
        }
    }
}

/// <summary>Defense (moss, left) against attack (red, right) as one bar, with a tick at the even split.</summary>
public partial class StrengthBar : Control
{
    private float _share = 0.5f;

    public void Set(double defenseShare)
    {
        _share = Mathf.Clamp((float)defenseShare, 0, 1);
        TooltipText = $"Defense {Fmt.Percent(_share)} · attack {Fmt.Percent(1 - _share)}. Past the middle mark they hold.";
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, Palette.Void);
        var split = Size.X * _share;
        DrawRect(new Rect2(1, 1, Math.Max(0, split - 1), Size.Y - 2), Palette.Good.Darkened(0.15f));
        DrawRect(new Rect2(split, 1, Math.Max(0, Size.X - split - 1), Size.Y - 2), Palette.Danger.Darkened(0.15f));
        var font = Fonts.Numbers;
        DrawString(font, new Vector2(8, Size.Y / 2 + 6), "DEFENSE", HorizontalAlignment.Left, -1, 14, Palette.Text);
        DrawString(font, new Vector2(Size.X - 70, Size.Y / 2 + 6), "ATTACK", HorizontalAlignment.Right, 62, 14, Palette.Text);
        DrawLine(new Vector2(Size.X / 2, -3), new Vector2(Size.X / 2, Size.Y + 3), Palette.Text, 2);
        DrawRect(rect, Palette.EdgeDark, false, 1);
    }
}
