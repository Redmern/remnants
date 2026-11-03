using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Remnants.Game.Audio;

/// <summary>
/// Adaptive music. A state (<see cref="States"/>) picks a track from <c>sound_events.json</c> "music"; changing to
/// another track waits for the current one's next bar (at most one bar) and crossfades, and re-entering the state
/// or a state on the same track never restarts it. Tracks with stems in the manifest play them in sync and
/// <see cref="Intensity"/> brings the upper stems in. Layers ("layers": <c>linked_layer</c>) play on top of whatever
/// runs and fade in and out on their own. Stings (<see cref="Sting"/>) are one-shot events on the Music bus that duck
/// the rest. Scene scripts (<see cref="ColonyAudio"/>, <see cref="ScavengeAudio"/>) and <see cref="UiAudio"/> set
/// the state; the director holds no game knowledge.
/// </summary>
public partial class MusicDirector : Node
{
    public static readonly string[] States =
    [
        "menu", "colony_day", "colony_night", "colony_tension", "world_map", "scavenge_explore", "scavenge_combat",
        "after_action", "coop_lobby",
    ];

    private const float Silent = -60f, FadeSeconds = 2.5f, LayerFadeSeconds = 3f, MaxWaitSeconds = 4f;

    private sealed class Deck
    {
        public AudioStreamPlayer Player = null!;
        public string? TrackId;
        public SoundAsset? Asset;
        public float Target = Silent;
        public float Speed;
    }

    private readonly Deck[] _decks = new Deck[2];
    private int _front;
    private readonly Dictionary<string, Deck> _layers = new();
    private readonly HashSet<string> _layersOn = [];
    private string? _pendingTrack;
    private double _switchAt;
    private float _intensity;

    /// <summary>The state asked for last (shown in traces and the self-test).</summary>
    public string State { get; private set; } = "";

    /// <summary>0..1: how many stems of a layered track are up (0 = base only).</summary>
    public float Intensity
    {
        get => _intensity;
        set
        {
            _intensity = Mathf.Clamp(value, 0, 1);
            ApplyStems(_decks[_front]);
        }
    }

    public IReadOnlySet<string> LayersOn => _layersOn;

    public override void _ExitTree()
    {
        // Stop the decks before the tree goes (see AudioManager._ExitTree): a track playing at quit leaks its playback.
        foreach (var deck in _decks.Concat(_layers.Values).Where(d => d?.Player is not null))
        {
            deck.Player.Stop();
            deck.Player.Stream = null;
        }
    }

    public string? CurrentTrack => _pendingTrack ?? _decks[_front].TrackId;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        for (var i = 0; i < 2; i++)
        {
            _decks[i] = new Deck { Player = new AudioStreamPlayer { Name = $"Deck{i}", Bus = AudioBuses.Music, VolumeDb = Silent } };
            AddChild(_decks[i].Player);
        }
    }

    public void SetState(string state)
    {
        if (state == State) return;
        State = state;
        Trace($"music state {state}");
        var track = AudioManager.Instance?.Map.Music.GetValueOrDefault(state);
        if (track == CurrentTrack) return;
        _pendingTrack = track;
        // Tracks in one sync group switch at the same position, so they can crossfade at once; others wait for the bar.
        _switchAt = Time.GetTicksMsec() / 1000.0 + (SameGroup(_decks[_front].Asset, track) ? 0 : UntilNextBar());
        if (track is null) _pendingTrack = "";  // fade to silence on the next bar
    }

    public void SetLayer(string layer, bool on)
    {
        // One stem never plays twice (two layers mapped to the same track would only get louder): the first one on wins.
        if (on && !_layersOn.Contains(layer) && SharesStem(layer)) return;
        if (on ? !_layersOn.Add(layer) : !_layersOn.Remove(layer)) return;
        Trace($"music layer {layer} {(on ? "on" : "off")}");
        if (!_layers.TryGetValue(layer, out var deck))
        {
            if (!on || AudioManager.Instance?.Map.Layers.GetValueOrDefault(layer) is not { } id) return;
            deck = new Deck { Player = new AudioStreamPlayer { Name = $"Layer_{layer}", Bus = AudioBuses.Music, VolumeDb = Silent } };
            AddChild(deck.Player);
            _layers[layer] = deck;
            if (!Load(deck, id)) return;
            var front = _decks[_front];
            deck.Player.Play(SyncedWith(front, deck) ? front.Player.GetPlaybackPosition() : 0);
        }
        else if (on && !deck.Player.Playing && deck.TrackId is not null)
            deck.Player.Play(SyncedWith(_decks[_front], deck) ? _decks[_front].Player.GetPlaybackPosition() : 0);
        deck.Target = on ? deck.Asset?.VolumeDb ?? 0 : Silent;
        deck.Speed = (60f) / LayerFadeSeconds;
    }

    /// <summary>
    /// A layer at a level (0..1, its loudness from silent to full): it starts when first above 0 and follows changes in
    /// under a second, so it can swell and calm with the fight (the scavenge chain layer). 0 is the same as off.
    /// </summary>
    public void SetLayerLevel(string layer, float level)
    {
        level = Mathf.Clamp(level, 0, 1);
        if (level <= 0.001f)
        {
            SetLayer(layer, false);
            return;
        }
        SetLayer(layer, true);
        if (!_layersOn.Contains(layer) || !_layers.TryGetValue(layer, out var deck)) return;
        var target = Mathf.Max(Silent, (deck.Asset?.VolumeDb ?? 0) + Mathf.LinearToDb(level));
        if (Mathf.IsEqualApprox(target, deck.Target)) return;
        deck.Target = target;
        deck.Speed = 40f;
    }

    /// <summary>Whether another layer that is on plays the same track as <paramref name="layer"/>.</summary>
    private bool SharesStem(string layer)
    {
        var layers = AudioManager.Instance?.Map.Layers;
        if (layers?.GetValueOrDefault(layer) is not { } id) return false;
        return _layersOn.Any(other => other != layer && layers.GetValueOrDefault(other) == id);
    }

    /// <summary>A one-shot musical sting (extraction, death): the mapped <c>music.{name}</c> event, which ducks the bed.</summary>
    public static void Sting(string name) => AudioManager.Fire($"music.{name}");

    public override void _Process(double delta)
    {
        if (_pendingTrack is { } pending && Time.GetTicksMsec() / 1000.0 >= _switchAt)
        {
            _pendingTrack = null;
            SwitchTo(pending.Length > 0 ? pending : null);
        }
        foreach (var deck in _decks.Concat(_layers.Values)) Fade(deck, (float)delta);
    }

    private void SwitchTo(string? track)
    {
        var old = _decks[_front];
        old.Target = Silent;
        old.Speed = (old.Player.VolumeDb - Silent) / FadeSeconds;
        if (track is null) return;
        _front = 1 - _front;
        var deck = _decks[_front];
        deck.Player.Stop();
        if (!Load(deck, track)) return;
        deck.Player.VolumeDb = Silent;
        deck.Target = deck.Asset?.VolumeDb ?? 0;
        deck.Speed = (deck.Target - Silent) / FadeSeconds;
        ApplyStems(deck);
        deck.Player.Play(SyncedWith(old, deck) ? old.Player.GetPlaybackPosition() : 0);
    }

    private static bool Load(Deck deck, string id)
    {
        deck.TrackId = id;
        deck.Asset = null;
        var audio = AudioManager.Instance;
        if (audio is null) return false;
        if (!audio.Manifest.TryGet(id, out var asset))
        {
            audio.WarnOnce(id, $"Audio: music '{id}' is not in the manifest (silent).", warn: false);
            return false;
        }
        deck.Asset = asset;
        AudioStream? stream;
        if (asset.Stems.Count > 0)
        {
            var sync = new AudioStreamSynchronized { StreamCount = asset.Stems.Count };
            for (var i = 0; i < asset.Stems.Count; i++)
            {
                var stem = audio.LoadFile(asset.Stems[i].File, asset);
                if (stem is not null) AudioManager.EnsureLoop(stem, asset.LoopStart);
                sync.SetSyncStream(i, stem);
            }
            stream = sync;
        }
        else
        {
            stream = audio.LoadFile(asset.Files[0], asset);
            if (stream is not null) AudioManager.EnsureLoop(stream, asset.LoopStart);
        }
        deck.Player.Stream = stream;
        return stream is not null;
    }

    /// <summary>Stems at or below the intensity play at full volume, the rest are silent.</summary>
    private void ApplyStems(Deck deck)
    {
        if (deck.Asset is not { Stems.Count: > 0 } asset || deck.Player.Stream is not AudioStreamSynchronized sync) return;
        for (var i = 0; i < asset.Stems.Count; i++)
            sync.SetSyncStreamVolume(i, asset.Stems[i].Intensity <= _intensity + 0.001f ? 0 : Silent);
    }

    private static void Fade(Deck deck, float delta)
    {
        var player = deck.Player;
        if (!player.Playing) return;
        if (!Mathf.IsEqualApprox(player.VolumeDb, deck.Target))
            player.VolumeDb = Mathf.MoveToward(player.VolumeDb, deck.Target, Mathf.Max(deck.Speed, 10f) * delta);
        if (deck.Target <= Silent && player.VolumeDb <= Silent + 0.5f) player.Stop();
    }

    /// <summary>Whether <paramref name="to"/> should start where <paramref name="from"/> is: both in one sync group, and it is playing.</summary>
    private static bool SyncedWith(Deck from, Deck to) =>
        from.Player.Playing && from.Asset?.SyncGroup is { } group && group == to.Asset?.SyncGroup;

    private static bool SameGroup(SoundAsset? current, string? track) =>
        current?.SyncGroup is { } group && track is not null && AudioManager.Instance?.Manifest.TryGet(track, out var next) == true
        && next.SyncGroup == group;

    /// <summary>Seconds to the current track's next bar line, capped; 0 when nothing plays.</summary>
    private double UntilNextBar()
    {
        var deck = _decks[_front];
        if (!deck.Player.Playing || deck.Asset is not { } asset) return 0;
        var bar = asset.BarSeconds;
        var into = deck.Player.GetPlaybackPosition() % bar;
        return Mathf.Min((float)(bar - into), MaxWaitSeconds);
    }

    private static void Trace(string message)
    {
        if (DebugCapture.HasFlag("--audio-trace") || DebugCapture.HasFlag("--audio-selftest")) GD.Print($"[audio] {message}");
    }
}
