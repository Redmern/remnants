using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Game.Hud;
using FileAccess = Godot.FileAccess;

namespace Remnants.Game.Audio;

/// <summary>
/// The audio autoload (<c>project.godot</c> [autoload]). Owns the buses, the manifest and the event map, pooled 2D and
/// 3D players, and the <see cref="MusicDirector"/>. Game code fires named events (<see cref="Fire"/>) — the map picks
/// the sound — or plays a sound id directly (<see cref="Play"/>, <see cref="PlayAt"/>). Each play picks a random
/// variant with pitch and volume jitter, and respects the id's cooldown and voice limit. A sound the manifest lacks
/// stays silent (or uses a <see cref="PlaceholderSounds"/> stand-in) and is logged once.
/// <para>Debug: <c>--audio-trace</c> prints every fired event and the sound it chose; <see cref="Fired"/> reports them
/// to code (the self-test, the visual cues).</para>
/// </summary>
public partial class AudioManager : Node
{
    private const int Pool2D = 16, Pool3D = 24;

    private readonly List<AudioStreamPlayer> _players2D = [];
    private readonly List<AudioStreamPlayer3D> _players3D = [];
    private readonly Dictionary<Node, (string Id, ulong Started)> _playing = new();
    private readonly Dictionary<string, ulong> _lastPlayed = new();
    private readonly Dictionary<string, int> _lastVariant = new();
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private readonly HashSet<string> _warned = [];
    private readonly RandomNumberGenerator _rng = new();

    private float _duckDb, _duckTarget;
    private double _duckHoldUntil, _duckRelease = 1.5;
    private bool _trace;

    public static AudioManager? Instance { get; private set; }

    public AudioManifest Manifest { get; private set; } = new();
    public SoundEventMap Map { get; private set; } = new();
    public MusicDirector Music { get; private set; } = null!;

    /// <summary>An event fired: its name, the sound the map chose (null if none) and whether it was heard (false when
    /// the file is missing, or it is cooling down or at its voice limit).</summary>
    public event Action<string, string?, bool>? Fired;

    /// <summary>An event with a visual cue fired (cue name, world position if any), for <see cref="SoundCues"/>.</summary>
    public event Action<string, Vector3?>? Cued;

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        AudioBuses.Ensure();
        _trace = DebugCapture.HasFlag("--audio-trace") || DebugCapture.HasFlag("--audio-selftest");
        Manifest = AudioManifest.Load();
        Map = SoundEventMap.Load();
        GD.Print($"Audio: {Manifest.Count} sounds from {Manifest.Source ?? "(no manifest yet)"}, {Map.Events.Count} events mapped.");

        for (var i = 0; i < Pool2D; i++)
        {
            var p = new AudioStreamPlayer { Name = $"Sfx2D{i}" };
            p.Finished += () => _playing.Remove(p);
            _players2D.Add(p);
            AddChild(p);
        }
        for (var i = 0; i < Pool3D; i++)
        {
            var p = new AudioStreamPlayer3D { Name = $"Sfx3D{i}", AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, PanningStrength = 0.6f };
            p.Finished += () => _playing.Remove(p);
            _players3D.Add(p);
            AddChild(p);
        }
        Music = new MusicDirector { Name = "Music" };
        AddChild(Music);
        AddChild(new UiAudio { Name = "UiAudio" });
        AddChild(new SoundCues { Name = "SoundCues" });
        AddChild(new WeatherAudio { Name = "Weather" });
        if (DebugCapture.HasFlag("--audio-selftest")) AddChild(new AudioSelfTest { Name = "AudioSelfTest" });
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) SetFocused(false);
        else if (what == NotificationApplicationFocusIn) SetFocused(true);
    }

    private static void SetFocused(bool focused)
    {
        GameSettings.Unfocused = !focused;
        GameSettings.ApplyVolumes();
    }

    public override void _Process(double delta)
    {
        // Ducking: snap down, hold, then ease back to 0 dB.
        var now = Time.GetTicksMsec() / 1000.0;
        if (now >= _duckHoldUntil) _duckTarget = 0;
        var step = (float)delta * (_duckTarget < _duckDb ? 60f : 9f / (float)Math.Max(_duckRelease, 0.1));
        var next = Mathf.MoveToward(_duckDb, _duckTarget, step);
        if (!Mathf.IsEqualApprox(next, _duckDb)) AudioBuses.Duck(_duckDb = next);
    }

    /// <summary>Fires a mapped event (<see cref="SoundEventMap"/>): 2D, or positional when <paramref name="at"/> is given.</summary>
    public static void Fire(string name, Vector3? at = null) => Instance?.FireEvent(name, at);

    /// <summary>Plays a sound id flat (UI, stings).</summary>
    public static void Play(string soundId) => Instance?.PlaySound(soundId, null, "");

    /// <summary>Plays a sound id in the 3D world at <paramref name="position"/>.</summary>
    public static void PlayAt(string soundId, Vector3 position) => Instance?.PlaySound(soundId, position, "");

    public void FireEvent(string name, Vector3? at = null)
    {
        var ev = Map.Resolve(name);
        string? chosen = null;
        var audible = false;
        if (ev is null) WarnOnce($"event:{name}", $"Audio: event '{name}' is not in sound_events.json (silent).");
        else
        {
            if (ev.Sounds.Count > 0)
            {
                chosen = ev.Sounds[ev.Sounds.Count == 1 ? 0 : _rng.RandiRange(0, ev.Sounds.Count - 1)];
                audible = PlaySound(chosen, at, name);
            }
            if (ev.Cue is { } cue) Cued?.Invoke(cue, at);
        }
        if (_trace)
            GD.Print($"[audio] {name} -> {chosen ?? "(nothing)"}{(chosen is not null && !audible ? " (silent)" : "")}{(at is { } p ? $" @ {p.X:0.#},{p.Z:0.#}" : "")}");
        Fired?.Invoke(name, chosen, audible);
    }

    /// <summary>Plays one variant of <paramref name="soundId"/>. False when it is missing, cooling down or at its voice limit.</summary>
    public bool PlaySound(string soundId, Vector3? at, string eventName)
    {
        var tuning = Map.Tuning(soundId);
        var now = Time.GetTicksMsec();
        if (_lastPlayed.TryGetValue(soundId, out var last) && now - last < (ulong)(tuning.Cooldown * 1000)) return false;
        if (_playing.Values.Count(v => v.Id == soundId) >= Math.Max(1, tuning.MaxVoices)) return false;
        var asset = Manifest.TryGet(soundId, out var a) ? a : null;
        if (Stream(soundId, asset) is not { } stream) return false;

        _lastPlayed[soundId] = now;
        var volume = tuning.VolumeDb + (asset?.VolumeDb ?? 0) + _rng.RandfRange(-tuning.VolumeJitter, tuning.VolumeJitter);
        var pitch = 1 + _rng.RandfRange(-tuning.PitchJitter, tuning.PitchJitter);
        var bus = tuning.Bus ?? AudioBuses.ForCategory(asset?.Category ?? (soundId.StartsWith("ui_") ? "ui" : "sfx"));
        if (at is { } pos)
        {
            var p = Take(_players3D);
            p.Stream = stream;
            p.Bus = bus;
            p.VolumeDb = volume;
            p.PitchScale = pitch;
            p.UnitSize = tuning.UnitSize;
            p.MaxDistance = tuning.MaxDistance;
            p.GlobalPosition = pos;
            Start(p, soundId, () => p.Play());
        }
        else
        {
            var p = Take(_players2D);
            p.Stream = stream;
            p.Bus = bus;
            p.VolumeDb = volume;
            p.PitchScale = pitch;
            Start(p, soundId, () => p.Play());
        }
        if (tuning.Duck is { } duck) Duck(duck);
        return true;
    }

    /// <summary>Music and ambience dip under an important sound.</summary>
    public void Duck(DuckSpec duck)
    {
        _duckTarget = Math.Min(_duckTarget, duck.Db);
        _duckHoldUntil = Math.Max(_duckHoldUntil, Time.GetTicksMsec() / 1000.0 + duck.Hold);
        _duckRelease = duck.Release;
    }

    /// <summary>
    /// The stream for a sound: a random variant (not the last one when there are several) from the manifest, else a
    /// placeholder, else null (logged once). Loops get their loop points.
    /// </summary>
    public AudioStream? Stream(string soundId, SoundAsset? asset = null)
    {
        asset ??= Manifest.TryGet(soundId, out var a) ? a : null;
        if (asset is null)
        {
            if (PlaceholderSounds.Get(soundId) is { } placeholder)
            {
                WarnOnce(soundId, $"Audio: '{soundId}' is not in the manifest; using a placeholder.", warn: false);
                return placeholder;
            }
            WarnOnce(soundId, $"Audio: '{soundId}' is not in the manifest (silent).", warn: false);
            return null;
        }
        var index = 0;
        if (asset.Files.Count > 1)
        {
            index = _rng.RandiRange(0, asset.Files.Count - 2);
            if (_lastVariant.TryGetValue(soundId, out var previous) && index >= previous) index++;
            _lastVariant[soundId] = index;
        }
        var stream = LoadFile(asset.Files[index], asset);
        if (stream is null) WarnOnce($"file:{asset.Files[index]}", $"Audio: cannot load {asset.Files[index]} for '{soundId}' (silent).");
        return stream;
    }

    /// <summary>Loads (and caches) a file: imported resource first, else a raw .ogg / .wav read at runtime.</summary>
    public AudioStream? LoadFile(string path, SoundAsset? asset = null)
    {
        if (_streams.TryGetValue(path, out var cached)) return cached;
        AudioStream? stream = null;
        if (ResourceLoader.Exists(path)) stream = ResourceLoader.Load<AudioStream>(path);
        else if (FileAccess.FileExists(path))
            stream = path.GetExtension().ToLowerInvariant() switch
            {
                "ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                "wav" => AudioStreamWav.LoadFromFile(path),
                "mp3" => AudioStreamMP3.LoadFromFile(path),
                _ => null,
            };
        if (stream is not null && asset is { Loop: true }) SetLoop(stream, asset);
        _streams[path] = stream;
        return stream;
    }

    private static void SetLoop(AudioStream stream, SoundAsset asset)
    {
        switch (stream)
        {
            case AudioStreamOggVorbis ogg:
                ogg.Loop = true;
                ogg.LoopOffset = asset.LoopStart;
                break;
            case AudioStreamMP3 mp3:
                mp3.Loop = true;
                mp3.LoopOffset = asset.LoopStart;
                break;
            case AudioStreamWav wav:
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = (int)(asset.LoopStart * wav.MixRate);
                wav.LoopEnd = asset.LoopEnd > asset.LoopStart ? (int)(asset.LoopEnd * wav.MixRate) : (int)(wav.GetLength() * wav.MixRate);
                break;
        }
    }

    /// <summary>Beds and music always loop, even when the manifest leaves the loop flag out.</summary>
    public static void EnsureLoop(AudioStream stream, double start)
    {
        switch (stream)
        {
            case AudioStreamOggVorbis ogg when !ogg.Loop:
                ogg.Loop = true;
                ogg.LoopOffset = start;
                break;
            case AudioStreamMP3 mp3 when !mp3.Loop:
                mp3.Loop = true;
                mp3.LoopOffset = start;
                break;
            case AudioStreamWav wav when wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled:
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = (int)(start * wav.MixRate);
                wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
                break;
        }
    }

    /// <summary>A free player, else the one that has played longest.</summary>
    private T Take<T>(List<T> pool) where T : Node
    {
        foreach (var p in pool)
            if (!_playing.ContainsKey(p)) return p;
        var oldest = pool.OrderBy(p => _playing[p].Started).First();
        _playing.Remove(oldest);
        return oldest;
    }

    private void Start(Node player, string id, Action play)
    {
        _playing[player] = (id, Time.GetTicksMsec());
        play();
    }

    public void WarnOnce(string key, string message, bool warn = true)
    {
        if (!_warned.Add(key)) return;
        if (warn) GD.PushWarning(message);
        else GD.Print(message);
    }
}
