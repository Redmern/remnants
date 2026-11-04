using System.Linq;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge.Fog;

/// <summary>
/// Debug: <c>--fog-demo</c> stages the debug mission for the fog of war — a Linked hidden in the fog within reach of the
/// party, everything else gone — so <see cref="ScavengeScreen"/> can play an ambush on a survivor while another stands
/// on overwatch (for screenshots and eyeballing the overlay).
/// </summary>
public static class FogDemo
{
    public static bool Requested => DebugCapture.HasFlag("--fog-demo");

    /// <summary>The survivor the demo puts on overwatch: the party's last in round 1's order, so it still holds next round.</summary>
    public static int? WatcherId { get; private set; }

    public static void Stage(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var map = state.Map;
        var party = state.Units.Where(u => u.PlayerId == playerId && u.OnMap).ToList();
        var watcher = state.TurnOrder.Select(state.FindUnit).Last(u => u is { PlayerControlled: true } && u.PlayerId == playerId)!;
        WatcherId = watcher.Id;
        var anchor = party.Where(u => u.Id != watcher.Id).OrderBy(u => u.Id).First();
        foreach (var enemy in state.Units.Where(u => u.Kind == UnitKind.Enemy && u.OnMap)) enemy.Status = UnitStatus.Dead;
        var def = sim.Data.Enemies["linked"];

        // Where the Linked spots the anchor but nobody in the party picks it out (its stealth beats their eyes out
        // there), the watcher's sharper overwatch eyes included if possible.
        Pos? Find(bool withOverwatch)
        {
            watcher.Stance = withOverwatch ? Stance.Overwatch : Stance.None;
            try
            {
                return map.AllPositions
                    .Where(p => map[p] == Terrain.Floor && state.UnitAt(p) is null && !map.IsExit(p))
                    .Where(p =>
                    {
                        var next = state.NextUnitId;
                        var probe = NewScavenge.EnemyUnit(sim.Data, state, def, p);
                        state.NextUnitId = next;
                        return Detection.Spots(sim.Data, state, probe, anchor) && party.All(u => !Detection.Spots(sim.Data, state, u, probe));
                    })
                    .OrderBy(p => p.DistanceSquared(anchor.Pos)).ThenBy(map.Index)
                    .Select(p => (Pos?)p).FirstOrDefault();
            }
            finally
            {
                watcher.Stance = Stance.None;
            }
        }

        var spot = Find(true) ?? Find(false) ?? anchor.Pos + new Pos(0, -6);
        var linked = NewScavenge.EnemyUnit(sim.Data, state, def, spot);
        linked.Ai.Mode = AiMode.Hunting;
        linked.Ai.TargetId = anchor.Id;
        linked.Ai.LastKnown = anchor.Pos;
        state.Units.Add(linked);
        state.TurnOrder.Add(linked.Id);
        linked.Ap = sim.Profile(linked).MaxAp;
        linked.Mp = sim.Profile(linked).MaxMp;
    }
}
