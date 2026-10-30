using System.Text.Json;
using Remnants.Core.Schematics;
using Remnants.Core.Serialization;

namespace Remnants.Core.Data;

/// <summary>Loads definitions from the data/ folder (resources.json, jobs.json, buildings.json, rules.json, recipes.json, perks.json, cybernetics.json, schematics.json, weather.json, food.json, electronics.json, gear_mods.json, medicine.json).</summary>
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
            Optional("perks.json"), Optional("cybernetics.json"), Optional("weather.json"), Optional("food.json"), Optional("electronics.json"),
            Optional("gear_mods.json"), Optional("medicine.json"), SchematicCatalogLoader.Files(directory).Select(File.ReadAllText).ToArray());
    }

    /// <summary>
    /// Without <paramref name="perks"/> there is no perk tree and no study (XP still counts levels); without
    /// <paramref name="cybernetics"/> no cybernetics tree (prosthetics only strap on); without <paramref name="weather"/> it is
    /// always clear; without <paramref name="food"/> nobody eats (no pantry, no harvests: the colony is always fed).
    /// </summary>
    /// <param name="electronics">electronics.json (optional): its buildings and recipes join the others (its schematics are a
    /// <c>schematics.electronics.json</c> catalogue extension), and it runs the power grid.</param>
    public static GameData LoadFromJson(string resources, string jobs, string buildings, string rules, string recipes = "[]", string? perks = null,
        string? cybernetics = null, string? weather = null, string? food = null, string? electronics = null, params string[]? schematics) =>
        LoadFromJson(resources, jobs, buildings, rules, recipes, perks, cybernetics, weather, food, electronics, gearMods: null, schematics ?? []);

    /// <summary>As above, with gear_mods.json: its recipes join the others (their schematics are in the catalogue files).</summary>
    public static GameData LoadFromJson(string resources, string jobs, string buildings, string rules, string recipes, string? perks,
        string? cybernetics, string? weather, string? food, string? electronics, string? gearMods, string[] schematics) =>
        LoadFromJson(resources, jobs, buildings, rules, recipes, perks, cybernetics, weather, food, electronics, gearMods, medicine: null, schematics);

    /// <summary>As above, with medicine.json (optional: without it, no conditions and no auto-treatment).</summary>
    public static GameData LoadFromJson(string resources, string jobs, string buildings, string rules, string recipes, string? perks,
        string? cybernetics, string? weather, string? food, string? electronics, string? gearMods, string? medicine, string[] schematics)
    {
        var elec = electronics is null ? null : Parse<ElectronicsDef>(electronics, "electronics");
        return new(
            Parse<List<ResourceDef>>(resources, "resources"),
            Parse<List<JobDef>>(jobs, "jobs"),
            Parse<List<BuildingDef>>(buildings, "buildings").Concat(elec?.Buildings ?? []),
            Parse<RulesDef>(rules, "rules"),
            Parse<List<RecipeDef>>(recipes, "recipes").Concat(elec?.AllRecipes() ?? [])
                .Concat(gearMods is null ? [] : Parse<Items.GearModsDef>(gearMods, "gear_mods").Recipes),
            perks is null ? null : Parse<ProgressionDef>(perks, "perks"),
            cybernetics is null ? null : Parse<CyberneticsDef>(cybernetics, "cybernetics"),
            SchematicCatalogLoader.LoadFromJson(schematics ?? []),
            weather is null ? null : Parse<WeatherRules>(weather, "weather"),
            food is null ? null : Parse<FoodDef>(food, "food"),
            elec,
            medicine is null ? null : Parse<MedicineDef>(medicine, "medicine"));
    }

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
