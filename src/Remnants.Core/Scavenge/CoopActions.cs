namespace Remnants.Core.Scavenge;

/// <summary>
/// What allies do for each other (anything on the unit's own side, so also within one party): hand items over, carry
/// the downed out, and mark tiles for the team. Healing an ally is plain <see cref="UseItem"/> (items work on anyone not
/// hostile). Tuned in scavenge.json <c>coop</c>.
/// </summary>
public static class CoopActions
{
    /// <summary>Why <paramref name="unit"/> cannot give <paramref name="itemId"/> to <paramref name="target"/>, or null.</summary>
    public static ScavengeResult? CheckGive(ScavengeSim sim, Unit unit, Unit? target, string itemId)
    {
        if (!unit.Pack.Contains(itemId)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} carries no '{itemId}'.");
        if (target is not { IsActive: true, Kind: UnitKind.Survivor } || target.Id == unit.Id || target.Side != unit.Side)
            return ScavengeResult.Fail(ScavengeError.InvalidTarget, "Items go to a conscious survivor on your side.");
        if (unit.Pos.Chebyshev(target.Pos) > 1) return ScavengeResult.Fail(ScavengeError.TooFar, $"{target.Name} must stand next to {unit.Name}.");
        var cost = sim.Data.Rules.Coop.GiveApCost;
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
        var item = sim.Item(itemId);
        var profile = sim.Profile(target);
        if (profile.Load + item.Weight > profile.CarryCapacity + 1e-9)
            return ScavengeResult.Fail(ScavengeError.CarryLimitExceeded, $"{target.Name} cannot carry {item.Name}.");
        return null;
    }

    internal static ScavengeResult Give(ScavengeSim sim, Unit unit, GiveItem c)
    {
        var target = sim.State.FindUnit(c.TargetId);
        if (CheckGive(sim, unit, target, c.ItemId) is { } problem) return problem;
        unit.Ap -= sim.Data.Rules.Coop.GiveApCost;
        unit.Pack.Remove(c.ItemId);
        target!.Pack.Add(c.ItemId);
        sim.Log(ScavengeEventType.ItemGiven, $"{unit.Name} hands {sim.Item(c.ItemId).Name} to {target.Name}.", unit, target);
        return ScavengeResult.Success($"{unit.Name} gave {sim.Item(c.ItemId).Name} to {target.Name}.", target.Id);
    }

    /// <summary>Why <paramref name="unit"/> cannot lift <paramref name="target"/>, or null.</summary>
    public static ScavengeResult? CheckCarry(ScavengeSim sim, Unit unit, Unit? target)
    {
        if (unit.CarryingId is not null) return ScavengeResult.Fail(ScavengeError.CannotCarry, $"{unit.Name} already carries someone.");
        if (target is not { Status: UnitStatus.Downed, CarriedById: null } || target.Side != unit.Side)
            return ScavengeResult.Fail(ScavengeError.CannotCarry, "Only a downed unit on your side can be carried.");
        if (unit.Pos.Chebyshev(target.Pos) > 1) return ScavengeResult.Fail(ScavengeError.TooFar, $"{target.Name} must lie next to {unit.Name}.");
        var cost = sim.Data.Rules.Coop.CarryApCost;
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
        return null;
    }

    internal static ScavengeResult Carry(ScavengeSim sim, Unit unit, Carry c)
    {
        var target = sim.State.FindUnit(c.TargetId);
        if (CheckCarry(sim, unit, target) is { } problem) return problem;
        unit.Ap -= sim.Data.Rules.Coop.CarryApCost;
        unit.CarryingId = target!.Id;
        target.CarriedById = unit.Id;
        target.Pos = unit.Pos;
        sim.Log(ScavengeEventType.PickedUpBody, $"{unit.Name} lifts {target.Name} onto their shoulders.", unit, target);
        return ScavengeResult.Success($"{unit.Name} carries {target.Name}.", target.Id);
    }

    internal static ScavengeResult PutDown(ScavengeSim sim, Unit unit, PutDown c)
    {
        var state = sim.State;
        if (unit.CarryingId is not { } id || state.FindUnit(id) is not { } carried)
            return ScavengeResult.Fail(ScavengeError.CannotCarry, $"{unit.Name} carries nobody.");
        if (!state.Map.InBounds(c.At) || unit.Pos.Chebyshev(c.At) != 1 || !sim.Data.Terrain(state.Map[c.At]).Passable
            || state.Map.IsExit(c.At) || state.UnitAt(c.At) is not null)
            return ScavengeResult.Fail(ScavengeError.InvalidTile, $"{carried.Name} can only be put down on a free tile next to {unit.Name}.");
        unit.CarryingId = null;
        carried.CarriedById = null;
        carried.Pos = c.At;
        sim.Log(ScavengeEventType.PutDown, $"{unit.Name} puts {carried.Name} down.", unit, carried);
        return ScavengeResult.Success($"{unit.Name} put {carried.Name} down.", carried.Id);
    }

    /// <summary>
    /// The carrier lets go (it went down, died, left the map, or the alliance broke): the carried unit is put on the
    /// nearest free floor tile (not an exit) next to it, else one two tiles off in a straight open line, or stays on the
    /// carrier's tile when there is none (both then stand there; very rare in practice).
    /// </summary>
    internal static void Release(ScavengeState state, Unit carrier)
    {
        if (carrier.CarryingId is not { } id) return;
        carrier.CarryingId = null;
        if (state.FindUnit(id) is not { } carried) return;
        carried.CarriedById = null;
        if (!carried.OnMap) return;
        var map = state.Map;
        for (var r = 1; r <= 2; r++)
        {
            var spot = Enumerable.Range(-r, 2 * r + 1)
                .SelectMany(dy => Enumerable.Range(-r, 2 * r + 1).Select(dx => new Pos(carrier.Pos.X + dx, carrier.Pos.Y + dy)))
                .Where(p => map.InBounds(p) && p.Chebyshev(carrier.Pos) == r && Passable(map[p]) && !map.IsExit(p)
                            && state.UnitAt(p) is null && (r == 1 || TileMap.Line(carrier.Pos, p).All(q => Passable(map[q]))))
                .OrderBy(map.Index)
                .Cast<Pos?>()
                .FirstOrDefault();
            if (spot is { } p)
            {
                carried.Pos = p;
                return;
            }
        }
        carried.Pos = carrier.Pos;
    }

    /// <summary>Tiles a body can be set on without data at hand: everything but walls and pylons (as scavenge.json has it).</summary>
    private static bool Passable(Terrain t) => t is not (Terrain.Wall or Terrain.Pylon or Terrain.Vehicle or Terrain.ArmoredVehicle);

    /// <summary>Keeps every carried unit on its carrier's tile, and lets go of it when the carrier cannot carry any more.</summary>
    internal static void Sync(ScavengeState state)
    {
        foreach (var carrier in state.Units.Where(u => u.CarryingId is not null).ToList())
        {
            var carried = state.FindUnit(carrier.CarryingId!.Value);
            if (carried is not { OnMap: true })
            {
                // Dead or gone with the carrier: the link just ends.
                carrier.CarryingId = null;
                if (carried is not null) carried.CarriedById = null;
                continue;
            }
            // Back on their feet, or the carrier went down or left without them: they get a tile of their own.
            if (carried.IsActive || !carrier.IsActive) Release(state, carrier);
            else carried.Pos = carrier.Pos;
        }
    }

    /// <summary>Why <paramref name="unit"/> cannot ping <paramref name="at"/> now, or null.</summary>
    public static ScavengeResult? CheckPing(ScavengeSim sim, Unit unit, Pos at)
    {
        if (!sim.State.Map.InBounds(at)) return ScavengeResult.Fail(ScavengeError.InvalidTile, "Pings go on a tile on the map.");
        if (unit.PingsUsed >= sim.Data.Rules.Coop.PingsPerTurn)
            return ScavengeResult.Fail(ScavengeError.PingLimit, $"{sim.Data.Rules.Coop.PingsPerTurn} pings a turn.");
        return null;
    }

    internal static ScavengeResult Ping(ScavengeSim sim, Unit unit, PingTile c)
    {
        if (CheckPing(sim, unit, c.At) is { } problem) return problem;
        var state = sim.State;
        unit.PingsUsed++;
        state.Pings.RemoveAll(p => p.Side == unit.Side && p.Pos == c.At);
        state.Pings.Add(new Ping(unit.Side, c.PlayerId, c.At, c.Mark, state.Round));
        var what = c.Mark switch { PingKind.Danger => "Danger", PingKind.GoHere => "Go here", _ => "Loot" };
        state.Log(new(state.Round, ScavengeEventType.Pinged, $"{unit.Name} pings {c.At}: {what}.", unit.Id, Pos: c.At, Side: unit.Side));
        return ScavengeResult.Success($"Pinged {c.At}.");
    }
}
