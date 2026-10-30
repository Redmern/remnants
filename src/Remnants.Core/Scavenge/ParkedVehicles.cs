namespace Remnants.Core.Scavenge;

/// <summary>
/// A party's vehicle as it enters a run (from the colony's vehicle, or a server player's): its tier id and number for the
/// model, HP (its condition) and armor, the stash's cargo limit, whether it parks as hard cover, and its seats (null = no
/// limit), which cap the party.
/// </summary>
public sealed record FieldVehicle(string TierId, string Name, int Tier, double Hp, double MaxHp, double Armor, double CargoKg,
    bool Armored, int? Seats = null);

/// <summary>
/// A vehicle parked at the spawn edge (<see cref="ScavengeState.Vehicles"/>). Its tile is <see cref="Terrain.Vehicle"/>
/// (or <see cref="Terrain.ArmoredVehicle"/>, hard cover): it blocks movement and covers who hides behind it. The owner's
/// side extracts from any tile next to it and can unload carried items into its <see cref="Stash"/> up to
/// <see cref="CargoKg"/>; the stash goes home with the vehicle. Hostiles next to it at the start of a round damage it; at 0
/// HP it is <see cref="Wrecked"/>: the stash is lost and nobody extracts there.
/// </summary>
public sealed class ParkedVehicle
{
    public int PlayerId { get; set; }
    public string TierId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Tier { get; set; }
    public Pos Pos { get; set; }
    public double Hp { get; set; }
    public double MaxHp { get; set; }
    public double Armor { get; set; }
    public double CargoKg { get; set; }
    public bool Armored { get; set; }
    public List<string> Stash { get; set; } = new();
    public bool Wrecked { get; set; }
}

/// <summary>What became of a party's vehicle in the run: its HP at the end, wrecked or not, and the stash it carries home.</summary>
public sealed record VehicleResult(double Hp, double MaxHp, bool Wrecked, List<string> Stash);

/// <summary>Placing the parked vehicles, stashing loot, extracting next to them, and enemies damaging them.</summary>
public static class ParkedVehicles
{
    public static ParkedVehicle? Of(ScavengeState state, int playerId) => state.Vehicles.Find(v => v.PlayerId == playerId);

    public static ParkedVehicle? At(ScavengeState state, Pos pos) => state.Vehicles.Find(v => v.Pos == pos);

    /// <summary>Weight of the stash now.</summary>
    public static double Load(ScavengeData data, ParkedVehicle vehicle) => vehicle.Stash.Sum(id => data.Items[id].Weight);

    /// <summary>
    /// Parks a party's vehicle on the spawn edge: the floor tile nearest the party's spawn point that is not an exit, so
    /// the party starts right beside it. Returns null when the edge has no room.
    /// </summary>
    internal static ParkedVehicle? Park(ScavengeState state, int playerId, FieldVehicle field, IEnumerable<Pos> edge, Pos anchor)
    {
        var map = state.Map;
        var spot = edge.Where(p => map[p] == Terrain.Floor && !map.IsExit(p) && state.UnitAt(p) is null && At(state, p) is null)
            .OrderBy(p => p.DistanceSquared(anchor)).ThenBy(map.Index).Cast<Pos?>().FirstOrDefault();
        if (spot is not { } pos) return null;
        var vehicle = new ParkedVehicle
        {
            PlayerId = playerId, TierId = field.TierId, Name = field.Name, Tier = field.Tier, Pos = pos,
            Hp = Math.Clamp(field.Hp, 1, field.MaxHp), MaxHp = field.MaxHp, Armor = field.Armor, CargoKg = field.CargoKg, Armored = field.Armored,
        };
        map[pos] = field.Armored ? Terrain.ArmoredVehicle : Terrain.Vehicle;
        state.Vehicles.Add(vehicle);
        return vehicle;
    }

    /// <summary>A working vehicle of the unit's side next to it (its own player's first), where it may extract or stash.</summary>
    public static ParkedVehicle? Beside(ScavengeState state, Unit unit) =>
        state.Vehicles
            .Where(v => !v.Wrecked && v.Pos.Chebyshev(unit.Pos) <= 1 && (v.PlayerId == unit.PlayerId || state.Allied(v.PlayerId, unit.PlayerId ?? -1)))
            .OrderBy(v => v.PlayerId == unit.PlayerId ? 0 : 1).FirstOrDefault();

    /// <summary>
    /// Unloads a carried item into the party's own vehicle next to the unit (<see cref="TurnRules.PickUpApCost"/> AP), within
    /// the vehicle's cargo limit.
    /// </summary>
    internal static ScavengeResult Stash(ScavengeSim sim, Unit unit, StashItem c)
    {
        var state = sim.State;
        if (Of(state, unit.PlayerId ?? -1) is not { Wrecked: false } vehicle || vehicle.Pos.Chebyshev(unit.Pos) > 1)
            return ScavengeResult.Fail(ScavengeError.TooFar, $"{unit.Name} is not next to the party's vehicle.");
        if (!unit.Pack.Contains(c.ItemId)) return ScavengeResult.Fail(ScavengeError.ItemNotCarried, $"{unit.Name} does not carry '{c.ItemId}'.");
        var cost = sim.Data.Rules.Turn.PickUpApCost;
        if (cost > unit.Ap) return ScavengeResult.Fail(ScavengeError.NotEnoughAp, $"{unit.Name} needs {cost} AP (has {unit.Ap}).");
        var item = sim.Item(c.ItemId);
        var load = Load(sim.Data, vehicle);
        if (load + item.Weight > vehicle.CargoKg + 1e-9)
            return ScavengeResult.Fail(ScavengeError.CargoFull, $"The {vehicle.Name} is full ({load:0.#}/{vehicle.CargoKg:0.#} kg).");

        unit.Ap -= cost;
        unit.Pack.Remove(c.ItemId);
        vehicle.Stash.Add(c.ItemId);
        sim.Log(ScavengeEventType.Stashed, $"{unit.Name} loads {item.Name} into the {vehicle.Name}.", unit);
        return ScavengeResult.Success($"{unit.Name} stashed {item.Name}.");
    }

    /// <summary>
    /// Round start: every active hostile enemy next to a working vehicle hits it for <see cref="VehicleFieldRules.DamagePerEnemy"/>
    /// less its armor (at least <see cref="VehicleFieldRules.MinDamage"/>). At 0 HP it is wrecked and its stash lost.
    /// </summary>
    public static void RoundStarted(ScavengeSim sim)
    {
        var rules = sim.Data.Rules.Vehicle;
        foreach (var vehicle in sim.State.Vehicles.Where(v => !v.Wrecked))
        {
            var owner = sim.State.FindParty(vehicle.PlayerId);
            var attackers = sim.State.Units.Count(u => u.IsActive && u.Kind == UnitKind.Enemy && u.Pos.Chebyshev(vehicle.Pos) <= 1
                                                       && (owner is null || u.Side != owner.Side));
            if (attackers == 0) continue;
            var damage = attackers * Math.Max(rules.MinDamage, rules.DamagePerEnemy - vehicle.Armor);
            vehicle.Hp = Math.Max(0, vehicle.Hp - damage);
            if (vehicle.Hp <= 0)
            {
                vehicle.Wrecked = true;
                vehicle.Stash.Clear();
                sim.State.Log(new(sim.State.Round, ScavengeEventType.VehicleWrecked, $"The {vehicle.Name} is wrecked; its cargo is lost.", Pos: vehicle.Pos));
            }
            else sim.State.Log(new(sim.State.Round, ScavengeEventType.VehicleHit,
                $"The {vehicle.Name} takes {damage:0} damage ({vehicle.Hp:0}/{vehicle.MaxHp:0}).", Pos: vehicle.Pos));
        }
    }

    internal static VehicleResult? Result(ScavengeState state, int playerId) =>
        Of(state, playerId) is { } v ? new VehicleResult(v.Hp, v.MaxHp, v.Wrecked, new(v.Stash)) : null;
}
