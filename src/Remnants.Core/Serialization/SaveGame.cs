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
        return new ColonySim(data, envelope.Colony);
    }
}
