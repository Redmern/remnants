using System;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;

namespace Remnants.Game;

/// <summary>A scavenge instance handed to the scavenge screen, and which player's party the screen controls.</summary>
public sealed record ScavengeRun(ScavengeSim Sim, int PlayerId);

/// <summary>
/// The seam between the colony and the scavenge screen. The colony calls <see cref="Begin"/> with the run built from
/// its expedition; the scavenge screen plays <see cref="Pending"/> and calls <see cref="Finish"/> with the party's
/// <see cref="ExtractionResult"/> once it is done, which returns to the colony scene and hands the result over.
/// While <see cref="ScenePath"/> is null (no scavenge screen yet) the run is resolved in place by <see cref="Autopilot"/>.
/// </summary>
public static class ScavengeHandoff
{
    public const string ColonyScene = "res://scenes/Colony.tscn";

    /// <summary>The scavenge scene to switch to; null resolves runs with the autopilot stub.</summary>
    public static string? ScenePath { get; set; }

    public static ScavengeRun? Pending { get; private set; }

    private static Action<ExtractionResult>? _onFinished;
    private static SceneTree? _tree;

    public static void Begin(SceneTree tree, ScavengeRun run, Action<ExtractionResult> onFinished)
    {
        Pending = run;
        _onFinished = onFinished;
        _tree = tree;
        if (ScenePath is { } scene)
        {
            tree.ChangeSceneToFile(scene);
            return;
        }
        var result = Autopilot.Play(run.Sim, run.PlayerId);
        Callable.From(() => Finish(result)).CallDeferred();
    }

    /// <summary>Called by the scavenge screen (or the stub) when the party is done.</summary>
    public static void Finish(ExtractionResult result)
    {
        var callback = _onFinished;
        var tree = _tree;
        Pending = null;
        _onFinished = null;
        _tree = null;
        callback?.Invoke(result);
        if (ScenePath is not null && tree is not null) tree.ChangeSceneToFile(ColonyScene);
    }
}

/// <summary>
/// Stand-in for the scavenge screen until it is wired: every party unit walks toward the nearest exit and extracts;
/// enemies act as usual, so injuries, deaths and loss of the downed still happen. Plays only through Core commands.
/// </summary>
public static class Autopilot
{
    private const int MaxCommands = 20000;

    public static ExtractionResult Play(ScavengeSim sim, int playerId)
    {
        var map = sim.State.Map;
        for (var i = 0; i < MaxCommands && !(sim.State.FindParty(playerId)?.Done ?? true); i++)
        {
            if (sim.State.CurrentUnit is not { } unit || unit.PlayerId != playerId) break;
            if (map.IsExit(unit.Pos) && sim.Execute(new Extract(playerId, unit.Id)).Ok) continue;

            int Distance(Pos p) => map.Exits.Min(e => e.Chebyshev(p));
            var step = sim.Reachable(unit)
                .Where(kv => kv.Value <= unit.Ap && Distance(kv.Key) < Distance(unit.Pos))
                .OrderBy(kv => Distance(kv.Key)).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
                .Select(kv => (Pos?)kv.Key)
                .FirstOrDefault();
            if (step is { } to && sim.Execute(new Move(playerId, unit.Id, to)).Ok) continue;
            sim.Execute(new EndTurn(playerId, unit.Id));
        }
        return sim.ResultFor(playerId);
    }
}
