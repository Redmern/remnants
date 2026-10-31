using System;
using System.Linq;
using Remnants.Core.Scavenge;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The scavenge HUD's layout and keyboard: turn bar on top; the selected unit's card on the left with the loot panel
/// docked under it; the right column (one fixed width) with whose turn it is (<see cref="ActingHeader"/>: portrait, name,
/// what a click does now), the story mission, then the orders in a fixed order: Attack · Use, Stance, what is in reach
/// here (<see cref="ActionPanel.Context"/>), Tech (<see cref="TechPanel"/>: hacking and the drone), Team
/// (<see cref="CoopPanel"/>); toasts and the log at the bottom; world tags (<see cref="WorldTags"/>) under everything;
/// the run summary, pause menu and settings on top; the alliance banner (<see cref="AllianceBanner"/>) under the turn
/// bar, and a device tooltip (<see cref="DeviceTip"/>) at the cursor. Hotkeys (GameSettings' table): Space/Enter end the
/// turn, 1–9 attacks then items, O/B/L stances, F loot, I salvage, T recruit, R load the vehicle, X extract, H hack,
/// K deploy / recall the drone, J fly it, M mark, P payload, Shift+R the drone's role, Shift+I ferry, G give, C carry,
/// Z/V/N pings, Y/U alliance, Tab next unit, Home centre on the acting unit, Backspace cancels aiming, Esc cancels aiming
/// or opens the menu. <see cref="ScavengeScreen"/> wires it to the map.
/// </summary>
public partial class ScavengeHud : CanvasLayer
{
    /// <summary>The right column's one width (the design system's narrow panel).</summary>
    public const int ColumnWidth = 300;

    /// <summary>The right column: whose turn, the mission, then the orders.</summary>
    public VBoxContainer Orders { get; }

    /// <summary>The acting unit's name and portrait, and what a click does now.</summary>
    public ActingHeader Acting { get; }

    private readonly Control[] _orderPanels;
    private bool _dimmed;

    /// <summary>
    /// Dims the order panels while they do not belong to what the player looks at: an enemy turn replaying, or another
    /// unit selected than the one acting (the header says whose orders they are). They stay where they are.
    /// </summary>
    public void DimOrders(bool dim)
    {
        if (dim == _dimmed) return;
        _dimmed = dim;
        var alpha = dim ? 0.5f : 1f;
        foreach (var panel in _orderPanels)
        {
            var target = new Color(1, 1, 1, alpha);
            if (!panel.IsInsideTree() || GameSettings.ReduceMotion) SetFade(panel, target);
            else Motion.Tween(panel).TweenMethod(Callable.From<Color>(c => SetFade(panel, c)), Fade(panel), target, Motion.Fast);
        }
    }

    private static readonly StringName FadeMeta = "orders_fade";

    private static Color Fade(Control panel) => panel.HasMeta(FadeMeta) ? panel.GetMeta(FadeMeta).AsColor() : Colors.White;

    /// <summary>The fade lives on the panel's first child (the content box), so Reveal / Conceal keep the panel's own modulate.</summary>
    private static void SetFade(Control panel, Color color)
    {
        panel.SetMeta(FadeMeta, color);
        if (panel.GetChildCount() > 0 && panel.GetChild(0) is CanvasItem content) content.Modulate = color;
    }

    /// <summary>Tab: select the next of your units.</summary>
    public event Action? NextUnitRequested;

    /// <summary>Home: select the acting unit and centre the camera on it.</summary>
    public event Action? CenterRequested;

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
    public ScavengeLog Log { get; } = new();
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
        var left = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        left.AddThemeConstantOverride("separation", 8);
        middle.AddChild(Scrolling(left));
        UnitPanel = new UnitPanel(presenter);
        left.AddChild(UnitPanel);
        // The pickup panel docks under the unit card, off the map's middle.
        Loot = new LootPanel(presenter);
        left.AddChild(Loot);
        middle.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        // The right column: whose turn (and what a click does), then a fixed order: Attack · Use, Stance, what is in
        // reach here, Tech, Team. It is one fixed width; panels that come and go only move what is below them.
        Orders = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(ColumnWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Orders.AddThemeConstantOverride("separation", 8);
        middle.AddChild(Scrolling(Orders));
        Acting = new ActingHeader();
        Orders.AddChild(Acting);
        Mission = new MissionPanel(presenter);
        Orders.AddChild(Mission);
        Actions = new ActionPanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        Orders.AddChild(Actions);
        Stances = new StancePanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        Orders.AddChild(Stances);
        Orders.AddChild(Actions.Context);
        Tech = new TechPanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        Orders.AddChild(Tech);
        Coop = new CoopPanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        Orders.AddChild(Coop);
        _orderPanels = [Actions, Stances, Actions.Context, Tech, Coop];

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

    /// <summary>
    /// A side column that scrolls on a short screen (720p, a long unit card or many orders) instead of pushing the log
    /// off. The scroller ignores the mouse itself, so the map under the column's empty part stays clickable; the panels
    /// in it hand it the wheel.
    /// </summary>
    private static ScrollContainer Scrolling(VBoxContainer column)
    {
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        scroll.AddChild(column);
        column.ChildEnteredTree += child =>
        {
            if (child is not Control panel) return;
            panel.GuiInput += e =>
            {
                if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel) return;
                if (scroll.GetVScrollBar() is not { Visible: true }) return;
                scroll.ScrollVertical += wheel.ButtonIndex == MouseButton.WheelUp ? -48 : 48;
                panel.AcceptEvent();
            };
        };
        return scroll;
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
        else if (Hotkeys.Pressed(e, "scav_cancel"))
        {
            if (BackRequested?.Invoke() != true) return;
        }
        else if (Hotkeys.Pressed(e, "scav_next_unit")) NextUnitRequested?.Invoke();
        else if (Hotkeys.Pressed(e, "scav_center")) CenterRequested?.Invoke();
        else if (!Loot.PressHotkey(e) && !Actions.PressHotkey(e) && !Stances.PressHotkey(e) && !Tech.PressHotkey(e) && !Coop.PressHotkey(e)) return;
        GetViewport().SetInputAsHandled();
    }
}
