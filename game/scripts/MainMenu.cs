using System.Linq;
using Godot;
using FileAccess = Godot.FileAccess;
using Remnants.Core.Colony;
using Remnants.Game.Colony;
using Remnants.Game.Hud;
using Remnants.Game.Multiplayer;
using Remnants.Game.Scavenge;

namespace Remnants.Game;

/// <summary>
/// The title screen over a living colony (<see cref="MenuBackdrop"/>): Continue (the colony still running in this
/// session, else the save), New Game, Load, Multiplayer, Settings, Quit, and a small debug mission link.
/// </summary>
public partial class MainMenu : Control
{
    private const ulong NewGameSeed = 1;
    private const string ColonyScene = "res://scenes/Colony.tscn";

    private Label _status = null!;
    private SettingsPanel _settings = null!;

    public override void _Ready()
    {
        GameSettings.Boot(GetTree());
        Theme = HudTheme.Shared;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        AddChild(new MenuBackdrop { Name = "Backdrop" });
        AddChild(Shade());
        AddChild(Menu());
        AddChild(Footer());
        _settings = new SettingsPanel();
        AddChild(_settings);

        // `-- --colony` skips the menu (handy with DebugCapture's --screenshot).
        if (DebugCapture.HasFlag("--colony")) CallDeferred(MethodName.NewGame);
        // `-- --scavenge` starts the debug mission (see ScavengeSession.DebugParty).
        else if (DebugCapture.HasFlag("--scavenge")) CallDeferred(MethodName.OnScavengePressed);
        // `-- --multiplayer` (or any --mp-host / --mp-join) opens the multiplayer menu (see MultiplayerMenu).
        else if (DebugCapture.HasFlag("--multiplayer") || OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--mp-")))
            CallDeferred(MethodName.OnMultiplayerPressed);
        else if (DebugCapture.HasFlag("--menu-settings")) _settings.CallDeferred(Overlay.MethodName.Open);
    }

    /// <summary>A dark wash from the left so the menu reads over the town, plus a vignette.</summary>
    private static Control Shade()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.03f, 0.028f, 0.025f, 0.92f));
        gradient.SetColor(1, new Color(0.03f, 0.028f, 0.025f, 0.05f));
        gradient.AddPoint(0.45f, new Color(0.03f, 0.028f, 0.025f, 0.6f));
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, Width = 256, Height = 4 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        return shade;
    }

    private Control Menu()
    {
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.LeftWide);
        margin.AddThemeConstantOverride("margin_left", 90);
        margin.AddThemeConstantOverride("margin_top", 60);
        margin.AddThemeConstantOverride("margin_bottom", 60);
        var column = Widgets.Column(10);
        column.Alignment = BoxContainer.AlignmentMode.Center;
        column.CustomMinimumSize = new Vector2(360, 0);
        margin.AddChild(column);

        var title = Widgets.Label("REMNANTS", "Title", Palette.Text, 96);
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        title.AddThemeConstantOverride("shadow_offset_y", 4);
        column.AddChild(title);
        var rule = new ColorRect { Color = Palette.Rust, CustomMinimumSize = new Vector2(120, 4) };
        rule.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        column.AddChild(rule);
        column.AddChild(Widgets.Label("Keep them alive. Keep them human.", "Muted", size: 18));
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 28) });

        var canContinue = ColonySession.Current is not null || FileAccess.FileExists(ColonySession.SavePath);
        var continueButton = Entry(column, "ui_continue", "Continue",
            ColonySession.Current is not null ? "Back to the colony in progress" : "Load the last save", Continue, primary: true);
        continueButton.Disabled = !canContinue;
        if (!canContinue) continueButton.TooltipText = "No colony yet: start a new game.";
        Entry(column, "ui_new_game", "New Game", "Found a new colony", NewGamePressed, primary: !canContinue);
        var load = Entry(column, "ui_load", "Load", "Load the saved colony", Load);
        load.Disabled = !FileAccess.FileExists(ColonySession.SavePath);
        Entry(column, "ui_multiplayer", "Multiplayer", "Join or host a shared scavenge run", OnMultiplayerPressed);
        Entry(column, "ui_settings", "Settings", "Display, audio, camera, controls", () => _settings.Open());
        Entry(column, "ui_quit", "Quit", "Quit to the desktop", () => GetTree().Quit());
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var debug = Widgets.IconButton("ui_attack", "Skirmish (debug mission)", "Play a stand-alone scavenge mission", OnScavengePressed, variation: "Flat");
        debug.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        column.AddChild(debug);
        _status = Widgets.Wrapped("", "Small");
        column.AddChild(_status);
        return margin;
    }

    private static Button Entry(Container column, string icon, string text, string tooltip, System.Action action, bool primary = false)
    {
        var button = Widgets.IconButton(icon, text.ToUpperInvariant(), tooltip, action, variation: primary ? "Primary" : "");
        button.CustomMinimumSize = new Vector2(340, 48);
        button.Alignment = HorizontalAlignment.Left;
        button.AddThemeFontOverride("font", Fonts.Display);
        button.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(button);
        return button;
    }

    private static Control Footer()
    {
        var label = Widgets.Label("Fonts: Barlow & Barlow Condensed (SIL OFL)", "Small", Palette.TextDim);
        label.SetAnchorsPreset(LayoutPreset.BottomRight);
        label.GrowHorizontal = GrowDirection.Begin;
        label.GrowVertical = GrowDirection.Begin;
        label.OffsetRight = -16;
        label.OffsetBottom = -10;
        return label;
    }

    private void Continue()
    {
        if (ColonySession.Current is null && !LoadSave()) return;
        GetTree().ChangeSceneToFile(ColonyScene);
    }

    private void NewGamePressed()
    {
        if (ColonySession.Current is null)
        {
            NewGame();
            return;
        }
        ConfirmDialog.Ask(this, "New game", "Start a new colony? The one in progress is lost unless you saved it.", "New game", NewGame);
    }

    private void NewGame()
    {
        ColonySession.Current = null; // the colony screen founds a new one
        GetTree().ChangeSceneToFile(ColonyScene);
    }

    private void Load()
    {
        if (ColonySession.Current is null)
        {
            if (LoadSave()) GetTree().ChangeSceneToFile(ColonyScene);
            return;
        }
        ConfirmDialog.Ask(this, "Load", "Load the saved colony? Progress since the last save is lost.", "Load", () =>
        {
            ColonySession.Current = null;
            if (LoadSave()) GetTree().ChangeSceneToFile(ColonyScene);
        });
    }

    /// <summary>Makes <see cref="ColonySession.Current"/> from the save; says why not on the status line.</summary>
    private bool LoadSave()
    {
        var data = GameDataPaths.Load();
        var session = new ColonySession(Remnants.Core.Colony.NewGame.Create(data, NewGameSeed, GameDataPaths.LoadScavenge()));
        string? error = null;
        session.Feedback += (message, isError) => { if (isError) error = message; };
        session.Load();
        session.DetachViews();
        if (error is not null)
        {
            _status.Text = error;
            return false;
        }
        ColonySession.Current = session;
        return true;
    }

    private void OnScavengePressed() => ScavengeSession.BeginDebug(GetTree());

    private void OnMultiplayerPressed() => GetTree().ChangeSceneToFile(MultiplayerSession.MenuScene);
}
