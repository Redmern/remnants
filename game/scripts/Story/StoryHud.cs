using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Story;
using Remnants.Game.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game.Story;

/// <summary>
/// Story mode in the colony HUD: a strip under the top bar (the radio with its waiting calls, the journal, the next
/// objective), the radio inbox, the journal, mission briefings and debriefs, and the dialogue panel. Colony time pauses
/// while a dialogue is open. Reads Core (<see cref="Campaign"/>, <see cref="Dialogue"/>) and sends its commands; no
/// rules here. Hidden in Sandbox.
/// </summary>
public partial class StoryHud : Control
{
    private readonly ColonySession _session;
    private readonly Button _radio;
    private readonly Button _journal;
    private readonly Label _tracker = Widgets.Label("", "Small", Palette.TextMuted);
    private readonly HBoxContainer _strip = Widgets.Row(8);
    private int _speedBefore = 1;
    private bool _paused;
    private int _seenCalls;
    private long _seenDebrief = -1;

    public RadioPanel Radio { get; }
    public JournalPanel Journal { get; }
    public BriefingPanel Briefing { get; }
    public DebriefPanel Debrief { get; }
    public DialoguePanel Dialogue { get; }

    /// <summary>A briefing's Launch: open the expedition planner for this mission.</summary>
    public event Action<string>? PlanMission;

    /// <summary>A call came in (for a toast).</summary>
    public event Action<string>? CallArrived;

    public StoryHud() : this(null!) { }

    public StoryHud(ColonySession session)
    {
        _session = session;
        Theme = HudTheme.Shared;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var stripBox = new PanelContainer { Theme = HudTheme.Shared };
        stripBox.AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Strip: Palette.Amber), 10, 6));
        stripBox.SetAnchorsPreset(LayoutPreset.CenterTop);
        stripBox.GrowHorizontal = GrowDirection.Both;
        stripBox.OffsetTop = 64;
        AddChild(stripBox);
        stripBox.AddChild(_strip);
        _radio = Widgets.IconButton("ui_radio", "Radio", "Radio Mast: incoming calls", () => Radio!.Open());
        _journal = Widgets.IconButton("ui_journal", "Journal", "Missions and objectives", () => Journal!.Open());
        _strip.AddChild(_radio);
        _strip.AddChild(_journal);
        _strip.AddChild(Icons.Rect(Icons.Get("story_objective", 16), 16, Palette.Amber));
        _strip.AddChild(_tracker);

        Radio = new RadioPanel(this);
        Journal = new JournalPanel(this);
        Briefing = new BriefingPanel(this);
        Debrief = new DebriefPanel(this);
        Dialogue = new DialoguePanel(Line, () => Send(new AdvanceDialogue()),
            i => Send(new ChooseDialogue(Remnants.Core.Story.Dialogue.View(Sim)!.NodeId, i)),
            () => Sim.State.Campaign?.Log.Select(l => (l.Speaker, l.Text, l.Choice)) ?? []);
        foreach (var o in new Control[] { Radio, Journal, Briefing, Debrief, Dialogue })
        {
            o.Visible = false;
            AddChild(o);
        }
    }

    public ColonySim Sim => _session.Sim;
    public ColonySession Session => _session;

    public void Send(IColonyCommand command)
    {
        _session.Execute(command);
        Refresh();
    }

    public void Refresh()
    {
        var campaign = Sim.State.Campaign;
        Visible = campaign is not null && Sim.Story is not null;
        if (!Visible) return;
        var waiting = Campaign.Waiting(Sim).ToList();
        _radio.Text = waiting.Count > 0 ? $"Radio · {waiting.Count} calling" : "Radio";
        _radio.Modulate = waiting.Count > 0 ? new Color(1.25f, 1.1f, 0.8f) : Colors.White;
        if (waiting.Count > _seenCalls && Sim.Story!.Dialogues.GetValueOrDefault(waiting[^1].DialogueId) is { } d)
            CallArrived?.Invoke(Campaign.CallerName(Sim, d));
        _seenCalls = waiting.Count;
        _tracker.Text = Tracker();

        Dialogue.Refresh();
        // Colony time stops while someone is talking.
        if (Dialogue.Visible && !_paused)
        {
            _paused = true;
            _speedBefore = _session.Speed;
            _session.SetSpeed(0);
        }
        else if (!Dialogue.Visible && _paused)
        {
            _paused = false;
            if (_session.Speed == 0) _session.SetSpeed(Math.Max(1, _speedBefore));
        }
        if (campaign!.LastDebrief is { } debrief && debrief.Tick != _seenDebrief && !Dialogue.Visible)
        {
            _seenDebrief = debrief.Tick;
            if (debrief.Tick >= Sim.State.Tick - 1) Debrief.Show(debrief);
        }
        if (Radio.Visible) Radio.Refresh();
        if (Journal.Visible) Journal.Refresh();
    }

    /// <summary>Forget what was shown (a load replaced the colony).</summary>
    public void Reset()
    {
        _seenCalls = 0;
        _seenDebrief = Sim.State.Campaign?.LastDebrief?.Tick ?? -1;
    }

    /// <summary>The first open main mission and its next step, in a line.</summary>
    private string Tracker()
    {
        foreach (var (def, p) in Campaign.Visible(Sim).Where(m => m.Def.Main && m.Progress.Status is MissionStatus.Active or MissionStatus.Available))
        {
            if (def.Type == MissionType.Colony)
            {
                var next = def.ColonyObjectives.FirstOrDefault(o => !p.Objectives.Contains(o.Id));
                return $"{def.Name}: {next?.Text ?? "done"}  ({p.Objectives.Count}/{def.ColonyObjectives.Count})";
            }
            return $"{def.Name}: ready — open the journal for the briefing";
        }
        return "No open mission. Keep the colony going; the radio will call.";
    }

    /// <summary>The open dialogue as the panel shows it, with the speaker's portrait.</summary>
    private DialogueLine? Line()
    {
        if (Remnants.Core.Story.Dialogue.View(Sim) is not { } v) return null;
        var radio = v.Mode == DialogueMode.Radio && v.Speaker is not ("player" or "pell" or "narration");
        Texture2D? portrait = StoryArt.Portrait(v.Speaker, v.Mood, radio, Sim);
        if (portrait is null && v.SurvivorId is { } id && Sim.State.FindSurvivor(id) is { } s) portrait = PortraitStudio.For(GetTree(), s);
        var callSign = Sim.Story!.Characters.GetValueOrDefault(v.Speaker)?.CallSign;
        return new DialogueLine(v.Speaker, v.SpeakerName, v.Text, v.Mood, radio, portrait, v.Choices, v.Title, callSign);
    }

    public void OpenBriefing(string missionId)
    {
        Journal.Close();
        Briefing.Show(missionId);
    }

    public void Launch(string missionId)
    {
        Briefing.Close();
        PlanMission?.Invoke(missionId);
    }

    /// <summary>Esc: close the topmost story panel. Returns whether one was open (a dialogue cannot be closed, only skipped).</summary>
    public bool Back()
    {
        foreach (var o in new Overlay[] { Debrief, Briefing, Journal, Radio })
            if (o.Visible) { o.Close(); return true; }
        return Dialogue.Visible;
    }
}

/// <summary>The Radio Mast's inbox: calls waiting (take now or later), and calls already taken (read again in the log).</summary>
public partial class RadioPanel : Overlay
{
    private readonly StoryHud _hud;
    private readonly VBoxContainer _list = Widgets.Column(6);

    public RadioPanel() : this(null!) { }

    public RadioPanel(StoryHud hud) : base(520)
    {
        _hud = hud;
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get("ui_radio", 22), 22, Palette.Amber));
        head.AddChild(Widgets.Label("Radio Mast", "Title"));
        head.AddChild(Widgets.Spacer());
        head.AddChild(Widgets.IconButton("ui_close", "", "Close (Esc)", Close, variation: "Flat"));
        Body.AddChild(head);
        Body.AddChild(Widgets.Wrapped("Calls wait until you take them. Colony time stops while you listen.", "Muted"));
        Body.AddChild(_list);
    }

    protected override void OnOpened() => Refresh();

    public void Refresh()
    {
        var sim = _hud.Sim;
        Widgets.Clear(_list);
        var waiting = Campaign.Waiting(sim).ToList();
        _list.AddChild(Widgets.SectionHeader(waiting.Count > 0 ? $"Calling ({waiting.Count})" : "Nobody calling", "story_radio"));
        foreach (var call in waiting)
        {
            var d = sim.Story!.Dialogues[call.DialogueId];
            var row = Widgets.Row(8);
            row.AddChild(Icons.Rect(Icons.Get(d.Mode == DialogueMode.Radio ? "story_radio" : "ui_dialogue", 18), 18, Palette.Amber));
            row.AddChild(Widgets.Label(Campaign.CallerName(sim, d), "Heading"));
            row.AddChild(Widgets.Label(d.Title, "Muted"));
            row.AddChild(Widgets.Spacer());
            row.AddChild(Widgets.IconButton("ui_play", "Take the call", "Open the dialogue", () => { Close(); _hud.Send(new TakeCall(call.Id)); }, variation: "Primary"));
            _list.AddChild(row);
        }
        var heard = sim.State.Campaign!.Inbox.Where(c => c.Taken).OrderByDescending(c => c.Tick).Take(8).ToList();
        if (heard.Count == 0) return;
        _list.AddChild(Widgets.SectionHeader("Heard", "ui_log"));
        foreach (var call in heard)
        {
            var d = sim.Story!.Dialogues[call.DialogueId];
            var day = call.Tick / sim.Data.Rules.TicksPerDay + 1;
            _list.AddChild(Widgets.Label($"Day {day} · {Campaign.CallerName(sim, d)} · {d.Title}", "Small", Palette.TextMuted));
        }
    }
}

/// <summary>The journal: every open and finished mission and colony task with its objectives; field missions open their briefing.</summary>
public partial class JournalPanel : Overlay
{
    private readonly StoryHud _hud;
    private readonly VBoxContainer _list = Widgets.Column(6);

    public JournalPanel() : this(null!) { }

    public JournalPanel(StoryHud hud) : base(620)
    {
        _hud = hud;
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get("ui_journal", 22), 22, Palette.Amber));
        head.AddChild(Widgets.Label("Journal", "Title"));
        head.AddChild(Widgets.Spacer());
        head.AddChild(Widgets.IconButton("ui_close", "", "Close (Esc)", Close, variation: "Flat"));
        Body.AddChild(head);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 460), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_list);
        Body.AddChild(scroll);
    }

    protected override void OnOpened() => Refresh();

    public void Refresh()
    {
        var sim = _hud.Sim;
        Widgets.Clear(_list);
        var visible = Campaign.Visible(sim).ToList();
        foreach (var (title, group) in new[]
                 {
                     ("Story", visible.Where(m => m.Def.Main)), ("Colony tasks", visible.Where(m => m.Def.Task)),
                     ("Side missions", visible.Where(m => !m.Def.Main && !m.Def.Task)),
                 })
        {
            var items = group.ToList();
            if (items.Count == 0) continue;
            _list.AddChild(Widgets.SectionHeader(title, title == "Story" ? "story_main" : "story_side"));
            foreach (var (def, p) in items.OrderBy(m => m.Progress.Status is MissionStatus.Done ? 1 : 0))
                _list.AddChild(Card(sim, def, p));
        }
    }

    private Control Card(ColonySim sim, MissionDef def, MissionProgress p)
    {
        var card = new PanelContainer { ThemeTypeVariation = "Card" };
        var col = Widgets.Column(4);
        card.AddChild(col);
        var head = Widgets.Row(8);
        var (icon, color, status) = p.Status switch
        {
            MissionStatus.Done when p.Outcome == "failed" => ("story_failed", Palette.Danger, "Failed"),
            MissionStatus.Done => ("story_done", Palette.Good, "Done"),
            MissionStatus.Failed => ("story_failed", Palette.Danger, $"Failed · back in {Math.Max(0, (p.RetryTick ?? 0) - sim.State.Tick) / sim.Data.Rules.TicksPerDay + 1} days"),
            MissionStatus.Active when def.Type == MissionType.Field => ("ui_expedition", Palette.Amber, "Under way"),
            MissionStatus.Active => ("story_objective", Palette.Amber, "In progress"),
            _ => (def.Main ? "story_main" : "story_side", Palette.Amber, "Open"),
        };
        head.AddChild(Icons.Rect(Icons.Get(icon, 18), 18, color));
        head.AddChild(Widgets.Label(def.Name, "Heading", p.Status == MissionStatus.Done ? Palette.TextMuted : Palette.Text));
        if (def.Main) head.AddChild(Widgets.Label($"Act {def.Act}", "Small", Palette.TextDim));
        head.AddChild(Widgets.Spacer());
        head.AddChild(Widgets.Chip(null, status, color));
        if (def.Type == MissionType.Field && p.Status == MissionStatus.Available)
            head.AddChild(Widgets.IconButton("ui_info", "Briefing", "Read the briefing and launch", () => _hud.OpenBriefing(def.Id), variation: "Primary"));
        col.AddChild(head);
        if (p.Status == MissionStatus.Done) return card;
        col.AddChild(Widgets.Wrapped(def.Summary, "Small", Palette.TextMuted));
        foreach (var o in def.ColonyObjectives)
        {
            var done = p.Objectives.Contains(o.Id);
            var row = Widgets.Row(6);
            row.AddChild(Icons.Rect(Icons.Get(done ? "ui_checkbox_on" : "ui_checkbox_off", 16), 16, done ? Palette.Good : Palette.TextMuted));
            row.AddChild(Widgets.Label(o.Text, "Small", done ? Palette.TextMuted : Palette.Text));
            col.AddChild(row);
        }
        return card;
    }
}

/// <summary>A field mission's card: threat, where, what to do, what it pays, what it needs; Launch opens the planner.</summary>
public partial class BriefingPanel : Overlay
{
    private readonly StoryHud _hud;
    private readonly VBoxContainer _card = Widgets.Column(8);

    public BriefingPanel() : this(null!) { }

    public BriefingPanel(StoryHud hud) : base(560)
    {
        _hud = hud;
        Body.AddChild(_card);
    }

    public void Show(string missionId)
    {
        var sim = _hud.Sim;
        if (sim.Story?.Mission(missionId) is not { } def) return;
        Widgets.Clear(_card);
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get(def.Main ? "story_main" : "story_side", 22), 22, Palette.Amber));
        head.AddChild(Widgets.Label(def.Name, "Title"));
        head.AddChild(Widgets.Spacer());
        head.AddChild(Widgets.IconButton("ui_close", "", "Close (Esc)", Close, variation: "Flat"));
        _card.AddChild(head);
        var zone = def.Zone is { } z && sim.Scavenge!.Zones.TryGetValue(z, out var zd) ? zd.Name : def.Zone;
        var where = def.Perimeter ? "the colony perimeter" : def.HandmadeId is not null ? $"{zone} (a fixed place)" : zone;
        var chips = new HFlowContainer();
        chips.AddChild(Widgets.Chip(Icons.Get("perk_combat", 14), $"Threat {def.Threat}", Palette.Warn));
        chips.AddChild(Widgets.Chip(Icons.Get("ui_world_map", 14), where ?? ""));
        chips.AddChild(Widgets.Chip(null, $"Act {def.Act}"));
        _card.AddChild(chips);
        _card.AddChild(Widgets.Wrapped(def.Summary));
        _card.AddChild(Widgets.SectionHeader("Objectives", "story_objective"));
        foreach (var o in def.Field!.Objectives.Where(o => !o.Hidden))
            _card.AddChild(Widgets.Label($"·  {o.Text}{(o.Optional ? " (optional)" : "")}{(o.FailsMission ? "  — must not fail" : "")}", "Small"));
        _card.AddChild(Widgets.SectionHeader("Rewards", "item_blueprint"));
        var rewards = def.Rewards.Where(e => e.When is null && e.Kind is EffectKind.Schematic or EffectKind.Item or EffectKind.Resource or EffectKind.ResearchData)
            .Where(e => e.Value > 0)
            .Select(e => e.Kind switch
            {
                EffectKind.Schematic => $"Schematic: {Campaign.SchematicName(e.Id)}",
                EffectKind.Item => $"{e.Value} × {sim.Scavenge!.Items.GetValueOrDefault(e.Id)?.Name ?? e.Id}",
                EffectKind.ResearchData => $"{e.Value} Research Data",
                _ => $"{e.Value} {sim.Data.Resources[e.Id].Name}",
            });
        _card.AddChild(Widgets.Wrapped(string.Join("  ·  ", rewards), "Small", Palette.Good));
        if (def.Teaches.Length > 0) _card.AddChild(Widgets.Wrapped($"Teaches: {def.Teaches}", "Small", Palette.TextMuted));
        if (def.MaxParty is { } party) _card.AddChild(Widgets.Label($"Up to {party} survivors may go.", "Small", Palette.TextMuted));
        var launch = Widgets.IconButton("ui_expedition", "Plan the party", "Pick who goes and what they carry", () => _hud.Launch(def.Id), variation: "Primary");
        launch.CustomMinimumSize = new Vector2(0, 40);
        launch.Disabled = Campaign.CannotLaunch(sim, def.Id) is not null || sim.State.Expedition is not null;
        if (sim.State.Expedition is not null) launch.TooltipText = "A party is already out.";
        _card.AddChild(launch);
        Open();
    }
}

/// <summary>What a mission came to: done or failed, the objectives, and what it paid (schematics first).</summary>
public partial class DebriefPanel : Overlay
{
    private readonly StoryHud _hud;
    private readonly VBoxContainer _card = Widgets.Column(8);

    public DebriefPanel() : this(null!) { }

    public DebriefPanel(StoryHud hud) : base(520)
    {
        _hud = hud;
        Body.AddChild(_card);
    }

    public void Show(MissionDebrief debrief)
    {
        var sim = _hud.Sim;
        var def = sim.Story?.Mission(debrief.MissionId);
        Widgets.Clear(_card);
        var (word, color, icon) = debrief.Outcome switch
        {
            "done" => ("Mission complete", Palette.Good, "story_done"),
            "failed" => ("Mission failed", Palette.Danger, "story_failed"),
            _ => ("Not done yet", Palette.Warn, "story_objective"),
        };
        var head = Widgets.Row(8);
        head.AddChild(Icons.Rect(Icons.Get(icon, 22), 22, color));
        head.AddChild(Widgets.Label($"{word}: {def?.Name ?? debrief.MissionId}", "Title", color));
        _card.AddChild(head);
        foreach (var line in debrief.Objectives) _card.AddChild(Widgets.Label(line, "Small"));
        if (debrief.Rewards.Count > 0)
        {
            _card.AddChild(Widgets.SectionHeader("Brought home", "item_blueprint"));
            foreach (var line in debrief.Rewards)
                _card.AddChild(Widgets.Label(line, "Small", line.StartsWith("Schematic", StringComparison.Ordinal) ? Palette.Amber : Palette.Text));
        }
        if (debrief.Outcome == "not done yet")
            _card.AddChild(Widgets.Wrapped("Leaving is not a failure: the mission stays on the map.", "Small", Palette.TextMuted));
        _card.AddChild(Widgets.TextButton("Continue", Close, variation: "Primary"));
        Open();
    }
}
