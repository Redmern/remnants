using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Game.Scavenge;

namespace Remnants.Game.Audio;

/// <summary>
/// Debug: <c>godot-mono --headless --path game -- --audio-selftest</c> checks the event map headless and quits with
/// 0 (pass) or 1 (fail). It checks that every Core colony and scavenge event type maps to something (a sound or a
/// deliberate []), and that every sound id is well formed. Then it plays a scripted colony (build, upgrade, work,
/// an expedition) and an autopiloted scavenge run through the same naming the screens use, logging each fired
/// event and its sound, and checks the key events chose the expected ids. Missing manifest files are reported but
/// do not fail: the game is meant to stay silent until the assets land.
/// </summary>
public partial class AudioSelfTest : Node
{
    private readonly List<(string Event, string? Sound, bool Audible)> _fired = [];
    private readonly List<string> _failures = [];

    public override void _Ready() => CallDeferred(MethodName.Run);

    private void Run()
    {
        var audio = AudioManager.Instance!;
        audio.Fired += (name, sound, audible) => _fired.Add((name, sound, audible));
        var map = audio.Map;

        foreach (var type in Enum.GetValues<ColonyEventType>())
            if (map.Resolve(ColonyAudio.EventName(type)) is null) Fail($"colony event {type} ({ColonyAudio.EventName(type)}) is not mapped");
        foreach (var state in MusicDirector.States)
            if (!map.Music.ContainsKey(state)) Fail($"music state {state} has no track");
        foreach (var id in map.AllSoundIds())
            if (id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))) Fail($"sound id '{id}' is not snake_case");

        Colony();
        Scavenge();
        foreach (var ui in new[] { "ui.click", "ui.hover", "ui.confirm", "ui.error", "ui.panel_open", "ui.panel_close", "ui.toast" }) audio.FireEvent(ui);

        Expect("colony.constructed", "ui_build_complete");
        Expect("colony.upgraded", "ui_upgrade");
        Expect("colony.expedition_launched", "ui_expedition_launch");
        Expect("scavenge.step.floor", "footstep_concrete");
        Expect("scavenge.extracted", "extraction_marker");
        Expect("ui.click", "ui_click");
        Expect("scavenge.hack_progress", "hack_progress_tick");
        Expect("scavenge.hacked", "hack_success");
        Expect("scavenge.device.turret", "turret_activate");
        Expect("scavenge.device.door", "door_open");
        Expect("scavenge.hack_failed", "hack_fail_alarm");
        Expect("scavenge.drone_deployed", "hack_start");
        Expect("scavenge.marked", "scanner_sweep");
        Expect("scavenge.died.drone", "linked_death");
        if (map.Ambience.GetValueOrDefault("drone") != "drone_buzz_loop") Fail("the drone hum is not drone_buzz_loop");

        WeatherAudio.Set("heavy_rain", 0.7f);
        WeatherAudio.Lightning(0);
        Expect("weather.lightning", "lightning_crack");
        foreach (var id in new[] { "rain_loop", "rain_heavy_loop", "rain_on_roof_loop", "thunder", "lightning_crack", "wind_loop", "dust_storm_loop", "debris_rattle" })
            if (!map.AllSoundIds().Contains(id)) Fail($"weather sound '{id}' is not mapped");
        if (!_fired.Any(f => f.Event == "ui.click" && f.Audible)) Fail("ui.click did not play (its placeholder should)");

        var missing = map.AllSoundIds().Where(id => !audio.Manifest.TryGet(id, out _) && !PlaceholderSounds.Has(id)).ToList();
        // Once the export is synced every mapped id must be in it: a typo or a renamed sound fails here, not in silence.
        if (audio.Manifest.Count > 0)
            foreach (var id in missing) Fail($"'{id}' is mapped but not in the manifest");
        if (audio.Manifest.Count > 0)
        {
            var used = map.AllSoundIds().ToHashSet();
            var unused = audio.Manifest.All.Select(a => a.Id).Where(id => !used.Contains(id)).OrderBy(id => id).ToList();
            GD.Print($"[audio-selftest] manifest sounds not mapped yet ({unused.Count}): {string.Join(", ", unused)}");
            foreach (var asset in audio.Manifest.All)
                if (audio.Stream(asset.Id, asset) is null) Fail($"'{asset.Id}' does not load ({asset.Files[0]})");
        }
        GD.Print($"[audio-selftest] {_fired.Count} events fired, {_fired.Count(f => f.Audible)} audible; " +
                 $"{map.AllSoundIds().Count()} sound ids mapped, {missing.Count} not in the manifest yet.");
        foreach (var f in _failures) GD.PrintErr($"[audio-selftest] FAIL {f}");
        GD.Print(_failures.Count == 0 ? "[audio-selftest] PASS" : $"[audio-selftest] FAILED ({_failures.Count})");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    /// <summary>A short scripted colony: build a farm, upgrade it, put everyone to work, send two adults out, run a day.</summary>
    private void Colony()
    {
        var sim = NewGame.Create(GameDataPaths.Load(), 1, GameDataPaths.LoadScavenge());
        var seen = sim.State.Events.Count > 0 ? sim.State.Events[^1] : null;
        void Flush()
        {
            var events = sim.State.Events;
            var start = seen is null ? 0 : events.LastIndexOf(seen) + 1;
            foreach (var e in events.Skip(start)) AudioManager.Fire(ColonyAudio.EventName(e.Type));
            if (events.Count > 0) seen = events[^1];
        }
        foreach (var res in sim.Data.Resources.Keys) sim.State.Stockpile.Add(res, 500);
        var farm = sim.Execute(new Construct("farm", 8)).EntityId;
        Flush();
        sim.Execute(new Upgrade(sim.State.Buildings.First(b => b.DefId == sim.Data.Rules.TownHallBuildingId).Id));
        Flush();
        var adults = sim.State.Survivors.Where(s => !s.IsChild).ToList();
        foreach (var s in adults.Skip(2))
            if (farm is { } at) sim.Execute(new AssignJob(s.Id, at));
        sim.Execute(new LaunchExpedition("outskirts", adults.Take(2).Select(s => new ExpeditionMember(s.Id, new())).ToList()));
        Flush();
        for (var i = 0; i < sim.Data.Rules.TicksPerDay; i++) sim.Tick();
        Flush();
        // Games start at dawn; run the clock into the night and back out, as Core's day phases have it.
        for (var i = 0; i < sim.Data.Rules.TicksPerDay && sim.Phase != DayPhase.Night; i++) sim.Tick();
        var nightHour = DayNight.Hour(sim.State.Tick, sim.Data.Rules);
        if (!ColonyAudio.IsNight(sim.Phase)) Fail($"no night within a day (at {nightHour}h the phase is {sim.Phase})");
        for (var i = 0; i < sim.Data.Rules.TicksPerDay && sim.Phase != DayPhase.Day; i++) sim.Tick();
        if (ColonyAudio.IsNight(sim.Phase)) Fail($"still night at {DayNight.Hour(sim.State.Tick, sim.Data.Rules)}h ({sim.Phase})");
        GD.Print($"[audio-selftest] colony: night from {nightHour}h, day again at {DayNight.Hour(sim.State.Tick, sim.Data.Rules)}h, tension = {ColonyAudio.Tension(sim)}");
    }

    /// <summary>The debug party walks the outskirts to an exit (autopilot); enemy turns happen as they come.</summary>
    private void Scavenge()
    {
        var sim = NewScavenge.Create(GameDataPaths.LoadScavenge(), "outskirts", 7, [new PartySetup(1, ScavengeSession.DebugParty())]);
        // Every event type, as the screen would name it (no actor: what a side hears from the fog).
        foreach (var type in Enum.GetValues<ScavengeEventType>())
            foreach (var name in ScavengeAudio.EventNames(sim, new ScavengeEvent(1, type, "")))
                if (AudioManager.Instance!.Map.Resolve(name) is null) Fail($"scavenge event {type} ({name}) is not mapped");
        HackAndDrone(sim);
        Autopilot.Play(sim, 1);
        foreach (var e in sim.State.Events)
            foreach (var name in ScavengeAudio.EventNames(sim, e))
                AudioManager.Fire(name);
    }

    /// <summary>Hacking and scout-drone events as Core logs them, against a turret and a door placed on the map.</summary>
    private static void HackAndDrone(ScavengeSim sim)
    {
        var state = sim.State;
        var hacker = state.Units.First(u => u.PlayerId == 1);
        var turretAt = new Pos(1, 1);
        var doorAt = new Pos(2, 1);
        Hacking.Place(sim.Data, state, DeviceKind.Turret, turretAt);
        Hacking.Place(sim.Data, state, DeviceKind.Door, doorAt);
        var drone = new Unit { Id = 90_000, Name = "Drone", Kind = UnitKind.Drone, Side = hacker.Side, PlayerId = 1, OperatorId = hacker.Id, Pos = new Pos(3, 3) };
        state.Units.Add(drone);
        ScavengeEvent[] script =
        [
            new(1, ScavengeEventType.HackProgress, "", hacker.Id, Pos: turretAt),
            new(1, ScavengeEventType.Hacked, "", hacker.Id, Pos: turretAt),
            new(1, ScavengeEventType.Hacked, "", hacker.Id, Pos: doorAt),
            new(1, ScavengeEventType.HackFailed, "", hacker.Id, Pos: doorAt),
            new(1, ScavengeEventType.DroneDeployed, "", hacker.Id, Pos: drone.Pos),
            new(1, ScavengeEventType.Marked, "", drone.Id, Pos: drone.Pos),
            new(1, ScavengeEventType.Died, "", drone.Id, Pos: drone.Pos),
        ];
        foreach (var e in script)
            foreach (var name in ScavengeAudio.EventNames(sim, e))
                AudioManager.Fire(name);
        state.Units.Remove(drone);
    }

    private void Expect(string evt, string sound)
    {
        if (!_fired.Any(f => f.Event == evt)) Fail($"{evt} never fired");
        else if (!_fired.Any(f => f.Event == evt && f.Sound == sound)) Fail($"{evt} chose {_fired.First(f => f.Event == evt).Sound ?? "nothing"}, expected {sound}");
    }

    private void Fail(string message) => _failures.Add(message);
}
