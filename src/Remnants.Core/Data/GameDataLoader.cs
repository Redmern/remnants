using System.Text.Json;
using Remnants.Core.Schematics;
using Remnants.Core.Serialization;

namespace Remnants.Core.Data;

/// <summary>Loads definitions from the data/ folder (resources.json, jobs.json, buildings.json, rules.json, recipes.json, perks.json, cybernetics.json, schematics.json, medicine.json).</summary>
public static class GameDataLoader
{
    public static GameData LoadFromDirectory(string directory)
    {
        string Read(string file)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path)) throw new GameDataException($"Missing data file '{path}'.");
            return File.ReadAllText(path);
        }

        string? Optional(string file) => File.Exists(Path.Combine(directory, file)) ? File.ReadAllText(Path.Combine(directory, file)) : null;

        return LoadFromJson(Read("resources.json"), Read("jobs.json"), Read("buildings.json"), Read("rules.json"), Read("recipes.json"),
            Optional("perks.json"), Optional("cybernetics.json"), Optional("medicine.json"), SchematicCatalogLoader.Files(directory).Select(File.ReadAllText).ToArray());
    }

    /// <summary>
    /// Without <paramref name="perks"/> there is no perk tree and no study (XP still counts levels); without
    /// <paramref name="cybernetics"/> no cybernetics tree (prosthetics only strap on).
    /// </summary>
    public static GameData LoadFromJson(string resources, string jobs, string buildings, string rules, string recipes = "[]", string? perks = null,
        string? cybernetics = null, params string[]? schematics) =>
        LoadFromJson(resources, jobs, buildings, rules, recipes, perks, cybernetics, null, schematics ?? []);

    /// <summary>The same with medicine.json (optional: without it, no conditions and no auto-treatment).</summary>
    public static GameData LoadFromJson(string resources, string jobs, string buildings, string rules, string recipes, string? perks,
        string? cybernetics, string? medicine, string[] schematics) =>
        new(
            Parse<List<ResourceDef>>(resources, "resources"),
            Parse<List<JobDef>>(jobs, "jobs"),
            Parse<List<BuildingDef>>(buildings, "buildings"),
            Parse<RulesDef>(rules, "rules"),
            Parse<List<RecipeDef>>(recipes, "recipes"),
            perks is null ? null : Parse<ProgressionDef>(perks, "perks"),
            cybernetics is null ? null : Parse<CyberneticsDef>(cybernetics, "cybernetics"),
            SchematicCatalogLoader.LoadFromJson(schematics),
            medicine is null ? null : Parse<MedicineDef>(medicine, "medicine"));

    private static T Parse<T>(string json, string name)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json.Options)
                ?? throw new GameDataException($"{name}.json is empty.");
        }
        catch (JsonException e)
        {
            throw new GameDataException($"{name}.json is invalid: {e.Message}");
        }
    }
}
