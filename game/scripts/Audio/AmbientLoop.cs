using System.Text;
using Godot;

namespace Remnants.Game.Audio;

/// <summary>
/// One looping ambience sound (a day bed, a campfire, a building's work) that fades toward <see cref="Level"/>
/// (linear 0..1) and frees its player once silent. Flat when made without a position, else a 3D source there.
/// A sound the manifest lacks stays silent; the loop still tracks its level so it starts when asked again.
/// </summary>
public sealed class AmbientLoop
{
    private const float Silent = -60f;

    private readonly Node _parent;
    private Vector3? _position;
    private readonly float _fadePerSecond;
    private Node? _player;

    public AmbientLoop(Node parent, string soundId, Vector3? position = null, float fadeSeconds = 2f)
    {
        _parent = parent;
        SoundId = soundId;
        _position = position;
        _fadePerSecond = 1f / Mathf.Max(fadeSeconds, 0.05f);
    }

    public string SoundId { get; }

    /// <summary>Moves a positional loop (a drone in flight).</summary>
    public Vector3? Position
    {
        get => _position;
        set
        {
            _position = value;
            if (_player is AudioStreamPlayer3D p && value is { } at) p.Position = at;
        }
    }

    /// <summary>Target loudness, linear 0..1 on top of the sound's own volume.</summary>
    public float Level { get; set; }

    private float _current;

    public bool Audible => _player is not null;

    public void Update(float delta)
    {
        _current = Mathf.MoveToward(_current, Level, _fadePerSecond * delta);
        if (_current <= 0.001f)
        {
            Stop();
            return;
        }
        if (_player is null && !Start()) return;
        var db = Mathf.LinearToDb(_current) + _baseDb;
        switch (_player)
        {
            case AudioStreamPlayer p: p.VolumeDb = Mathf.Max(db, Silent); break;
            case AudioStreamPlayer3D p: p.VolumeDb = Mathf.Max(db, Silent); break;
        }
    }

    private float _baseDb;

    private bool Start()
    {
        if (AudioManager.Instance is not { } audio) return false;
        var asset = audio.Manifest.TryGet(SoundId, out var a) ? a : null;
        if (audio.Stream(SoundId, asset) is not { } stream) return false;
        AudioManager.EnsureLoop(stream, asset?.LoopStart ?? 0);
        var tuning = audio.Map.Tuning(SoundId);
        _baseDb = (asset?.VolumeDb ?? 0) + tuning.VolumeDb;
        var bus = tuning.Bus ?? AudioBuses.ForManifest(asset?.Bus ?? "") ?? AudioBuses.Ambience;
        if (_position is { } at)
        {
            var p = new AudioStreamPlayer3D
            {
                Stream = stream, Bus = bus, VolumeDb = Silent, UnitSize = tuning.UnitSize, MaxDistance = tuning.MaxDistance,
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance, PanningStrength = 0.5f, Position = at,
            };
            _parent.AddChild(p);
            p.Play((float)(GD.Randf() * Mathf.Max(stream.GetLength() - 0.1, 0)));
            _player = p;
        }
        else
        {
            var p = new AudioStreamPlayer { Stream = stream, Bus = bus, VolumeDb = Silent };
            _parent.AddChild(p);
            p.Play();
            _player = p;
        }
        if (DebugCapture.HasFlag("--audio-trace")) GD.Print($"[audio] loop {SoundId} on{(_position is { } pos ? $" @ {pos.X:0.#},{pos.Z:0.#}" : "")}");
        return true;
    }

    public void Stop()
    {
        if (_player is null) return;
        _player.QueueFree();
        _player = null;
        if (DebugCapture.HasFlag("--audio-trace")) GD.Print($"[audio] loop {SoundId} off");
    }
}

/// <summary>Event names from Core enums: <c>ExpeditionLaunched</c> → <c>expedition_launched</c>.</summary>
public static class AudioNames
{
    public static string Snake(string pascal)
    {
        var sb = new StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
