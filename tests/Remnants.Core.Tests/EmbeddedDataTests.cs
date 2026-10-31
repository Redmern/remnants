using Remnants.Core.Data;
using Remnants.Core.Raids;
using Remnants.Core.Scavenge;
using Remnants.Core.Story;
using Remnants.Core.Trade;

namespace Remnants.Core.Tests;

/// <summary>
/// An exported build reads the copy of data/ embedded by EmbeddedData.props (the same import as the game's csproj). It
/// must load exactly what the folder does, catalogue by catalogue, so a feature's new file can't go missing in exports.
/// </summary>
public class EmbeddedDataTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly DataSource Dev = DataSource.FromDirectory(Folder);
    private static readonly DataSource Embedded = DataSource.FromAssembly(typeof(EmbeddedDataTests).Assembly);

    private static IEnumerable<string> AllFiles(DataSource source, string folder = "")
    {
        foreach (var file in source.List(folder)) yield return file;
        var full = Path.Combine(Folder, folder);
        foreach (var sub in Directory.GetDirectories(full).Select(Path.GetFileName).Order(StringComparer.Ordinal))
            foreach (var file in AllFiles(source, folder.Length == 0 ? sub! : $"{folder}/{sub}"))
                yield return file;
    }

    private static void Same<T>(IEnumerable<T> dev, IEnumerable<T> embedded) =>
        Assert.Equal(dev.Order().ToList(), embedded.Order().ToList());

    [Fact]
    public void Every_data_file_is_embedded_with_the_same_text()
    {
        var files = AllFiles(Dev).ToList();
        Assert.Contains("loot_tables.json", files);
        Assert.Contains("story/characters.json", files);
        Assert.Equal(files, AllFiles(Embedded).ToList());
        Assert.All(files, f => Assert.Equal(Dev.Read(f), Embedded.Read(f)));
    }

    [Fact]
    public void Colony_catalogues_load_the_same_ids()
    {
        GameData dev = GameDataLoader.Load(Dev), embedded = GameDataLoader.Load(Embedded);
        Same(dev.Resources.Keys, embedded.Resources.Keys);
        Same(dev.Jobs.Keys, embedded.Jobs.Keys);
        Same(dev.Buildings.Keys, embedded.Buildings.Keys);
        Same(dev.Recipes.Keys, embedded.Recipes.Keys);
        Same(dev.Perks.Keys, embedded.Perks.Keys);
        Same(dev.Cybernetics!.Nodes.Keys, embedded.Cybernetics!.Nodes.Keys);
        Same(dev.Schematics.All.Keys, embedded.Schematics.All.Keys);
        Same(dev.Schematics.Traders.Keys, embedded.Schematics.Traders.Keys);
        Same(dev.Food!.Items.Keys, embedded.Food!.Items.Keys);
        Same(dev.Medicine!.Conditions.Keys, embedded.Medicine!.Conditions.Keys);
        Same(dev.VehicleTiers.Keys, embedded.VehicleTiers.Keys);
        Same(dev.VehicleUpgrades.Keys, embedded.VehicleUpgrades.Keys);
        Assert.Equal(dev.Weather.Enabled, embedded.Weather.Enabled);
        Assert.NotNull(embedded.Electronics);
    }

    [Fact]
    public void Scavenge_catalogues_load_the_same_ids()
    {
        ScavengeData dev = ScavengeDataLoader.Load(Dev), embedded = ScavengeDataLoader.Load(Embedded);
        Same(dev.Items.Keys, embedded.Items.Keys);
        Same(dev.Enemies.Keys, embedded.Enemies.Keys);
        Same(dev.Zones.Keys, embedded.Zones.Keys);
        Same(dev.Schematics.All.Keys, embedded.Schematics.All.Keys);
        Assert.NotEmpty(embedded.Loot.Tables);
        Same(dev.Loot.Tables.Keys, embedded.Loot.Tables.Keys);
        Assert.NotEmpty(embedded.Salvage.Nodes);
        Same(dev.Salvage.Nodes.Select(n => n.Id), embedded.Salvage.Nodes.Select(n => n.Id));
        Assert.NotNull(embedded.Medicine);
        Assert.NotNull(embedded.GearMods);
    }

    [Fact]
    public void Story_raids_and_trade_load_the_same()
    {
        StoryData dev = StoryDataLoader.Load(Dev.Sub("story")), embedded = StoryDataLoader.Load(Embedded.Sub("story"));
        Same(dev.Characters.Keys, embedded.Characters.Keys);
        Assert.Equal(dev.Missions.Select(m => m.Id), embedded.Missions.Select(m => m.Id));
        Same(dev.Dialogues.Keys, embedded.Dialogues.Keys);
        Same(dev.Maps.Keys, embedded.Maps.Keys);
        Same(dev.Traits.Keys, embedded.Traits.Keys);
        Same(dev.Endings.Keys, embedded.Endings.Keys);
        Assert.Equal(dev.Codex.Count, embedded.Codex.Count);

        RaidData devRaids = RaidDataLoader.Load(Dev)!, embeddedRaids = RaidDataLoader.Load(Embedded)!;
        Same(devRaids.Defenses.Keys, embeddedRaids.Defenses.Keys);

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(TradeRules.Load(Dev)), System.Text.Json.JsonSerializer.Serialize(TradeRules.Load(Embedded)));
    }

    [Fact]
    public void Catalogue_is_the_main_file_then_its_extensions_by_name()
    {
        var source = DataSource.FromFiles(new Dictionary<string, string>
        {
            ["loot_tables.story.json"] = "s", ["loot_tables.json"] = "m", ["loot_tables.medicine.json"] = "x", ["loot.json"] = "no",
            ["story/maps/b.json"] = "b", ["story/maps/a.json"] = "a",
        });
        Assert.Equal(["loot_tables.json", "loot_tables.medicine.json", "loot_tables.story.json"], source.Catalogue("loot_tables.json"));
        Assert.Empty(source.Catalogue("schematics.json"));
        Assert.Equal(["maps/a.json", "maps/b.json"], source.Sub("story").List("maps"));
    }
}
