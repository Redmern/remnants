using System;
using System.Linq;
using Godot;
using Remnants.Core.Scavenge;
using Remnants.Game.Scavenge;

namespace Remnants.Game;

/// <summary>A scavenge instance handed to the scavenge screen, and which player's party the screen controls.</summary>
public sealed record ScavengeRun(ScavengeSim Sim, int PlayerId);

/// <summary>
/// The colony's way into scavenge mode. <see cref="Begin"/> plays the run on the scavenge screen
/// (<see cref="ScavengeSession"/>); when the party is done and the player closes the summary, the result goes to
/// <c>onFinished</c> and the game returns to the colony scene. With <see cref="UseAutopilot"/> (debug) the run is
/// resolved in place by <see cref="Autopilot"/> instead.
/// </summary>
public static class ScavengeHandoff
{
    public const string ColonyScene = "res://scenes/Colony.tscn";

    /// <summary>Debug / screenshots: resolve runs in place instead of opening the scavenge screen.</summary>
    public static bool UseAutopilot { get; set; }

    public static void Begin(SceneTree tree, ScavengeRun run, Action<ExtractionResult> onFinished)
    {
        if (UseAutopilot)
        {
            var result = Autopilot.Play(run.Sim, run.PlayerId);
            Callable.From(() => onFinished(result)).CallDeferred();
            return;
        }
        ScavengeSession.Begin(tree, run.Sim, run.PlayerId, result =>
        {
            onFinished(result);
            tree.ChangeSceneToFile(ColonyScene);
        }, ColonyScene);
    }
}

/// <summary>
/// Debug stand-in for playing a run: every party unit walks toward the nearest exit and extracts;
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
                .Where(kv => kv.Value <= unit.Mp && Distance(kv.Key) < Distance(unit.Pos))
                .OrderBy(kv => Distance(kv.Key)).ThenBy(kv => kv.Value).ThenBy(kv => map.Index(kv.Key))
                .Select(kv => (Pos?)kv.Key)
                .FirstOrDefault();
            if (step is { } to && sim.Execute(new Move(playerId, unit.Id, to)).Ok) continue;
            sim.Execute(new EndTurn(playerId, unit.Id));
        }
        return sim.ResultFor(playerId);
    }
}
