using System.Linq;
using Remnants.Core.Items;
using Remnants.Core.Scavenge;

namespace Remnants.Game.Scavenge;

/// <summary>
/// Debug: <c>--scavenge --gear-demo</c> stages the debug mission for gear mods. The first survivor up carries a Pistol
/// Mk II with a Scope, Suppressor and EMP Coil, a Padded Vest with Scrap Plates and a Scrap Helmet, Jury-rig
/// (the field hook) and a Reflex Sight and Laser Sight in the pack; every hostile but one Linked is gone, and that Linked
/// stands two tiles off, revealed, so <see cref="ScavengeScreen"/> can show the attack preview with its mod tags (+1 AP close for the scope, EMP).
/// </summary>
public static class GearDemo
{
    public static bool Requested => DebugCapture.HasFlag("--gear-demo");

    public static void Stage(ScavengeSim sim, int playerId)
    {
        var state = sim.State;
        var data = sim.Data;
        if (data.GearMods is not { } mods) return;
        var map = state.Map;
        foreach (var u in state.Units.Where(u => u.PlayerId is null && u.Kind is UnitKind.Enemy or UnitKind.Device)) u.Status = UnitStatus.Dead;
        var acting = state.CurrentUnit!;
        acting.Equipment[EquipSlot.Weapon] = mods.Canonical("pistol", 3, ["mod_scope", "mod_suppressor", "mod_emp_coil"]);
        acting.Equipment[EquipSlot.Body] = mods.Canonical("padded_vest", 1, ["mod_armor_scrap_plates"]);
        acting.Equipment[EquipSlot.Head] = "helmet_scrap";
        acting.Pack.AddRange(["mod_reflex_sight", "mod_laser"]);
        acting.Hooks[mods.Def.Rules.FieldHook] = 1;
        acting.Ap = sim.Profile(acting).MaxAp;
        acting.Mp = sim.Profile(acting).MaxMp;

        bool Free(Pos p) => map.InBounds(p) && map[p] == Terrain.Floor && state.UnitAt(p) is null && !map.IsExit(p) && state.Caches.All(c => c.Pos != p);
        var spot = map.AllPositions.Where(p => Free(p) && p.Chebyshev(acting.Pos) == 2
                                               && Combat.CheckReach(data, map, acting.Pos, sim.Profile(acting).Actions[0], p) == ScavengeError.None)
            .OrderBy(map.Index).FirstOrDefault();
        if (!map.InBounds(spot)) return;
        var linked = NewScavenge.EnemyUnit(data, state, data.Enemies["linked"], spot);
        state.Units.Add(linked);
        state.Reveals.Add(new Reveal(linked.Id, acting.Side, state.Round + 5));
        Visibility.UpdateExplored(data, state);
    }
}
