using System.Text.Json;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;

namespace Remnants.Core.Serialization;

/// <summary>
/// Save/load the colony state as JSON. Definitions are not saved; they reload from data/.
/// Version 2 adds the item inventory, the expedition (with the scavenge run in progress), patients,
/// recipes and the memorial; version 1 saves still load (those start empty).
/// </summary>
public static class SaveGame
{
    public const int Version = 2;
    private const int OldestVersion = 1;

    private sealed record Envelope(int Version, ColonyState Colony);

    public static string Save(ColonyState state) => JsonSerializer.Serialize(new Envelope(Version, state), Json.Options);

    /// <summary>
    /// Loads a colony. Pass <paramref name="scavenge"/> to also check item ids and the expedition's scavenge run
    /// (the game always does; without it those parts are taken on trust).
    /// </summary>
    public static ColonySim Load(GameData data, string json, ScavengeData? scavenge = null)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, Json.Options)
            ?? throw new JsonException("Save file is empty.");
        if (envelope.Version < OldestVersion || envelope.Version > Version)
            throw new JsonException($"Unsupported save version {envelope.Version} (expected {OldestVersion}..{Version}).");
        var colony = envelope.Colony ?? throw new JsonException("Save file has no colony.");
        Validate(data, colony);
        if (scavenge is not null) Validate(scavenge, colony);
        return new ColonySim(data, colony, scavenge);
    }

    /// <summary>A save must still fit the current data/ (ids can be renamed, levels removed by a balancing edit).</summary>
    private static void Validate(GameData data, ColonyState colony)
    {
        foreach (var b in colony.Buildings)
        {
            if (!data.Buildings.TryGetValue(b.DefId, out var def))
                throw new JsonException($"Save references unknown building type '{b.DefId}'.");
            if (b.Level < 1 || b.Level > def.MaxLevel)
                throw new JsonException($"Save has {def.Name} at level {b.Level}; data allows 1..{def.MaxLevel}.");
            if (b.Plot < 0 || b.Plot >= data.Rules.Layout.PlotCount)
                throw new JsonException($"Save has {def.Name} on plot {b.Plot}; the town has plots 0..{data.Rules.Layout.PlotCount - 1}.");
        }
        if (colony.Buildings.GroupBy(b => b.Plot).FirstOrDefault(g => g.Count() > 1) is { } shared)
            throw new JsonException($"Save has several buildings on plot {shared.Key}.");
        foreach (var res in colony.Stockpile.Amounts.Keys)
            if (!data.Resources.ContainsKey(res))
                throw new JsonException($"Save references unknown resource '{res}'.");
        foreach (var b in colony.Buildings.Where(b => b.RecipeId is not null))
            if (data.Buildings[b.DefId].Recipes.All(r => r.Id != b.RecipeId))
                throw new JsonException($"Save has {data.Buildings[b.DefId].Name} on unknown recipe '{b.RecipeId}'.");
        foreach (var s in colony.Survivors.Where(s => s.PatientAtBuildingId is not null))
            if (colony.FindBuilding(s.PatientAtBuildingId!.Value) is not { } bay || bay.DefId != data.Rules.Medical.BuildingId)
                throw new JsonException($"Save has {s.Name} admitted to building {s.PatientAtBuildingId}, which is not a Med Bay.");

        var members = colony.Expedition?.Members.Select(m => m.SurvivorId).ToHashSet() ?? [];
        foreach (var id in members)
            if (colony.FindSurvivor(id) is not { Away: true })
                throw new JsonException($"Save has expedition member {id} who is not away.");
        if (colony.Survivors.FirstOrDefault(s => s.Away && !members.Contains(s.Id)) is { } stray)
            throw new JsonException($"Save has {stray.Name} away without an expedition.");
    }

    /// <summary>Items and the expedition must still fit items.json and zones.json.</summary>
    private static void Validate(ScavengeData data, ColonyState colony)
    {
        void RequireItem(string id)
        {
            if (!data.Items.ContainsKey(id)) throw new JsonException($"Save references unknown item '{id}'.");
        }

        foreach (var id in colony.Inventory.Counts.Keys) RequireItem(id);
        foreach (var part in colony.Survivors.SelectMany(s => s.Body.Parts.Values))
            if (part.ProstheticItemId is { } id) RequireItem(id);
        if (colony.Expedition is not { } expedition) return;
        if (!data.Zones.ContainsKey(expedition.ZoneId)) throw new JsonException($"Save references unknown zone '{expedition.ZoneId}'.");
        foreach (var id in expedition.Members.SelectMany(m => m.AllItems)) RequireItem(id);
        if (expedition.Scavenge is { } run) ScavengeSave.Validate(data, run);
    }
}
