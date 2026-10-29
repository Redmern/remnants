using System.Text.Json;
using Remnants.Core.Colony;
using Remnants.Core.Data;

namespace Remnants.Core.Serialization;

/// <summary>Save/load the colony state as JSON. Definitions are not saved; they reload from data/.</summary>
public static class SaveGame
{
    public const int Version = 1;

    private sealed record Envelope(int Version, ColonyState Colony);

    public static string Save(ColonyState state) => JsonSerializer.Serialize(new Envelope(Version, state), Json.Options);

    public static ColonySim Load(GameData data, string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, Json.Options)
            ?? throw new JsonException("Save file is empty.");
        if (envelope.Version != Version)
            throw new JsonException($"Unsupported save version {envelope.Version} (expected {Version}).");
        var colony = envelope.Colony ?? throw new JsonException("Save file has no colony.");
        Validate(data, colony);
        return new ColonySim(data, colony);
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
    }
}
