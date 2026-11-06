using Remnants.Core.Colony;
using Remnants.Core.Items;

namespace Remnants.Core.Scavenge;

internal static class ScavengeCommandHandlers
{
    /// <summary>Checks shared by every command: instance running, unit exists, belongs to the player, and it can act.</summary>
    public static ScavengeResult? Validate(ScavengeSim sim, IScavengeCommand c, out Unit? unit)
    {
        unit = null;
        if (sim.State.Finished) return ScavengeResult.Fail(ScavengeError.Finished, "This zone run is over.");
        unit = sim.State.FindUnit(c.UnitId);
        if (unit is null) return ScavengeResult.Fail(ScavengeError.UnknownUnit, $"No unit {c.UnitId}.");
        if (unit.PlayerId != c.PlayerId || !unit.PlayerControlled)
            return ScavengeResult.Fail(ScavengeError.NotYourUnit, $"{unit.Name} does not take orders from player {c.PlayerId}.");
        if (!unit.IsActive) return ScavengeResult.Fail(ScavengeError.Incapacitated, $"{unit.Name} cannot act ({unit.Status}).");
        return null;
    }

    /// <summary>Queues a move from where the plan ends: its MP is paid now, it is walked in the movement phase.</summary>
    public static ScavengeResult Move(ScavengeSim sim, Unit unit, Move c)
    {
        if (!Plans.HasRoom(sim, unit, PlanSlot.Move)) return Plans.Full(unit, PlanSlot.Move);
        if (!sim.Reachable(unit).TryGetValue(c.To, out var cost))
            return ScavengeResult.Fail(ScavengeError.Unreachable, $"{unit.Name} cannot reach {c.To} in one move.");
        if (cost > unit.Mp) return ScavengeResult.Fail(ScavengeError.NotEnoughMp, $"{unit.Name} needs {cost} MP (has {unit.Mp}).");
        Plans.AddMove(sim, unit, c.To, cost);
        return ScavengeResult.Success($"{unit.Name} will move to {c.To}.");
    }

    public static ScavengeResult UseItem(ScavengeSim sim, Unit unit, UseItem c, bool checkOnly = false)
    {
        if (!unit.Pack.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} carries no '{c.ItemId}'.");
        var item = sim.Item(c.ItemId);
        if (item.Use is not { } use) return ScavengeResult.Fail(ScavengeError.NotUsable, $"{item.Name} cannot be used.");
        if (ElectronicWarfare.CheckCharges(sim.Data, unit, item) is { } flat) return flat;
        if (use.TargetsTile) return UseOnTile(sim, unit, c, item, use, checkOnly);

        var target = c.TargetId is { } id ? sim.State.FindUnit(id) : unit;
        if (target is not { OnMap: true } || unit.HostileTo(target))
            return ScavengeResult.Fail(ScavengeError.InvalidTarget, $"Unit {c.TargetId} is not a valid target.");
        if (unit.Pos.Chebyshev(target.Pos) > use.Range)
            return ScavengeResult.Fail(ScavengeError.TooFar, $"{target.Name} is out of {item.Name}'s reach.");
        var apCost = FieldMedicine.ApCost(unit, use);
        var downed = target.Status == UnitStatus.Downed;
        if (checkOnly)
            return downed && !use.Evacuate && !use.Stabilize
                ? ScavengeResult.Fail(ScavengeError.NotUsable, $"{item.Name} does nothing for {target.Name} ({target.Status}).")
                : ScavengeResult.Success("");
        if (apCost > unit.Ap) return NotEnoughAp(unit, apCost);

        if (use.Evacuate)
        {
            target.Status = UnitStatus.Evacuated;
            sim.Log(ScavengeEventType.Evacuated, $"{target.Name} was pulled out by {item.Name}.", unit, target);
        }
        else if (downed && use.Stabilize)
        {
            target.Stabilized = true;
            MissionRules.Treated(sim, unit, target);
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
        else if (downed || !FieldMedicine.Use(sim, unit, target, item, use))
        {
            return ScavengeResult.Fail(ScavengeError.NotUsable, $"{item.Name} does nothing for {target.Name} ({target.Status}).");
        }

        unit.Ap -= apCost;
        ElectronicWarfare.Spend(unit, item, use);
        return ScavengeResult.Success($"{unit.Name} used {item.Name} on {target.Name}.", target.Id);
    }

    /// <summary>A flare thrown at, or a motion sensor placed on, a tile within the item's range.</summary>
    private static ScavengeResult UseOnTile(ScavengeSim sim, Unit unit, UseItem c, Items.ItemDef item, Items.UseEffect use, bool checkOnly)
    {
        var state = sim.State;
        if (c.At is not { } at || !state.Map.InBounds(at))
            return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{item.Name} needs a tile on the map.");
        if (unit.Pos.Chebyshev(at) > use.Range) return ScavengeResult.Fail(ScavengeError.TooFar, $"{at} is out of {item.Name}'s reach.");
        if (use.SensorRadius > 0 && (!sim.Data.Terrain(state.Map[at]).Passable || state.DeviceAt(at) is not null))
            return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{item.Name} cannot be placed on {state.Map[at]}.");
        if (use.Decoy is not null && !ElectronicWarfare.CanPlaceDecoy(sim, at))
            return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{item.Name} needs a free floor tile.");
        if (checkOnly) return ScavengeResult.Success("");
        if (use.ApCost > unit.Ap) return NotEnoughAp(unit, use.ApCost);
        PlaceOnTile(sim, unit, item, use, at);
        return ScavengeResult.Success($"{unit.Name} used {item.Name} at {at}.");
    }

    /// <summary>Pays, uses up and sets off a flare or motion sensor at a tile (by hand, or dropped by a drone).</summary>
    internal static void PlaceOnTile(ScavengeSim sim, Unit unit, ItemDef item, UseEffect use, Pos at)
    {
        var state = sim.State;
        unit.Ap -= use.ApCost;
        ElectronicWarfare.Spend(unit, item, use);
        if (use.Emp is { } emp) ElectronicWarfare.Emp(sim, unit, item, emp, at);
        if (use.Decoy is { } decoy && ElectronicWarfare.CanPlaceDecoy(sim, at)) ElectronicWarfare.Decoy(sim, unit, item, decoy, at);
        if (use.NoiseRadius > 0) ElectronicWarfare.Noise(sim, unit, item, use.NoiseRadius, at);
        if (use.LightRadius > 0)
        {
            var effects = FieldEnvironment.Effects(sim.Data, state);
            var rounds = Math.Max(1, (int)Math.Round(use.LightRounds * effects.FlareRoundsMultiplier));
            state.Flares.Add(new Flare(at, use.LightRadius * effects.FlareRadiusMultiplier, state.Round + rounds - 1));
            state.Log(new(state.Round, ScavengeEventType.FlareLit, $"{unit.Name} throws a {item.Name}: {at} is lit up.", unit.Id, Pos: at));
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
        MissionRules.PickedUp(sim, unit, c.ItemId);
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
        if (npc is not { Kind: UnitKind.Npc, IsActive: true, Side: Unit.NeutralSide, NoRecruit: false })
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
        if (!sim.State.Map.IsExit(unit.Pos) && ParkedVehicles.Beside(sim.State, unit) is null)
            return ScavengeResult.Fail(ScavengeError.NotOnExit, $"{unit.Name} is not on an exit tile or next to the party's vehicle.");

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

    /// <summary>Jury-rig: a mod from the pack onto equipped gear, or off it into the pack (<see cref="GearBench"/>).</summary>
    public static ScavengeResult FieldMod(ScavengeSim sim, Unit unit, FieldMod c)
    {
        if (sim.Data.GearMods is not { } mods) return ScavengeResult.Fail(ScavengeError.CannotMod, "There are no gear mods.");
        if (unit.Hook(mods.Def.Rules.FieldHook) <= 0)
            return ScavengeResult.Fail(ScavengeError.MissingPerk, $"{unit.Name} needs Jury-rig to modify gear in the field.");
        if (!unit.Equipment.TryGetValue(c.Slot, out var gear)) return ScavengeResult.Fail(ScavengeError.CannotMod, $"{unit.Name} has nothing in the {c.Slot} slot.");
        var cost = mods.Def.Rules.FieldApCost;
        if (cost > unit.Ap) return NotEnoughAp(unit, cost);
        GearBench.Outcome outcome;
        if (c.ModItemId is { } mod)
        {
            if (!unit.Pack.Contains(mod)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} carries no '{mod}'.");
            outcome = GearBench.Install(sim.Data, gear, mod);
            if (outcome.Ok) unit.Pack.Remove(mod);
        }
        else if (c.RemoveSlot is { } slot) outcome = GearBench.Remove(sim.Data, gear, slot);
        else return ScavengeResult.Fail(ScavengeError.CannotMod, "Name a mod to fit or a slot to clear.");
        if (!outcome.Ok) return ScavengeResult.Fail(ScavengeError.CannotMod, outcome.Message);

        unit.Equipment[c.Slot] = outcome.ItemId!;
        if (outcome.Returned is { } back) unit.Pack.Add(back);
        unit.Ap -= cost;
        sim.Log(ScavengeEventType.GearModded, $"{unit.Name} jury-rigs their gear: {outcome.Message}", unit);
        return ScavengeResult.Success(outcome.Message);
    }

    private static ScavengeResult NotEnoughAp(Unit unit, int cost) =>
        ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
}
