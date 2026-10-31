using System;
using System.Linq;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Settings, in tabs: Game (day length), Display (UI scale, fullscreen, window size, vsync), Audio (master / music / effects / ambience
/// volume on their buses, mute when unfocused, visual sound cues), Camera (pan and zoom speed) and Controls (the hotkey list). Changes apply at once and are saved
/// when the panel closes (<see cref="GameSettings"/>).
/// </summary>
public partial class SettingsPanel : Overlay
{
    private readonly Label _scaleNote = Widgets.Label("", "Small");

    public SettingsPanel() : base(560)
    {
        Visible = false;
        Body.AddChild(Title("SETTINGS"));
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 360) };
        Body.AddChild(tabs);
        tabs.AddChild(Game());
        tabs.AddChild(Display());
        tabs.AddChild(Audio());
        tabs.AddChild(Camera());
        tabs.AddChild(Controls());
        for (var i = 0; i < tabs.GetTabCount(); i++)
            tabs.SetTabIcon(i, Icons.Get(new[] { "ui_play", "ui_settings", "stat_sight", "ui_world_map", "ui_menu" }[i], 16));

        var close = Widgets.IconButton("stat_check", "Done", "Save and close", Close, Hotkeys.KeyName("pause_menu"), "Primary");
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        close.CustomMinimumSize = new Vector2(120, 36);
        Body.AddChild(close);
    }

    public override void Close()
    {
        GameSettings.Save();
        base.Close();
    }

    protected override void OnOpened() => UpdateScaleNote();

    private static Control Game()
    {
        var box = Page("Game");
        var day = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var m in GameSettings.DayLengths) day.AddItem(m == 1 ? "1 minute" : $"{m} minutes");
        day.Select(Math.Max(0, Array.IndexOf(GameSettings.DayLengths, GameSettings.DayLengthMinutes)));
        day.ItemSelected += i => GameSettings.DayLengthMinutes = GameSettings.DayLengths[i];
        box.AddChild(Row("Day length", day,
            "Real time for one in-game day at normal speed; 2×, 4× and 8× go faster from there. Only the pace changes: work, food and healing still count in-game hours."));
        return Scroll(box);
    }

    private Control Display()
    {
        var box = Page("Display");
        var scale = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var s in GameSettings.UiScales) scale.AddItem($"{s * 100:0}%");
        scale.Select(Nearest(GameSettings.UiScales, GameSettings.UiScale));
        scale.ItemSelected += i =>
        {
            GameSettings.UiScale = GameSettings.UiScales[i];
            GameSettings.Apply(window: false);
            UpdateScaleNote();
        };
        box.AddChild(Row("UI scale", scale, "On top of the automatic scale for your window height."));
        box.AddChild(_scaleNote);

        var full = new CheckBox { Text = "Fullscreen", ButtonPressed = GameSettings.Fullscreen, FocusMode = FocusModeEnum.None };
        full.Toggled += on => { GameSettings.Fullscreen = on; GameSettings.Apply(); };
        box.AddChild(full);

        var res = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var r in GameSettings.Resolutions) res.AddItem($"{r.X} × {r.Y}");
        res.Select(Math.Max(0, Array.IndexOf(GameSettings.Resolutions, GameSettings.WindowSize)));
        res.ItemSelected += i => { GameSettings.WindowSize = GameSettings.Resolutions[i]; GameSettings.Apply(); };
        box.AddChild(Row("Window size", res, "Used when not fullscreen."));

        var vsync = new CheckBox { Text = "Vertical sync", ButtonPressed = GameSettings.Vsync, FocusMode = FocusModeEnum.None };
        vsync.Toggled += on => { GameSettings.Vsync = on; GameSettings.Apply(); };
        box.AddChild(vsync);

        var weather = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var w in new[] { "Full", "Reduced (no flashes)", "Off" }) weather.AddItem(w);
        weather.Select((int)GameSettings.WeatherEffects);
        weather.ItemSelected += i => GameSettings.WeatherEffects = (WeatherEffects)(int)i;
        box.AddChild(Row("Weather effects", weather, "Rain, dust and lightning on screen. Reduced draws less and never flashes; the weather's effects on play stay the same."));
        return Scroll(box);
    }

    private void UpdateScaleNote() =>
        _scaleNote.Text = $"Window {GetTree().Root.Size.X}×{GetTree().Root.Size.Y} → interface at {GameSettings.EffectiveScale * 100:0}%";

    private static Control Audio()
    {
        var box = Page("Audio");
        box.AddChild(Slider("Master", GameSettings.MasterVolume, v => GameSettings.MasterVolume = v));
        box.AddChild(Slider("Music", GameSettings.MusicVolume, v => GameSettings.MusicVolume = v));
        box.AddChild(Slider("Effects", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v));
        box.AddChild(Slider("Ambience", GameSettings.AmbienceVolume, v => GameSettings.AmbienceVolume = v));
        var mute = new CheckBox { Text = "Mute when the window is in the background", ButtonPressed = GameSettings.MuteWhenUnfocused, FocusMode = FocusModeEnum.None };
        mute.Toggled += on => { GameSettings.MuteWhenUnfocused = on; GameSettings.ApplyVolumes(); };
        box.AddChild(mute);
        var cues = new CheckBox
        {
            Text = "Visual sound cues", ButtonPressed = GameSettings.VisualSoundCues, FocusMode = FocusModeEnum.None,
            TooltipText = "Show a marker at the screen edge toward ambushes and contacts you hear but cannot see.",
        };
        cues.Toggled += on => GameSettings.VisualSoundCues = on;
        box.AddChild(cues);
        return Scroll(box);
    }

    private static Control Camera()
    {
        var box = Page("Camera");
        box.AddChild(Slider("Pan speed", GameSettings.CameraPanSpeed / 2, v => GameSettings.CameraPanSpeed = Mathf.Max(0.1f, v * 2), percentOf: 2));
        box.AddChild(Slider("Zoom speed", GameSettings.CameraZoomSpeed / 2, v => GameSettings.CameraZoomSpeed = Mathf.Max(0.1f, v * 2), percentOf: 2));
        return Scroll(box);
    }

    private static Control Controls()
    {
        var box = Page("Controls");
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var b in Hotkeys.All)
        {
            grid.AddChild(KeyCap(Hotkeys.KeyName(b.Action)));
            grid.AddChild(Widgets.Label(b.Label));
            grid.AddChild(Widgets.Label(b.Context, "Small"));
        }
        foreach (var (label, keys) in Hotkeys.Fixed)
        {
            grid.AddChild(KeyCap(keys));
            grid.AddChild(Widgets.Label(label));
            grid.AddChild(Widgets.Label("", "Small"));
        }
        box.AddChild(grid);
        return Scroll(box);
    }

    private static Control KeyCap(string keys)
    {
        var chip = new PanelContainer { ThemeTypeVariation = "Chip" };
        var label = Widgets.Label(keys, "Number", Palette.Amber, 14);
        chip.AddChild(label);
        chip.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        return chip;
    }

    private static VBoxContainer Page(string name)
    {
        var box = Widgets.Column(12);
        box.Name = name;
        return box;
    }

    private static ScrollContainer Scroll(VBoxContainer content)
    {
        var scroll = new ScrollContainer { Name = content.Name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(content);
        return scroll;
    }

    private static Control Row(string label, Control control, string tooltip)
    {
        var row = Widgets.Row(10);
        var text = Widgets.Label(label);
        text.CustomMinimumSize = new Vector2(130, 0);
        text.TooltipText = tooltip;
        text.MouseFilter = MouseFilterEnum.Stop;
        row.AddChild(text);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        control.TooltipText = tooltip;
        row.AddChild(control);
        return row;
    }

    private static Control Slider(string label, float value, Action<float> set, float percentOf = 1)
    {
        var row = Widgets.Row(10);
        var text = Widgets.Label(label);
        text.CustomMinimumSize = new Vector2(130, 0);
        row.AddChild(text);
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var number = Widgets.Label($"{value * percentOf * 100:0}%", "Number");
        number.CustomMinimumSize = new Vector2(48, 0);
        number.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += v =>
        {
            set((float)v);
            number.Text = $"{v * percentOf * 100:0}%";
            GameSettings.Apply(window: false);
        };
        row.AddChild(slider);
        row.AddChild(number);
        return row;
    }

    private static int Nearest(float[] values, float value) =>
        values.Select((v, i) => (Math.Abs(v - value), i)).Min().i;
}
