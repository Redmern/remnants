using System;
using Godot;
using Remnants.Core.Raids;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony.Ui;

/// <summary>
/// The raid warning under the top bar while a raid is seen coming (<see cref="RaidStage.Warned"/>): who (threat name),
/// from which side, the watch's size estimate, the countdown to arrival with the phase it lands in, what the colony has
/// chosen so far (defenders, children, gates, fight or auto-resolve), and Prepare, which opens the prep screen. When a fought
/// raid is at the fence (<see cref="RaidStage.Battle"/>) it says so and the button becomes Fight! (or Resume after a Leave).
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

    /// <summary>The raid is at the fence and fought: open (or resume) the battle.</summary>
    public event Action? FightPressed;

    private readonly Button _prepare;
    private readonly Label _until = Widgets.Label("TO ARRIVAL", "Small");

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
        _until.HorizontalAlignment = HorizontalAlignment.Right;
        clock.AddChild(_until);
        row.AddChild(clock);
        _prepare = Widgets.IconButton("ui_defend", "Prepare", "", () =>
        {
            if (_session.Sim.State.Defense.Raid is { Stage: RaidStage.Battle }) FightPressed?.Invoke();
            else PreparePressed?.Invoke();
        }, variation: "Primary");
        _prepare.CustomMinimumSize = new Vector2(130, 40);
        _prepare.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(_prepare);
        Visible = false;
    }

    public void Refresh()
    {
        var sim = _session.Sim;
        if (sim.Raids is not { } raids || sim.State.Defense.Raid is not { Stage: RaidStage.Warned or RaidStage.Battle } raid)
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
        _prep.Text += raid.Fight ? " · plan: fight it on the map" : " · plan: auto-resolve";
        var battle = raid.Stage == RaidStage.Battle;
        if (battle)
        {
            // At the fence: the countdown is how long until they settle it themselves (unless the fight is already on).
            _title.Text = $"THEY'RE AT THE FENCE — {threat.Name.ToUpperInvariant()}";
            _detail.Text = $"From the {RaidText.Side(raid.Approach)} · {RaidText.Estimate(raid)} of them · the defenders are waiting for you";
            var left = Math.Max(0, raid.ArrivalTick + raids.Def.Battle.MaxWaitHours - sim.State.Tick);
            _countdown.Text = raid.Battle is null ? $"{left} h" : "ON";
            _until.Text = raid.Battle is null ? "UNTIL IT SETTLES" : "FIGHT ON HOLD";
            _countdown.AddThemeColorOverride("font_color", Palette.Danger);
        }
        else
        {
            var hours = Math.Max(0, raid.ArrivalTick - sim.State.Tick);
            _countdown.Text = $"{hours} h";
            _until.Text = "TO ARRIVAL";
            _countdown.AddThemeColorOverride("font_color", hours <= 2 ? Palette.Danger : Palette.Warn);
        }
        _prepare.Text = battle ? (raid.Battle is null ? "Fight!" : "Resume") : "Prepare";
        _prepare.Icon = Icons.Get(battle ? "ui_attack" : "ui_defend", 18, "stat_armor");
        _prepare.TooltipText = battle
            ? raid.Battle is null ? "Open the fight on a map of the colony." : "Back into the fight where you left it."
            : "Pick the defenders, shelter the children, shut the gates, choose to fight or auto-resolve, see the odds";
        TooltipText = $"Seen {raid.ArrivalTick - raid.WarnTick} h out. The watch's guess at their numbers gets better with more lead time.";
        if (_shownRaid != raid.Id * 4 + (int)raid.Stage)
        {
            _shownRaid = raid.Id * 4 + (int)raid.Stage;
            Motion.Reveal(this, new Vector2(0, -8));
            Motion.Flash(this, Palette.Danger);
        }
    }
}
