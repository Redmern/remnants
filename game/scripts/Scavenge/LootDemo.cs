using System.Linq;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Debug: <c>--loot-demo=pile|salvage</c> stages the debug mission for field loot, everything hostile gone, so
/// <see cref="ScavengeScreen"/> can play the moment for a screenshot: a 1 HP Husk next to the first survivor up whose
/// loot roll is a good one (<c>pile</c>: the kill, its pile, the pickup panel), or a dead tree next to a survivor whose
/// pack is already heavy with stone (<c>salvage</c>: one Salvage action, the progress ring and the carry warning).
/// </summary>
public static class LootDemo
{
    public static string? Mode => DebugCapture.Value("--loot-demo");
    public static bool Requested => Mode is { Length: > 0 };

    public static void Stage(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var map = state.Map;
        foreach (var u in state.Units.Where(u => u.PlayerId is null && u.Kind is UnitKind.Enemy or UnitKind.Device)) u.Status = UnitStatus.Dead;
        state.Devices.Clear();
        state.SalvageNodes.RemoveAll(n => n.Pos.Chebyshev(state.CurrentUnit!.Pos) <= 3);
        var acting = state.CurrentUnit!;
        bool Free(Pos p) => map.InBounds(p) && map[p] == Terrain.Floor && state.UnitAt(p) is null && !map.IsExit(p) && state.Caches.All(c => c.Pos != p);
        Pos Near(Pos from, int min, int max) => map.AllPositions.Where(p => Free(p) && p.Chebyshev(from) >= min && p.Chebyshev(from) <= max)
            .OrderBy(p => p.Chebyshev(from)).ThenBy(map.Index).First();

        switch (Mode)
        {
            case "pile":
                var husk = NewScavenge.EnemyUnit(sim.Data, state, sim.Data.Enemies["husk"], Near(acting.Pos, 1, 1));
                husk.Hp = 1;
                state.Units.Add(husk);
                state.TurnOrder.Add(husk.Id);
                // A kill worth showing: the first loot seed whose roll for this Husk has a few things in it.
                var table = sim.Data.Loot.Tables["husk"];
                var context = FieldLoot.Context(sim, acting);
                for (ulong seed = 1; seed < 500; seed++)
                {
                    state.LootSeed = seed;
                    if (sim.Data.Loot.Roll(sim.Data, table, context, FieldLoot.Rng(state, husk.Id)).Distinct().Count() >= 3) break;
                }
                acting.Pack.AddRange(["stone", "stone"]);
                break;
            case "salvage":
                var tree = sim.Data.Salvage.Node("dead_tree")!;
                FieldSalvage.Add(sim.Data, state, tree, Near(acting.Pos, 1, 1), FieldLoot.Rng(state, 1));
                // Heavy already: the carry bar warns before the next lot comes off.
                while (sim.Profile(acting).Load < sim.Profile(acting).CarryCapacity * 0.62) acting.Pack.Add("stone");
                break;
        }
        Visibility.UpdateExplored(sim.Data, state);
    }
}
