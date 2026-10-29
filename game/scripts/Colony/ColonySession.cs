using System;
using Godot;
using FileAccess = Godot.FileAccess;
using Remnants.Core.Colony;
using Remnants.Core.Serialization;

namespace Remnants.Game.Colony;

/// <summary>
/// Presenter between the colony screen and Core: owns the <see cref="ColonySim"/>, turns frame time into
/// fixed sim ticks at the chosen speed, and is the only place the UI sends commands through.
/// Holds no game rules.
/// </summary>
public sealed class ColonySession
{
    /// <summary>Real seconds per sim tick at 1x (one tick is one in-game hour).</summary>
    public const double SecondsPerTick = 0.5;

    /// <summary>Upper bound on ticks run in one frame, so a long hitch cannot stall the game.</summary>
    private const int MaxTicksPerFrame = 16;

    public const string SavePath = "user://colony.json";

    public static readonly int[] Speeds = [0, 1, 2, 4];

    private double _accumulator;

    public ColonySession(ColonySim sim) => Sim = sim;

    public ColonySim Sim { get; private set; }

    /// <summary>Sim ticks per <see cref="SecondsPerTick"/>; 0 is paused.</summary>
    public int Speed { get; private set; } = 1;

    /// <summary>Fired after ticks, commands, speed changes and loads. Views re-read Core state.</summary>
    public event Action? Changed;

    /// <summary>Fired when a whole new sim replaced the old one (load); views drop cached ids.</summary>
    public event Action? Replaced;

    /// <summary>Feedback for the event log: text and whether it is an error.</summary>
    public event Action<string, bool>? Feedback;

    public void SetSpeed(int speed)
    {
        Speed = Math.Max(0, speed);
        _accumulator = 0;
        Changed?.Invoke();
    }

    public void Advance(double delta)
    {
        if (Speed == 0) return;
        _accumulator += delta * Speed;
        var ticks = 0;
        while (_accumulator >= SecondsPerTick && ticks < MaxTicksPerFrame)
        {
            _accumulator -= SecondsPerTick;
            Sim.Tick();
            ticks++;
        }
        if (ticks == MaxTicksPerFrame) _accumulator = 0;
        if (ticks > 0) Changed?.Invoke();
    }

    public CommandResult Execute(IColonyCommand command)
    {
        var events = Sim.State.Events.Count;
        var last = events > 0 ? Sim.State.Events[^1] : null;
        var result = Sim.Execute(command);
        // Core already logged an event for this (e.g. "Built Farm."); don't say it twice.
        var logged = Sim.State.Events.Count != events || (events > 0 && !ReferenceEquals(Sim.State.Events[^1], last));
        if (!result.Ok || !logged) Feedback?.Invoke(result.Message, !result.Ok);
        Changed?.Invoke();
        return result;
    }

    public void Save()
    {
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
            Sim = SaveGame.Load(Sim.Data, FileAccess.GetFileAsString(SavePath));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        {
            Feedback?.Invoke($"Could not load: {e.Message}", true);
            return;
        }
        _accumulator = 0;
        Replaced?.Invoke();
        Feedback?.Invoke("Loaded save game.", false);
        Changed?.Invoke();
    }
}
