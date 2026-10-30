using System.Text.Json;
using Remnants.Core.Serialization;

namespace Remnants.Core.Data;

/// <summary>Loads definitions from the data/ folder (resources.json, jobs.json, buildings.json, rules.json, recipes.json, perks.json).</summary>
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

        var perks = Path.Combine(directory, "perks.json");
        return LoadFromJson(Read("resources.json"), Read("jobs.json"), Read("buildings.json"), Read("rules.json"), Read("recipes.json"),
            File.Exists(perks) ? File.ReadAllText(perks) : null);
    }

    /// <summary>Without <paramref name="perks"/> there is no perk tree and no study (XP still counts levels).</summary>
    public static GameData LoadFromJson(string resources, string jobs, string buildings, string rules, string recipes = "[]", string? perks = null) =>
        new(
            Parse<List<ResourceDef>>(resources, "resources"),
            Parse<List<JobDef>>(jobs, "jobs"),
            Parse<List<BuildingDef>>(buildings, "buildings"),
            Parse<RulesDef>(rules, "rules"),
            Parse<List<RecipeDef>>(recipes, "recipes"),
            perks is null ? null : Parse<ProgressionDef>(perks, "perks"));

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
