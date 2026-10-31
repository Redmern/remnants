using System;
using System.Linq;
using Remnants.Core.Scavenge;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The scavenge HUD's layout and keyboard: turn bar on top; the selected unit's card on the left; actions and stances
/// on the right; toasts and the log at the bottom; world tags (<see cref="WorldTags"/>) under everything; the run
/// summary, pause menu and settings on top; the alliance banner (<see cref="AllianceBanner"/>) under the turn bar, the Tech
/// panel (<see cref="TechPanel"/>: hacking and the scout drone) and the Team panel (<see cref="CoopPanel"/>) under the
/// stances, and a device tooltip (<see cref="DeviceTip"/>) at the cursor. Hotkeys: Space/Enter end the turn, 1–3 attacks, 4–6 items,
/// O/B/L stances, F loot, T recruit, X extract, H hack, K deploy / recall the drone, J fly it, M mark, P payload, G give, C carry,
/// Z/V/N pings, Y/U alliance, Esc cancels aiming or opens
/// the menu. <see cref="ScavengeScreen"/> wires it to the map.
/// </summary>
public partial class ScavengeHud : CanvasLayer
{
    public WorldTags Tags { get; } = new() { Name = "Tags" };
    public TurnBar TurnBar { get; }
    public UnitPanel UnitPanel { get; }
    public ActionPanel Actions { get; }

    /// <summary>Story missions: objectives, chatter, and the field prompt (hidden on a plain run).</summary>
    public MissionPanel Mission { get; }
    public ChatterStrip Chatter { get; }
    public Remnants.Game.Story.DialoguePanel Prompt { get; }
    public StancePanel Stances { get; }
    public TechPanel Tech { get; }

    /// <summary>A hovered device's state, owner and what hacking it does, next to the cursor.</summary>
    public DeviceTip DeviceTip { get; } = new();
    public LootTip LootTip { get; } = new();
    public LootPanel Loot { get; }
    public CoopPanel Coop { get; }
    public AllianceBanner Banner { get; }
    public ScavengeLog Log { get; } = new() { CustomMinimumSize = new Vector2(440, 150) };
    public Toasts Toasts { get; } = new();
    public ResultPanel Result { get; } = new();
    public PauseMenu Pause { get; }
    public SettingsPanel Settings { get; } = new();

    /// <summary>Esc with no menu open: the screen cancels aiming first (returns true if it did).</summary>
    public event Func<bool>? BackRequested;

    public event Action? EndTurnRequested;
    public event Action? LeaveRequested;

    public ScavengeHud() : this(null!) { }

    public ScavengeHud(ScavengePresenter presenter)
    {
        Name = "Hud";
        Tags.Theme = HudTheme.Shared;
        AddChild(Tags);
        var root = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = HudTheme.Shared };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
        foreach (var side in new[] { "left", "right", "top", "bottom" }) root.AddThemeConstantOverride($"margin_{side}", 8);
        AddChild(root);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 8);
        root.AddChild(column);
        TurnBar = new TurnBar(presenter);
        column.AddChild(TurnBar);
        Banner = new AllianceBanner(presenter);
        column.AddChild(Banner);

        var middle = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(middle);
        // Panels hug their content (unit top-left, orders top-right, log bottom-right) so the map stays open.
        UnitPanel = new UnitPanel(presenter);
        middle.AddChild(UnitPanel);
        // The pickup panel sits low in the middle, between the unit card and the orders.
        var centre = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        Loot = new LootPanel(presenter) { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        centre.AddChild(Loot);
        middle.AddChild(centre);
        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        right.AddThemeConstantOverride("separation", 8);
        middle.AddChild(right);
        Mission = new MissionPanel(presenter);
        right.AddChild(Mission);
        Actions = new ActionPanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        right.AddChild(Actions);
        Stances = new StancePanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        right.AddChild(Stances);
        Tech = new TechPanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        right.AddChild(Tech);
        Coop = new CoopPanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        right.AddChild(Coop);

        var bottom = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bottom.AddThemeConstantOverride("separation", 8);
        column.AddChild(bottom);
        // Toasts sit at the side, just left of the log, so the middle of the map stays clear.
        var toastBox = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Toasts.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        Chatter = new ChatterStrip(presenter);
        bottom.AddChild(Chatter);
        toastBox.AddChild(Toasts);
        bottom.AddChild(toastBox);
        bottom.AddChild(Log);

        Prompt = new Remnants.Game.Story.DialoguePanel(() => PromptLine(presenter), () => { },
            i => Choose(presenter, i), () => presenter.State.Mission?.Chatter.Select(c => (c.Speaker, c.Text, false)) ?? []);
        AddChild(Prompt);
        DeviceTip.Theme = HudTheme.Shared;
        AddChild(DeviceTip);
        LootTip.Theme = HudTheme.Shared;
        AddChild(LootTip);
        AddChild(Result);
        Pause = new PauseMenu(
        [
            new("Resume", "ui_play", () => Pause!.Close(), Hint: "Back to the mission (Esc)"),
            new("Settings", "ui_settings", () => Settings.Open(), Hint: "Display, audio, camera, controls"),
            new("Help", "ui_info", () => Hud.Help.HelpLayer.Instance?.Index.OpenAt(null), Hint: "Every topic, searchable (F1)"),
            new("Leave the zone", "ui_leave", () => LeaveRequested?.Invoke(),
                "Leave now? The run is suspended and the party stays out; Expedition → Play mission resumes it.", "Suspend the run"),
            new("Quit", "ui_quit", () => GetTree().Quit(), "Quit the game? The run is lost unless the colony was saved.", "Quit to the desktop"),
        ], "MISSION PAUSED");
        AddChild(Pause);
        AddChild(Settings);

        TurnBar.MenuPressed += () => Pause.Open();
        TurnBar.EndTurnPressed += () => EndTurnRequested?.Invoke();
        TurnBar.LeavePressed += () => LeaveRequested?.Invoke();
    }

    /// <summary>A field prompt (the Tally at the terminal) as a dialogue line; it pauses the turn until answered.</summary>
    private static Remnants.Game.Story.DialogueLine? PromptLine(ScavengePresenter presenter)
    {
        if (presenter.State.Mission?.Pending is not { } p) return null;
        var speaker = p.Speaker.StartsWith("Cole") ? "cole" : p.Speaker == "Lantern" ? "iris" : "narration";
        var choices = p.Options.Select((o, i) => (i, o.Text, o.Hint)).ToList();
        return new Remnants.Game.Story.DialogueLine(speaker, p.Speaker, p.Text, "neutral", speaker == "iris",
            Remnants.Game.Story.StoryArt.Portrait(speaker, speaker == "cole" ? "angry" : "neutral", speaker == "iris"), choices, presenter.State.Mission.Def.Name);
    }

    private static async void Choose(ScavengePresenter presenter, int index)
    {
        if (presenter.State.Mission?.Pending is not { } p || presenter.ActingUnit is not { } u) return;
        await presenter.Execute(new ChooseOption(presenter.PlayerId, u.Id, p.Id, p.Options[index].Id));
    }

    /// <summary>The story parts of the HUD, from the presenter's state.</summary>
    public void RefreshStory()
    {
        Mission.Refresh();
        Chatter.Refresh();
        Prompt.Refresh();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false }) return;
        if (Pause.Visible || Settings.Visible || Result.Visible) return; // the overlays handle their own keys
        if (Hotkeys.Pressed(e, "pause_menu"))
        {
            if (BackRequested?.Invoke() != true) Pause.Open();
        }
        else if (Hotkeys.Pressed(e, "end_turn")) EndTurnRequested?.Invoke();
        else if (!Loot.PressHotkey(e) && !Actions.PressHotkey(e) && !Stances.PressHotkey(e) && !Tech.PressHotkey(e) && !Coop.PressHotkey(e)) return;
        GetViewport().SetInputAsHandled();
    }
}
