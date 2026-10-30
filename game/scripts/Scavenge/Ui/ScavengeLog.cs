using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Hud;
using UiKit = Remnants.Game.Colony.Ui.Ui;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// Core's scavenge events as they replay, plus command feedback (errors in red): round, an icon and a colour by kind.
/// Network events (reinforcements, flanking, suppression) are the only cyan lines.
/// </summary>
public partial class ScavengeLog : PanelContainer
{
    private const int MaxLines = 150;
    private readonly RichTextLabel _text = UiKit.Rich(scroll: true);
    private int _lines;

    public ScavengeLog()
    {
        CustomMinimumSize = new Vector2(0, 120);
        var box = UiKit.Panel(this, 4);
        box.AddChild(Widgets.SectionHeader("Log", "ui_log"));
        _text.ScrollFollowing = true;
        box.AddChild(_text);
    }

    public void Feedback(string message, bool error) =>
        Append("", error ? "stat_warning" : "ui_info", error ? Palette.Danger : Palette.TextMuted, message);

    public void Event(ScavengeEvent e)
    {
        var (icon, color) = e.Type switch
        {
            ScavengeEventType.Downed or ScavengeEventType.BledOut => ("stat_injured", Palette.Danger),
            ScavengeEventType.Died => ("stat_dead", Palette.Danger),
            ScavengeEventType.LimbLost => ("body_missing", Palette.Danger),
            ScavengeEventType.Attacked => ("ui_attack", Palette.Warn),
            ScavengeEventType.Injured => ("stat_injured", Palette.Warn),
            ScavengeEventType.Missed => ("ui_attack", Palette.TextMuted),
            ScavengeEventType.Moved => ("ui_move", Palette.TextMuted),
            ScavengeEventType.Extracted or ScavengeEventType.Evacuated => ("ui_extract", Palette.Good),
            ScavengeEventType.Recruited => ("stat_recruit", Palette.Good),
            ScavengeEventType.Healed or ScavengeEventType.Stabilized or ScavengeEventType.Revived => ("stat_health", Palette.Good),
            ScavengeEventType.PickedUp => ("stat_loot", Palette.Good),
            ScavengeEventType.RoundStarted => ("stat_time", Palette.Amber),
            ScavengeEventType.Reinforced or ScavengeEventType.Flanked or ScavengeEventType.Suppressed => ("stat_linked", Palette.Network),
            ScavengeEventType.Ambushed => ("stat_warning", Palette.Danger),
            ScavengeEventType.Alarm or ScavengeEventType.OverrideEnded => ("stat_linked", Palette.Network),
            ScavengeEventType.HackFailed => ("stat_warning", Palette.Danger),
            ScavengeEventType.LimbLocked => ("body_prosthetic", Palette.Network),
            ScavengeEventType.Hacked or ScavengeEventType.HackProgress or ScavengeEventType.Overridden => ("skill_hacking", Palette.Amber),
            ScavengeEventType.DroneDeployed or ScavengeEventType.DroneFlown or ScavengeEventType.DroneRecalled or ScavengeEventType.DroneLanded
                or ScavengeEventType.Marked or ScavengeEventType.PayloadDropped => ("skill_dronepiloting", Palette.Amber),
            ScavengeEventType.DeviceDisabled or ScavengeEventType.RelayDisrupted => ("ui_attack", Palette.Good),
            ScavengeEventType.WeatherChanged => ("weather_overcast", Palette.TextMuted),
            ScavengeEventType.Thunder => ("weather_thunderstorm", Palette.TextMuted),
            ScavengeEventType.Lightning => ("weather_lightning", Palette.Warn),
            ScavengeEventType.Overwatch => ("stance_overwatch", Palette.Amber),
            ScavengeEventType.Braced => ("stance_brace", Palette.Amber),
            ScavengeEventType.Lookout => ("stance_scan", Palette.Amber),
            ScavengeEventType.SensorTripped or ScavengeEventType.FlareLit or ScavengeEventType.SensorPlaced => ("stat_sight", Palette.Good),
            ScavengeEventType.AllianceProposed or ScavengeEventType.AllianceAccepted => ("stat_ally", Palette.Ally),
            ScavengeEventType.AllianceDeclined or ScavengeEventType.AllianceBreakWarning => ("stat_ally", Palette.Warn),
            ScavengeEventType.AllianceBroken => ("stat_ally", Palette.Danger),
            ScavengeEventType.Pinged => ("ui_ping", Palette.Ally),
            ScavengeEventType.ItemGiven => ("stat_loot", Palette.Ally),
            ScavengeEventType.PickedUpBody or ScavengeEventType.PutDown => ("stat_injured", Palette.Ally),
            _ => ("ui_info", Palette.Text),
        };
        Append($"R{e.Round}", icon, color, e.Message);
    }

    private void Append(string round, string icon, Color color, string message)
    {
        if (_lines >= MaxLines)
        {
            _text.RemoveParagraph(0);
            _lines--;
        }
        if (_lines > 0) _text.Newline();
        _text.PushColor(Palette.TextDim);
        _text.PushFont(Fonts.Numbers, 13);
        _text.AddText(round.PadRight(4));
        _text.Pop();
        _text.Pop();
        _text.AddImage(Icons.Get(icon, 14), 14, 14, color);
        _text.AddText(" ");
        _text.PushColor(color == Palette.TextMuted ? Palette.TextMuted : color.Lerp(Palette.Text, 0.3f));
        _text.AddText(message);
        _text.Pop();
        _lines++;
    }
}
