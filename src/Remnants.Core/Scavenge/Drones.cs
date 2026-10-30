using Remnants.Core.Colony;
using Remnants.Core.Items;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Scout drones (scavenge.json <c>drone</c>, items with <see cref="ItemDef.Drone"/>). A carried drone is deployed onto a
/// tile next to its operator and becomes a flying unit of the operator's side (<see cref="UnitKind.Drone"/>): it takes no
/// turns, sees far, has little HP and stealth, and counts for its side's sight, so allies share what it sees.
/// <list type="bullet">
/// <item>The operator flies it with their own AP (<see cref="FlyDrone"/>): up to the item's flight range over any terrain
/// (it cannot end on a wall, pylon, unit or device), never farther from the operator than the control range (item +
/// Drone Piloting + <c>drone_range_bonus</c>).</item>
/// <item>Battery: rounds aloft (item + <c>drone_battery_bonus</c>), one per round (more inside any jammer's field, where it
/// also flies only 1 tile). Recalled within control range it goes back in the pack and recharges each round. A drone whose
/// battery runs out, or whose operator is down or gone, lands: it is left as an item on its tile to pick up.</item>
/// <item>Melee cannot reach it; Husks ignore it; the Linked shoot it down on sight. A destroyed drone is lost.</item>
/// <item>Spotter (<c>drone_can_mark</c>): <see cref="MarkTarget"/> marks a hostile the drone sees: the side sees it through
/// walls and stealth and hits it more easily for <see cref="DroneRules.MarkRounds"/>. Payload (<c>drone_payload</c>):
/// <see cref="DropPayload"/> drops a carried flare or motion sensor on the drone's tile.</item>
/// </list>
/// </summary>
public static class Drones
{
    /// <summary>The scout drone item the unit carries (equipped first), or null.</summary>
    public static string? CarriedDrone(ScavengeData data, Unit unit) =>
        unit.Equipment.TryGetValue(EquipSlot.Tool, out var tool) && data.Items[tool].Drone is not null ? tool
            : unit.Pack.FirstOrDefault(id => data.Items[id].Drone is not null);

    /// <summary>The operator's deployed drone, if it is still in the air.</summary>
    public static Unit? DroneOf(ScavengeState state, Unit operatorUnit) =>
        operatorUnit.DroneId is { } id && state.FindUnit(id) is { IsActive: true } d ? d : null;

    public static int MaxBattery(ScavengeData data, Unit operatorUnit, string itemId) =>
        data.Items[itemId].Drone!.Battery + (int)Math.Round(operatorUnit.Hook(PerkHooks.DroneBatteryBonus));

    /// <summary>Battery of the operator's packed drone (full when never flown).</summary>
    public static int PackedBattery(ScavengeData data, Unit operatorUnit, string itemId) =>
        operatorUnit.DroneBattery ?? MaxBattery(data, operatorUnit, itemId);

    public static int ControlRange(ScavengeData data, Unit operatorUnit, string itemId) =>
        data.Items[itemId].Drone!.ControlRange
        + (int)Math.Floor(operatorUnit.Get(Skill.DronePiloting) * data.Rules.Drone.ControlPerSkill)
        + (int)Math.Round(operatorUnit.Hook(PerkHooks.DroneRangeBonus));

    /// <summary>Whether any working jammer's field covers the tile (jammers do not care whose drone it is).</summary>
    public static bool Jammed(ScavengeSim sim, Pos tile) =>
        sim.State.Units.Where(u => u.IsActive).Any(u => Devices.Working(sim.Data, u).Any(d => d.Effect == DeviceEffect.Jammer && u.Pos.Chebyshev(tile) <= d.Radius));

    public static int FlyRange(ScavengeSim sim, Unit drone) =>
        Jammed(sim, drone.Pos) ? 1 : sim.Data.Items[drone.DroneItemId!].Drone!.FlyRange;

    /// <summary>Tiles a drone may hover over: on the map, not a wall or pylon, no unit, no device.</summary>
    public static bool CanHover(ScavengeSim sim, Pos p, Unit? self = null)
    {
        var state = sim.State;
        if (!state.Map.InBounds(p) || !sim.Data.Terrain(state.Map[p]).Passable) return false;
        if (state.DeviceAt(p) is not null) return false;
        return state.UnitAt(p) is not { } other || other.Id == self?.Id;
    }

    /// <summary>Where the operator's drone can fly with one action now (for UI highlights); empty without a drone in the air.</summary>
    public static List<Pos> FlightTargets(ScavengeSim sim, Unit operatorUnit)
    {
        if (DroneOf(sim.State, operatorUnit) is not { } drone || drone.Battery <= 0) return [];
        var range = FlyRange(sim, drone);
        var control = ControlRange(sim.Data, operatorUnit, drone.DroneItemId!);
        var result = new List<Pos>();
        for (var y = drone.Pos.Y - range; y <= drone.Pos.Y + range; y++)
            for (var x = drone.Pos.X - range; x <= drone.Pos.X + range; x++)
            {
                var p = new Pos(x, y);
                if (p != drone.Pos && p.Chebyshev(operatorUnit.Pos) <= control && CanHover(sim, p)) result.Add(p);
            }
        return result;
    }

    /// <summary>The weather keeps drones down (a dust storm): none takes off or flies; a drone up can still be recalled.</summary>
    public static bool Grounded(ScavengeSim sim) => FieldEnvironment.Effects(sim.Data, sim.State).DronesGrounded;

    internal static ScavengeResult Deploy(ScavengeSim sim, Unit unit, DeployDrone c)
    {
        var data = sim.Data;
        var state = sim.State;
        var rules = data.Rules.Drone;
        if (DroneOf(state, unit) is not null) return ScavengeResult.Fail(ScavengeError.DroneAlreadyOut, $"{unit.Name}'s drone is already out.");
        if (CarriedDrone(data, unit) is not { } itemId) return ScavengeResult.Fail(ScavengeError.NoDrone, $"{unit.Name} carries no scout drone.");
        if (Grounded(sim)) return ScavengeResult.Fail(ScavengeError.DroneGrounded, "The weather keeps drones on the ground.");
        if (unit.Pos.Chebyshev(c.At) != 1 || !CanHover(sim, c.At))
            return ScavengeResult.Fail(ScavengeError.InvalidTile, $"The drone needs a free tile next to {unit.Name}.");
        var battery = PackedBattery(data, unit, itemId);
        if (battery < rules.MinBattery) return ScavengeResult.Fail(ScavengeError.LowBattery, $"The drone is still charging ({battery}/{rules.MinBattery} rounds).");
        if (data.Items[itemId].Drone!.RequiresHook is { } hook && unit.Hook(hook) <= 0)
            return ScavengeResult.Fail(ScavengeError.MissingPerk, $"{data.Items[itemId].Name} needs a perk {unit.Name} does not have ({hook}).");
        if (rules.DeployApCost > unit.Ap) return NotEnoughAp(unit, rules.DeployApCost);

        unit.Ap -= rules.DeployApCost;
        var spec = data.Items[itemId].Drone!;
        var fromTool = unit.Equipment.TryGetValue(EquipSlot.Tool, out var tool) && tool == itemId;
        if (fromTool) unit.Equipment.Remove(EquipSlot.Tool);
        else unit.Pack.Remove(itemId);
        var drone = new Unit
        {
            Id = state.NextUnitId++,
            Name = $"{unit.Name}'s drone",
            Kind = UnitKind.Drone,
            Side = unit.Side,
            PlayerId = unit.PlayerId,
            OperatorId = unit.Id,
            Pos = c.At,
            Hp = spec.Hp ?? rules.Hp,
            MaxHp = spec.Hp ?? rules.Hp,
            CanBeDowned = false,
            Attributes = Enum.GetValues<AttributeKind>().ToDictionary(a => a, a => rules.Attributes.GetValueOrDefault(a, 50)),
            Equipment = DroneRoles.Equipment(data, spec),
            // A combat drone shoots with its operator's piloting.
            Skills = spec.Role == DroneRole.Combat ? new() { [Skill.Firearms] = unit.Get(Skill.DronePiloting) } : new(),
            Battery = battery,
            DroneItemId = itemId,
            DroneFromTool = fromTool,
        };
        state.Units.Add(drone);
        unit.DroneId = drone.Id;
        unit.DroneBattery = null;
        state.Log(new(state.Round, ScavengeEventType.DroneDeployed, $"{unit.Name} launches a {data.Items[itemId].Name} ({battery} rounds of battery).",
            unit.Id, drone.Id, c.At));
        sim.Moved(drone, unit.Pos);
        return ScavengeResult.Success($"{unit.Name} launched the drone.", drone.Id);
    }

    internal static ScavengeResult Fly(ScavengeSim sim, Unit unit, FlyDrone c)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Drone;
        if (DroneOf(state, unit) is not { } drone) return ScavengeResult.Fail(ScavengeError.NoDrone, $"{unit.Name} has no drone in the air.");
        if (drone.Battery <= 0) return ScavengeResult.Fail(ScavengeError.LowBattery, "The drone's battery is flat.");
        if (Grounded(sim)) return ScavengeResult.Fail(ScavengeError.DroneGrounded, "The weather keeps drones on the ground: recall it.");
        if (!CanHover(sim, c.To) || c.To == drone.Pos) return ScavengeResult.Fail(ScavengeError.InvalidTile, $"The drone cannot hover at {c.To}.");
        if (drone.Pos.Chebyshev(c.To) > FlyRange(sim, drone)) return ScavengeResult.Fail(ScavengeError.TooFar, $"{c.To} is beyond one flight.");
        if (unit.Pos.Chebyshev(c.To) > ControlRange(sim.Data, unit, drone.DroneItemId!))
            return ScavengeResult.Fail(ScavengeError.OutOfRange, $"{c.To} is beyond {unit.Name}'s control range.");
        if (rules.FlyApCost > unit.Ap) return NotEnoughAp(unit, rules.FlyApCost);

        unit.Ap -= rules.FlyApCost;
        var from = drone.Pos;
        drone.Pos = c.To;
        unit.GainSkill(Skill.DronePiloting, rules.SkillPerFlight);
        unit.FieldXp += rules.XpPerFlight;
        state.Log(new(state.Round, ScavengeEventType.DroneFlown, $"{drone.Name} flew {from} -> {c.To}.", drone.Id, Pos: c.To));
        sim.Moved(drone, from);
        return ScavengeResult.Success($"{drone.Name} flew to {c.To}.", drone.Id);
    }

    internal static ScavengeResult Recall(ScavengeSim sim, Unit unit)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Drone;
        if (DroneOf(state, unit) is not { } drone) return ScavengeResult.Fail(ScavengeError.NoDrone, $"{unit.Name} has no drone in the air.");
        if (unit.Pos.Chebyshev(drone.Pos) > ControlRange(sim.Data, unit, drone.DroneItemId!))
            return ScavengeResult.Fail(ScavengeError.OutOfRange, $"The drone is beyond {unit.Name}'s control range.");
        if (rules.RecallApCost > unit.Ap) return NotEnoughAp(unit, rules.RecallApCost);
        unit.Ap -= rules.RecallApCost;
        Stow(state, unit, drone);
        state.Log(new(state.Round, ScavengeEventType.DroneRecalled, $"{unit.Name} recalls the drone.", unit.Id, Pos: unit.Pos));
        return ScavengeResult.Success($"{unit.Name} recalled the drone.", unit.Id);
    }

    internal static ScavengeResult Mark(ScavengeSim sim, Unit unit, MarkTarget c)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Drone;
        if (unit.Hook(PerkHooks.DroneCanMark) <= 0) return ScavengeResult.Fail(ScavengeError.MissingPerk, "Marking needs the Spotter perk.");
        if (DroneOf(state, unit) is not { } drone) return ScavengeResult.Fail(ScavengeError.NoDrone, $"{unit.Name} has no drone in the air.");
        var target = state.FindUnit(c.TargetId);
        // As with attacks, missing and unseen answer alike.
        if (target is not { IsActive: true } || !Detection.Spots(sim.Data, state, drone, target))
            return ScavengeResult.Fail(ScavengeError.NotVisible, $"The drone sees no unit {c.TargetId}.");
        if (!drone.HostileTo(target)) return ScavengeResult.Fail(ScavengeError.InvalidTarget, $"{target.Name} is not hostile.");
        if (rules.MarkApCost > unit.Ap) return NotEnoughAp(unit, rules.MarkApCost);
        unit.Ap -= rules.MarkApCost;
        state.Marks.RemoveAll(m => m.UnitId == target.Id && m.Side == unit.Side);
        state.Marks.Add(new Mark(target.Id, unit.Side, state.Round + rules.MarkRounds - 1));
        unit.GainSkill(Skill.DronePiloting, rules.SkillPerFlight);
        state.Log(new(state.Round, ScavengeEventType.Marked, $"{drone.Name} marks {target.Name} for the team.", drone.Id, target.Id, target.Pos,
            Side: unit.Side));
        return ScavengeResult.Success($"{target.Name} is marked.", target.Id);
    }

    internal static ScavengeResult Drop(ScavengeSim sim, Unit unit, DropPayload c)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Drone;
        if (unit.Hook(PerkHooks.DronePayload) <= 0) return ScavengeResult.Fail(ScavengeError.MissingPerk, "Dropping a payload needs the Payload perk.");
        if (DroneOf(state, unit) is not { } drone) return ScavengeResult.Fail(ScavengeError.NoDrone, $"{unit.Name} has no drone in the air.");
        if (!unit.Pack.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} carries no '{c.ItemId}'.");
        if (sim.Data.Items[c.ItemId].Use is not { TargetsTile: true } use)
            return ScavengeResult.Fail(ScavengeError.NotUsable, $"The drone can only drop flares and motion sensors.");
        var cost = rules.PayloadApCost;
        if (cost > unit.Ap) return NotEnoughAp(unit, cost);
        unit.Ap -= cost;
        state.Log(new(state.Round, ScavengeEventType.PayloadDropped, $"{drone.Name} drops a {sim.Data.Items[c.ItemId].Name}.", drone.Id, Pos: drone.Pos));
        ScavengeCommandHandlers.PlaceOnTile(sim, unit, sim.Data.Items[c.ItemId], use with { ApCost = 0 }, drone.Pos);
        return ScavengeResult.Success($"{drone.Name} dropped the payload.", drone.Id);
    }

    /// <summary>The drone goes back to its operator (pack, or the Tool slot it came from if that is free) with its battery.</summary>
    internal static void Stow(ScavengeState state, Unit operatorUnit, Unit drone)
    {
        if (drone.DroneFromTool && !operatorUnit.Equipment.ContainsKey(EquipSlot.Tool)) operatorUnit.Equipment[EquipSlot.Tool] = drone.DroneItemId!;
        else operatorUnit.Pack.Add(drone.DroneItemId!);
        operatorUnit.Pack.AddRange(drone.Pack);
        drone.Pack.Clear();
        operatorUnit.DroneBattery = drone.Battery;
        operatorUnit.DroneId = null;
        drone.Status = UnitStatus.Extracted;
        drone.OperatorId = null;
        state.Marks.RemoveAll(m => m.UnitId == drone.Id);
    }

    /// <summary>
    /// Round start: drones in the air use battery (more in a jammer's field); one that runs flat, or whose operator is no
    /// longer up, lands and is left on its tile as an item. Packed drones recharge.
    /// </summary>
    internal static void RoundStarted(ScavengeSim sim)
    {
        var state = sim.State;
        var rules = sim.Data.Rules.Drone;
        foreach (var drone in state.Units.Where(u => u.Kind == UnitKind.Drone && u.IsActive).OrderBy(u => u.Id).ToList())
        {
            drone.Battery = Math.Max(0, drone.Battery - (Jammed(sim, drone.Pos) ? rules.JammedDrain : 1));
            var op = drone.OperatorId is { } oid ? state.FindUnit(oid) : null;
            if (drone.Battery > 0 && op is { IsActive: true }) continue;
            Land(sim, drone, op, drone.Battery > 0 ? "lost its operator" : "ran out of battery");
        }
        foreach (var op in state.Units.Where(u => u.DroneBattery is not null && u.DroneId is null && u.IsActive))
            if (CarriedDrone(sim.Data, op) is { } item && MaxBattery(sim.Data, op, item) is var max)
                op.DroneBattery = op.DroneBattery + rules.RechargePerRound >= max ? null : op.DroneBattery + rules.RechargePerRound;
    }

    /// <summary>A drone forced down (an EMP): it lands where it is with a flat battery, as if it ran out.</summary>
    internal static void Downed(ScavengeSim sim, Unit drone, string why)
    {
        drone.Battery = 0;
        Land(sim, drone, drone.OperatorId is { } oid ? sim.State.FindUnit(oid) : null, why);
    }

    /// <summary>A drone comes down where it is: its item lies there (with any cargo) to pick up again.</summary>
    private static void Land(ScavengeSim sim, Unit drone, Unit? op, string why)
    {
        var state = sim.State;
        drone.Status = UnitStatus.Extracted;
        var cache = state.Caches.Find(c => c.Pos == drone.Pos);
        if (cache is null) state.Caches.Add(cache = new LootCache { Id = state.NextCacheId++, Pos = drone.Pos });
        cache.Items.Add(drone.DroneItemId!);
        cache.Items.AddRange(drone.Pack);
        drone.Pack.Clear();
        if (op is not null)
        {
            op.DroneId = null;
            op.DroneBattery = 0;
        }
        state.Marks.RemoveAll(m => m.UnitId == drone.Id);
        state.Log(new(state.Round, ScavengeEventType.DroneLanded, $"{drone.Name} {why} and lands at {drone.Pos}.", drone.Id, Pos: drone.Pos));
    }

    /// <summary>A destroyed drone is gone; its operator's link with it too.</summary>
    internal static void Destroyed(ScavengeState state, Unit drone)
    {
        if (drone.OperatorId is { } oid && state.FindUnit(oid) is { } op && op.DroneId == drone.Id)
        {
            op.DroneId = null;
            op.DroneBattery = null;
        }
        state.Marks.RemoveAll(m => m.UnitId == drone.Id);
    }

    public static bool IsMarked(ScavengeState state, int unitId, string side) =>
        state.Marks.Any(m => m.UnitId == unitId && m.Side == side && m.UntilRound >= state.Round);

    /// <summary>Melee cannot reach a flying drone.</summary>
    public static bool CanStrike(ActionProfile action, Unit target) => target.Kind != UnitKind.Drone || action.Pattern != AttackPattern.Melee;

    private static ScavengeResult NotEnoughAp(Unit unit, int cost) =>
        ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
}
