using System.Text.Json;
using Remnants.Core.Schematics;
using Remnants.Core.Serialization;

namespace Remnants.Core.Data;

/// <summary>Loads definitions from the data/ folder (resources.json, jobs.json, buildings.json, rules.json, recipes.json, perks.json, cybernetics.json, schematics.json, weather.json, food.json, electronics.json, vehicles.json, gear_mods.json, medicine.json).</summary>
public static class GameDataLoader
{
    public static GameData LoadFromDirectory(string directory) => Load(DataSource.FromDirectory(directory));

    /// <summary>The colony definitions in <paramref name="data"/>: the required files, the optional feature files and the schematic catalogue.</summary>
    public static GameData Load(DataSource data)
    {
        var loaded = LoadFromJson(data.Read("resources.json"), data.Read("jobs.json"), data.Read("buildings.json"), data.Read("rules.json"), data.Read("recipes.json"),
            data.TryRead("perks.json"), data.TryRead("cybernetics.json"), data.TryRead("weather.json"), data.TryRead("food.json"), data.TryRead("electronics.json"),
            data.TryRead("gear_mods.json"), data.TryRead("medicine.json"), data.ReadCatalogue(SchematicCatalogLoader.FileName),
            vehicles: data.TryRead("vehicles.json"));
        if (data.TryRead(Scavenge.ScavengeDataLoader.CardsFileName) is { } cards) CheckRecipeCards(loaded.Recipes.Values, Parse<Scavenge.CardsDef>(cards, "cards"));
        return loaded;
    }

    /// <summary>Every recipe's card output names a card of cards.json (the recipes and the cards load apart).</summary>
    public static void CheckRecipeCards(IEnumerable<RecipeDef> recipes, Scavenge.CardsDef cards)
    {
        var known = cards.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var r in recipes.Where(r => r.Card is { } id && !known.Contains(id)).OrderBy(r => r.Id, StringComparer.Ordinal))
            throw new GameDataException($"Recipe '{r.Id}': unknown card '{r.Card}'.");
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

    /// <summary>
    /// As above, with medicine.json (optional: without it, no conditions and no auto-treatment) and vehicles.json (optional:
    /// without it, no vehicles and every party walks).
    /// </summary>
    public static GameData LoadFromJson(string resources, string jobs, string buildings, string rules, string recipes, string? perks,
        string? cybernetics, string? weather, string? food, string? electronics, string? gearMods, string? medicine, string[] schematics,
        string? vehicles = null)
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
            medicine is null ? null : Parse<MedicineDef>(medicine, "medicine"),
            vehicles is null ? null : Parse<VehicleData>(vehicles, "vehicles"));
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
