using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Game.Scavenge;
using Remnants.Game.Scavenge.Ui;

namespace Remnants.Game.Audio;

/// <summary>
/// The scavenge screen's sound, added by <see cref="ScavengeScreen"/>, which hands it each event as the replay
/// reaches it (<see cref="Step"/>: an attack's at its clip's impact / fire event), so sounds land with the animation;
/// footfalls come from the walk clips (<see cref="Footstep"/>). Events are already what the local side may
/// hear (<see cref="FogLog"/>). Names: <c>scavenge.{event}</c>, qualified where it matters — moves and deaths by who
/// (<c>scavenge.moved.husk</c>, <c>scavenge.died.survivor</c>), attacks by action (<c>scavenge.attack.aimed_shot</c>,
/// then <c>scavenge.hit</c> / <c>scavenge.miss</c>), barks by enemy, overwatch shots apart from going on overwatch,
/// hacked devices by kind; the map falls back to the bare name. New contacts on the local side's map fire
/// <c>scavenge.contact.{kind}</c>. Also runs the zone ambience (zone → tags → loops), drone and flare loops, and the
/// music: scavenge_combat while hostiles are in sight or attacked this round, else scavenge_explore, the Linked
/// layer once any Linked has been seen, the extraction sting and after_action when the run is over.
/// </summary>
public partial class ScavengeAudio : Node3D
{
    private ScavengePresenter _presenter = null!;
    private TacticalCamera _camera = null!;
    private ScavengeHud _hud = null!;
    private readonly AudioListener3D _listener = new() { Name = "Listener" };
    private readonly List<AmbientLoop> _beds = [];
    private readonly Dictionary<int, AmbientLoop> _drones = new();
    private readonly HashSet<(Pos, int, ContactKind)> _contacts = [];
    private readonly HashSet<Pos> _hacking = [];
    private readonly Dictionary<Pos, AmbientLoop> _flares = new();
    private int _attackRound = -1, _combatRound = -10;
    private bool _linkedSeen, _stung;
    private double _since;

    public void Setup(ScavengePresenter presenter, TacticalCamera camera, ScavengeHud hud)
    {
        _presenter = presenter;
        _camera = camera;
        _hud = hud;
        AddChild(_listener);
        _listener.MakeCurrent();
        presenter.Feedback += (_, error) =>
        {
            if (error) AudioManager.Fire("ui.error");
        };
        // Contacts already on the map at the start are not news.
        foreach (var c in presenter.Contacts) _contacts.Add((c.Pos, c.Round, c.Kind));

        if (AudioManager.Instance?.Map is { } map && map.ZoneTags.GetValueOrDefault(presenter.State.ZoneId) is { } tags)
            foreach (var tag in tags)
                if (map.ZoneAmbience.GetValueOrDefault(tag) is { Length: > 0 } id) _beds.Add(new AmbientLoop(this, id, fadeSeconds: 4) { Level = 1 });
        AudioManager.Instance?.Music.SetLayer("linked_layer", false);
        Refresh();
    }

    public override void _ExitTree()
    {
        AudioManager.Instance?.Music.SetLayer("linked_layer", false);
        AudioManager.Instance?.Music.SetLayer("contact_layer", false);
    }

    /// <summary>One replayed event: its sound, at its tile.</summary>
    public void Step(ScavengeEvent e)
    {
        var at = e.Pos is { } p ? TileGrid.ToWorld(p) + Vector3.Up : (Vector3?)null;
        // Moves keep the unit's own sound (servos); their footfalls come from the walk clips (Footstep).
        foreach (var name in EventNames(_presenter.Sim, e))
            if (e.Type != ScavengeEventType.Moved || !name.StartsWith("scavenge.step")) AudioManager.Fire(name, at);
        // The first work on a device starts the hack; later rounds tick.
        if (e.Type == ScavengeEventType.HackProgress && e.Pos is { } device && _hacking.Add(device)) AudioManager.Fire("scavenge.hack_start", at);
        if (e.Type is ScavengeEventType.Hacked or ScavengeEventType.HackFailed && e.Pos is { } done) _hacking.Remove(done);
        if (e.Type is ScavengeEventType.Attacked or ScavengeEventType.Missed or ScavengeEventType.Ambushed) _attackRound = e.Round;
        CheckContacts();
    }

    /// <summary>
    /// A footfall (a walk clip's step event, or one per move for a rigless or unseen unit): the unit kind's own feet
    /// (<c>scavenge.step.husk</c>) when the map has them, else the ground under it for walkers (<c>scavenge.step.rubble</c>).
    /// </summary>
    public void Footstep(int unitId, Vector3 at)
    {
        if (_presenter.State.FindUnit(unitId) is not { } unit || AudioManager.Instance?.Map is not { } map) return;
        var own = $"scavenge.step.{Who(unit)}";
        var tile = TileGrid.FromWorld(at);
        var name = map.Events.ContainsKey(own) ? own
            : Walks(unit) && _presenter.State.Map.InBounds(tile) ? $"scavenge.step.{AudioNames.Snake(_presenter.State.Map[tile].ToString())}"
            : null;
        if (name is not null) AudioManager.Fire(name, at + Vector3.Up * 0.2f);
    }

    /// <summary>The event names one scavenge event fires, most specific first in each name (see the class summary).</summary>
    public static IEnumerable<string> EventNames(ScavengeSim sim, ScavengeEvent e)
    {
        var state = sim.State;
        var actor = e.UnitId is { } a ? state.FindUnit(a) : null;
        var target = e.TargetId is { } t ? state.FindUnit(t) : null;
        var type = AudioNames.Snake(e.Type.ToString());
        switch (e.Type)
        {
            case ScavengeEventType.Attacked or ScavengeEventType.Missed:
                yield return $"scavenge.attack.{(actor is null ? "unseen" : ActionId(sim, actor, e.Message) ?? "unknown")}";
                yield return e.Type == ScavengeEventType.Attacked ? $"scavenge.hit.{Who(target)}" : "scavenge.miss";
                break;
            case ScavengeEventType.Moved or ScavengeEventType.Died or ScavengeEventType.BledOut or ScavengeEventType.Downed
                or ScavengeEventType.Injured or ScavengeEventType.LimbLost:
                // Injured / LimbLost / Downed / Died name the victim as the unit.
                var name = e.Type == ScavengeEventType.BledOut ? "died" : type;
                yield return $"scavenge.{name}.{Who(actor)}";
                // Walkers also step on the tile's ground (concrete floor, rubble, gravel cover, puddles).
                if (e.Type == ScavengeEventType.Moved && actor is not null && e.Pos is { } to && state.Map.InBounds(to) && Walks(actor))
                    yield return $"scavenge.step.{AudioNames.Snake(state.Map[to].ToString())}";
                break;
            case ScavengeEventType.Spotted or ScavengeEventType.Flanked or ScavengeEventType.Retreated:
                yield return actor?.Kind == UnitKind.Enemy ? $"scavenge.bark.{Who(actor)}" : $"scavenge.{type}";
                break;
            case ScavengeEventType.Overwatch:
                // The same event type both sets the stance and reports a reaction shot (that one has a target).
                yield return e.TargetId is null ? "scavenge.overwatch" : "scavenge.overwatch_trigger";
                break;
            default:
                yield return $"scavenge.{type}";
                // A hacked device also sounds as it comes over to the hacker: a turret powering up, a door opening.
                if (e.Type == ScavengeEventType.Hacked && e.Pos is { } at && state.DeviceAt(at) is { } device)
                    yield return $"scavenge.device.{AudioNames.Snake(device.Kind.ToString())}";
                break;
        }
    }

    /// <summary>Units with their own movement sound (servos, a stomp, rotors) instead of footsteps.</summary>
    private static readonly HashSet<string> NoFootsteps = ["linked_drone", "linked_warden", "turret", "drone", "device"];

    private static bool Walks(Unit unit) => !NoFootsteps.Contains(Who(unit));

    /// <summary>Who an event is about, for the map: the enemy def id, else the unit kind (<c>survivor</c>, <c>npc</c>, <c>drone</c>).</summary>
    private static string Who(Unit? unit) =>
        unit is null ? "unseen" : unit.EnemyDefId ?? AudioNames.Snake(unit.Kind.ToString());

    /// <summary>The action an attack used: its name is in the log line ("… (Aimed Shot)." / "… (Swing, 72%)").</summary>
    public static string? ActionId(ScavengeSim sim, Unit attacker, string message)
    {
        var actions = sim.Profile(attacker).Actions;
        return actions.Where(a => message.Contains($"({a.Name})") || message.Contains($"({a.Name},") || message.Contains($": {a.Name} at"))
            .OrderByDescending(a => a.Name.Length).FirstOrDefault()?.Id
            ?? (attacker.Equipment.GetValueOrDefault(EquipSlot.Weapon) is { } weapon && sim.Data.Items.TryGetValue(weapon, out var item)
                ? item.Actions.FirstOrDefault()?.Id : null);
    }

    public override void _Process(double delta)
    {
        if (_presenter is null) return;
        var eye = _camera.Camera.GlobalTransform;
        _listener.GlobalTransform = new Transform3D(eye.Basis, _camera.GlobalPosition + Vector3.Up * (6 + _camera.Camera.Size * 0.35f));
        _since += delta;
        if (_since >= 0.3)
        {
            _since = 0;
            Refresh();
        }
        var dt = (float)delta;
        foreach (var bed in _beds) bed.Update(dt);
        foreach (var drone in _drones.Values) drone.Update(dt);
        foreach (var flare in _flares.Values) flare.Update(dt);
    }

    private void Refresh()
    {
        var state = _presenter.State;
        var hostiles = state.Units.Where(u => u.OnMap && u.Kind == UnitKind.Enemy && _presenter.CanSee(u)).ToList();
        if (!_linkedSeen && hostiles.Any(u => u.EnemyDefId?.StartsWith("linked") == true)) _linkedSeen = true;

        var music = AudioManager.Instance?.Music;
        if (music is not null)
        {
            if (_presenter.Done || _hud.Result.Visible)
            {
                if (!_stung && state.Units.Any(u => _presenter.IsMine(u) && u.Status is UnitStatus.Extracted or UnitStatus.Evacuated))
                    MusicDirector.Sting("extraction_sting");
                _stung = true;
                music.SetState("after_action");
                music.SetLayer("linked_layer", false);
                music.SetLayer("contact_layer", false);
            }
            else
            {
                // Combat holds for a round after the last sighting or shot, so the music does not flicker between turns.
                if (hostiles.Count > 0 || _attackRound == state.Round) _combatRound = state.Round;
                var combat = state.Round - _combatRound <= 1;
                music.SetState(combat ? "scavenge_combat" : "scavenge_explore");
                music.Intensity = combat ? Mathf.Clamp(hostiles.Count / 4f, 0.25f, 1) : 0;
                music.SetLayer("linked_layer", _linkedSeen);
                // Something heard but not seen: the combat layer creeps in under the explore track.
                music.SetLayer("contact_layer", !combat && _presenter.Contacts.Any(c => c.Round >= state.Round - 1));
            }
        }
        SyncDrones();
        CheckContacts();
    }

    /// <summary>"Something is there" markers new on the local side's map.</summary>
    private void CheckContacts()
    {
        foreach (var c in _presenter.Contacts)
            if (_contacts.Add((c.Pos, c.Round, c.Kind)))
                AudioManager.Fire($"scavenge.contact.{AudioNames.Snake(c.Kind.ToString())}", TileGrid.ToWorld(c.Pos) + Vector3.Up);
    }

    /// <summary>A hum on each drone in flight the side can see (scout drones and Linked drones), a hiss on each burning flare.</summary>
    private void SyncDrones()
    {
        var map = AudioManager.Instance?.Map;
        if (map?.Ambience.GetValueOrDefault("drone") is { Length: > 0 } id)
        {
            var flying = _presenter.State.Units
                .Where(u => u.OnMap && (u.Kind == UnitKind.Drone || u.EnemyDefId == "linked_drone") && _presenter.CanSee(u)).ToDictionary(u => u.Id);
            foreach (var (unitId, unit) in flying)
            {
                if (!_drones.TryGetValue(unitId, out var loop)) _drones[unitId] = loop = new AmbientLoop(this, id, fadeSeconds: 0.5f);
                loop.Position = TileGrid.ToWorld(unit.Pos) + Vector3.Up * 2;
                loop.Level = 1;
            }
            foreach (var (unitId, loop) in _drones)
                if (!flying.ContainsKey(unitId)) loop.Level = 0;
        }
        if (map?.Ambience.GetValueOrDefault("flare") is { Length: > 0 } hiss)
        {
            var burning = _presenter.State.Flares.Where(f => f.UntilRound >= _presenter.State.Round).Select(f => f.Pos).ToHashSet();
            foreach (var at in burning)
                if (!_flares.ContainsKey(at)) _flares[at] = new AmbientLoop(this, hiss, TileGrid.ToWorld(at) + Vector3.Up * 0.5f, fadeSeconds: 0.3f) { Level = 1 };
            foreach (var (at, loop) in _flares)
                if (!burning.Contains(at)) loop.Level = 0;
        }
    }
}
