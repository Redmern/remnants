using Remnants.Core.Items;
using Remnants.Core.Random;
using Remnants.Core.Schematics;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>A survivor sent on a run, with the gear they wear and carry.</summary>
/// <summary>A survivor going into a zone with their gear. <paramref name="FirstRoundAp"/>: AP added (or taken) on the first round, from what they ate on the way (food buffs).</summary>
public sealed record PartyMember(Survivor Survivor, Dictionary<EquipSlot, string>? Equipment = null, List<string>? Pack = null, int FirstRoundAp = 0);

/// <summary>
/// One player's party entering a zone. Parties with the same <see cref="Team"/> start allied (one side, see
/// <see cref="Alliances"/>) and spawn next to each other; null = on its own. <see cref="Name"/> is the player's name
/// for log lines (null = "Player N").
/// </summary>
/// <see cref="Vehicle"/>: the vehicle the party drove in (parked on the spawn edge; its seats cap the party).
public sealed record PartySetup(int PlayerId, List<PartyMember> Members, int? Team = null, string? Name = null)
{
    public FieldVehicle? Vehicle { get; init; }
}

/// <summary>
/// A story mission's setup for <see cref="NewScavenge.Create"/>: the field script, and either a handmade
/// <see cref="Map"/> (fixtures only, nothing random) or a fixed <see cref="Room"/> stamped into the generated zone map.
/// <see cref="Towers"/>: how many of the map's watchtower posts are built (the colony's Watchtowers).
/// <see cref="ExtraEnemies"/>: more fixed enemies (a retry's harder variant).
/// </summary>
public sealed record MissionSetup(FieldMission Mission, HandmadeMapDef? Map = null, HandmadeMapDef? Room = null, int Towers = 0,
    List<MapEnemyDef>? ExtraEnemies = null);

/// <summary>
/// How a zone is found: its depletion (0..1 thins out the loot caches), the time of day and weather the run is played in
/// (midday, clear, if null), <see cref="KnownSchematics"/> (what the colony already knows: picked at half weight, so
/// duplicates do not dominate) and <see cref="HuskMultiplier"/>: more Husks drawn by an arriving vehicle's noise (1 = none).
/// </summary>
public sealed record ZoneConditions(double Depletion = 0, ScavengeEnvironment? Environment = null, IReadOnlyCollection<string>? KnownSchematics = null,
    double HuskMultiplier = 1);

/// <summary>
/// Builds a scavenge instance: generates the zone map from its template and a seed, places every party
/// along the spawn edge, then enemies, loot caches, NPC survivors and hackable devices. Each survivor's perks become its
/// unit's hooks (<see cref="ScavengeData.HooksFor"/>). Survivors are copied in; the colony's
/// own records are never touched (the <see cref="ExtractionResult"/> carries changes back).
/// </summary>
public static class NewScavenge
{
    public static ScavengeSim Create(ScavengeData data, string zoneId, ulong seed, IReadOnlyList<PartySetup> parties, ZoneConditions? conditions = null,
        MissionSetup? mission = null)
    {
        if (!data.Zones.TryGetValue(zoneId, out var zone)) throw new ArgumentException($"Unknown zone '{zoneId}'.", nameof(zoneId));
        ValidateParties(data, parties);
        if (mission?.Map is { } handmade) return CreateHandmade(data, zone, seed, parties, mission, handmade, conditions);

        var rng = new DeterministicRng(seed);
        var generated = MapGenerator.Generate(data, zone, rng);
        (SortedDictionary<string, List<Pos>> Markers, HashSet<Pos> Tiles)? fixedRoom = null;
        if (mission?.Room is { } roomDef)
        {
            fixedRoom = HandmadeMaps.Stamp(data, generated.Map, zone, generated.SpawnAnchor, roomDef);
            generated = generated with { Reachable = MapGenerator.Flood(data, generated.Map, generated.SpawnAnchor) };
        }
        var env = conditions?.Environment;
        var state = new ScavengeState
        {
            ZoneId = zone.Id, Map = generated.Map,
            Environment = env is null ? new ScavengeEnvironment() : env.Copy(),
            KnownSchematics = new(conditions?.KnownSchematics ?? [], StringComparer.Ordinal),
        };
        var map = state.Map;
        var open = generated.Reachable.Where(p => !map.IsExit(p)).OrderBy(map.Index).ToList();
        bool Free(Pos p) => state.UnitAt(p) is null;

        var spawnEdge = MapGenerator.EdgeTiles(map, zone.SpawnEdge);
        // One spawn point per group: a team spawns together, everyone else on their own.
        string Group(int i) => parties[i].Team is { } t ? $"t{t}" : $"p{i}";
        var groups = Enumerable.Range(0, parties.Count).Select(Group).Distinct().ToList();
        var anchors = new List<Pos>();
        var teamSides = new Dictionary<int, string>();
        for (var i = 0; i < parties.Count; i++)
        {
            var party = parties[i];
            var g = groups.IndexOf(Group(i));
            var anchor = spawnEdge[(int)((g + 1) * (long)spawnEdge.Count / (groups.Count + 1))];
            anchors.Add(anchor);
            var side = Unit.PlayerSide(party.PlayerId);
            if (party.Team is { } team && parties.Count(p => p.Team == team) > 1)
            {
                if (!teamSides.TryGetValue(team, out var shared)) teamSides[team] = shared = Unit.TeamSide(state.NextTeamId++);
                side = shared;
            }
            state.Parties.Add(new Party { PlayerId = party.PlayerId, Name = party.Name ?? "", Side = side });
            if (party.Vehicle is { } vehicle && ParkedVehicles.Park(state, party.PlayerId, vehicle, spawnEdge, anchor) is null)
                throw new ArgumentException($"Zone '{zone.Id}' has no room to park player {party.PlayerId}'s vehicle.");
            var spots = open
                .Where(p => MapGenerator.Depth(map, zone.SpawnEdge, p) <= 2 && Free(p) && data.Terrain(map[p]).Passable)
                .OrderBy(p => p.DistanceSquared(anchor)).ThenBy(map.Index)
                .ToList();
            if (spots.Count < party.Members.Count) throw new ArgumentException($"Zone '{zone.Id}' has no room for player {party.PlayerId}'s party.");
            for (var m = 0; m < party.Members.Count; m++)
            {
                var unit = SurvivorUnit(data, state, party.PlayerId, party.Members[m], spots[m]);
                unit.Side = side;
                state.Units.Add(unit);
            }
        }

        bool FarFromSpawn(Pos p, int distance) => anchors.All(a => a.Chebyshev(p) >= distance);

        var enemySpots = open.Where(p => map[p] == Terrain.Floor && FarFromSpawn(p, data.Rules.Ai.MinSpawnDistance)).ToList();
        var mix = zone.EnemyMix.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        // More players, more enemies (and higher caps), so a shared zone is not just easier.
        var scale = EnemyScale(data, parties.Count);
        var enemyCount = (int)Math.Round(rng.NextInclusive(zone.Enemies[0], zone.Enemies[1]) * scale);
        var spawned = new Dictionary<string, int>();
        for (var i = 0; i < enemyCount; i++)
        {
            // A capped entry (max) drops out of the mix once it is full.
            var allowed = mix.Where(e => e.Max is not { } max || spawned.GetValueOrDefault(e.Id) < (int)Math.Ceiling(max * scale - 1e-9)).ToList();
            if (allowed.Count == 0) break;
            var spot = TakeRandom(rng, enemySpots, Free);
            if (spot is null) break;
            var def = data.Enemies[PickWeighted(rng, allowed, e => e.Weight).Id];
            spawned[def.Id] = spawned.GetValueOrDefault(def.Id) + 1;
            state.Units.Add(EnemyUnit(data, state, def, spot.Value));
        }

        // The dark brings out more Husks: extra ones from the Husk part of the mix (none by day, so day runs are as before).
        var huskMix = mix.Where(e => data.Enemies[e.Id].Ai == AiKind.Husk).ToList();
        var huskMult = FieldEnvironment.Effects(data, state).HuskCountMultiplier * Math.Max(1, conditions?.HuskMultiplier ?? 1);
        if (huskMult > 1 && huskMix.Count > 0)
        {
            var husks = spawned.Where(kv => data.Enemies[kv.Key].Ai == AiKind.Husk).Sum(kv => kv.Value);
            var extra = (int)Math.Round(husks * (huskMult - 1));
            for (var i = 0; i < extra; i++)
            {
                var room = huskMix.Where(e => e.Max is not { } max || spawned.GetValueOrDefault(e.Id) < (int)Math.Ceiling(max * scale - 1e-9)).ToList();
                if (room.Count == 0) break;
                var spot = TakeRandom(rng, enemySpots, Free);
                if (spot is null) break;
                var def = data.Enemies[PickWeighted(rng, room, e => e.Weight).Id];
                spawned[def.Id] = spawned.GetValueOrDefault(def.Id) + 1;
                state.Units.Add(EnemyUnit(data, state, def, spot.Value));
            }
        }

        var lootTable = data.Items.Values.Where(i => i.LootWeight > 0 && i.Tier <= zone.LootTier)
            .OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        var cacheSpots = open.Where(p => MapGenerator.Depth(map, zone.SpawnEdge, p) >= 3).ToList();
        var cacheCount = (int)Math.Round(rng.NextInclusive(zone.Caches[0], zone.Caches[1]) * (1 - Math.Clamp(conditions?.Depletion ?? 0, 0, 1)));
        for (var i = 0; i < cacheCount; i++)
        {
            var spot = TakeRandom(rng, cacheSpots, p => Free(p) && state.Caches.All(c => c.Pos != p));
            if (spot is null) break;
            var cache = new LootCache { Id = state.NextCacheId++, Pos = spot.Value };
            var items = rng.NextInclusive(zone.ItemsPerCache[0], zone.ItemsPerCache[1]);
            for (var k = 0; k < items; k++)
                cache.Items.Add(SchematicDrops.Roll(data, zone, DropSite.Cache, rng, state.KnownSchematics) ?? PickWeighted(rng, lootTable, it => it.LootWeight).Id);
            state.Caches.Add(cache);
        }

        var npcSpots = open.Where(p => map[p] == Terrain.Floor && FarFromSpawn(p, 5)).ToList();
        var npcCount = rng.NextInclusive(zone.Npcs[0], zone.Npcs[1]);
        var usedNames = new HashSet<string>(state.Units.Select(u => u.Name));
        for (var i = 0; i < npcCount; i++)
        {
            var spot = TakeRandom(rng, npcSpots, p => Free(p) && state.Caches.All(c => c.Pos != p));
            if (spot is null) break;
            state.Units.Add(NpcUnit(data, state, rng, spot.Value, usedNames));
        }

        Hacking.PlaceZoneDevices(data, state, zone, seed, anchors, generated.Reachable);
        if (mission is not null)
        {
            if (fixedRoom is { } r)
            {
                // The fixed room holds only what the mission puts there.
                foreach (var d in state.Devices.Where(d => r.Tiles.Contains(d.Pos)).ToList())
                {
                    state.Devices.Remove(d);
                    if (d.UnitId is { } uid) state.Units.RemoveAll(u => u.Id == uid);
                }
                state.Units.RemoveAll(u => u.Kind != UnitKind.Survivor && r.Tiles.Contains(u.Pos));
                state.Caches.RemoveAll(c => r.Tiles.Contains(c.Pos));
            }
            if (mission.Mission.ExcludeEnemyTags.Count > 0)
                state.Units.RemoveAll(u => u.Kind == UnitKind.Enemy && u.EnemyDefId is { } e
                                           && data.Enemies[e].Tags.Any(mission.Mission.ExcludeEnemyTags.Contains));
            var markers = fixedRoom?.Markers ?? new(StringComparer.Ordinal);
            markers[HandmadeMaps.SpawnMarker] = anchors.ToList();
            AddMission(data, state, mission, mission.Room, markers);
        }

        state.RngState = rng.State;
        var sim = new ScavengeSim(data, state);
        sim.Start();
        return sim;
    }

    /// <summary>A handmade map: the parties on its spawn tiles, then only the map's own fixtures.</summary>
    private static ScavengeSim CreateHandmade(ScavengeData data, ZoneDef zone, ulong seed, IReadOnlyList<PartySetup> parties, MissionSetup mission, HandmadeMapDef def,
        ZoneConditions? conditions)
    {
        var built = HandmadeMaps.Build(def);
        var env = conditions?.Environment;
        var state = new ScavengeState
        {
            ZoneId = zone.Id, Map = built.Map,
            Environment = env is null ? new ScavengeEnvironment() : ScavengeEnvironment.At(env.Hour, env.Phase, env.Weather),
        };
        var map = state.Map;
        var spawn = built.Spawns.Count > 0 ? built.Spawns[0] : map.AllPositions.First(p => data.Terrain(map[p]).Passable);
        for (var i = 0; i < parties.Count; i++)
        {
            var party = parties[i];
            // A second party (co-op) starts at the last spawn tile.
            var anchor = i == 0 ? spawn : built.Spawns[^1];
            var side = Unit.PlayerSide(party.PlayerId);
            state.Parties.Add(new Party { PlayerId = party.PlayerId, Name = party.Name ?? "", Side = side });
            var spots = map.AllPositions
                .Where(p => data.Terrain(map[p]).Passable && !map.IsExit(p) && state.UnitAt(p) is null)
                .OrderBy(p => built.Spawns.Contains(p) ? 0 : 1).ThenBy(p => p.DistanceSquared(anchor)).ThenBy(map.Index)
                .ToList();
            if (spots.Count < party.Members.Count) throw new ArgumentException($"Map '{def.Id}' has no room for player {party.PlayerId}'s party.");
            for (var m = 0; m < party.Members.Count; m++)
            {
                var unit = SurvivorUnit(data, state, party.PlayerId, party.Members[m], spots[m]);
                unit.Side = side;
                state.Units.Add(unit);
            }
        }
        AddMission(data, state, mission, def, built.Markers);
        state.RngState = new DeterministicRng(seed).State;
        var sim = new ScavengeSim(data, state);
        sim.Start();
        return sim;
    }

    /// <summary>The mission's run state, and the fixtures of its map or room at their markers.</summary>
    private static void AddMission(ScavengeData data, ScavengeState state, MissionSetup setup, HandmadeMapDef? def, SortedDictionary<string, List<Pos>> markers)
    {
        var run = new MissionRun { Def = setup.Mission, MapId = def?.Id, MapHash = def is null ? null : HandmadeMaps.Hash(def), Markers = markers };
        state.Mission = run;
        MissionRules.Init(run);
        List<Pos> At(string marker) => markers.TryGetValue(marker, out var tiles) ? tiles : [];
        Pos? FreeAt(string marker) => At(marker).Where(p => state.UnitAt(p) is null && state.DeviceAt(p) is null).Cast<Pos?>().FirstOrDefault();
        if (def is null) return;

        if (def.Towers is { } towers) run.Raised = At(towers).Take(Math.Max(0, setup.Towers)).ToList();
        foreach (var c in def.Caches)
            if (At(c.Marker).FirstOrDefault() is var p && At(c.Marker).Count > 0)
            {
                var cache = state.Caches.Find(x => x.Pos == p);
                if (cache is null) state.Caches.Add(cache = new LootCache { Id = state.NextCacheId++, Pos = p });
                cache.Items.AddRange(c.Items);
            }
        foreach (var d in def.Devices)
            if (FreeAt(d.Marker) is { } p)
                Hacking.Place(data, state, d.Kind, p, d.Items).Tag = d.Tag;
        foreach (var e in def.Enemies.Concat(setup.ExtraEnemies ?? []))
        {
            var tiles = At(e.Marker).Where(p => state.UnitAt(p) is null).ToList();
            var patrol = (e.Patrol ?? []).Select(m => At(m).FirstOrDefault()).ToList();
            foreach (var p in tiles.Take(e.Count > 0 ? e.Count : tiles.Count))
            {
                var unit = EnemyUnit(data, state, data.Enemies[e.Enemy], p);
                unit.Tag = e.Tag;
                if (e.Name is { } name) unit.Name = name;
                if (e.Side is { } side) unit.Side = side == "neutral" ? Unit.NeutralSide : side;
                unit.Patrol = patrol.ToList();
                state.Units.Add(unit);
            }
        }
        foreach (var n in def.Npcs)
            if (FreeAt(n.Marker) is { } p)
                state.Units.Add(HandmadeMaps.NpcUnit(state, n, p));
        var partySide = state.Parties.Count > 0 ? state.Parties[0].Side : Unit.NeutralSide;
        foreach (var s in def.Structures)
            if (FreeAt(s.Marker) is { } p)
                state.Units.Add(new Unit
                {
                    Id = state.NextUnitId++, Name = s.Name, Kind = UnitKind.Npc, Side = partySide, Pos = p, Hp = s.Hp, MaxHp = s.Hp,
                    CanBeDowned = false, Tag = s.Tag, NoRecruit = true,
                    Attributes = Enum.GetValues<AttributeKind>().ToDictionary(a => a, _ => 10.0),
                });
    }

    /// <summary>How many times the zone's enemies a mission with <paramref name="players"/> parties gets (1 for one).</summary>
    public static double EnemyScale(ScavengeData data, int players) =>
        1 + data.Rules.Coop.EnemiesPerExtraPlayer * Math.Max(0, players - 1);

    /// <summary>
    /// The profile a colony survivor would have in the field with this gear (movement mode, AP, actions...),
    /// for the expedition planner. Uses the same unit construction as a real run.
    /// </summary>
    public static UnitProfile PreviewProfile(ScavengeData data, PartyMember member) =>
        ProfileDeriver.Derive(data, SurvivorUnit(data, new ScavengeState(), 1, member, default));

    private static void ValidateParties(ScavengeData data, IReadOnlyList<PartySetup> parties)
    {
        if (parties.Count == 0) throw new ArgumentException("A scavenge run needs at least one party.");
        if (parties.Select(p => p.PlayerId).Distinct().Count() != parties.Count) throw new ArgumentException("Player ids must be unique.");
        foreach (var party in parties)
        {
            if (party.Members.Count == 0) throw new ArgumentException($"Player {party.PlayerId}'s party is empty.");
            if (party.Vehicle is { Seats: { } seats } v && party.Members.Count > seats)
                throw new ArgumentException($"Player {party.PlayerId}'s {v.Name} seats {seats}, not {party.Members.Count}.");
            if (party.Vehicle is { } pv && (pv.MaxHp <= 0 || pv.CargoKg < 0 || pv.Armor < 0))
                throw new ArgumentException($"Player {party.PlayerId}'s vehicle has impossible stats.");
            foreach (var m in party.Members)
            {
                if (m.Survivor.IsChild) throw new ArgumentException($"{m.Survivor.Name} is a child; children never go on a run.");
                foreach (var (slot, id) in m.Equipment ?? new())
                    if (!data.Items.TryGetValue(id, out var item) || item.Slot != slot)
                        throw new ArgumentException($"{m.Survivor.Name}: '{id}' is not an item for the {slot} slot.");
                foreach (var id in m.Pack ?? [])
                    if (!data.Items.ContainsKey(id)) throw new ArgumentException($"{m.Survivor.Name}: unknown item '{id}'.");
            }
        }
    }

    private static Unit SurvivorUnit(ScavengeData data, ScavengeState state, int playerId, PartyMember member, Pos pos)
    {
        var origin = Clone(member.Survivor);
        return new Unit
        {
            Id = state.NextUnitId++,
            Name = origin.Name,
            Kind = UnitKind.Survivor,
            Side = Unit.PlayerSide(playerId),
            PlayerId = playerId,
            Pos = pos,
            Hp = origin.Health,
            MaxHp = Survivor.MaxHealth,
            Attributes = new(origin.Attributes),
            Skills = new(origin.Skills),
            Body = Clone(origin.Body),
            Equipment = new(member.Equipment ?? new()),
            Pack = new(member.Pack ?? []),
            Origin = origin,
            Hooks = data.HooksFor(origin.Perks),
            CyberHooks = data.CyberHooksFor(origin.Body),
            FirstRoundAp = member.FirstRoundAp,
        };
    }

    public static Unit EnemyUnit(ScavengeData data, ScavengeState state, EnemyDef def, Pos pos)
    {
        var number = state.Units.Count(u => u.EnemyDefId == def.Id) + 1;
        return new Unit
        {
            Id = state.NextUnitId++,
            Name = $"{def.Name} {number}",
            Kind = UnitKind.Enemy,
            Side = Unit.EnemySide,
            EnemyDefId = def.Id,
            Pos = pos,
            Hp = def.Hp,
            MaxHp = def.Hp,
            CanBeDowned = def.CanBeDowned,
            Attributes = Enum.GetValues<AttributeKind>().ToDictionary(a => a, a => def.Attributes.GetValueOrDefault(a, 50)),
            Skills = new(def.Skills),
            Equipment = def.Equipment.ToDictionary(id => data.Items[id].Slot!.Value, id => id),
        };
    }

    private static Unit NpcUnit(ScavengeData data, ScavengeState state, DeterministicRng rng, Pos pos, HashSet<string> usedNames)
    {
        var npc = data.Rules.Npc;
        var free = npc.Names.Where(n => !usedNames.Contains(n)).ToList();
        var name = free.Count > 0 ? rng.Pick(free) : $"{rng.Pick(npc.Names)} {usedNames.Count + 1}";
        usedNames.Add(name);
        var survivor = new Survivor { Name = name, Sex = rng.Next(0, 2) == 0 ? Sex.Male : Sex.Female };
        foreach (var a in Enum.GetValues<AttributeKind>()) survivor.Set(a, rng.NextInclusive(npc.AttributeRange[0], npc.AttributeRange[1]));

        return new Unit
        {
            Id = state.NextUnitId++,
            Name = name,
            Kind = UnitKind.Npc,
            Side = Unit.NeutralSide,
            Pos = pos,
            Hp = survivor.Health,
            MaxHp = Survivor.MaxHealth,
            Attributes = new(survivor.Attributes),
            Skills = new(survivor.Skills),
            Origin = survivor,
        };
    }

    /// <summary>Removes and returns a random candidate that passes the filter, or null when none is left.</summary>
    private static Pos? TakeRandom(DeterministicRng rng, List<Pos> candidates, Func<Pos, bool> ok)
    {
        candidates.RemoveAll(p => !ok(p));
        if (candidates.Count == 0) return null;
        var index = rng.Next(0, candidates.Count);
        var pick = candidates[index];
        candidates.RemoveAt(index);
        return pick;
    }

    internal static T PickWeighted<T>(DeterministicRng rng, IReadOnlyList<T> items, Func<T, double> weight)
    {
        var roll = rng.NextDouble() * items.Sum(weight);
        foreach (var item in items)
        {
            if (roll < weight(item)) return item;
            roll -= weight(item);
        }
        return items[^1];
    }

    internal static T Clone<T>(T value) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value, Json.Options), Json.Options)!;
}
