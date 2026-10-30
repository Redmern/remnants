using Godot;
using Remnants.Core.Scavenge;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>Core's scavenge events as they replay, plus command feedback (errors in red).</summary>
public partial class ScavengeLog : PanelContainer
{
    private const int MaxLines = 150;
    private readonly RichTextLabel _text = UiKit.Rich(scroll: true);
    private int _lines;

    public ScavengeLog()
    {
        CustomMinimumSize = new Vector2(0, 120);
        var box = UiKit.Panel(this, 2);
        box.AddChild(UiKit.Heading("Log"));
        _text.ScrollFollowing = true;
        box.AddChild(_text);
    }

    public void Feedback(string message, bool error) =>
        Append(error ? $"[color=#{UiKit.Hex(UiKit.Bad)}]{message}[/color]" : $"[color=#aaa]{message}[/color]");

    public void Event(ScavengeEvent e)
    {
        var color = e.Type switch
        {
            ScavengeEventType.Downed or ScavengeEventType.Died or ScavengeEventType.BledOut or ScavengeEventType.LimbLost => UiKit.Hex(UiKit.Bad),
            ScavengeEventType.Attacked or ScavengeEventType.Injured => "e0a070",
            ScavengeEventType.Extracted or ScavengeEventType.Evacuated or ScavengeEventType.Recruited or ScavengeEventType.Healed
                or ScavengeEventType.Stabilized or ScavengeEventType.Revived or ScavengeEventType.PickedUp => UiKit.Hex(UiKit.Good),
            ScavengeEventType.RoundStarted => "e8d8a0",
            ScavengeEventType.Reinforced or ScavengeEventType.Flanked or ScavengeEventType.Suppressed => "70d8e8",
            ScavengeEventType.Ambushed => "ff6a50",
            ScavengeEventType.Overwatch or ScavengeEventType.Lookout or ScavengeEventType.Braced => "8ce6ff",
            ScavengeEventType.SensorTripped or ScavengeEventType.FlareLit or ScavengeEventType.SensorPlaced => "b4f08c",
            _ => "dddddd",
        };
        Append($"[color=#888]R{e.Round}[/color]  [color=#{color}]{e.Message}[/color]");
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
