using System.Text.Json;
using Remnants.Core.Serialization;

namespace Remnants.Core.Scavenge;

/// <summary>
/// Save/load a scavenge instance as JSON. Definitions are not saved; they reload from data/.
/// Version 2 adds the Network alarm (<see cref="ScavengeState.AlarmRounds"/>, reinforcement waves), suppression and
/// pylon tiles; version 1 saves load with those at their defaults.
/// </summary>
public static class ScavengeSave
{
    public const int Version = 2;
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
        Validate(data, state);
        return new ScavengeSim(data, state);
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
        }
        foreach (var id in state.Caches.SelectMany(c => c.Items)) RequireItem(id);
        foreach (var id in state.TurnOrder)
            if (state.FindUnit(id) is null) throw new JsonException($"Turn order references unknown unit {id}.");
    }
}
