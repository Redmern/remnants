using System;
using Godot;
using Remnants.Core.Raids;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The raid warning under the top bar while a raid is seen coming (<see cref="RaidStage.Warned"/>): who (threat name),
/// from which side, the watch's size estimate, the countdown to arrival with the phase it lands in, what the colony has
/// chosen so far (defenders, children, gates), and Prepare, which opens the prep screen.
/// </summary>
public partial class RaidBanner : PanelContainer
{
    private readonly ColonySession _session;
    private readonly TextureRect _icon = Icons.Rect(Icons.Get("stat_threat", 26), 26, Palette.Danger);
    private readonly Label _title = Widgets.Label("", "Display", Palette.Danger);
    private readonly Label _detail = Widgets.Label("", "");
    private readonly Label _prep = Widgets.Label("", "Small");
    private readonly Label _countdown = Widgets.Label("", "Number", Palette.Warn);
    private int? _shownRaid;

    public event Action? PreparePressed;

    public RaidBanner(ColonySession session)
    {
        _session = session;
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        CustomMinimumSize = new Vector2(620, 0);
        AddThemeStyleboxOverride("panel", PanelArt.Box(new(Palette.Panel, Palette.EdgeDark, 0.05f, Rivets: true, Strip: Palette.Danger, StripHeight: 3), 14, 8));
        var row = Widgets.Row(12);
        AddChild(row);
        row.AddChild(_icon);
        var text = Widgets.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _title.AddThemeFontSizeOverride("font_size", 20);
        text.AddChild(_title);
        text.AddChild(_detail);
        text.AddChild(_prep);
        row.AddChild(text);
        var clock = Widgets.Column(-4);
        _countdown.AddThemeFontSizeOverride("font_size", 26);
        _countdown.HorizontalAlignment = HorizontalAlignment.Right;
        clock.AddChild(_countdown);
        var until = Widgets.Label("TO ARRIVAL", "Small");
        until.HorizontalAlignment = HorizontalAlignment.Right;
        clock.AddChild(until);
        row.AddChild(clock);
        var prepare = Widgets.IconButton("ui_defend", "Prepare", "Pick the defenders, shelter the children, shut the gates, see the odds", () => PreparePressed?.Invoke(), variation: "Primary");
        prepare.Icon = Icons.Get("ui_defend", 18, "stat_armor");
        prepare.CustomMinimumSize = new Vector2(130, 40);
        prepare.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(prepare);
        Visible = false;
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        if (sim.Raids is not { } raids || sim.State.Defense.Raid is not { Stage: RaidStage.Warned } raid)
        {
            Visible = false;
            _shownRaid = null;
            return;
        }
        var threat = raids.Threat(raid.Kind);
        _icon.Texture = Icons.Get(RaidText.ThreatIcon(raid.Kind), 26, "stat_threat");
        _title.Text = $"{threat.Name.ToUpperInvariant()} INCOMING";
        _detail.Text = $"From the {RaidText.Side(raid.Approach)} · {RaidText.Estimate(raid)} of them · arriving {RaidText.Arrival(sim, raid)}";
        var defenders = raid.Defenders.Count == 0 ? "everyone able defends" : $"{raid.Defenders.Count} picked to defend";
        _prep.Text = $"{defenders} · children {(raid.ChildrenSheltered ? "in the shelter" : "not sheltered")} · gates {(raid.GatesClosed ? "shut" : "open")}";
        var hours = Math.Max(0, raid.ArrivalTick - sim.State.Tick);
        _countdown.Text = $"{hours} h";
        _countdown.AddThemeColorOverride("font_color", hours <= 2 ? Palette.Danger : Palette.Warn);
        TooltipText = $"Seen {raid.ArrivalTick - raid.WarnTick} h out. The watch's guess at their numbers gets better with more lead time.";
        if (_shownRaid != raid.Id)
        {
            _shownRaid = raid.Id;
            Motion.Reveal(this, new Vector2(0, -8));
            Motion.Flash(this, Palette.Danger);
        }
    }
}
