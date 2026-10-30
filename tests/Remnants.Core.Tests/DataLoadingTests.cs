using Remnants.Core.Data;

namespace Remnants.Core.Tests;

public class DataLoadingTests
{
    [Fact]
    public void Real_data_folder_loads_and_has_the_m1_content()
    {
        var data = TestData.Real;
        foreach (var id in new[] { "town_hall", "shelter", "woodcutter", "quarry", "scrapyard", "farm" })
            Assert.Contains(id, data.Buildings.Keys);
        foreach (var id in new[] { "wood", "stone", "scrap" })
            Assert.Contains(id, data.Resources.Keys);
        Assert.All(data.Buildings.Values.Where(b => b.Category == BuildingCategory.Gathering),
            b => Assert.NotNull(b.Job));
    }

    [Fact]
    public void Unknown_resource_in_a_cost_is_rejected()
    {
        var ex = Assert.Throws<GameDataException>(() => GameDataLoader.LoadFromJson(
            resources: """[{ "id": "wood" }, { "id": "food" }]""",
            jobs: "[]",
            buildings: """[{ "id": "town_hall", "levels": [{ "cost": { "unobtainium": 5 } }] }]""",
            rules: """{ "newGame": { "maleNames": ["A"], "femaleNames": ["B"] } }"""));
        Assert.Contains("unobtainium", ex.Message);
    }

    [Fact]
    public void Malformed_json_is_reported_as_data_error()
    {
        Assert.Throws<GameDataException>(() => GameDataLoader.LoadFromJson("[{", "[]", "[]", "{}"));
    }
}

public class RulesValidationTests
{
    private static GameDataException Load(string rules) => Assert.Throws<GameDataException>(() => GameDataLoader.LoadFromJson(
        """[{ "id": "food" }]""", "[]", """[{ "id": "town_hall", "levels": [{}] }]""", rules));

    [Fact]
    public void Fewer_than_two_adults_is_rejected() =>
        Assert.Contains("adults", Load("""{ "newGame": { "adults": 1, "maleNames": ["A"], "femaleNames": ["B"] } }""").Message);

    [Fact]
    public void Inverted_range_is_rejected() =>
        Assert.Contains("attributeRange", Load("""{ "newGame": { "attributeRange": [60, 20], "maleNames": ["A"], "femaleNames": ["B"] } }""").Message);
}
