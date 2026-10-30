using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Player settings in <c>user://settings.cfg</c>: UI scale, window mode and size, vsync, master / music / sfx /
/// ambience volume (on the buses of <see cref="Audio.AudioBuses"/>), mute when unfocused, visual sound cues, camera
/// pan and zoom speed. Also owns the hotkey
/// actions (<see cref="Hotkeys"/>), added to the InputMap at boot. <see cref="Boot"/> runs once per process from
/// whichever scene starts first.
/// <para>The UI scale is the root window's content scale: <c>auto × UiScale</c>, where auto follows the window height
/// (about 1000 logical pixels tall from 850px up, a little denser below), so 720p, 1080p and 1440p all fit the HUD.</para>
/// </summary>
public static class GameSettings
{
    private const string Path = "user://settings.cfg";

    public static float UiScale { get; set; } = 1f;
    public static bool Fullscreen { get; set; }
    public static Vector2I WindowSize { get; set; } = new(1280, 720);
    public static bool Vsync { get; set; } = true;
    public static float MasterVolume { get; set; } = 0.8f;
    public static float MusicVolume { get; set; } = 0.7f;
    public static float SfxVolume { get; set; } = 0.8f;
    public static float AmbienceVolume { get; set; } = 0.7f;
    public static bool MuteWhenUnfocused { get; set; } = true;

    /// <summary>Accessibility: small HUD markers toward off-screen ambush and contact sounds (<see cref="Audio.SoundCues"/>).</summary>
    public static bool VisualSoundCues { get; set; }

    /// <summary>The window lost focus (set by the audio autoload); mutes Master when <see cref="MuteWhenUnfocused"/>.</summary>
    public static bool Unfocused { get; set; }
    public static float CameraPanSpeed { get; set; } = 1f;
    public static float CameraZoomSpeed { get; set; } = 1f;

    public static readonly Vector2I[] Resolutions = [new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160)];

    public static readonly float[] UiScales = [0.75f, 0.85f, 1f, 1.15f, 1.3f, 1.5f];

    private static bool _booted;
    private static Window? _root;

    /// <summary>Once per process: load settings, make the buses and hotkeys, apply, attach the debug capture.</summary>
    public static void Boot(SceneTree tree)
    {
        if (_booted) return;
        _booted = true;
        _root = tree.Root;
        Load();
        EnsureBuses();
        Hotkeys.Install();
        // A screenshot run (DebugCapture) keeps the window it was given, so captures match the asked size.
        Apply(window: !DebugCapture.HasFlag("--keep-window") && Godot.OS.GetCmdlineUserArgs().All(a => !a.StartsWith("--screenshot") && !a.StartsWith("--record")));
        _root.SizeChanged += ApplyScale;
        DebugCapture.Attach(tree);
    }

    public static void Apply(bool window = true)
    {
        if (window && _root is not null && !OS.HasFeature("headless"))
        {
            DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
            if (!Fullscreen && DisplayServer.WindowGetSize() != WindowSize) DisplayServer.WindowSetSize(WindowSize);
            DisplayServer.WindowSetVsyncMode(Vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }
        ApplyVolumes();
        ApplyScale();
    }

    public static void ApplyVolumes()
    {
        SetVolume("Master", Unfocused && MuteWhenUnfocused ? 0 : MasterVolume);
        SetVolume("Music", MusicVolume);
        SetVolume("SFX", SfxVolume);
        SetVolume("Ambience", AmbienceVolume);
    }

    /// <summary>The automatic part of the UI scale for a window height.</summary>
    public static float AutoScale(float height) => Mathf.Clamp(height / 1000f, 0.85f, 3f);

    public static float EffectiveScale => _root is null ? 1 : AutoScale(_root.Size.Y) * UiScale;

    private static void ApplyScale()
    {
        if (_root is null) return;
        _root.ContentScaleFactor = EffectiveScale;
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("ui", "scale", UiScale);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.SetValue("display", "width", WindowSize.X);
        cfg.SetValue("display", "height", WindowSize.Y);
        cfg.SetValue("display", "vsync", Vsync);
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ambience", AmbienceVolume);
        cfg.SetValue("audio", "mute_unfocused", MuteWhenUnfocused);
        cfg.SetValue("audio", "visual_cues", VisualSoundCues);
        cfg.SetValue("camera", "pan", CameraPanSpeed);
        cfg.SetValue("camera", "zoom", CameraZoomSpeed);
        cfg.Save(Path);
    }

    private static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        UiScale = Mathf.Clamp((float)cfg.GetValue("ui", "scale", UiScale), 0.5f, 2f);
        Fullscreen = (bool)cfg.GetValue("display", "fullscreen", Fullscreen);
        WindowSize = new Vector2I((int)cfg.GetValue("display", "width", WindowSize.X), (int)cfg.GetValue("display", "height", WindowSize.Y));
        Vsync = (bool)cfg.GetValue("display", "vsync", Vsync);
        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume);
        MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume);
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
        AmbienceVolume = (float)cfg.GetValue("audio", "ambience", AmbienceVolume);
        MuteWhenUnfocused = (bool)cfg.GetValue("audio", "mute_unfocused", MuteWhenUnfocused);
        VisualSoundCues = (bool)cfg.GetValue("audio", "visual_cues", VisualSoundCues);
        CameraPanSpeed = (float)cfg.GetValue("camera", "pan", CameraPanSpeed);
        CameraZoomSpeed = (float)cfg.GetValue("camera", "zoom", CameraZoomSpeed);
    }

    private static void EnsureBuses() => Audio.AudioBuses.Ensure();

    private static void SetVolume(string bus, float linear)
    {
        var index = AudioServer.GetBusIndex(bus);
        if (index < 0) return;
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
        AudioServer.SetBusMute(index, linear <= 0.001f);
    }
}

/// <summary>
/// The game's hotkeys as InputMap actions, so input code asks for <c>"build"</c> rather than a key, and the settings
/// screen and tooltips show the same names.
/// </summary>
public static class Hotkeys
{
    public sealed record Binding(string Action, string Label, string Context, Key[] Keys);

    public static readonly IReadOnlyList<Binding> All =
    [
        new("pause_menu", "Pause menu / back", "Everywhere", [Key.Escape]),
        new("toggle_pause", "Pause / resume time", "Colony", [Key.Space]),
        new("speed_1", "Normal speed", "Colony", [Key.Key1]),
        new("speed_2", "Fast (2×)", "Colony", [Key.Key2]),
        new("speed_3", "Fastest (4×)", "Colony", [Key.Key3]),
        new("open_build", "Build menu", "Colony", [Key.B]),
        new("open_expedition", "Expedition", "Colony", [Key.E]),
        new("open_items", "Items", "Colony", [Key.I]),
        new("open_log", "Event log filters", "Colony", [Key.L]),
        new("cycle_survivor", "Next survivor", "Colony", [Key.Tab]),
        new("camera_rotate_left", "Rotate camera left", "Colony", [Key.Q]),
        new("camera_rotate_right", "Rotate camera right", "Colony", [Key.R]),
        new("end_turn", "End turn", "Scavenge", [Key.Space, Key.Enter]),
        new("scav_attack_1", "First attack", "Scavenge", [Key.Key1]),
        new("scav_attack_2", "Second attack", "Scavenge", [Key.Key2]),
        new("scav_attack_3", "Third attack", "Scavenge", [Key.Key3]),
        new("scav_item_1", "Use first item", "Scavenge", [Key.Key4]),
        new("scav_item_2", "Use second item", "Scavenge", [Key.Key5]),
        new("scav_item_3", "Use third item", "Scavenge", [Key.Key6]),
        new("scav_item_4", "Use fourth item", "Scavenge", [Key.Key7]),
        new("scav_item_5", "Use fifth item", "Scavenge", [Key.Key8]),
        new("scav_item_6", "Use sixth item", "Scavenge", [Key.Key9]),
        new("scav_overwatch", "Overwatch", "Scavenge", [Key.O]),
        new("scav_brace", "Brace", "Scavenge", [Key.B]),
        new("scav_lookout", "Lookout", "Scavenge", [Key.L]),
        new("scav_loot", "Take the first loot", "Scavenge", [Key.F]),
        new("scav_recruit", "Recruit", "Scavenge", [Key.T]),
        new("scav_extract", "Extract", "Scavenge", [Key.X]),
        new("scav_give", "Give an item to an ally", "Scavenge", [Key.G]),
        new("scav_carry", "Carry / put down a downed ally", "Scavenge", [Key.C]),
        new("scav_ping_danger", "Ping: danger", "Scavenge", [Key.Z]),
        new("scav_ping_go", "Ping: go here", "Scavenge", [Key.V]),
        new("scav_ping_loot", "Ping: loot", "Scavenge", [Key.N]),
        new("scav_ally", "Offer / accept an alliance", "Scavenge", [Key.Y]),
        new("scav_decline", "Decline an alliance", "Scavenge", [Key.U]),
        new("scav_hack", "Hack the first device in reach", "Scavenge", [Key.H]),
        new("scav_drone", "Deploy / recall the scout drone", "Scavenge", [Key.K]),
        new("scav_fly", "Fly the scout drone", "Scavenge", [Key.J]),
        new("scav_mark", "Mark a target with the drone", "Scavenge", [Key.M]),
        new("scav_payload", "Drop a payload from the drone", "Scavenge", [Key.P]),
    ];

    /// <summary>Camera panning is not an action (it reads WASD and arrows directly); listed for the settings screen.</summary>
    public static readonly (string Label, string Keys)[] Fixed =
    [
        ("Pan camera", "W A S D / arrows / middle-drag"),
        ("Zoom", "Mouse wheel"),
        ("Rotate camera (scavenge)", "Q / E"),
        ("Cancel aiming (scavenge)", "Right-click / Esc"),
        ("Orbit camera (colony)", "Right-drag"),
    ];

    public static void Install()
    {
        foreach (var binding in All)
        {
            if (InputMap.HasAction(binding.Action)) continue;
            InputMap.AddAction(binding.Action);
            foreach (var key in binding.Keys) InputMap.ActionAddEvent(binding.Action, new InputEventKey { Keycode = key });
        }
    }

    /// <summary>"B", "Space / Enter": the keys bound to an action, for tooltips.</summary>
    public static string KeyName(string action)
    {
        if (!InputMap.HasAction(action)) return "";
        var keys = InputMap.ActionGetEvents(action).OfType<InputEventKey>()
            .Select(k => OS.GetKeycodeString(k.Keycode != Key.None ? k.Keycode : k.PhysicalKeycode));
        return string.Join(" / ", keys);
    }

    /// <summary>A fresh key press of <paramref name="action"/> (no echo).</summary>
    public static bool Pressed(InputEvent e, string action) =>
        e is InputEventKey { Pressed: true, Echo: false } && e.IsAction(action, exactMatch: false);
}
