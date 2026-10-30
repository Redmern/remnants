using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The colony feed: Core events, command feedback and first-time tips (<see cref="Advice"/>, each once per session),
/// each line with time, an icon and a colour by kind, filterable (All / People / Colony / Alerts / Tips) and
/// collapsible. Important events (a death, a birth, someone leaving, a party home, a level up, a perk learned, a class
/// finished) are also raised as
/// <see cref="Important"/> for toasts.
/// </summary>
public partial class EventLog : PanelContainer
{
    private const int MaxEntries = 160;

    public enum Kind { People, Colony, Alert, Tip }

    private sealed record Entry(string Time, string Icon, Color Color, string Text, Kind Kind);

    private readonly ColonySession _session;
    private readonly RichTextLabel _text = Ui.Rich(scroll: true);
    private readonly List<Entry> _entries = new();
    private readonly HashSet<string> _tipsShown = new();
    private readonly Control _body;
    private readonly Button _collapse;
    private Kind? _filter;
    private ColonyEvent? _lastSeen;

    /// <summary>Title, text, icon and accent for an event worth a toast.</summary>
    public event Action<string, string, string, Color>? Important;

    public EventLog(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(360, 0);
        var box = Ui.Panel(this, 6);

        var header = Widgets.Row(4);
        header.AddChild(Icons.Rect(Icons.Get("ui_log", 16), 16, Palette.Rust));
        var title = Widgets.Label("LOG", "Heading");
        header.AddChild(title);
        header.AddChild(Widgets.Spacer());
        var group = new ButtonGroup();
        foreach (var (label, icon, kind) in new (string, string, Kind?)[]
                 {
                     ("All", "ui_filter", null), ("People", "stat_pop", Kind.People), ("Colony", "ui_build", Kind.Colony),
                     ("Alerts", "stat_warning", Kind.Alert), ("Tips", "ui_info", Kind.Tip),
                 })
        {
            var button = Widgets.IconButton(icon, "", $"Show {label.ToLowerInvariant()}", () => SetFilter(kind), variation: "Tab");
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = kind is null;
            button.CustomMinimumSize = new Vector2(28, 26);
            header.AddChild(button);
        }
        _collapse = Widgets.IconButton("ui_chevron_down", "", "Collapse the log", ToggleCollapsed, Hotkeys.KeyName("open_log"), "Flat");
        header.AddChild(_collapse);
        box.AddChild(header);

        _text.ScrollFollowing = true;
        _text.CustomMinimumSize = new Vector2(0, 132);
        _body = _text;
        box.AddChild(_text);
        session.Feedback += (message, error) => Add("", error ? "stat_warning" : "ui_info", error ? Palette.Danger : Palette.TextMuted, message, error ? Kind.Alert : Kind.Colony);
    }

    public void ToggleCollapsed()
    {
        _body.Visible = !_body.Visible;
        _collapse.Icon = Icons.Get(_body.Visible ? "ui_chevron_down" : "ui_chevron_up", 18);
        _collapse.TooltipText = Widgets.Tip(_body.Visible ? "Collapse the log" : "Expand the log", Hotkeys.KeyName("open_log"));
    }

    private void SetFilter(Kind? kind)
    {
        _filter = kind;
        Render();
    }

    /// <summary>After a load: don't replay the loaded save's history as new.</summary>
    public void Reset()
    {
        var events = _session.Sim.State.Events;
        _lastSeen = events.Count > 0 ? events[^1] : null;
    }

    public void Refresh()
    {
        var events = _session.Sim.State.Events;
        // Core caps the list, so find where we left off by identity rather than index.
        var start = _lastSeen is null ? 0 : events.LastIndexOf(_lastSeen) + 1;
        var rules = _session.Sim.Data.Rules;
        for (var i = start; i < events.Count; i++)
        {
            var e = events[i];
            var (icon, color, kind) = Style(e.Type);
            Add($"D{e.Tick / rules.TicksPerDay + 1} {e.Tick % rules.TicksPerDay:00}h", icon, color, e.Message, kind);
            if (ToastFor(e.Type) is { } toast) Important?.Invoke(toast.Title, e.Message, icon, color);
        }
        if (events.Count > 0) _lastSeen = events[^1];
        if (Advice.Next(_session.Sim) is { } tip && _tipsShown.Add(tip.Id))
            Add("Tip", "ui_info", Palette.Amber, tip.Text, Kind.Tip);
    }

    private static (string Title, bool _)? ToastFor(ColonyEventType type) => type switch
    {
        ColonyEventType.Died => ("Death", true),
        ColonyEventType.Born => ("A birth", true),
        ColonyEventType.Left => ("Someone left", true),
        ColonyEventType.ExpeditionReturned => ("The party is home", true),
        ColonyEventType.ExpeditionArrived => ("The party has arrived", true),
        ColonyEventType.LimbLost => ("A limb is lost", true),
        ColonyEventType.Recruited => ("A new face", true),
        ColonyEventType.LevelUp => ("Level up", true),
        ColonyEventType.PerkLearned => ("Perk learned", true),
        ColonyEventType.StudyFinished => ("Class finished", true),
        _ => null,
    };

    private static (string Icon, Color Color, Kind Kind) Style(ColonyEventType type) => type switch
    {
        ColonyEventType.Died => ("stat_dead", Palette.Danger, Kind.Alert),
        ColonyEventType.Left => ("ui_quit", Palette.Danger, Kind.Alert),
        ColonyEventType.Starving => ("stat_starving", Palette.Danger, Kind.Alert),
        ColonyEventType.Homeless => ("stat_homeless", Palette.Danger, Kind.Alert),
        ColonyEventType.InjuryWorsened or ColonyEventType.LimbLost => ("stat_injured", Palette.Danger, Kind.Alert),
        ColonyEventType.Grieving or ColonyEventType.BrokeUp or ColonyEventType.Miscarried => ("stat_mood", Palette.Warn, Kind.People),
        ColonyEventType.Constructed or ColonyEventType.Upgraded => ("ui_build", Palette.Good, Kind.Colony),
        ColonyEventType.Housed => ("stat_beds", Palette.Good, Kind.People),
        ColonyEventType.NewCouple or ColonyEventType.Pregnant => ("stat_health", Palette.Amber, Kind.People),
        ColonyEventType.Born or ColonyEventType.BecameChild or ColonyEventType.BecameAdult => ("stat_pregnant", Palette.Amber, Kind.People),
        ColonyEventType.Recruited => ("stat_recruit", Palette.Good, Kind.People),
        ColonyEventType.ExpeditionLaunched or ColonyEventType.ExpeditionArrived or ColonyEventType.ExpeditionHeadingHome
            or ColonyEventType.ExpeditionReturned => ("ui_expedition", Palette.Amber, Kind.Colony),
        ColonyEventType.ZoneRevealed => ("ui_world_map", Palette.Amber, Kind.Colony),
        ColonyEventType.Admitted or ColonyEventType.Discharged or ColonyEventType.Healed or ColonyEventType.ProstheticFitted
            => ("job_patient", Palette.Good, Kind.People),
        ColonyEventType.Crafted => ("skill_crafting", Palette.Text, Kind.Colony),
        ColonyEventType.LevelUp => ("stat_xp", Palette.Amber, Kind.People),
        ColonyEventType.PerkLearned => ("stat_perk", Palette.Good, Kind.People),
        ColonyEventType.StudyStarted or ColonyEventType.StudyFinished => ("ui_study", Palette.Amber, Kind.People),
        ColonyEventType.StudyEnded => ("ui_study", Palette.TextMuted, Kind.People),
        _ => ("ui_info", Palette.TextMuted, Kind.Colony),
    };

    private void Add(string time, string icon, Color color, string text, Kind kind)
    {
        var entry = new Entry(time, icon, color, text, kind);
        _entries.Add(entry);
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(0);
            Render();
            return;
        }
        if (_filter is null || _filter == kind) Append(entry, _text.GetParagraphCount() > 0 && _text.GetParsedText().Length > 0);
    }

    private void Render()
    {
        _text.Clear();
        var first = true;
        foreach (var e in _entries.Where(e => _filter is null || e.Kind == _filter))
        {
            Append(e, !first);
            first = false;
        }
    }

    private void Append(Entry e, bool newline)
    {
        if (newline) _text.Newline();
        _text.PushColor(Palette.TextDim);
        _text.PushFont(Fonts.Numbers, 13);
        _text.AddText(e.Time.PadRight(7));
        _text.Pop();
        _text.Pop();
        _text.AddImage(Icons.Get(e.Icon, 14), 14, 14, e.Color);
        _text.AddText(" ");
        _text.PushColor(e.Kind == Kind.Colony && e.Color == Palette.TextMuted ? Palette.TextMuted : e.Kind == Kind.Tip ? Palette.Text : e.Color.Lerp(Palette.Text, 0.35f));
        _text.AddText(e.Text);
        _text.Pop();
    }
}
