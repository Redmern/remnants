using System;
using Godot;
using FileAccess = Godot.FileAccess;
using Remnants.Core.Colony;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;
using Remnants.Game.Hud;

namespace Remnants.Game.Colony;

/// <summary>
/// Presenter between the colony screen and Core: owns the <see cref="ColonySim"/>, turns frame time into
/// fixed sim ticks at the chosen speed, and is the only place the UI sends commands through.
/// Outlives the colony scene (<see cref="Current"/>) so an expedition can leave for the scavenge scene and come back.
/// Holds no game rules.
/// </summary>
public sealed class ColonySession
{
    /// <summary>
    /// Real seconds per sim tick at 1x (one tick is one in-game hour), from the Day length setting
    /// (<see cref="GameSettings.DayLengthMinutes"/>, 2 min per day by default: 5 s per tick). Only the real-time pacing
    /// changes with it; the rules count in ticks.
    /// </summary>
    public double SecondsPerTick => GameSettings.DayLengthMinutes * 60.0 / Math.Max(1, Sim.Data.Rules.TicksPerDay);

    /// <summary>Real seconds one tick takes at the current speed (infinite while paused): how long a tick's change takes to show.</summary>
    public double RealSecondsPerTick => Speed == 0 ? double.PositiveInfinity : SecondsPerTick / Speed;

    /// <summary>Upper bound on ticks run in one frame, so a long hitch cannot stall the game.</summary>
    private const int MaxTicksPerFrame = 16;

    public const string SavePath = "user://colony.json";

    public static readonly int[] Speeds = [0, 1, 2, 4, 8];

    private double _accumulator;

    public ColonySession(ColonySim sim)
    {
        Sim = sim;
        Sim.Story ??= GameDataPaths.LoadStory();
        Sim.Raids ??= GameDataPaths.LoadRaids();
        Phase = sim.Phase;
        SeenRaidReport = Sim.State.Defense.LastReport;
    }

    /// <summary>
    /// The raid report the colony screen last showed (or that was there at load), so only a new one pops up — kept here
    /// because a fought raid's report is made while the colony scene is away.
    /// </summary>
    public Remnants.Core.Raids.RaidReport? SeenRaidReport { get; set; }

    /// <summary>
    /// The raid at the fence (<see cref="Remnants.Core.Raids.RaidStage.Battle"/>): opens the fight on the scavenge screen
    /// (Core's <c>RaidBattle.Open</c> makes it, or resumes the one kept in the raid). When it ends the result goes back
    /// with <c>ApplyRaidBattle</c> and the raid report pops in the colony; leaving mid-fight keeps it for later.
    /// </summary>
    public void PlayRaidBattle(SceneTree tree)
    {
        if (Sim.State.Defense.Raid is not { Stage: Remnants.Core.Raids.RaidStage.Battle })
        {
            Feedback?.Invoke("No raid is at the fence.", true);
            return;
        }
        ScavengeSim battle;
        try
        {
            battle = Remnants.Core.Raids.RaidBattle.Open(Sim);
        }
        catch (InvalidOperationException e)
        {
            Feedback?.Invoke($"Cannot fight: {e.Message}", true);
            return;
        }
        SetSpeed(0);
        ScavengeHandoff.Begin(tree, new ScavengeRun(battle, ColonySim.ExpeditionPlayerId), ApplyBattleResult);
    }

    private void ApplyBattleResult(ExtractionResult result)
    {
        if (!result.Complete)
        {
            UnseenNotice = "The fight is on hold: they are still at the fence. Press Fight on the banner to go back in.";
            return;
        }
        var applied = Execute(new Remnants.Core.Raids.ApplyRaidBattle(result));
        if (!applied.Ok) UnseenNotice = applied.Message;
    }

    /// <summary>The running colony for this game session; survives scene changes.</summary>
    public static ColonySession? Current { get; set; }

    /// <summary>The after-action report of an expedition that came home and has not been shown yet.</summary>
    public ExpeditionReport? UnseenReport { get; set; }

    public ColonySim Sim { get; private set; }

    /// <summary>Sim ticks per <see cref="SecondsPerTick"/>; 0 is paused.</summary>
    public int Speed { get; private set; } = 1;

    /// <summary>Fired after ticks, commands, speed changes and loads. Views re-read Core state.</summary>
    public event Action? Changed;

    /// <summary>
    /// The colony's day phase (dawn / day / dusk / night) as of the last tick; see Core <c>DayNight</c>. Audio and views
    /// switch on this rather than on raw hours.
    /// </summary>
    public DayPhase Phase { get; private set; }

    /// <summary>Fired when <see cref="Phase"/> changes (after the tick that crossed into it, or a load), with the new phase.</summary>
    public event Action<DayPhase>? PhaseChanged;

    /// <summary>How far real time has run into the next tick (0..1), for smooth presentation between ticks.</summary>
    public double TickProgress => Math.Clamp(_accumulator / SecondsPerTick, 0, 1);

    /// <summary>Fired when a whole new sim replaced the old one (load); views drop cached ids.</summary>
    public event Action? Replaced;

    /// <summary>Feedback for the event log: text and whether it is an error.</summary>
    public event Action<string, bool>? Feedback;

    /// <summary>A screen is going away: drop its handlers so the session does not call into freed nodes.</summary>
    public void DetachViews()
    {
        Changed = null;
        Replaced = null;
        PhaseChanged = null;
        Feedback = null;
    }

    /// <summary>
    /// Starts (or, after a load, resumes) the active expedition's scavenge run through <see cref="ScavengeHandoff"/>;
    /// the result is applied to the colony when the run ends.
    /// </summary>
    public void PlayExpedition(SceneTree tree)
    {
        if (Sim.State.Expedition is not { } expedition)
        {
            Feedback?.Invoke("No party is out.", true);
            return;
        }
        switch (expedition.PhaseAt(Sim.State.Tick))
        {
            case ExpeditionPhase.Outbound:
                Feedback?.Invoke($"The party is still on the road ({expedition.ArrivalTick - Sim.State.Tick} h to go).", true);
                return;
            case ExpeditionPhase.Returning:
                Feedback?.Invoke($"The party is heading home ({expedition.ReturnTick - Sim.State.Tick} h to go).", true);
                return;
        }
        SetSpeed(0);
        Sim.LateZones = GameSettings.LateZones;
        ScavengeHandoff.Begin(tree, new ScavengeRun(Sim.OpenExpeditionScavenge(), ColonySim.ExpeditionPlayerId), ApplyResult);
    }

    /// <summary>
    /// Multiplayer: the expedition's party, if one is in its zone and has not started a local run, to take into a
    /// shared instance instead (see <see cref="ApplyMultiplayerResult"/>).
    /// </summary>
    public PartySetup? MultiplayerParty() =>
        Sim.State.Expedition is { Scavenge: null } e && e.PhaseAt(Sim.State.Tick) == ExpeditionPhase.InZone ? Sim.ExpeditionParty() : null;

    /// <summary>
    /// Applies a shared instance's result to this colony as the expedition's (the server numbered the player
    /// differently) and saves. The client is trusted here; see docs/MULTIPLAYER.md, "Trust gap".
    /// </summary>
    public void ApplyMultiplayerResult(ExtractionResult result)
    {
        ApplyResult(result with { PlayerId = ColonySim.ExpeditionPlayerId });
        Save();
    }

    /// <summary>A message for the colony log that arose while no colony screen was listening.</summary>
    public string? UnseenNotice { get; set; }

    /// <summary>
    /// Applies a finished run. A run left early (the scavenge screen's Leave) is not final: the party stays out and
    /// the run, kept in the expedition, resumes from the Expedition panel; a colony save keeps it too.
    /// </summary>
    private void ApplyResult(ExtractionResult result)
    {
        if (!result.Complete)
        {
            UnseenNotice = "The party is still out: open Expedition → Play mission to continue the run.";
            return;
        }
        var before = Sim.State.LastReport;
        var applied = Execute(new ApplyExtractionResult(result));
        if (!applied.Ok) return;
        // A far zone's party walks home first; its report arrives with it (see TakeArrivedReport).
        if (!ReferenceEquals(before, Sim.State.LastReport)) UnseenReport = Sim.State.LastReport;
        else UnseenNotice = applied.Message;
        SetSpeed(1);
    }

    public void Notify(string message, bool error = false) => Feedback?.Invoke(message, error);

    public void SetSpeed(int speed)
    {
        // The way into the current tick is kept (it counts 1x seconds, so it holds at any speed): with ticks several
        // seconds long, pausing or changing speed would otherwise throw away part of an hour and the light would stall.
        Speed = Math.Max(0, speed);
        Changed?.Invoke();
    }

    public void Advance(double delta)
    {
        if (Speed == 0) return;
        _accumulator += delta * Speed;
        var ticks = 0;
        var report = Sim.State.LastReport;
        var perTick = SecondsPerTick;
        while (_accumulator >= perTick && ticks < MaxTicksPerFrame)
        {
            _accumulator -= perTick;
            Sim.Tick();
            ticks++;
        }
        if (!ReferenceEquals(report, Sim.State.LastReport)) UnseenReport = Sim.State.LastReport;
        if (ticks == MaxTicksPerFrame) _accumulator = 0;
        if (ticks > 0) Changed?.Invoke();
        SyncPhase();
    }

    /// <summary>Debug: run up to <paramref name="maxTicks"/> ticks at once, stopping early when <paramref name="until"/> holds.</summary>
    public void FastForward(int maxTicks, Func<ColonySim, bool> until)
    {
        var report = Sim.State.LastReport;
        for (var i = 0; i < maxTicks && !until(Sim); i++) Sim.Tick();
        if (!ReferenceEquals(report, Sim.State.LastReport)) UnseenReport = Sim.State.LastReport;
        Changed?.Invoke();
        SyncPhase();
    }

    /// <summary>Events the player's own commands logged (newest few): the log shows them, toasts skip them.</summary>
    private readonly System.Collections.Generic.Queue<ColonyEvent> _commandEvents = new();

    /// <summary>Whether this event came from a command the player just gave (so it needs no toast).</summary>
    public bool FromCommand(ColonyEvent e) => System.Linq.Enumerable.Any(_commandEvents, x => ReferenceEquals(x, e));

    public CommandResult Execute(IColonyCommand command)
    {
        var events = Sim.State.Events.Count;
        var last = events > 0 ? Sim.State.Events[^1] : null;
        var result = Sim.Execute(command);
        var start = last is null ? 0 : Sim.State.Events.LastIndexOf(last) + 1;
        for (var i = start; i < Sim.State.Events.Count; i++) _commandEvents.Enqueue(Sim.State.Events[i]);
        while (_commandEvents.Count > 32) _commandEvents.Dequeue();
        // Core already logged an event for this (e.g. "Built Farm."); don't say it twice.
        var logged = Sim.State.Events.Count != events || (events > 0 && !ReferenceEquals(Sim.State.Events[^1], last));
        // Dialogue steps speak in the dialogue panel, not the log.
        var dialogue = command is Remnants.Core.Story.AdvanceDialogue or Remnants.Core.Story.ChooseDialogue or Remnants.Core.Story.TakeCall
            || command is Remnants.Core.Schematics.AnswerCall && Sim.State.Campaign?.Active is not null;
        if (!result.Ok || (!logged && !dialogue)) Feedback?.Invoke(result.Message, !result.Ok);
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// Sends several commands in a row (e.g. "queue all repairs", a dragged wall line) with one line of feedback: the
    /// first refusal if any (and stops there), else <paramref name="summary"/>.
    /// </summary>
    public int ExecuteAll(System.Collections.Generic.IEnumerable<IColonyCommand> commands, string summary)
    {
        var done = 0;
        foreach (var command in commands)
        {
            var result = Sim.Execute(command);
            if (!result.Ok)
            {
                Feedback?.Invoke(done > 0 ? $"{result.Message} ({done} done)" : result.Message, true);
                Changed?.Invoke();
                return done;
            }
            done++;
        }
        if (done > 0) Feedback?.Invoke(summary, false);
        Changed?.Invoke();
        return done;
    }

    public void Save()
    {
        // Scripted runs (screenshots, demos) leave the player's real save alone.
        if (DebugCapture.HasFlag("--no-save")) return;
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            Feedback?.Invoke($"Could not save: {FileAccess.GetOpenError()}.", true);
            return;
        }
        file.StoreString(SaveGame.Save(Sim.State));
        Feedback?.Invoke($"Saved to {ProjectSettings.GlobalizePath(SavePath)}.", false);
    }

    public void Load()
    {
        if (!FileAccess.FileExists(SavePath))
        {
            Feedback?.Invoke("No save game yet.", true);
            return;
        }
        try
        {
            Sim = SaveGame.Load(Sim.Data, FileAccess.GetFileAsString(SavePath), Sim.Scavenge);
            Sim.Story = GameDataPaths.LoadStory();
            Sim.Raids = GameDataPaths.LoadRaids();
            SeenRaidReport = Sim.State.Defense.LastReport;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
        {
            Feedback?.Invoke($"Could not load: {e.Message}", true);
            return;
        }
        _accumulator = 0;
        Replaced?.Invoke();
        Feedback?.Invoke("Loaded save game.", false);
        Changed?.Invoke();
        SyncPhase();
    }

    private void SyncPhase()
    {
        if (Sim.Phase == Phase) return;
        Phase = Sim.Phase;
        PhaseChanged?.Invoke(Phase);
    }
}
