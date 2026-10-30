using Remnants.Core.Colony;
using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

internal static class ScavengeCommandHandlers
{
    /// <summary>Checks shared by every command: instance running, unit exists, belongs to the player, and it is its turn.</summary>
    public static ScavengeResult? Validate(ScavengeSim sim, IScavengeCommand c, out Unit? unit)
    {
        unit = null;
        if (sim.State.Finished) return ScavengeResult.Fail(ScavengeError.Finished, "This zone run is over.");
        unit = sim.State.FindUnit(c.UnitId);
        if (unit is null) return ScavengeResult.Fail(ScavengeError.UnknownUnit, $"No unit {c.UnitId}.");
        if (unit.PlayerId != c.PlayerId || !unit.PlayerControlled)
            return ScavengeResult.Fail(ScavengeError.NotYourUnit, $"{unit.Name} does not take orders from player {c.PlayerId}.");
        if (!unit.IsActive) return ScavengeResult.Fail(ScavengeError.Incapacitated, $"{unit.Name} cannot act ({unit.Status}).");
        if (sim.State.CurrentUnitId != unit.Id) return ScavengeResult.Fail(ScavengeError.NotYourTurn, $"It is not {unit.Name}'s turn.");
        return null;
    }

    public static ScavengeResult Move(ScavengeSim sim, Unit unit, Move c)
    {
        if (!sim.Reachable(unit).TryGetValue(c.To, out var cost))
            return ScavengeResult.Fail(ScavengeError.Unreachable, $"{unit.Name} cannot reach {c.To} in one move.");
        if (cost > unit.Ap) return NotEnoughAp(unit, cost);

        var from = unit.Pos;
        unit.Pos = c.To;
        unit.Ap -= cost;
        // The carried unit moves along before anything reacts to the move.
        if (unit.CarryingId is { } carried && sim.State.FindUnit(carried) is { } load) load.Pos = c.To;
        sim.State.Log(new(sim.State.Round, ScavengeEventType.Moved, $"{unit.Name} moved {from} -> {c.To}.", unit.Id, Pos: c.To));
        sim.Moved(unit, from);
        return ScavengeResult.Success($"{unit.Name} moved to {c.To}.");
    }

    public static ScavengeResult Attack(ScavengeSim sim, Unit unit, Attack c)
    {
        var target = sim.State.FindUnit(c.TargetId);
        // Missing, off the map or unseen all answer alike, so ids cannot be probed for hidden units.
        if (target is not { OnMap: true } || !Detection.SideSees(sim.Data, sim.State, unit.Side, target))
            return ScavengeResult.Fail(ScavengeError.NotVisible, $"{unit.Name}'s side sees no unit {c.TargetId}.");
        if (target.Side == unit.Side && target.PlayerId is { } owner && owner != unit.PlayerId)
            return ScavengeResult.Fail(ScavengeError.FriendlyFire, $"{target.Name} is an ally.");
        if (!unit.HostileTo(target))
            return ScavengeResult.Fail(ScavengeError.InvalidTarget, $"{target.Name} is not a hostile target.");
        var action = sim.Profile(unit).Action(c.ActionId);
        if (action is null) return ScavengeResult.Fail(ScavengeError.UnknownAction, $"{unit.Name} cannot use '{c.ActionId}' now.");
        if (action.ApCost > unit.Ap) return NotEnoughAp(unit, action.ApCost);
        var reach = Combat.CheckReach(sim.Data, sim.State.Map, unit.Pos, action, target.Pos);
        if (reach != ScavengeError.None) return ScavengeResult.Fail(reach, $"{target.Name} is not in reach of {action.Name} ({reach}).");
        if (!Drones.CanStrike(action, target)) return ScavengeResult.Fail(ScavengeError.OutOfRange, $"{action.Name} cannot reach a flying drone.");

        Combat.Resolve(sim, unit, action, target);
        return ScavengeResult.Success($"{unit.Name} used {action.Name} on {target.Name}.", target.Id);
    }

    public static ScavengeResult UseItem(ScavengeSim sim, Unit unit, UseItem c)
    {
        if (!unit.Pack.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} carries no '{c.ItemId}'.");
        var item = sim.Item(c.ItemId);
        if (item.Use is not { } use) return ScavengeResult.Fail(ScavengeError.NotUsable, $"{item.Name} cannot be used.");
        if (use.TargetsTile) return UseOnTile(sim, unit, c, item, use);

        var target = c.TargetId is { } id ? sim.State.FindUnit(id) : unit;
        if (target is not { OnMap: true } || unit.HostileTo(target))
            return ScavengeResult.Fail(ScavengeError.InvalidTarget, $"Unit {c.TargetId} is not a valid target.");
        if (unit.Pos.Chebyshev(target.Pos) > use.Range)
            return ScavengeResult.Fail(ScavengeError.TooFar, $"{target.Name} is out of {item.Name}'s reach.");
        if (use.ApCost > unit.Ap) return NotEnoughAp(unit, use.ApCost);

        var downed = target.Status == UnitStatus.Downed;
        if (use.Evacuate)
        {
            target.Status = UnitStatus.Evacuated;
            sim.Log(ScavengeEventType.Evacuated, $"{target.Name} was pulled out by {item.Name}.", unit, target);
        }
        else if (downed && use.Stabilize)
        {
            target.Stabilized = true;
            target.BleedOut += (int)Math.Round(unit.Hook(PerkHooks.BleedoutRoundsBonus));
            sim.Log(ScavengeEventType.Stabilized, $"{unit.Name} stabilized {target.Name}.", unit, target);
            if (use.ReviveHp > 0)
            {
                target.Status = UnitStatus.Active;
                target.Hp = Math.Min(target.MaxHp, use.ReviveHp + unit.Hook(PerkHooks.ReviveHpBonus));
                target.Stabilized = false;
                sim.Log(ScavengeEventType.Revived, $"{target.Name} is back on their feet.", unit, target);
            }
        }
        else if (!downed && use.Heal > 0)
        {
            target.Hp = Math.Min(target.MaxHp, target.Hp + use.Heal * (1 + unit.Hook(PerkHooks.FieldHealBonus)));
            sim.Log(ScavengeEventType.Healed, $"{unit.Name} treated {target.Name} with {item.Name}.", unit, target);
        }
        else
        {
            return ScavengeResult.Fail(ScavengeError.NotUsable, $"{item.Name} does nothing for {target.Name} ({target.Status}).");
        }

        unit.Ap -= use.ApCost;
        if (use.Consumed) unit.Pack.Remove(c.ItemId);
        return ScavengeResult.Success($"{unit.Name} used {item.Name} on {target.Name}.", target.Id);
    }

    /// <summary>A flare thrown at, or a motion sensor placed on, a tile within the item's range.</summary>
    private static ScavengeResult UseOnTile(ScavengeSim sim, Unit unit, UseItem c, Items.ItemDef item, Items.UseEffect use)
    {
        var state = sim.State;
        if (c.At is not { } at || !state.Map.InBounds(at))
            return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{item.Name} needs a tile on the map.");
        if (unit.Pos.Chebyshev(at) > use.Range) return ScavengeResult.Fail(ScavengeError.TooFar, $"{at} is out of {item.Name}'s reach.");
        if (use.SensorRadius > 0 && (!sim.Data.Terrain(state.Map[at]).Passable || state.DeviceAt(at) is not null))
            return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{item.Name} cannot be placed on {state.Map[at]}.");
        if (use.ApCost > unit.Ap) return NotEnoughAp(unit, use.ApCost);
        PlaceOnTile(sim, unit, item, use, at);
        return ScavengeResult.Success($"{unit.Name} used {item.Name} at {at}.");
    }

    /// <summary>Pays, uses up and sets off a flare or motion sensor at a tile (by hand, or dropped by a drone).</summary>
    internal static void PlaceOnTile(ScavengeSim sim, Unit unit, ItemDef item, UseEffect use, Pos at)
    {
        var state = sim.State;
        unit.Ap -= use.ApCost;
        if (use.Consumed) unit.Pack.Remove(item.Id);
        if (use.LightRadius > 0)
        {
            state.Flares.Add(new Flare(at, use.LightRadius * FieldEnvironment.Effects(sim.Data, state).FlareRadiusMultiplier, state.Round + use.LightRounds - 1));
            state.Log(new(state.Round, ScavengeEventType.FlareLit, $"{unit.Name} throws a {item.Name}: {at} is lit up.", unit.Id, Pos: at));
            // Hostiles standing in the light are seen now; overwatch on them may fire.
            foreach (var u in state.Units.Where(u => u.IsActive && Detection.IsLit(state, u.Pos)).OrderBy(u => u.Id).ToList())
                sim.CheckOverwatch(u);
        }
        if (use.SensorRadius > 0)
        {
            var sensor = new Sensor(state.NextSensorId++, unit.Side, at, use.SensorRadius, item.Id, unit.PlayerId);
            state.Sensors.Add(sensor);
            state.Log(new(state.Round, ScavengeEventType.SensorPlaced, $"{unit.Name} sets up a {item.Name} at {at}.", unit.Id, Pos: at,
                Side: unit.Side));
            foreach (var u in state.Units.Where(u => u.IsActive && u.Side != unit.Side && u.Side != Unit.NeutralSide
                                                     && u.Pos.Chebyshev(at) <= sensor.Radius).OrderBy(u => u.Id).ToList())
                sim.Trip(sensor, u);
        }
    }

    /// <summary>Overwatch or brace: pays the AP and ends the turn (see <see cref="ScavengeSim"/>).</summary>
    public static ScavengeResult SetStance(ScavengeSim sim, Unit unit, SetStance c)
    {
        var fog = sim.Data.Rules.Fog;
        var cost = c.Stance switch
        {
            Stance.Overwatch => fog.Overwatch.ApCost,
            Stance.Brace => fog.Brace.ApCost,
            _ => -1,
        };
        if (cost < 0) return ScavengeResult.Fail(ScavengeError.InvalidStance, $"'{c.Stance}' is not a stance a unit can take.");
        if (cost > unit.Ap) return NotEnoughAp(unit, cost);
        if (c.Stance == Stance.Overwatch && sim.Profile(unit).Actions.Count == 0)
            return ScavengeResult.Fail(ScavengeError.InvalidStance, $"{unit.Name} has nothing to fire on overwatch.");

        var left = unit.Ap - cost;
        unit.Ap = 0;
        Enter(sim, unit, c.Stance, left);
        return ScavengeResult.Success(c.Stance == Stance.Overwatch ? $"{unit.Name} is on overwatch." : $"{unit.Name} braces.");
    }

    /// <summary>
    /// Puts a unit in a stance and logs it (players through <see cref="SetStance"/>, the Linked lurking). On overwatch the
    /// <paramref name="apLeft"/> after paying become its reactions (1..<see cref="OverwatchRules.Reactions"/>).
    /// </summary>
    internal static void Enter(ScavengeSim sim, Unit unit, Stance stance, int apLeft)
    {
        unit.Stance = stance;
        var max = sim.Data.Rules.Fog.Overwatch.Reactions + (int)Math.Round(unit.Hook(PerkHooks.OverwatchReactionsBonus));
        unit.Reactions = stance == Stance.Overwatch ? Math.Clamp(apLeft + (max - sim.Data.Rules.Fog.Overwatch.Reactions), 1, max) : 0;
        if (stance == Stance.Overwatch)
            sim.State.Log(new(sim.State.Round, ScavengeEventType.Overwatch, $"{unit.Name} goes on overwatch.", unit.Id, Pos: unit.Pos));
        else
            sim.State.Log(new(sim.State.Round, ScavengeEventType.Braced, $"{unit.Name} braces for an ambush.", unit.Id, Pos: unit.Pos));
    }

    public static ScavengeResult Lookout(ScavengeSim sim, Unit unit, Lookout c)
    {
        if (unit.Lookout) return ScavengeResult.Fail(ScavengeError.AlreadyOnLookout, $"{unit.Name} is already on lookout.");
        var cost = sim.Data.Rules.Fog.Lookout.ApCost;
        if (cost > unit.Ap) return NotEnoughAp(unit, cost);
        unit.Ap -= cost;
        unit.Lookout = true;
        sim.Log(ScavengeEventType.Lookout, $"{unit.Name} scans the surroundings.", unit);
        return ScavengeResult.Success($"{unit.Name} is on lookout.");
    }

    public static ScavengeResult PickUp(ScavengeSim sim, Unit unit, PickUp c)
    {
        var cache = sim.State.FindCache(c.CacheId);
        if (cache is null) return ScavengeResult.Fail(ScavengeError.UnknownCache, $"No cache {c.CacheId}.");
        if (unit.Pos.Chebyshev(cache.Pos) > 1) return ScavengeResult.Fail(ScavengeError.TooFar, $"{unit.Name} is not next to cache {cache.Id}.");
        if (!cache.Items.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotInCache, $"Cache {cache.Id} has no '{c.ItemId}'.");
        var cost = sim.Data.Rules.Turn.PickUpApCost;
        if (cost > unit.Ap) return NotEnoughAp(unit, cost);
        var item = sim.Item(c.ItemId);
        var profile = sim.Profile(unit);
        if (profile.Load + item.Weight > profile.CarryCapacity + 1e-9)
            return ScavengeResult.Fail(ScavengeError.CarryLimitExceeded,
                $"{unit.Name} cannot carry {item.Name} ({profile.Load + item.Weight:0.#}/{profile.CarryCapacity:0.#} kg).");

        unit.Ap -= cost;
        cache.Items.Remove(c.ItemId);
        unit.Pack.Add(c.ItemId);
        if (cache.Items.Count == 0) sim.State.Caches.Remove(cache);
        sim.Log(ScavengeEventType.PickedUp, $"{unit.Name} picked up {item.Name}.", unit);
        // Scavenger: a sharp eye turns up a second lot of a material.
        if (item.Kind == ItemKind.Material && unit.Hook(PerkHooks.LootBonus) is var bonus and > 0
            && profile.Load + 2 * item.Weight <= profile.CarryCapacity + 1e-9 && sim.Rng.NextDouble() < bonus)
        {
            unit.Pack.Add(c.ItemId);
            sim.Log(ScavengeEventType.PickedUp, $"{unit.Name} finds more {item.Name}.", unit);
        }
        return ScavengeResult.Success($"{unit.Name} picked up {item.Name}.", cache.Id);
    }

    public static ScavengeResult Recruit(ScavengeSim sim, Unit unit, Recruit c)
    {
        var npc = sim.State.FindUnit(c.NpcId);
        if (npc is not { Kind: UnitKind.Npc, IsActive: true, Side: Unit.NeutralSide })
            return ScavengeResult.Fail(ScavengeError.NotRecruitable, $"Unit {c.NpcId} cannot be recruited.");
        if (unit.Pos.Chebyshev(npc.Pos) > 1) return ScavengeResult.Fail(ScavengeError.TooFar, $"{unit.Name} must stand next to {npc.Name}.");
        var cost = sim.Data.Rules.Turn.RecruitApCost;
        if (cost > unit.Ap) return NotEnoughAp(unit, cost);

        unit.Ap -= cost;
        npc.Side = unit.Side;
        npc.PlayerId = unit.PlayerId;
        npc.FollowingId = unit.Id;
        sim.Log(ScavengeEventType.Recruited, $"{npc.Name} agrees to follow {unit.Name} home.", unit, npc);
        return ScavengeResult.Success($"{npc.Name} joined {unit.Name}.", npc.Id);
    }

    /// <summary>
    /// Leaves from an exit tile with the unit it carries (who goes home to their own player). Followers on the unit's
    /// side within the escort radius leave too, and a recruit joins whoever escorts it out, an ally's included.
    /// </summary>
    public static ScavengeResult Extract(ScavengeSim sim, Unit unit)
    {
        if (!sim.State.Map.IsExit(unit.Pos)) return ScavengeResult.Fail(ScavengeError.NotOnExit, $"{unit.Name} is not on an exit tile.");

        // A drone still in the air comes home with its operator.
        if (Drones.DroneOf(sim.State, unit) is { } drone) Drones.Stow(sim.State, unit, drone);
        unit.Status = UnitStatus.Extracted;
        sim.Log(ScavengeEventType.Extracted, $"{unit.Name} extracted.", unit);
        if (unit.CarryingId is { } carriedId && sim.State.FindUnit(carriedId) is { Status: UnitStatus.Downed } carried)
        {
            carried.Status = UnitStatus.Extracted;
            carried.CarriedById = null;
            unit.CarryingId = null;
            sim.Log(ScavengeEventType.Extracted, $"{carried.Name} was carried out by {unit.Name}.", carried, unit);
        }
        var radius = sim.Data.Rules.EscortRadius;
        foreach (var f in sim.State.Units.Where(u =>
                     u.IsActive && u.FollowingId is not null && u.Side == unit.Side && u.Pos.Chebyshev(unit.Pos) <= radius))
        {
            f.Status = UnitStatus.Extracted;
            f.PlayerId = unit.PlayerId;
            f.FollowingId = unit.Id;
            sim.Log(ScavengeEventType.Extracted, $"{f.Name} extracted with {unit.Name}.", f);
        }
        return ScavengeResult.Success($"{unit.Name} extracted.", unit.Id);
    }

    private static ScavengeResult NotEnoughAp(Unit unit, int cost) =>
        ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
}
