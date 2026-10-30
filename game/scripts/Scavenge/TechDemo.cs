using System.Linq;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Debug: <c>--tech-demo=hack|turret|drone</c> stages the debug mission for hacking and the scout drone, everything hostile
/// gone, so <see cref="ScavengeScreen"/> can play the moment for a screenshot: a terminal half-hacked next to the first
/// survivor up (<c>hack</c>), a hacked turret beside the party with a Husk walking into its sights (<c>turret</c>), or a
/// drone in the pack with a Husk hidden behind the fog ahead (<c>drone</c>).
/// </summary>
public static class TechDemo
{
    public static string? Mode => DebugCapture.Value("--tech-demo");
    public static bool Requested => Mode is { Length: > 0 };

    public static void Stage(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var map = state.Map;
        foreach (var u in state.Units.Where(u => u.PlayerId is null && u.Kind is UnitKind.Enemy or UnitKind.Device)) u.Status = UnitStatus.Dead;
        state.Devices.Clear();
        var acting = state.CurrentUnit!;
        bool Free(Pos p) => map.InBounds(p) && map[p] == Terrain.Floor && state.UnitAt(p) is null && !map.IsExit(p) && state.Caches.All(c => c.Pos != p);
        Pos Near(Pos from, int min, int max) => map.AllPositions.Where(p => Free(p) && p.Chebyshev(from) >= min && p.Chebyshev(from) <= max)
            .OrderBy(p => p.Chebyshev(from)).ThenBy(map.Index).First();

        switch (Mode)
        {
            case "hack":
                acting.Pack.Add("hacking_kit");
                var terminal = Hacking.Place(sim.Data, state, DeviceKind.Terminal, Near(acting.Pos, 1, 1), ["ds_override"]);
                var locker = Hacking.Place(sim.Data, state, DeviceKind.Locker, Near(acting.Pos, 3, 4), ["med_kit"]);
                _ = terminal;
                _ = locker;
                break;
            case "turret":
                var turret = Hacking.Place(sim.Data, state, DeviceKind.Turret, Near(acting.Pos, 1, 2));
                var gun = state.FindUnit(turret.UnitId!.Value)!;
                gun.Side = acting.Side;
                gun.PlayerId = playerId;
                turret.Controller = acting.Side;
                turret.ControllerPlayerId = playerId;
                var husk = NewScavenge.EnemyUnit(sim.Data, state, sim.Data.Enemies["husk"], Near(gun.Pos, 4, 5));
                husk.Ai.Mode = AiMode.Hunting;
                husk.Ai.LastKnown = acting.Pos;
                state.Units.Add(husk);
                state.TurnOrder.Add(gun.Id);
                state.TurnOrder.Add(husk.Id);
                gun.Ap = sim.Profile(gun).MaxAp;
                husk.Ap = sim.Profile(husk).MaxAp;
                break;
            case "drone":
                acting.Pack.Add("scout_drone");
                acting.Hooks[Remnants.Core.Colony.PerkHooks.DroneCanMark] = 1;
                var sight = sim.Profile(acting).Sight;
                var hidden = map.AllPositions.Where(p => Free(p) && p.Chebyshev(acting.Pos) >= (int)sight + 2 && p.Chebyshev(acting.Pos) <= (int)sight + 4)
                    .OrderBy(p => p.Y).ThenBy(p => p.Chebyshev(acting.Pos)).ThenBy(map.Index).First();
                var lurker = NewScavenge.EnemyUnit(sim.Data, state, sim.Data.Enemies["husk"], hidden);
                state.Units.Add(lurker);
                break;
        }
        Visibility.UpdateExplored(sim.Data, state);
    }
}
