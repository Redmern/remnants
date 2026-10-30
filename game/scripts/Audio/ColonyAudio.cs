using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Game.Colony;
using Remnants.Game.Colony.Ui;

namespace Remnants.Game.Audio;

/// <summary>
/// The colony's sound, added by <see cref="ColonyScreen"/>: fires <c>colony.{event}</c> for each new Core event
/// (constructed, upgraded, crafted, level_up, perk_learned, born, died, left, expedition_launched, …), placed at the
/// building when the event names one; <c>ui.error</c> for refused commands. Runs the ambience — a day / night bed
/// following the in-game clock, the campfire on the commons, and a work loop at each building with workers present —
/// and sets the music: world_map or after_action while those are open, colony_tension under pressure (people
/// homeless, beds over capacity, someone starving, under three days of food), else colony_day / colony_night (held at
/// least <see cref="MusicDwellSeconds"/>, since a colony day passes in seconds; the beds follow the clock).
/// The listener rides above the camera's focus at a height that grows with zoom, so zooming out quietens the town.
/// </summary>
public partial class ColonyAudio : Node3D
{
    private ColonySession _session = null!;
    private TownLayout _layout = null!;
    private OrbitCamera _camera = null!;
    private ColonyHud _hud = null!;
    private readonly AudioListener3D _listener = new() { Name = "Listener" };
    private AmbientLoop? _day, _night, _campfire;
    private readonly Dictionary<int, AmbientLoop> _work = new();
    private ColonyEvent? _lastSeen;

    /// <summary>Real seconds the colony music keeps to day or night before following the clock again.</summary>
    private const double MusicDwellSeconds = 90;

    private bool _musicNight;
    private double _musicFlippedAt = double.NegativeInfinity;
    private double _since;

    public void Setup(ColonySession session, TownLayout layout, OrbitCamera camera, ColonyHud hud)
    {
        _session = session;
        _layout = layout;
        _camera = camera;
        _hud = hud;
        AddChild(_listener);
        _listener.MakeCurrent();
        session.Feedback += (_, error) =>
        {
            if (error) AudioManager.Fire("ui.error");
        };
        session.Replaced += Reset;
        session.PhaseChanged += _ => Refresh();
        Reset();

        var map = AudioManager.Instance?.Map;
        if (map?.Ambience.GetValueOrDefault("colony_day") is { } day) _day = new AmbientLoop(this, day, fadeSeconds: 6);
        if (map?.Ambience.GetValueOrDefault("colony_night") is { } night) _night = new AmbientLoop(this, night, fadeSeconds: 6);
        if (map?.Ambience.GetValueOrDefault("campfire") is { } fire && Hall() is { } hall)
            _campfire = new AmbientLoop(this, fire, layout.Commons(hall.Plot).Campfire + Vector3.Up * 0.5f) { Level = 1 };
        AudioManager.Instance?.Music.SetLayer("linked_layer", false);
        Refresh();
    }

    /// <summary>After a load: the loaded history is not news.</summary>
    private void Reset()
    {
        var events = _session.Sim.State.Events;
        _lastSeen = events.Count > 0 ? events[^1] : null;
        foreach (var loop in _work.Values) loop.Stop();
        _work.Clear();
    }

    public override void _Process(double delta)
    {
        if (_session is null) return;
        FollowCamera();
        FireNewEvents();
        _since += delta;
        if (_since >= 0.5)
        {
            _since = 0;
            Refresh();
        }
        var dt = (float)delta;
        _day?.Update(dt);
        _night?.Update(dt);
        _campfire?.Update(dt);
        foreach (var loop in _work.Values) loop.Update(dt);
    }

    private void FollowCamera()
    {
        var eye = _camera.Camera.GlobalTransform;
        var distance = eye.Origin.DistanceTo(_camera.GlobalPosition);
        _listener.GlobalTransform = new Transform3D(eye.Basis, _camera.GlobalPosition + Vector3.Up * (4 + distance * 0.4f));
    }

    private void FireNewEvents()
    {
        var events = _session.Sim.State.Events;
        if (events.Count == 0 || ReferenceEquals(events[^1], _lastSeen)) return;
        // Core caps the list, so find where we left off by identity rather than index (as the event log does).
        var start = _lastSeen is null ? 0 : events.LastIndexOf(_lastSeen) + 1;
        _lastSeen = events[^1];
        foreach (var e in events.Skip(start))
            AudioManager.Fire(EventName(e.Type), e.BuildingId is { } b && _session.Sim.State.FindBuilding(b) is { } at ? _layout.Center(at.Plot) : null);
    }

    public static string EventName(ColonyEventType type) => $"colony.{AudioNames.Snake(type.ToString())}";

    private void Refresh()
    {
        var sim = _session.Sim;
        var night = IsNight(_session.Phase);
        if (_day is not null) _day.Level = night ? 0 : 1;
        if (_night is not null) _night.Level = night ? 1 : 0;
        // A colony day is a few real minutes at most (12 s at 1x), so the music only turns day/night after holding.
        var now = Time.GetTicksMsec() / 1000.0;
        if (night != _musicNight && now - _musicFlippedAt >= MusicDwellSeconds)
        {
            _musicNight = night;
            _musicFlippedAt = now;
        }
        AudioManager.Instance?.Music.SetState(
            _hud.WorldMap.Visible ? "world_map"
            : _hud.Report.Visible ? "after_action"
            : Tension(sim) ? "colony_tension"
            : _musicNight ? "colony_night" : "colony_day");
        SyncWork();
    }

    /// <summary>
    /// The one place audio decides day or night (bed and music both ask here), from Core's day phase
    /// (<see cref="DayNight.PhaseAt"/>): the night bed and music for <see cref="DayPhase.Night"/>, the day ones for
    /// dawn, day and dusk.
    /// </summary>
    public static bool IsNight(DayPhase phase) => phase == DayPhase.Night;

    /// <summary>The colony is under pressure: beds or food are short, or someone is starving or sleeping rough.</summary>
    public static bool Tension(ColonySim sim) =>
        sim.State.Present.Any(s => s.StarvingTicks > 0 || s.HomelessTicks > 0)
        || Bonds.FreeBedsForBabies(sim.State, sim.Data) < 0
        || sim.FoodDays < 3;

    /// <summary>A work loop per building with workers on site, louder with more of them; the rest fade out.</summary>
    private void SyncWork()
    {
        var map = AudioManager.Instance?.Map;
        if (map is null || map.WorkLoops.Count == 0) return;
        var sim = _session.Sim;
        var workers = sim.State.Present.Where(s => s.AssignedBuildingId is not null)
            .GroupBy(s => s.AssignedBuildingId!.Value).ToDictionary(g => g.Key, g => g.Count());
        foreach (var building in sim.State.Buildings)
        {
            var count = workers.GetValueOrDefault(building.Id);
            if (!_work.TryGetValue(building.Id, out var loop))
            {
                if (count == 0) continue;
                if ((map.WorkLoops.TryGetValue(building.DefId, out var own) ? own : map.WorkLoops.GetValueOrDefault("default")) is not { Length: > 0 } id)
                    continue;
                loop = new AmbientLoop(this, id, _layout.Center(building.Plot) + Vector3.Up, fadeSeconds: 1.5f);
                _work[building.Id] = loop;
            }
            // Paused time: the town holds still, so the work does too.
            loop.Level = _session.Speed == 0 ? 0 : count switch { 0 => 0, 1 => 0.55f, 2 => 0.8f, _ => 1 };
        }
        foreach (var gone in _work.Keys.Where(id => sim.State.FindBuilding(id) is null).ToList())
        {
            _work[gone].Stop();
            _work.Remove(gone);
        }
    }

    private Building? Hall() => _session.Sim.State.Buildings.FirstOrDefault(b => b.DefId == _session.Sim.Data.Rules.TownHallBuildingId);
}
