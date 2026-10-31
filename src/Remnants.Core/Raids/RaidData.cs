using System.Text.Json;
using Remnants.Core.Colony;
using Remnants.Core.Data;
using Remnants.Core.Scavenge;
using Remnants.Core.Schematics;
using Remnants.Core.Serialization;

namespace Remnants.Core.Raids;

/// <summary>
/// The raid model and defense catalogue (data/raids.json), indexed and checked against the colony data (and the defense
/// schematics in the schematics catalogue). Immutable after load. A <see cref="ColonySim"/> without it has no raids.
/// </summary>
public sealed class RaidData
{
    public RaidsDef Def { get; }
    public IReadOnlyDictionary<string, DefenseDef> Defenses { get; }

    public RaidData(RaidsDef def)
    {
        Def = def;
        var defenses = new Dictionary<string, DefenseDef>(StringComparer.Ordinal);
        foreach (var d in def.Defenses)
        {
            if (string.IsNullOrWhiteSpace(d.Id)) throw new GameDataException("raids.json: a defense has no id.");
            if (!defenses.TryAdd(d.Id, d)) throw new GameDataException($"raids.json: duplicate defense id '{d.Id}'.");
        }
        Defenses = defenses;
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
            foreach (var r in t.SpoilResources.Where(r => r is not (CropDamage.Crops or CropDamage.Food)))
                if (!data.Resources.ContainsKey(r)) throw new GameDataException($"raids.json threat {kind}: unknown resource '{r}'.");
            if (scavenge is null) continue;
            foreach (var e in t.Enemies)
                if (!scavenge.Enemies.ContainsKey(e)) throw new GameDataException($"raids.json threat {kind}: unknown enemy '{e}'.");
            foreach (var i in t.Loot.Keys)
                if (!scavenge.Items.ContainsKey(i)) throw new GameDataException($"raids.json threat {kind}: unknown loot item '{i}'.");
        }
        var battle = Def.Battle;
        if (battle.Rounds < 1 || battle.Waves.Count == 0 || battle.Waves.Any(w => w.Round < 1 || w.Share <= 0))
            throw new GameDataException("raids.json battle: at least a round and one wave (round ≥ 1, share > 0).");
        if (scavenge is not null && !scavenge.Zones.ContainsKey(battle.Zone))
            throw new GameDataException($"raids.json battle: unknown zone '{battle.Zone}'.");
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
        foreach (var s in data.Schematics.All.Values)
            foreach (var u in s.Unlocks.Where(u => u.Kind == UnlockKind.Defense))
                if (!Defenses.ContainsKey(u.Id)) throw new GameDataException($"Schematic '{s.Id}': unknown defense '{u.Id}'.");
    }
}

/// <summary>Reads data/raids.json (its schematics, schematics.defense.json, load with the schematics catalogue).</summary>
public static class RaidDataLoader
{
    /// <summary>Null when raids.json is missing (no raids).</summary>
    public static RaidData? LoadFromDirectory(string directory) => Load(DataSource.FromDirectory(directory));

    /// <summary>Null when raids.json is missing (no raids).</summary>
    public static RaidData? Load(DataSource data) => data.TryRead("raids.json") is { } raids ? LoadFromJson(raids) : null;

    public static RaidData LoadFromJson(string raids)
    {
        try
        {
            return new RaidData(JsonSerializer.Deserialize<RaidsDef>(raids, Json.Options) ?? throw new GameDataException("raids.json is empty."));
        }
        catch (JsonException e)
        {
            throw new GameDataException($"raids.json is invalid: {e.Message}");
        }
    }
}
