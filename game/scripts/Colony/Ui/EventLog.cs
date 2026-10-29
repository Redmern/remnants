using System.Collections.Generic;
using Godot;
using Remnants.Core.Colony;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// Colony events from Core (starvation, departures, construction, ...), command feedback, and first-time tips:
/// Core's <see cref="Advice"/> for the next sensible step, each tip shown once per session.
/// </summary>
public partial class EventLog : PanelContainer
{
    private const int MaxLines = 120;

    private readonly ColonySession _session;
    private readonly RichTextLabel _text = Ui.Rich(scroll: true);
    private ColonyEvent? _lastSeen;
    private int _lines;
    private readonly HashSet<string> _tipsShown = new();

    public EventLog(ColonySession session)
    {
        _session = session;
        CustomMinimumSize = new Vector2(0, 130);
        var box = Ui.Panel(this, 2);
        box.AddChild(Ui.Heading("Log"));
        _text.ScrollFollowing = true;
        box.AddChild(_text);
        session.Feedback += (message, error) => Append(error ? $"[color=#{Ui.Hex(Ui.Bad)}]{message}[/color]" : $"[color=#aaa]{message}[/color]");
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
        for (var i = start; i < events.Count; i++) Append(Format(events[i]));
        if (events.Count > 0) _lastSeen = events[^1];
        if (Advice.Next(_session.Sim) is { } tip && _tipsShown.Add(tip.Id))
            Append($"[color=#7fc8e8]Tip:[/color] [color=#cfe6ef]{tip.Text}[/color]");
    }

    private string Format(ColonyEvent e)
    {
        var color = e.Type switch
        {
            ColonyEventType.Died or ColonyEventType.Left or ColonyEventType.Starving or ColonyEventType.Homeless
                or ColonyEventType.Grieving or ColonyEventType.BrokeUp => Ui.Hex(Ui.Bad),
            ColonyEventType.Constructed or ColonyEventType.Upgraded or ColonyEventType.Housed => Ui.Hex(Ui.Good),
            ColonyEventType.NewCouple or ColonyEventType.Pregnant or ColonyEventType.Born
                or ColonyEventType.BecameChild or ColonyEventType.BecameAdult => "e8c27a",
            _ => "dddddd",
        };
        var rules = _session.Sim.Data.Rules;
        return $"[color=#888]D{e.Tick / rules.TicksPerDay + 1} {e.Tick % rules.TicksPerDay:00}h[/color]  [color=#{color}]{e.Message}[/color]";
    }

    private void Append(string bbcode)
    {
        if (_lines >= MaxLines)
        {
            _text.RemoveParagraph(0);
            _lines--;
        }
        _text.AppendText((_lines > 0 ? "\n" : "") + bbcode);
        _lines++;
    }
}
