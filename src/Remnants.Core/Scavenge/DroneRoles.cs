using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// What the drone variants do beyond scouting (<see cref="DroneItemDef.Role"/>); the operator pays the AP and the drone
/// must be in the air and within control range, as for a flight.
/// <list type="bullet">
/// <item><b>Medic</b> (<see cref="DroneHeal"/>): a medical item from the operator's pack (the least that does the job, a
/// bandage first; see <see cref="HealItem"/>) on a friendly unit next to the drone: it heals and treats a conscious one
/// (dressings, splints, cures as by hand, <see cref="Treatment.Apply"/>), stabilizes (and with a med-kit or blood bag
/// revives) a downed one, with the operator's medic perks. Never a stim or the Calm injector.</item>
/// <item><b>Cargo</b> (<see cref="DroneLoad"/>, <see cref="DroneFerry"/>): loads items from a cache on or next to its tile,
/// up to its cargo; next to an exit (or the parked vehicle, see <see cref="FerryPoint"/>) it flies them out: they are
/// <see cref="Party.Secured"/>, home whatever happens to the party, and so is the drone.</item>
/// <item><b>Combat</b> (<see cref="DroneFire"/>): the drone's gun at a hostile it sees, with the operator's Drone Piloting as
/// its skill; loud, and a muzzle flash like anyone's.</item>
/// </list>
/// </summary>
public static class DroneRoles
{
    /// <summary>The flying unit's items: the drone frame, plus a combat drone's gun.</summary>
    internal static Dictionary<EquipSlot, string> Equipment(ScavengeData data, DroneItemDef spec)
    {
        var gear = data.Rules.Drone.Equipment.ToDictionary(id => data.Items[id].Slot!.Value, id => id);
        if (spec.Weapon is { } gun && data.Items[gun].Slot is { } slot) gear[slot] = gun;
        return gear;
    }

    public static DroneItemDef? Spec(ScavengeData data, Unit drone) =>
        drone.DroneItemId is { } id && data.Items.TryGetValue(id, out var item) ? item.Drone : null;

    /// <summary>The healing item the medic drone would apply from the operator's pack for this target (null if none fits).</summary>
    public static string? HealItem(ScavengeData data, Unit operatorUnit, Unit target)
    {
        var downed = target.Status == UnitStatus.Downed;
        return operatorUnit.Pack.Distinct()
            .Select(id => (Id: id, Use: data.Items[id].Use))
            // Medical supplies count when they would do something (a dressing, splint or cure); never a stim or an injector.
            .Where(t => t.Use is { TargetsTile: false, Evacuate: false, StimAp: 0, HumanityCost: 0 } u
                        && (downed ? u.Stabilize : u.Heal > 0 && target.Hp < target.MaxHp || Treatment.Helps(data.Medicine, target.Body, u)))
            // The least that does the job: a bandage before a med-kit.
            .OrderBy(t => t.Use!.Heal + t.Use.ReviveHp).ThenBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => t.Id).FirstOrDefault();
    }

    /// <summary>Whether the drone can ferry from where it is: next to (or on) an exit tile; the vehicles feature may add its parked vehicle.</summary>
    public static bool FerryPoint(ScavengeSim sim, Pos at)
    {
        var map = sim.State.Map;
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var p = new Pos(at.X + dx, at.Y + dy);
                if (map.InBounds(p) && map.IsExit(p)) return true;
            }
        return false;
    }

    /// <summary>Hostiles the combat drone could shoot now (for UI highlights).</summary>
    public static List<Unit> FireTargets(ScavengeSim sim, Unit operatorUnit)
    {
        if (Drones.DroneOf(sim.State, operatorUnit) is not { } drone || Spec(sim.Data, drone) is not { Role: DroneRole.Combat }) return [];
        var action = sim.Profile(drone).Actions.FirstOrDefault();
        if (action is null) return [];
        return sim.State.Units.Where(u => u.IsActive && drone.HostileTo(u) && Detection.Spots(sim.Data, sim.State, drone, u)
                                          && Combat.CheckReach(sim.Data, sim.State.Map, drone.Pos, action, u.Pos) == ScavengeError.None)
            .OrderBy(u => u.Id).ToList();
    }

    private static ScavengeResult? Ready(ScavengeSim sim, Unit unit, DroneRole role, out Unit drone, out DroneItemDef spec)
    {
        drone = null!;
        spec = null!;
        if (Drones.DroneOf(sim.State, unit) is not { } d) return ScavengeResult.Fail(ScavengeError.NoDrone, $"{unit.Name} has no drone in the air.");
        drone = d;
        if (Spec(sim.Data, d) is not { } s || s.Role != role)
            return ScavengeResult.Fail(ScavengeError.NoDrone, $"{unit.Name}'s drone is not a {role.ToString().ToLowerInvariant()} drone.");
        spec = s;
        if (unit.Pos.Chebyshev(d.Pos) > Drones.ControlRange(sim.Data, unit, d.DroneItemId!))
            return ScavengeResult.Fail(ScavengeError.OutOfRange, $"The drone is beyond {unit.Name}'s control range.");
        return null;
    }

    internal static ScavengeResult Heal(ScavengeSim sim, Unit unit, DroneHeal c)
    {
        if (Ready(sim, unit, DroneRole.Medic, out var drone, out var spec) is { } fail) return fail;
        var state = sim.State;
        var target = state.FindUnit(c.TargetId);
        if (target is not { OnMap: true } || target.Side != unit.Side || target.Kind is not (UnitKind.Survivor or UnitKind.Npc))
            return ScavengeResult.Fail(ScavengeError.InvalidTarget, $"Unit {c.TargetId} is not someone of your side to treat.");
        if (target.Pos.Chebyshev(drone.Pos) > 1) return ScavengeResult.Fail(ScavengeError.TooFar, $"{target.Name} must be next to the drone.");
        if (HealItem(sim.Data, unit, target) is not { } itemId)
            return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} carries nothing the drone can use on {target.Name}.");
        if (spec.ActionApCost > unit.Ap) return NotEnoughAp(unit, spec.ActionApCost);
        var item = sim.Data.Items[itemId];
        var use = item.Use!;
        unit.Ap -= spec.ActionApCost;
        ElectronicWarfare.Spend(unit, item, use);
        if (target.Status == UnitStatus.Downed)
        {
            target.Stabilized = true;
            target.BleedOut += (int)Math.Round(unit.Hook(PerkHooks.BleedoutRoundsBonus));
            if (use.ReviveHp > 0)
            {
                target.Status = UnitStatus.Active;
                target.Hp = Math.Min(target.MaxHp, use.ReviveHp + unit.Hook(PerkHooks.ReviveHpBonus));
                target.Stabilized = false;
            }
        }
        else
        {
            target.Hp = Math.Min(target.MaxHp, target.Hp + use.Heal * (1 + unit.Hook(PerkHooks.FieldHealBonus)));
            Treatment.Apply(sim.Data.Medicine, target.Body, use, field: true);
        }
        unit.GainSkill(Skill.DronePiloting, sim.Data.Rules.Drone.SkillPerFlight);
        unit.GainSkill(Skill.Medicine, sim.Data.Rules.Drone.SkillPerFlight / 2);
        state.Log(new(state.Round, ScavengeEventType.DroneHealed, $"{drone.Name} drops a {item.Name} on {target.Name}.", drone.Id, target.Id, target.Pos));
        return ScavengeResult.Success($"{drone.Name} treated {target.Name}.", target.Id);
    }

    internal static ScavengeResult Load(ScavengeSim sim, Unit unit, DroneLoad c)
    {
        if (Ready(sim, unit, DroneRole.Cargo, out var drone, out var spec) is { } fail) return fail;
        var state = sim.State;
        var cache = state.Caches.Find(x => x.Id == c.CacheId);
        if (cache is null || cache.Pos.Chebyshev(drone.Pos) > 1) return ScavengeResult.Fail(ScavengeError.UnknownCache, "No cache next to the drone.");
        if (!cache.Items.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotInCache, $"That cache holds no '{c.ItemId}'.");
        if (drone.Pack.Count >= spec.Cargo) return ScavengeResult.Fail(ScavengeError.CarryLimitExceeded, $"The drone's hold is full ({spec.Cargo}).");
        if (spec.ActionApCost > unit.Ap) return NotEnoughAp(unit, spec.ActionApCost);
        unit.Ap -= spec.ActionApCost;
        cache.Items.Remove(c.ItemId);
        if (cache.Items.Count == 0) state.Caches.Remove(cache);
        drone.Pack.Add(c.ItemId);
        state.Log(new(state.Round, ScavengeEventType.DroneLoaded, $"{drone.Name} winches up {sim.Data.Items[c.ItemId].Name} ({drone.Pack.Count}/{spec.Cargo}).",
            drone.Id, Pos: drone.Pos));
        return ScavengeResult.Success($"{drone.Name} loaded {sim.Data.Items[c.ItemId].Name}.", drone.Id);
    }

    internal static ScavengeResult Ferry(ScavengeSim sim, Unit unit)
    {
        if (Ready(sim, unit, DroneRole.Cargo, out var drone, out var spec) is { } fail) return fail;
        var state = sim.State;
        if (!FerryPoint(sim, drone.Pos)) return ScavengeResult.Fail(ScavengeError.NotOnExit, "The drone must be next to an exit to fly its load out.");
        if (spec.ActionApCost > unit.Ap) return NotEnoughAp(unit, spec.ActionApCost);
        unit.Ap -= spec.ActionApCost;
        var party = state.FindParty(unit.PlayerId!.Value)!;
        var load = drone.Pack.Count;
        party.Secured.AddRange(drone.Pack);
        party.Secured.Add(drone.DroneItemId!);
        drone.Pack.Clear();
        drone.Status = UnitStatus.Extracted;
        unit.DroneId = null;
        unit.DroneBattery = null;
        state.Marks.RemoveAll(m => m.UnitId == drone.Id);
        unit.GainSkill(Skill.DronePiloting, sim.Data.Rules.Drone.SkillPerFlight);
        state.Log(new(state.Round, ScavengeEventType.DroneFerried, $"{drone.Name} flies {load} item{(load == 1 ? "" : "s")} out of the zone: they are safe.",
            drone.Id, Pos: drone.Pos, Side: unit.Side));
        return ScavengeResult.Success($"{drone.Name} ferried its load out.", drone.Id);
    }

    internal static ScavengeResult Fire(ScavengeSim sim, Unit unit, DroneFire c, bool checkOnly = false)
    {
        if (Ready(sim, unit, DroneRole.Combat, out var drone, out _) is { } fail) return fail;
        var state = sim.State;
        var target = state.FindUnit(c.TargetId);
        // As with attacks, missing and unseen answer alike.
        if (target is not { OnMap: true } || !Detection.Spots(sim.Data, state, drone, target))
            return ScavengeResult.Fail(ScavengeError.NotVisible, $"The drone sees no unit {c.TargetId}.");
        if (!drone.HostileTo(target)) return ScavengeResult.Fail(ScavengeError.InvalidTarget, $"{target.Name} is not hostile.");
        var action = sim.Profile(drone).Actions.FirstOrDefault();
        if (action is null) return ScavengeResult.Fail(ScavengeError.UnknownAction, "The drone has no gun.");
        var reach = Combat.CheckReach(sim.Data, state.Map, drone.Pos, action, target.Pos);
        if (reach != ScavengeError.None) return ScavengeResult.Fail(reach, $"{target.Name} is not in reach of the drone's gun ({reach}).");
        if (checkOnly) return ScavengeResult.Success("");
        if (action.ApCost > unit.Ap) return NotEnoughAp(unit, action.ApCost);
        unit.Ap -= action.ApCost;
        drone.Skills[Skill.Firearms] = unit.Get(Skill.DronePiloting);
        state.Log(new(state.Round, ScavengeEventType.DroneFired, $"{drone.Name} opens fire on {target.Name}!", drone.Id, target.Id, target.Pos,
            Anonymous: $"Gunfire from the air at {target.Name}!"));
        Combat.Resolve(sim, drone, action, target);
        unit.GainSkill(Skill.DronePiloting, sim.Data.Rules.Drone.SkillPerFlight);
        unit.FieldXp += sim.Data.Rules.Drone.XpPerFlight;
        return ScavengeResult.Success($"{drone.Name} fired at {target.Name}.", target.Id);
    }

    /// <summary>A planned shot comes up: on the tile it was aimed at, whoever is there now, through block like any blow.</summary>
    internal static ScavengeResult FireAt(ScavengeSim sim, Unit unit, Pos tile)
    {
        if (Ready(sim, unit, DroneRole.Combat, out var drone, out _) is { } fail) return fail;
        var state = sim.State;
        if (sim.Profile(drone).Actions.FirstOrDefault() is not { } action) return ScavengeResult.Fail(ScavengeError.UnknownAction, "The drone has no gun.");
        var reach = Combat.CheckReach(sim.Data, state.Map, drone.Pos, action, tile);
        if (reach != ScavengeError.None) return ScavengeResult.Fail(reach, $"the drone's gun no longer reaches {tile}.");
        drone.Skills[Skill.Firearms] = unit.Get(Skill.DronePiloting);
        state.Log(new(state.Round, ScavengeEventType.DroneFired, $"{drone.Name} opens fire on {tile}!", drone.Id, state.UnitAt(tile)?.Id, tile, Anonymous: "Gunfire from the air!"));
        if (TileStrikes.Strike(sim, drone, action, [tile], tile, Combat.Face(drone.Pos, tile)).Count == 0)
            return ScavengeResult.Fail(ScavengeError.NotVisible, $"{drone.Name} hits nothing.");
        unit.GainSkill(Skill.DronePiloting, sim.Data.Rules.Drone.SkillPerFlight);
        unit.FieldXp += sim.Data.Rules.Drone.XpPerFlight;
        return ScavengeResult.Success($"{drone.Name} fired at {tile}.");
    }

    private static ScavengeResult NotEnoughAp(Unit unit, int cost) =>
        ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
}
