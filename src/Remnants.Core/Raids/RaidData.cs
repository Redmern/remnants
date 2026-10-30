using System.Text.Json;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Serialization;

namespace Remnants.Core.Raids;

/// <summary>
/// The raid model and defense catalogue (data/raids.json) plus the defense schematics (data/schematics.defense.json),
/// indexed and checked against the colony data. Immutable after load. A <see cref="ColonySim"/> without it has no raids.
/// </summary>
public sealed class RaidData
{
    public RaidsDef Def { get; }
    public IReadOnlyDictionary<string, DefenseDef> Defenses { get; }

    /// <summary>The defense schematics, in file order (see <see cref="DefenseSchematics"/>).</summary>
    public IReadOnlyList<DefenseSchematicDef> Schematics { get; }

    public RaidData(RaidsDef def, IEnumerable<DefenseSchematicDef>? schematics = null)
    {
        Def = def;
        var defenses = new Dictionary<string, DefenseDef>(StringComparer.Ordinal);
        foreach (var d in def.Defenses)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) throw new GameDataException("raids.json: a defense has no id.");
            if (!defenses.TryAdd(d.Id, d)) throw new GameDataException($"raids.json: duplicate defense id '{d.Id}'.");
        }
        Defenses = defenses;
        Schematics = (schematics ?? []).ToList();
    }

    public DefenseDef Defense(string id) => Defenses[id];

    public ThreatDef Threat(ThreatKind kind) => Def.Threats[kind];

    /// <summary>Checks every id against the colony data (and enemies when scavenge data is given). Throws <see cref="GameDataException"/>.</summary>
    public void Validate(GameData data, ScavengeData? scavenge = null)
    {
        void Resources(Dictionary<string, double> cost, string where)
        {
            foreach (var r in cost.Keys)
                if (!data.Resources.ContainsKey(r)) throw new GameDataException($"raids.json {where}: unknown resource '{r}'.");
        }

        foreach (var kind in Enum.GetValues<ThreatKind>())
            if (!Def.Threats.ContainsKey(kind)) throw new GameDataException($"raids.json: no threat '{kind}'.");
        foreach (var (kind, t) in Def.Threats)
        {
            if (t.MinSize < 1 || t.MaxSize < t.MinSize) throw new GameDataException($"raids.json threat {kind}: sizes must be 1 ≤ min ≤ max.");
            foreach (var r in t.SpoilResources)
                if (!data.Resources.ContainsKey(r)) throw new GameDataException($"raids.json threat {kind}: unknown resource '{r}'.");
            if (scavenge is null) continue;
            foreach (var e in t.Enemies)
                if (!scavenge.Enemies.ContainsKey(e)) throw new GameDataException($"raids.json threat {kind}: unknown enemy '{e}'.");
            foreach (var i in t.Loot.Keys)
                if (!scavenge.Items.ContainsKey(i)) throw new GameDataException($"raids.json threat {kind}: unknown loot item '{i}'.");
        }
        if (scavenge is not null)
            foreach (var i in Def.Noise.Items.Keys.Append(Def.Warning.DroneItem))
                if (!scavenge.Items.ContainsKey(i)) throw new GameDataException($"raids.json: unknown item '{i}'.");
        foreach (var d in Defenses.Values)
        {
            Resources(d.Cost, d.Id);
            Resources(d.Upkeep, d.Id);
            Resources(d.RearmCost, d.Id);
            if (d.Hp <= 0) throw new GameDataException($"raids.json defense '{d.Id}': hp must be positive.");
            if (d.UpgradesTo is { } next && (!Defenses.TryGetValue(next, out var up) || up.Kind != d.Kind))
                throw new GameDataException($"raids.json defense '{d.Id}': upgrades to unknown or different-kind '{next}'.");
            if (d.IsTrap && !(d.TrapDamage > 0 || d.Lure || d.WarningHours > 0))
                throw new GameDataException($"raids.json defense '{d.Id}': a trap needs damage, a lure or warning hours.");
            if (d.Rearmable && !d.IsTrap) throw new GameDataException($"raids.json defense '{d.Id}': only traps re-arm.");
        }
        var hours = Def.Guard;
        foreach (var w in new[] { hours.DaySleepHours })
            if (w.Length != 2 || w.Any(h => h < 0 || h >= data.Rules.TicksPerDay))
                throw new GameDataException("raids.json guard: hour windows are [start, end) within the day.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in Schematics)
        {
            if (!seen.Add(s.Id)) throw new GameDataException($"schematics.defense.json: duplicate schematic '{s.Id}'.");
            if (s.Unlocks.Count == 0) throw new GameDataException($"schematics.defense.json '{s.Id}': unlocks nothing.");
            foreach (var u in s.Unlocks)
                if (u.Kind != DefenseSchematics.UnlockKind || !Defenses.ContainsKey(u.Id))
                    throw new GameDataException($"schematics.defense.json '{s.Id}': unknown unlock {u.Kind} '{u.Id}'.");
            if (!s.Default && s.Sources.All(src => src == "Story"))
                throw new GameDataException($"schematics.defense.json '{s.Id}': needs a source besides Story.");
        }
        foreach (var d in Defenses.Values)
            if (!Schematics.Any(s => s.Unlocks.Any(u => u.Id == d.Id)))
                throw new GameDataException($"raids.json defense '{d.Id}': no schematic unlocks it.");
    }
}

/// <summary>Reads data/raids.json and data/schematics.defense.json.</summary>
public static class RaidDataLoader
{
    /// <summary>Null when raids.json is missing (no raids).</summary>
    public static RaidData? LoadFromDirectory(string directory)
    {
        var path = Path.Combine(directory, "raids.json");
        if (!File.Exists(path)) return null;
        var schematics = Path.Combine(directory, DefenseSchematics.FileName);
        return LoadFromJson(File.ReadAllText(path), File.Exists(schematics) ? File.ReadAllText(schematics) : null);
    }

    public static RaidData LoadFromJson(string raids, string? schematics = null)
    {
        try
        {
            var def = JsonSerializer.Deserialize<RaidsDef>(raids, Json.Options) ?? throw new GameDataException("raids.json is empty.");
            var sch = schematics is null ? null : JsonSerializer.Deserialize<DefenseSchematicFile>(schematics, Json.Options);
            return new RaidData(def, sch?.Schematics);
        }
        catch (JsonException e)
        {
            throw new GameDataException($"raids.json or {DefenseSchematics.FileName} is invalid: {e.Message}");
        }
    }
}
