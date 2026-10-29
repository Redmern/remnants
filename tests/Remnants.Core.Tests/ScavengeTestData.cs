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
    public static ScavengeSim Arena(int width, int height, IEnumerable<Unit> units, string[]? rows = null, ulong seed = 1, string zoneId = "outskirts")
    {
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
        state.TurnOrder = state.Units.Where(u => u.IsActive && u.Side != Unit.NeutralSide).Select(u => u.Id).ToList();
        var sim = new ScavengeSim(Real, state);
        foreach (var u in state.Units) u.Ap = sim.Profile(u).MaxAp;
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
        foreach (var target in sim.State.Units.Where(t => t.IsActive && u.HostileTo(t)).OrderBy(t => t.Id))
            foreach (var a in profile.Actions.Where(a => a.ApCost <= u.Ap))
                if (Combat.CheckReach(sim.Data, map, u.Pos, a, target.Pos) == ScavengeError.None)
                    return new Attack(pid, u.Id, target.Id, a.Id);

        var exit = map.Exits.OrderBy(e => e.Chebyshev(u.Pos)).First();
        var field = Movement.DistanceField(map, profile.Movement, exit);
        var here = field.GetValueOrDefault(u.Pos, int.MaxValue);
        var step = sim.Reachable(u)
            .Where(kv => kv.Value <= u.Ap && field.ContainsKey(kv.Key) && field[kv.Key] < here)
            .OrderBy(kv => field[kv.Key]).ThenBy(kv => map.Index(kv.Key))
            .Select(kv => (Pos?)kv.Key).FirstOrDefault();
        return step is { } to ? new Move(pid, u.Id, to) : new EndTurn(pid, u.Id);
    }
}
