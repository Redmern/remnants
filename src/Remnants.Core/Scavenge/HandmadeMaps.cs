using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Remnants.Core.Data;
using Remnants.Core.Serialization;
using Remnants.Core.Survivors;

namespace Remnants.Core.Scavenge;

/// <summary>A loot cache at a marker (an always-there quest item, a toolbox by the gate).</summary>
public sealed record MapCacheDef(string Marker, List<string> Items);

/// <summary>Salvage nodes of a kind (salvage.json) on every tile of a marker.</summary>
public sealed record MapSalvageDef(string Kind, string Marker);

/// <summary>A hackable device at a marker, tagged for objectives; <see cref="Items"/> replace its rolled contents.</summary>
public sealed record MapDeviceDef(DeviceKind Kind, string Marker, string? Tag = null, List<string>? Items = null);

/// <summary>
/// A fixed enemy group: one <see cref="Enemy"/> per <see cref="Marker"/> tile (up to <see cref="Count"/>; 0 = all of
/// them), tagged; <see cref="Patrol"/> markers make an idle unit walk them in a loop.
/// </summary>
public sealed record MapEnemyDef(string Enemy, string Marker, string? Tag = null, int Count = 0, List<string>? Patrol = null, string? Name = null, string? Side = null);

/// <summary>
/// A story NPC (an escort target, a bystander): a survivor-like unit on the neutral side. <see cref="Character"/> makes
/// it a named story character (it never joins the colony as a recruit); <see cref="Hp"/> starts it wounded.
/// </summary>
public sealed record MapNpcDef(string Name, string Marker, string? Tag = null, string? Character = null, double Hp = 100, bool NoRecruit = false, string Sex = "Female");

/// <summary>Something of the party's to defend (the Radio Mast): a unit on the party's side that takes no turns.</summary>
public sealed record MapStructureDef(string Name, string Marker, double Hp, string? Tag = null);

/// <summary>
/// A handmade map or a fixed room (<see cref="Room"/>: stamped into a generated map; no spawn or exit of its own).
/// <see cref="Rows"/> is an ASCII grid, row 0 north: <c>#</c> wall, <c>.</c> floor, <c>~</c> water, <c>%</c> rubble,
/// <c>=</c> cover, <c>P</c> pylon, <c>S</c> party spawn, <c>E</c> exit; any other character must be named in
/// <see cref="Markers"/> (a named floor tile; several tiles may share a name). <see cref="Towers"/>: the marker whose
/// tiles become raised watchtower posts, one per Watchtower the colony has (the rest stay floor).
/// </summary>
public sealed record HandmadeMapDef
{
    public string Id { get; init; } = "";
    public string Zone { get; init; } = "";
    public bool Room { get; init; }
    public List<string> Rows { get; init; } = new();
    public Dictionary<string, string> Markers { get; init; } = new();
    public List<MapCacheDef> Caches { get; init; } = new();
    public List<MapDeviceDef> Devices { get; init; } = new();
    public List<MapEnemyDef> Enemies { get; init; } = new();
    public List<MapNpcDef> Npcs { get; init; } = new();
    public List<MapStructureDef> Structures { get; init; } = new();

    /// <summary>Salvage nodes at markers (null = none; left out of the map's hash then, so older saves still match).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<MapSalvageDef>? Salvage { get; init; }
    public string? Towers { get; init; }

    public int Width => Rows.Count == 0 ? 0 : Rows[0].Length;
    public int Height => Rows.Count;
}

/// <summary>A parsed grid: terrain, exits, spawn tiles and named markers.</summary>
public sealed record BuiltMap(TileMap Map, List<Pos> Spawns, SortedDictionary<string, List<Pos>> Markers);

/// <summary>
/// Handmade maps (<c>data/story/maps/*.json</c>): parsing, validation, and stamping a fixed room into a generated map.
/// Fixtures (caches, devices, enemies, NPCs, structures) are placed by <see cref="NewScavenge"/>.
/// </summary>
public static class HandmadeMaps
{
    public const string SpawnMarker = "spawn";

    private static readonly Dictionary<char, Terrain> TerrainChars = new()
    {
        ['#'] = Terrain.Wall, ['.'] = Terrain.Floor, ['~'] = Terrain.Water, ['%'] = Terrain.Rubble, ['='] = Terrain.Cover, ['P'] = Terrain.Pylon,
    };

    public static HandmadeMapDef Parse(string json, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<HandmadeMapDef>(json, Json.Options) ?? throw new GameDataException($"{name} is empty.");
        }
        catch (JsonException e)
        {
            throw new GameDataException($"{name} is invalid: {e.Message}");
        }
    }

    /// <summary>A short hash of the map's data, kept in saves so a run can tell its map changed under it.</summary>
    public static string Hash(HandmadeMapDef def) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(def, Json.Options))))[..16];

    public static BuiltMap Build(HandmadeMapDef def)
    {
        var map = new TileMap(def.Width, def.Height);
        var spawns = new List<Pos>();
        var markers = new SortedDictionary<string, List<Pos>>(StringComparer.Ordinal);
        for (var y = 0; y < def.Height; y++)
            for (var x = 0; x < def.Width; x++)
            {
                var c = def.Rows[y][x];
                var p = new Pos(x, y);
                if (TerrainChars.TryGetValue(c, out var terrain)) { map[p] = terrain; continue; }
                map[p] = Terrain.Floor;
                if (c == 'S') spawns.Add(p);
                else if (c == 'E') map.Exits.Add(p);
                else if (def.Markers.TryGetValue(c.ToString(), out var name))
                {
                    if (!markers.TryGetValue(name, out var tiles)) markers[name] = tiles = new();
                    tiles.Add(p);
                }
            }
        if (spawns.Count > 0) markers[SpawnMarker] = spawns.ToList();
        return new BuiltMap(map, spawns, markers);
    }

    /// <summary>
    /// Every problem with a map: ragged rows, unknown characters, no spawn or exit (a full map), fixtures at unknown
    /// markers or naming unknown items/enemies, and tiles a party cannot walk to from its spawn. Empty = valid.
    /// </summary>
    public static List<string> Validate(ScavengeData data, HandmadeMapDef def)
    {
        var errors = new List<string>();
        if (def.Rows.Count == 0) return [$"{def.Id}: no rows."];
        if (def.Rows.Any(r => r.Length != def.Width)) errors.Add($"{def.Id}: rows differ in width.");
        foreach (var c in def.Rows.SelectMany(r => r).Distinct())
            if (!TerrainChars.ContainsKey(c) && c != 'S' && c != 'E' && !def.Markers.ContainsKey(c.ToString()))
                errors.Add($"{def.Id}: unknown map character '{c}'.");
        if (errors.Count > 0) return errors;
        if (!data.Zones.ContainsKey(def.Zone)) errors.Add($"{def.Id}: unknown zone '{def.Zone}'.");

        var built = Build(def);
        if (!def.Room)
        {
            if (built.Spawns.Count == 0) errors.Add($"{def.Id}: no spawn tile (S).");
            if (built.Map.Exits.Count == 0) errors.Add($"{def.Id}: no exit tile (E).");
        }
        else if (built.Spawns.Count > 0 || built.Map.Exits.Count > 0) errors.Add($"{def.Id}: a room has no spawn or exit of its own.");

        bool Known(string? marker) => marker is not null && built.Markers.ContainsKey(marker);
        foreach (var c in def.Caches)
        {
            if (!Known(c.Marker)) errors.Add($"{def.Id}: cache at unknown marker '{c.Marker}'.");
            errors.AddRange(c.Items.Where(i => !data.Items.ContainsKey(i)).Select(i => $"{def.Id}: cache holds unknown item '{i}'."));
        }
        foreach (var s in def.Salvage ?? [])
        {
            if (!Known(s.Marker)) errors.Add($"{def.Id}: salvage at unknown marker '{s.Marker}'.");
            if (data.Salvage.Node(s.Kind) is null) errors.Add($"{def.Id}: unknown salvage node '{s.Kind}'.");
        }
        foreach (var d in def.Devices)
        {
            if (!Known(d.Marker)) errors.Add($"{def.Id}: device at unknown marker '{d.Marker}'.");
            errors.AddRange((d.Items ?? []).Where(i => !data.Items.ContainsKey(i)).Select(i => $"{def.Id}: device holds unknown item '{i}'."));
        }
        foreach (var e in def.Enemies)
        {
            if (!Known(e.Marker)) errors.Add($"{def.Id}: enemy at unknown marker '{e.Marker}'.");
            if (!data.Enemies.ContainsKey(e.Enemy)) errors.Add($"{def.Id}: unknown enemy '{e.Enemy}'.");
            errors.AddRange((e.Patrol ?? []).Where(m => !Known(m)).Select(m => $"{def.Id}: patrol through unknown marker '{m}'."));
        }
        foreach (var n in def.Npcs.Where(n => !Known(n.Marker))) errors.Add($"{def.Id}: NPC {n.Name} at unknown marker '{n.Marker}'.");
        foreach (var s in def.Structures.Where(s => !Known(s.Marker))) errors.Add($"{def.Id}: structure {s.Name} at unknown marker '{s.Marker}'.");
        if (def.Towers is { } towers && !Known(towers)) errors.Add($"{def.Id}: towers at unknown marker '{towers}'.");
        if (errors.Count > 0 || def.Room) return errors;

        // Connected: every passable tile, marker and exit can be walked to from the first spawn. Devices block their
        // tiles, except cameras and turrets (in walls or standing aside) and doors (a door is hacked or opened).
        var map = built.Map;
        var blocked = def.Devices.Where(d => d.Kind is not (DeviceKind.Camera or DeviceKind.Turret or DeviceKind.Door))
            .SelectMany(d => built.Markers[d.Marker]).ToHashSet();
        var reach = Flood(data, map, built.Spawns[0], blocked);
        foreach (var p in map.AllPositions.Where(p => data.Terrain(map[p]).Passable && !blocked.Contains(p) && !reach.Contains(p)))
        {
            errors.Add($"{def.Id}: tile {p} cannot be reached from the spawn.");
            break;
        }
        return errors;
    }

    private static HashSet<Pos> Flood(ScavengeData data, TileMap map, Pos start, HashSet<Pos> blocked)
    {
        var seen = new HashSet<Pos> { start };
        var queue = new Queue<Pos>([start]);
        while (queue.TryDequeue(out var p))
            foreach (var d in Pos.Orthogonal)
            {
                var n = p + d;
                if (map.InBounds(n) && data.Terrain(map[n]).Passable && !blocked.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
        return seen;
    }

    /// <summary>
    /// Stamps a fixed room into a generated map, deep in the zone (away from the spawn edge, near the middle across),
    /// then carves a path from each of its border doorways back toward the spawn anchor so it is always reachable.
    /// Returns the room's markers in map coordinates and the room's tiles (kept free of random placement).
    /// </summary>
    public static (SortedDictionary<string, List<Pos>> Markers, HashSet<Pos> Tiles) Stamp(ScavengeData data, TileMap map, ZoneDef zone, Pos anchor, HandmadeMapDef room)
    {
        var built = Build(room);
        var w = room.Width;
        var h = room.Height;
        // The room's top-left corner: centred across the map, two thirds of the way from the spawn edge.
        var (ox, oy) = zone.SpawnEdge switch
        {
            MapEdge.South => ((map.Width - w) / 2, Math.Max(2, map.Height / 3 - h / 2)),
            MapEdge.North => ((map.Width - w) / 2, Math.Min(map.Height - h - 2, 2 * map.Height / 3 - h / 2)),
            MapEdge.West => (Math.Min(map.Width - w - 2, 2 * map.Width / 3 - w / 2), (map.Height - h) / 2),
            _ => (Math.Max(2, map.Width / 3 - w / 2), (map.Height - h) / 2),
        };
        var offset = new Pos(ox, oy);
        var tiles = new HashSet<Pos>();
        foreach (var p in built.Map.AllPositions)
        {
            var q = p + offset;
            map[q] = built.Map[p];
            tiles.Add(q);
        }
        // Clear a ring of floor round the room so its doorways open onto the map.
        for (var x = ox - 1; x <= ox + w; x++)
            for (var y = oy - 1; y <= oy + h; y++)
            {
                var q = new Pos(x, y);
                if (map.InBounds(q) && !tiles.Contains(q) && !data.Terrain(map[q]).Passable) map[q] = Terrain.Floor;
            }
        // The ring is open all round, so linking it to the spawn anchor and to every exit links the doorways too. Carving
        // from the ring tile nearest each goal only ever moves away from the room, so its walls stay whole.
        var ring = map.AllPositions.Where(q => !tiles.Contains(q) && q.X >= ox - 1 && q.X <= ox + w && q.Y >= oy - 1 && q.Y <= oy + h).ToList();
        foreach (var goal in map.Exits.Prepend(anchor).ToList())
        {
            var start = ring.OrderBy(q => Math.Abs(q.X - goal.X) + Math.Abs(q.Y - goal.Y)).ThenBy(map.Index).First();
            Carve(data, map, start, goal);
        }
        var markers = new SortedDictionary<string, List<Pos>>(StringComparer.Ordinal);
        foreach (var (name, list) in built.Markers) markers[name] = list.Select(p => p + offset).ToList();
        return (markers, tiles);
    }

    /// <summary>Greedy walk from start to goal that turns any impassable tile on the way into floor.</summary>
    private static void Carve(ScavengeData data, TileMap map, Pos from, Pos to)
    {
        var p = from;
        while (true)
        {
            if (!data.Terrain(map[p]).Passable) map[p] = Terrain.Floor;
            if (p == to) return;
            var dx = to.X - p.X;
            var dy = to.Y - p.Y;
            p = Math.Abs(dx) >= Math.Abs(dy) ? p + new Pos(Math.Sign(dx), 0) : p + new Pos(0, Math.Sign(dy));
        }
    }

    /// <summary>A survivor-like story NPC (see <see cref="MapNpcDef"/>), neutral until a trigger or a recruit says otherwise.</summary>
    public static Unit NpcUnit(ScavengeState state, MapNpcDef npc, Pos pos)
    {
        var survivor = new Survivor { Name = npc.Name, Sex = Enum.Parse<Sex>(npc.Sex) };
        foreach (var a in Enum.GetValues<AttributeKind>()) survivor.Set(a, 45);
        survivor.Health = Math.Clamp(npc.Hp, 1, Survivor.MaxHealth);
        return new Unit
        {
            Id = state.NextUnitId++,
            Name = npc.Name,
            Kind = UnitKind.Npc,
            Side = Unit.NeutralSide,
            Pos = pos,
            Hp = survivor.Health,
            MaxHp = Survivor.MaxHealth,
            Attributes = new(survivor.Attributes),
            Skills = new(survivor.Skills),
            Origin = survivor,
            Tag = npc.Tag,
            CharacterId = npc.Character,
            NoRecruit = npc.NoRecruit,
        };
    }
}
