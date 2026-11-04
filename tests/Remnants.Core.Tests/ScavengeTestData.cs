using Remnants.Core.Items;
using Remnants.Core.Scavenge;
using Remnants.Core.Survivors;

namespace Remnants.Core.Tests;

/// <summary>Scavenge fixtures: the real data/ definitions plus hand-built arenas with exactly placed units.</summary>
public static class ScavengeTestData
{
    private static readonly Lazy<ScavengeData> RealData =
        new(() => ScavengeDataLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));

    public static ScavengeData Real => RealData.Value;

    private static readonly Lazy<ScavengeData> WholeTurnData = new(() => Real.WithTurn(Real.Rules.Turn with { SubRounds = 1 }));

    /// <summary>
    /// The real data played in whole turns (one sub-round a round): the default for <see cref="Arena"/> and <see cref="With"/>,
    /// so tests of other rules count rounds as before. Sub-round tests set <see cref="TurnRules.SubRounds"/> themselves.
    /// </summary>
    public static ScavengeData WholeTurns => WholeTurnData.Value;

    public static Survivor Survivor(string name = "Test", double attributes = 50)
    {
        var s = new Survivor { Id = 1, Name = name };
        foreach (var a in Enum.GetValues<AttributeKind>()) s.Set(a, attributes);
        return s;
    }

    /// <summary>A party survivor unit, unplaced (id 0) until added to an arena.</summary>
    public static Unit SurvivorUnit(
        int playerId = 1, Pos pos = default, double attributes = 50, string? weapon = null, string? body = null,
        string? tool = null, params string[] pack)
    {
        var s = Survivor($"P{playerId}", attributes);
        var u = new Unit
        {
            Name = s.Name,
            Kind = UnitKind.Survivor,
            Side = Unit.PlayerSide(playerId),
            PlayerId = playerId,
            Pos = pos,
            Hp = 100,
            MaxHp = 100,
            Attributes = new(s.Attributes),
            Skills = new(s.Skills),
            Origin = s,
            Pack = pack.ToList(),
        };
        if (weapon is not null) u.Equipment[EquipSlot.Weapon] = weapon;
        if (body is not null) u.Equipment[EquipSlot.Body] = body;
        if (tool is not null) u.Equipment[EquipSlot.Tool] = tool;
        return u;
    }

    public static Unit EnemyUnit(string defId, Pos pos)
    {
        var def = Real.Enemies[defId];
        return new Unit
        {
            Name = def.Name,
            Kind = UnitKind.Enemy,
            Side = Unit.EnemySide,
            EnemyDefId = defId,
            Pos = pos,
            Hp = def.Hp,
            MaxHp = def.Hp,
            CanBeDowned = def.CanBeDowned,
            Attributes = Enum.GetValues<AttributeKind>().ToDictionary(a => a, a => def.Attributes.GetValueOrDefault(a, 50)),
            Skills = new(def.Skills),
            Equipment = def.Equipment.ToDictionary(id => Real.Items[id].Slot!.Value, id => id),
        };
    }

    public static Unit NpcUnit(Pos pos)
    {
        var s = Survivor("Stranger", 40);
        return new Unit
        {
            Name = s.Name, Kind = UnitKind.Npc, Side = Unit.NeutralSide, Pos = pos, Hp = 100, MaxHp = 100,
            Attributes = new(s.Attributes), Skills = new(s.Skills), Origin = s,
        };
    }

    /// <summary>
    /// An open floor arena. Units get ids in order; parties are created for every player id present.
    /// The turn order is exactly the units in the given order (not initiative), and the first one is up
    /// with full AP, so tests control who acts. Optional rows draw terrain: . floor, # wall, P pylon, r rubble, c cover, ~ water, E exit.
    /// </summary>
    /// <summary>The real data, in whole turns (<see cref="WholeTurns"/>), with scavenge rules changed (e.g. a fixed hack chance).</summary>
    public static ScavengeData With(Func<ScavengeRules, ScavengeRules> change) =>
        new(Real.Items.Values.Where(i => i.BaseId is null), Real.Enemies.Values, Real.Zones.Values,
            change(Real.Rules with { Turn = Real.Rules.Turn with { SubRounds = 1 } }), Real.PerkHooks, Real.Cybernetics?.Def, Real.Schematics, Real.Electronics, medicine: Real.Medicine?.Def,
            loot: Real.LootFiles, salvage: Real.Salvage, cards: Real.Cards.Def);

    /// <summary>The data with other loot tables (and optionally salvage rules) in place of the real ones.</summary>
    public static ScavengeData WithLoot(ScavengeData data, string lootJson, string? salvageJson = null) =>
        ScavengeDataLoader.WithLoot(data, [lootJson], salvageJson);

    /// <param name="setup">Runs on the state before the turn order is made (place devices, ...).</param>
    /// <param name="data">Data to run on (default <see cref="WholeTurns"/>).</param>
    public static ScavengeSim Arena(int width, int height, IEnumerable<Unit> units, string[]? rows = null, ulong seed = 1, string zoneId = "outskirts",
        Action<ScavengeData, ScavengeState>? setup = null, ScavengeData? data = null)
    {
        data ??= WholeTurns;
        var map = new TileMap(width, height);
        if (rows is not null)
            for (var y = 0; y < rows.Length; y++)
                for (var x = 0; x < rows[y].Length; x++)
                {
                    var p = new Pos(x, y);
                    map[p] = rows[y][x] switch
                    {
                        '#' => Terrain.Wall, 'P' => Terrain.Pylon, 'r' => Terrain.Rubble, 'c' => Terrain.Cover, '~' => Terrain.Water, _ => Terrain.Floor,
                    };
                    if (rows[y][x] == 'E') map.Exits.Add(p);
                }

        var state = new ScavengeState { ZoneId = zoneId, Map = map, Round = 1, RngState = new Random.DeterministicRng(seed).State };
        foreach (var u in units)
        {
            u.Id = state.NextUnitId++;
            state.Units.Add(u);
            if (u.PlayerId is { } pid && state.FindParty(pid) is null)
                state.Parties.Add(new Party { PlayerId = pid, Side = Unit.PlayerSide(pid) });
        }
        setup?.Invoke(data, state);
        state.TurnOrder = state.Units.Where(u => u.IsActive && u.Side != Unit.NeutralSide && u.Kind != UnitKind.Drone).Select(u => u.Id).ToList();
        var sim = new ScavengeSim(data, state);
        foreach (var u in state.Units) (u.Ap, u.Mp) = (sim.Profile(u).MaxAp, sim.Profile(u).MaxMp);
        return sim;
    }

    public static PartySetup Party(int playerId, int members = 2, string weapon = "pipe")
    {
        var list = Enumerable.Range(0, members).Select(i =>
        {
            var s = Survivor($"P{playerId}-{i}", 55);
            s.Id = i + 1;
            s.Set(Skill.Melee, 30);
            return new PartyMember(s,
                new Dictionary<EquipSlot, string> { [EquipSlot.Weapon] = weapon, [EquipSlot.Body] = "padded_vest" },
                ["med_kit"]);
        }).ToList();
        return new PartySetup(playerId, list);
    }

    /// <summary>
    /// The tile strike at <paramref name="target"/>'s tile with the action (a stab / slash aimed as the AI aims); an
    /// unknown or out-of-reach action is sent as is, for Core to refuse.
    /// </summary>
    public static AttackTile StrikeOn(ScavengeSim sim, Unit attacker, Unit target, string actionId) =>
        sim.Profile(attacker).Action(actionId) is { } action && TileStrikes.StrikeAt(sim, attacker, action, target) is { } strike
            ? strike
            : new AttackTile(attacker.PlayerId ?? 0, attacker.Id, target.Pos, actionId);

    /// <summary>
    /// Readies a tile strike at <paramref name="target"/>'s tile (<see cref="StrikeOn"/>), then ends the player units'
    /// sub-turns until it has come down. Returns the AttackTile's result.
    /// </summary>
    public static ScavengeResult StrikeNow(ScavengeSim sim, Unit attacker, Unit target, string actionId)
    {
        var result = sim.Execute(StrikeOn(sim, attacker, target, actionId));
        for (var i = 0; result.Ok && i < 50 && TileStrikes.Readied(sim.State, attacker) && sim.State.CurrentUnit is { PlayerControlled: true } next; i++)
            Assert.True(sim.Execute(new EndTurn(next.PlayerId!.Value, next.Id)).Ok);
        return result;
    }

    /// <summary>
    /// Stages a readied one-tile strike of the unit's (no checks, no AP), as a declaration earlier this sub-round leaves it.
    /// </summary>
    public static TileStrike Ready(ScavengeSim sim, Unit unit, Pos tile, string actionId, int? targetId = null)
    {
        var strike = new TileStrike { UnitId = unit.Id, ActionId = actionId, From = unit.Pos, Tile = tile, Direction = Combat.Face(unit.Pos, tile), Tiles = [tile], TargetId = targetId };
        sim.State.Strikes.Add(strike);
        return strike;
    }

    /// <summary>
    /// A blow on its own, for tests of what a hit does: the tile strike at <paramref name="target"/>'s tile is readied
    /// (Core's checks, AP paid) and comes down on the spot, the turn not moving on. Returns the readying's result.
    /// </summary>
    public static ScavengeResult Blow(ScavengeSim sim, Unit attacker, Unit target, string actionId)
    {
        var result = TileStrikes.Declare(sim, attacker, StrikeOn(sim, attacker, target, actionId));
        if (result.Ok) TileStrikes.ResolveAll(sim);
        return result;
    }

    /// <summary>
    /// A simple deterministic player bot: extract on an exit, attack anything in reach, else walk toward
    /// the nearest exit, else end the turn. Returns the commands it issued.
    /// </summary>
    public static List<IScavengeCommand> PlayBot(ScavengeSim sim, int maxCommands = 2000)
    {
        var issued = new List<IScavengeCommand>();
        while (!sim.State.Finished && issued.Count < maxCommands)
        {
            var u = sim.State.CurrentUnit!;
            var command = BotCommand(sim, u);
            issued.Add(command);
            var result = sim.Execute(command);
            if (!result.Ok) throw new InvalidOperationException($"Bot command {command} failed: {result.Message}");
        }
        return issued;
    }

    private static IScavengeCommand BotCommand(ScavengeSim sim, Unit u)
    {
        var pid = u.PlayerId!.Value;
        var map = sim.State.Map;
        if (map.IsExit(u.Pos)) return new Extract(pid, u.Id);

        var profile = sim.Profile(u);
        foreach (var target in sim.State.Units.Where(t => t.IsActive && u.HostileTo(t) && Detection.SideSees(sim.Data, sim.State, u.Side, t)).OrderBy(t => t.Id))
            foreach (var a in profile.Actions.Where(a => a.ApCost <= u.Ap))
                if (Combat.CheckReach(sim.Data, map, u.Pos, a, target.Pos) == ScavengeError.None && TileStrikes.StrikeAt(sim, u, a, target) is { } strike)
                    return strike;

        var exit = map.Exits.OrderBy(e => e.Chebyshev(u.Pos)).First();
        var field = Movement.DistanceField(map, profile.Movement, exit);
        var here = field.GetValueOrDefault(u.Pos, int.MaxValue);
        var step = sim.Reachable(u)
            .Where(kv => kv.Value <= u.Mp && field.ContainsKey(kv.Key) && field[kv.Key] < here)
            .OrderBy(kv => field[kv.Key]).ThenBy(kv => map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key).FirstOrDefault();
        return step is { } to ? new Move(pid, u.Id, to) : new EndTurn(pid, u.Id);
    }
}
