using System.Text.Json;
using Remnants.Core.Serialization;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Save/load a scavenge instance as JSON. Definitions are not saved; they reload from data/.
/// Version 2 adds the Network alarm (<see cref="ScavengeState.AlarmRounds"/>, reinforcement waves), suppression and
/// pylon tiles; version 1 saves load with those at their defaults. Version 3 adds the fog of war: stances, lookout,
/// contacts, reveals, flares and motion sensors; older saves load with none (everyone out of stance, nothing placed).
/// Version 4 adds co-op: alliance offers and breaks, team pings, the next team id, carried units and pings used, and who
/// placed a motion sensor; older saves load with every player on their own side and nobody carried.
/// Version 5 adds hacking and drones: devices (with hacks in progress, lockouts and contents), drone marks, units' perk
/// hooks, field XP, drones and their operators' batteries, overridden Linked; older saves load with no devices, no drones
/// and no perks in the field.
/// Version 6 adds the run's environment (hour, day phase, weather slot); older saves load at midday in clear weather.
/// Version 7 adds cybernetics: survivor units' cybernetic hooks (<see cref="Unit.CyberHooks"/>) and parts locked by
/// interference; an older run gets its survivors' hooks resolved from their bodies on load, with nothing locked.
/// Version 8 adds story missions: <see cref="ScavengeState.Mission"/> (the field script, objectives, triggers fired,
/// chatter, the pending prompt, field flags, markers, the handmade map id and hash) and units' and devices' story fields
/// (tags, character ids, patrols, raised tiles); older saves load as plain runs.
/// Version 9 adds the weather's intensity, its changes still to come, and the rounds of the last thunder and lightning;
/// older runs load with steady weather.
/// Version 10 adds electronics: holo-decoy units and their fade round, EMP stuns, charges spent per device and a party's
/// secured cargo; an older run loads with none of them.
/// Version 11 adds gear mods: modded gear ids in equipment, packs and caches, the Head slot, and per unit whether it moved
/// this turn (<see cref="Unit.Moved"/>) and where it faces (<see cref="Unit.Facing"/>); older runs load with nobody moved
/// and no facing (a weapon light shines all round until its carrier moves or shoots).
/// Version 12 adds parked vehicles (<see cref="ScavengeState.Vehicles"/>, their stash, HP and upgrades) and stripped wrecks
/// (<see cref="ScavengeState.SalvagedWrecks"/>); older runs have none.
/// <see cref="LootVersion"/> adds field loot and salvage: pile kinds, sources and stash owners, salvage nodes, the loot
/// seed and the difficulty; older runs load with plain caches, no nodes and a loot seed from their RNG state.
/// Version 14 adds Story Acts 2–4 field rules: the run's campaign flags, the survivors the Linked took and who they are
/// holding, the party's survivors at the start and a co-op guest's schematic copies (<see cref="MissionRun"/>), and
/// devices' failed hacks (<see cref="ZoneDevice.Failures"/>); an older run loads with none of them.
/// <see cref="IntentsVersion"/> adds telegraphed plans (<see cref="Unit.Plan"/>), dodged attacks
/// (<see cref="Unit.OutplayedId"/>) and hit tiers on events; an older run loads with no plans pending.
/// </summary>
public static class ScavengeSave
{
    /// <summary>Field loot and salvage (keyed: renumber to the next free version at merge).</summary>
    public const int LootVersion = 13;
    /// <summary>Telegraphed plans (keyed: renumber to the next free version at merge).</summary>
    public const int IntentsVersion = 15;
    public const int Version = IntentsVersion;
    private const int OldestVersion = 1;

    private sealed record Envelope(int Version, ScavengeState Scavenge);

    public static string Save(ScavengeState state) => JsonSerializer.Serialize(new Envelope(Version, state), Json.Options);

    public static ScavengeSim Load(ScavengeData data, string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, Json.Options)
            ?? throw new JsonException("Save file is empty.");
        if (envelope.Version < OldestVersion || envelope.Version > Version)
            throw new JsonException($"Unsupported scavenge save version {envelope.Version} (expected {OldestVersion}..{Version}).");
        var state = envelope.Scavenge ?? throw new JsonException("Save file has no scavenge state.");
        if (envelope.Version < 7) MigrateCybernetics(data, state);
        if (envelope.Version < LootVersion) MigrateLoot(state);
        Validate(data, state);
        return new ScavengeSim(data, state);
    }

    /// <summary>Before version 7 the field knew nothing of cybernetics: resolve survivors' hooks from their bodies.</summary>
    internal static void MigrateCybernetics(ScavengeData data, ScavengeState state)
    {
        foreach (var u in state.Units.Where(u => u.Kind == UnitKind.Survivor && u.CyberHooks.Count == 0))
            u.CyberHooks = data.CyberHooksFor(u.Body);
    }

    /// <summary>Before field loot the run had no loot seed: derive one from the RNG state, so kills still roll their loot.</summary>
    internal static void MigrateLoot(ScavengeState state)
    {
        if (state.LootSeed == 0) state.LootSeed = state.RngState ^ 0x100D_5EED_0000_0001UL;
    }

    /// <summary>A save must still fit the current data/ (an item or enemy may have been renamed by a balancing edit).</summary>
    internal static void Validate(ScavengeData data, ScavengeState state)
    {
        if (!data.Zones.ContainsKey(state.ZoneId)) throw new JsonException($"Save references unknown zone '{state.ZoneId}'.");
        if (state.Map.Tiles.Count != state.Map.Width * state.Map.Height)
            throw new JsonException($"Save map has {state.Map.Tiles.Count} tiles; expected {state.Map.Width}x{state.Map.Height}.");

        void RequireItem(string id)
        {
            if (!data.Items.ContainsKey(id)) throw new JsonException($"Save references unknown item '{id}'.");
        }

        foreach (var u in state.Units)
        {
            if (u.EnemyDefId is { } e && !data.Enemies.ContainsKey(e)) throw new JsonException($"Save references unknown enemy '{e}'.");
            if (!state.Map.InBounds(u.Pos)) throw new JsonException($"Unit {u.Id} is off the map at {u.Pos}.");
            foreach (var id in u.AllItems) RequireItem(id);
            foreach (var (slot, id) in u.Equipment)
                if (data.Items[id].Slot != slot) throw new JsonException($"Unit {u.Id} has '{id}' in the {slot} slot.");
            if (u.Plan is { } plan && plan.Path.Append(plan.AttackTile ?? u.Pos).Any(p => !state.Map.InBounds(p)))
                throw new JsonException($"Unit {u.Id}'s plan leaves the map.");
        }
        foreach (var id in state.Caches.SelectMany(c => c.Items)) RequireItem(id);
        foreach (var n in state.SalvageNodes)
        {
            if (data.Salvage.Node(n.Kind) is null) throw new JsonException($"Save references unknown salvage node '{n.Kind}'.");
            if (!state.Map.InBounds(n.Pos)) throw new JsonException($"Salvage node {n.Id} is off the map at {n.Pos}.");
        }
        foreach (var sensor in state.Sensors)
        {
            RequireItem(sensor.ItemId);
            if (!state.Map.InBounds(sensor.Pos)) throw new JsonException($"Sensor {sensor.Id} is off the map at {sensor.Pos}.");
        }
        foreach (var d in state.Devices)
        {
            if (!state.Map.InBounds(d.Pos)) throw new JsonException($"Device {d.Id} is off the map at {d.Pos}.");
            if (d.UnitId is { } uid && state.FindUnit(uid) is null) throw new JsonException($"Device {d.Id} references unknown unit {uid}.");
            foreach (var id in d.Items) RequireItem(id);
        }
        foreach (var u in state.Units.Where(u => u.DroneItemId is not null)) RequireItem(u.DroneItemId!);
        if (state.Mission is { } mission)
        {
            foreach (var a in mission.Def.Triggers.SelectMany(t => t.Actions))
                if (a.Enemy is { } e && !data.Enemies.ContainsKey(e)) throw new JsonException($"Save's mission spawns unknown enemy '{e}'.");
            foreach (var o in mission.Def.Objectives.Where(o => o.Item is not null)) RequireItem(o.Item!);
            foreach (var p in mission.Markers.Values.SelectMany(t => t).Concat(mission.Raised))
                if (!state.Map.InBounds(p)) throw new JsonException($"Save's mission marks {p}, off the map.");
        }
        foreach (var v in state.Vehicles)
        {
            if (!state.Map.InBounds(v.Pos)) throw new JsonException($"The {v.Name} is parked off the map at {v.Pos}.");
            foreach (var id in v.Stash) RequireItem(id);
        }
        foreach (var id in state.TurnOrder)
            if (state.FindUnit(id) is null) throw new JsonException($"Turn order references unknown unit {id}.");
    }
}
