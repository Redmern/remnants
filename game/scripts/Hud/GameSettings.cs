using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Player settings in <c>user://settings.cfg</c>: UI scale, window mode and size, vsync, master / music / sfx /
/// ambience volume (on the buses of <see cref="Audio.AudioBuses"/>), mute when unfocused, visual sound cues, camera
/// pan and zoom speed, day length, scavenge replay pace (walk speed, skip animations, auto end turn) and feel (effects
/// intensity, shake, hit-stop, flashes, CRT shader, music reactivity). Also owns the hotkey
/// actions (<see cref="Hotkeys"/>), added to the InputMap at boot. <see cref="Boot"/> runs once per process from
/// whichever scene starts first.
/// <para>The UI scale is the root window's content scale: <c>auto × UiScale</c>, where auto follows the window height
/// (about 1000 logical pixels tall from 850px up, a little denser below), so 720p, 1080p and 1440p all fit the HUD.</para>
/// </summary>
public enum WeatherEffects { Full, Reduced, Off }

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

    /// <summary>
    /// How much weather is drawn (<see cref="WeatherFx.WeatherView"/>): Full; Reduced (fewer particles, no lightning flashes:
    /// the accessibility option); Off (only the sky's tint). Rules are the same either way.
    /// </summary>
    public static WeatherEffects WeatherEffects { get; set; } = WeatherEffects.Full;

    /// <summary>The window lost focus (set by the audio autoload); mutes Master when <see cref="MuteWhenUnfocused"/>.</summary>
    public static bool Unfocused { get; set; }
    /// <summary>Accessibility: HUD tweens (panels, lists, counters, toasts) finish at once instead of animating (<see cref="Motion"/>).</summary>
    public static bool ReduceMotion { get; set; }

    /// <summary>Debug: the UI frame-time overlay (<see cref="FrameTimeOverlay"/>; also F3 or <c>--frame-overlay</c>).</summary>
    public static bool ShowFrameTime { get; set; }
    public static float CameraPanSpeed { get; set; } = 1f;

    /// <summary>
    /// Real minutes one in-game day takes at 1x (2x and up scale from it); see <see cref="Colony.ColonySession.SecondsPerTick"/>.
    /// Pacing only: the rules count in-game hours, so balance is the same at every length.
    /// </summary>
    public static int DayLengthMinutes { get; set; } = 2;

    /// <summary>Which runs take the late zones' retune (zones.json "retune"): to compare the tunings.</summary>
    public static Remnants.Core.Scavenge.LateZoneTuning LateZones { get; set; } = Remnants.Core.Scavenge.LateZoneTuning.Expeditions;

    public static readonly int[] DayLengths = [1, 2, 4, 8];

    /// <summary>The Sandbox start's number of people last picked on the New Game screen (0 = the rules' default).</summary>
    public static int SandboxHabitants { get; set; }

    public static float CameraZoomSpeed { get; set; } = 1f;

    /// <summary>
    /// Scavenge: replays snap each step to its end instead of animating (also the <c>scav_fast_forward</c> hotkey), so input
    /// is never held. Pacing only: the run plays out the same either way.
    /// </summary>
    public static bool SkipAnimations { get; set; }

    /// <summary>Scavenge: seconds a unit takes to walk one tile in a replay (one of <see cref="WalkSpeeds"/>; runs go a little quicker).</summary>
    public static float WalkTileSeconds { get; set; } = 0.2f;

    public static readonly (string Label, float Seconds)[] WalkSpeeds = [("Slow", 0.25f), ("Normal", 0.2f), ("Fast", 0.14f), ("Very fast", 0.08f)];

    /// <summary>Scavenge: a unit with nothing affordable left to do ends its turn by itself (<see cref="Remnants.Core.Scavenge.TurnAdvice"/>).</summary>
    public static bool AutoEndTurn { get; set; } = true;

    /// <summary>
    /// Scavenge feel, master level (<see cref="Remnants.Core.Scavenge.ReplayJuice"/>): pops, card motion, shake, hit-stop,
    /// flashes and the music's chain layer scale with it; Off leaves the plain replay. Presentation only.
    /// </summary>
    public static Remnants.Core.Scavenge.EffectsLevel EffectsLevel { get; set; } = Remnants.Core.Scavenge.EffectsLevel.Normal;

    /// <summary>Scavenge: the camera shakes on hits, kills and shoves (never with <see cref="ReduceMotion"/>).</summary>
    public static bool ScreenShake { get; set; } = true;

    /// <summary>Scavenge: the replay freezes a moment on a heavy blow (never while skipping animations).</summary>
    public static bool HitStop { get; set; } = true;

    /// <summary>Scavenge: a colour flash / vignette on big hits and kills, capped against strobing (never with <see cref="ReduceMotion"/>).</summary>
    public static bool HitFlashes { get; set; } = true;

    /// <summary>Scavenge: the mild CRT look (scanlines, curve, a slow wobble; the wobble and curve go with <see cref="ReduceMotion"/>).</summary>
    public static bool CrtShader { get; set; } = true;

    /// <summary>Scavenge: the music's chain layer swells with the squad's hit chain and calms when the round ends.</summary>
    public static bool MusicReactive { get; set; } = true;

    public static readonly string[] EffectsLevels = ["Off", "Low", "Normal", "High"];

    /// <summary>The feel settings as Core's view model takes them.</summary>
    public static Remnants.Core.Scavenge.JuiceSettings Juice =>
        new(EffectsLevel, ScreenShake, HitStop, HitFlashes, ReduceMotion) { Crt = CrtShader, Music = MusicReactive };

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
        // Debug: a run with replays snapped (not saved unless the settings panel saves).
        if (DebugCapture.HasFlag("--skip-animations")) SkipAnimations = true;
        // Debug: a UI scale for this run only (--ui-scale=1.18 on a 1390 px tall window lays out like 1280x720), not saved.
        if (float.TryParse(DebugCapture.Value("--ui-scale"), System.Globalization.CultureInfo.InvariantCulture, out var uiScale))
            UiScale = Mathf.Clamp(uiScale, 0.5f, 2f);
        // Debug: a run at a given effects level (--effects=off|low|normal|high), for the determinism and perf checks.
        if (DebugCapture.Value("--effects") is { } effects
            && Array.FindIndex(EffectsLevels, l => l.Equals(effects, StringComparison.OrdinalIgnoreCase)) is >= 0 and var level)
            EffectsLevel = (Remnants.Core.Scavenge.EffectsLevel)level;
        EnsureBuses();
        Hotkeys.Install();
        Help.HelpLayer.Attach(tree);
        FrameTimeOverlay.Attach(tree);
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
        cfg.SetValue("display", "weather", (int)WeatherEffects);
        cfg.SetValue("ui", "reduce_motion", ReduceMotion);
        cfg.SetValue("ui", "frame_time", ShowFrameTime);
        cfg.SetValue("camera", "pan", CameraPanSpeed);
        cfg.SetValue("camera", "zoom", CameraZoomSpeed);
        cfg.SetValue("game", "day_minutes", DayLengthMinutes);
        cfg.SetValue("game", "late_zones", (int)LateZones);
        cfg.SetValue("game", "sandbox_habitants", SandboxHabitants);
        cfg.SetValue("scavenge", "skip_animations", SkipAnimations);
        cfg.SetValue("scavenge", "walk_tile_seconds", WalkTileSeconds);
        cfg.SetValue("scavenge", "auto_end_turn", AutoEndTurn);
        foreach (var (key, value) in Juice.Save()) cfg.SetValue("effects", key, value is bool b ? b : (int)value);
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
        WeatherEffects = (WeatherEffects)Math.Clamp((int)cfg.GetValue("display", "weather", (int)WeatherEffects), 0, 2);
        ReduceMotion = (bool)cfg.GetValue("ui", "reduce_motion", ReduceMotion);
        ShowFrameTime = (bool)cfg.GetValue("ui", "frame_time", ShowFrameTime);
        CameraPanSpeed = (float)cfg.GetValue("camera", "pan", CameraPanSpeed);
        CameraZoomSpeed = (float)cfg.GetValue("camera", "zoom", CameraZoomSpeed);
        DayLengthMinutes = Math.Clamp((int)cfg.GetValue("game", "day_minutes", DayLengthMinutes), DayLengths[0], DayLengths[^1]);
        LateZones = (Remnants.Core.Scavenge.LateZoneTuning)Math.Clamp((int)cfg.GetValue("game", "late_zones", (int)LateZones), 0, 2);
        SandboxHabitants = Math.Max(0, (int)cfg.GetValue("game", "sandbox_habitants", SandboxHabitants));
        SkipAnimations = (bool)cfg.GetValue("scavenge", "skip_animations", SkipAnimations);
        WalkTileSeconds = Mathf.Clamp((float)cfg.GetValue("scavenge", "walk_tile_seconds", WalkTileSeconds), WalkSpeeds[^1].Seconds, WalkSpeeds[0].Seconds);
        AutoEndTurn = (bool)cfg.GetValue("scavenge", "auto_end_turn", AutoEndTurn);
        var saved = cfg.HasSection("effects")
            ? cfg.GetSectionKeys("effects").ToDictionary(k => k, k => cfg.GetValue("effects", k).Obj ?? "")
            : new Dictionary<string, object>();
        var juice = Remnants.Core.Scavenge.JuiceSettings.Load(saved, Juice);
        (EffectsLevel, ScreenShake, HitStop, HitFlashes, CrtShader, MusicReactive) =
            (juice.Level, juice.Shake, juice.HitStop, juice.Flashes, juice.Crt, juice.Music);
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
        new("open_help", "Help: every topic, searchable", "Everywhere", [Key.F1]),
        new("toggle_frame_time", "Show / hide the frame-time overlay", "Everywhere", [Key.F3]),
        new("toggle_pause", "Pause / resume time", "Colony", [Key.Space]),
        new("speed_1", "Normal speed", "Colony", [Key.Key1]),
        new("speed_2", "Fast (2×)", "Colony", [Key.Key2]),
        new("speed_3", "Faster (4×)", "Colony", [Key.Key3]),
        new("speed_4", "Fastest (8×)", "Colony", [Key.Key4]),
        new("open_build", "Build menu", "Colony", [Key.B]),
        new("open_defend", "Defend: place defenses", "Colony", [Key.F]),
        new("open_expedition", "Expedition", "Colony", [Key.E]),
        new("open_items", "Items", "Colony", [Key.I]),
        new("open_log", "Event log filters", "Colony", [Key.L]),
        new("cycle_survivor", "Next survivor", "Colony", [Key.Tab]),
        new("open_power", "Power grid overlay", "Colony", [Key.P]),
        new("open_world_map", "World map", "Colony", [Key.M]),
        new("open_garage", "Garage", "Colony", [Key.G]),
        new("open_radio", "Radio Mast: calls and the trader", "Colony", [Key.Y]),
        new("open_research", "Research (Town Hall)", "Colony", [Key.T]),
        new("open_pantry", "Pantry", "Colony", [Key.K]),
        new("camera_rotate_left", "Rotate camera left", "Colony", [Key.Q]),
        new("camera_rotate_right", "Rotate camera right", "Colony", [Key.R]),
        new("end_turn", "Ready: commit the plan", "Scavenge", [Key.Space, Key.Enter]),
        new("scav_fast_forward", "Skip animations on / off (fast-forward replays)", "Scavenge", [Key.Period]),
        // The number keys go down the Actions panel: the attacks first, then the items (2 attacks: items from 3).
        new("scav_attack_1", "Order 1 (attacks, then items)", "Scavenge", [Key.Key1]),
        new("scav_attack_2", "Order 2", "Scavenge", [Key.Key2]),
        new("scav_attack_3", "Order 3", "Scavenge", [Key.Key3]),
        new("scav_item_1", "Order 4", "Scavenge", [Key.Key4]),
        new("scav_item_2", "Order 5", "Scavenge", [Key.Key5]),
        new("scav_item_3", "Order 6", "Scavenge", [Key.Key6]),
        new("scav_item_4", "Order 7", "Scavenge", [Key.Key7]),
        new("scav_item_5", "Order 8", "Scavenge", [Key.Key8]),
        new("scav_item_6", "Order 9", "Scavenge", [Key.Key9]),
        new("scav_push", "Push", "Scavenge", [Key.O]),
        new("scav_dash", "Dash", "Scavenge", [Key.B]),
        new("scav_recover", "Recover stamina", "Scavenge", [Shift(Key.L)]),
        new("scav_undo", "Clear the latest plan slot", "Scavenge", [Shift(Key.Z)]),
        new("scav_turn_aim", "Turn the aimed pattern", "Scavenge", [Key.Period]),
        new("scav_lookout", "Lookout", "Scavenge", [Key.L]),
        new("scav_loot", "Take all from the nearest pile", "Scavenge", [Key.F]),
        new("scav_salvage", "Salvage the nearest node (a wreck gives parts)", "Scavenge", [Key.I]),
        new("scav_recruit", "Recruit", "Scavenge", [Key.T]),
        new("scav_extract", "Extract", "Scavenge", [Key.X]),
        new("scav_stash", "Load loot into the party's vehicle", "Scavenge", [Key.R]),
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
        // Drone orders that share a letter with an everyday order take Shift: R loads the vehicle, Shift+R is the
        // drone's role; I salvages, Shift+I ferries the cargo drone's hold out.
        new("scav_drone_action", "Drone role: heal / load / fire", "Scavenge", [Shift(Key.R)]),
        new("scav_ferry", "Cargo drone: ferry the load out", "Scavenge", [Shift(Key.I)]),
        new("scav_next_unit", "Select your next unit", "Scavenge", [Key.Tab]),
        new("scav_center", "Centre the camera on the acting unit", "Scavenge", [Key.Home]),
        new("scav_cancel", "Cancel aiming (item, drone, ping, push, dash)", "Scavenge", [Key.Backspace]),
    ];

    /// <summary>A chord: <paramref name="key"/> with Shift held.</summary>
    private static Key Shift(Key key) => (Key)((long)key | (long)KeyModifierMask.MaskShift);

    private static readonly Dictionary<string, Binding> ByAction = All.ToDictionary(b => b.Action);

    /// <summary>Camera panning is not an action (it reads WASD and arrows directly); listed for the settings screen.</summary>
    public static readonly (string Label, string Keys)[] Fixed =
    [
        ("Pan camera", "W A S D / arrows / middle-drag"),
        ("Zoom", "Mouse wheel"),
        ("Rotate camera (scavenge)", "Q / E"),
        ("Cancel aiming (scavenge)", "Right-click / Esc"),
        ("Orbit camera (colony)", "Right-drag"),
        ("Turn the defense being placed", "R (while placing)"),
    ];

    public static void Install()
    {
        foreach (var binding in All)
        {
            if (InputMap.HasAction(binding.Action)) continue;
            InputMap.AddAction(binding.Action);
            foreach (var key in binding.Keys)
                InputMap.ActionAddEvent(binding.Action, new InputEventKey
                {
                    Keycode = (Key)((long)key & (long)KeyModifierMask.CodeMask),
                    ShiftPressed = ((long)key & (long)KeyModifierMask.MaskShift) != 0,
                });
        }
    }

    /// <summary>"B", "Space / Enter", "Shift+R": the keys bound to an action, for tooltips and keycaps.</summary>
    public static string KeyName(string action)
    {
        if (!InputMap.HasAction(action)) return "";
        var keys = InputMap.ActionGetEvents(action).OfType<InputEventKey>()
            .Select(k => OS.GetKeycodeString(k.Keycode != Key.None ? k.GetKeycodeWithModifiers() : k.GetPhysicalKeycodeWithModifiers()));
        return string.Join(" / ", keys);
    }

    /// <summary>
    /// A fresh key press of <paramref name="action"/> (no echo). The exact chord wins: with Shift held, a plain binding
    /// still answers (Shift+Space ends the turn) unless an action of the same context is bound to exactly that chord
    /// (Shift+R is the drone's order, not R's vehicle load).
    /// </summary>
    public static bool Pressed(InputEvent e, string action)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false }) return false;
        if (e.IsAction(action, exactMatch: true)) return true;
        if (!e.IsAction(action, exactMatch: false)) return false;
        var context = ByAction.TryGetValue(action, out var mine) ? mine.Context : "";
        return !All.Any(b => (b.Context == context || b.Context == "Everywhere") && b.Action != action && e.IsAction(b.Action, exactMatch: true));
    }
}
