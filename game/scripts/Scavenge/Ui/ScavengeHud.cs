using System;
using Godot;
using Remnants.Game.Hud;

namespace Remnants.Game.Scavenge.Ui;

/// <summary>
/// The scavenge HUD's layout and keyboard: turn bar on top; the selected unit's card on the left; actions and stances
/// on the right; toasts and the log at the bottom; world tags (<see cref="WorldTags"/>) under everything; the run
/// summary, pause menu and settings on top; the alliance banner (<see cref="AllianceBanner"/>) under the turn bar and the
/// Team panel (<see cref="CoopPanel"/>) under the stances. Hotkeys: Space/Enter end the turn, 1–3 attacks, 4–6 items,
/// O/B/L stances, F loot, T recruit, X extract, G give, C carry, Z/V/N pings, Y/U alliance, Esc cancels aiming or opens
/// the menu. <see cref="ScavengeScreen"/> wires it to the map.
/// </summary>
public partial class ScavengeHud : CanvasLayer
{
    public WorldTags Tags { get; } = new() { Name = "Tags" };
    public TurnBar TurnBar { get; }
    public UnitPanel UnitPanel { get; }
    public ActionPanel Actions { get; }
    public StancePanel Stances { get; }
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
        middle.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var right = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        right.AddThemeConstantOverride("separation", 8);
        middle.AddChild(right);
        Actions = new ActionPanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        right.AddChild(Actions);
        Stances = new StancePanel(presenter) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        right.AddChild(Stances);
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
        toastBox.AddChild(Toasts);
        bottom.AddChild(toastBox);
        bottom.AddChild(Log);

        AddChild(Result);
        Pause = new PauseMenu(
        [
            new("Resume", "ui_play", () => Pause!.Close(), Hint: "Back to the mission (Esc)"),
            new("Settings", "ui_settings", () => Settings.Open(), Hint: "Display, audio, camera, controls"),
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

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false }) return;
        if (Pause.Visible || Settings.Visible || Result.Visible) return; // the overlays handle their own keys
        if (Hotkeys.Pressed(e, "pause_menu"))
        {
            if (BackRequested?.Invoke() != true) Pause.Open();
        }
        else if (Hotkeys.Pressed(e, "end_turn")) EndTurnRequested?.Invoke();
        else if (!Actions.PressHotkey(e) && !Stances.PressHotkey(e) && !Coop.PressHotkey(e)) return;
        GetViewport().SetInputAsHandled();
    }
}
