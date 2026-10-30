using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Remnants.Core.Data;
using Remnants.Core.Raids;
using Remnants.Core.Scavenge;

namespace Remnants.Game;

/// <summary>
/// Finds and loads the game data once. Lookup order:
/// 1. <c>data/</c> next to the executable (exported builds; lets players override balancing),
/// 2. the repo's <c>data/</c> one level above the Godot project (editor and dev runs),
/// 3. the copy embedded in the game assembly at build time (always present).
/// </summary>
public static class GameDataPaths
{
    private static readonly string[] Files = ["resources.json", "jobs.json", "buildings.json", "rules.json", "recipes.json", "perks.json", "cybernetics.json", "schematics.json", "food.json"];
    private static readonly string[] ScavengeFiles = ["items.json", "enemies.json", "zones.json", "scavenge.json"];
    private const string EmbeddedPrefix = "Remnants.Data.";

    private static GameData? _data;
    private static ScavengeData? _scavenge;
    private static Remnants.Core.Story.StoryData? _story;
    private static RaidData? _raids;
    private static bool _raidsLoaded;

    /// <summary>Where the loaded data came from, for diagnostics.</summary>
    public static string Source { get; private set; } = "";

    public static GameData Load() => _data ??= LoadFresh();

    /// <summary>Scavenge definitions (items, enemies, zones, rules), found the same way as the colony data.</summary>
    public static ScavengeData LoadScavenge() => _scavenge ??= LoadScavengeFresh();

    private static Remnants.Core.Trade.TradeRules? _trade;

    /// <summary>Player trading rules (trade.json), found the same way.</summary>
    public static Remnants.Core.Trade.TradeRules LoadTrade() => _trade ??=
        CandidateDirectories().FirstOrDefault(dir => File.Exists(Path.Combine(dir, "trade.json"))) is { } dir
            ? Remnants.Core.Trade.TradeRules.Load(dir)
            : Remnants.Core.Trade.TradeRules.Parse(ReadEmbedded("trade.json"));

    /// <summary>The story (data/story): missions, dialogue, maps. A Sandbox colony carries it too and simply never uses it.</summary>
    public static Remnants.Core.Story.StoryData LoadStory() => _story ??= LoadStoryFresh();

    private static Remnants.Core.Story.StoryData LoadStoryFresh()
    {
        foreach (var dir in CandidateDirectories())
            if (File.Exists(Path.Combine(dir, "story", "characters.json")))
                return Remnants.Core.Story.StoryDataLoader.LoadFromDirectory(Path.Combine(dir, "story"));
        const string prefix = "Remnants.Story.";
        var names = typeof(GameDataPaths).Assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        string Read(string name)
        {
            using var stream = typeof(GameDataPaths).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        // RecursiveDir keeps the folder ("missions/", or "missions\" built on Windows).
        IEnumerable<(string, string)> In(string folder) => names
            .Where(n => n[prefix.Length..].Replace('\\', '/').StartsWith(folder + "/", StringComparison.Ordinal))
            .Select(n => (n, Read(n)));
        return Remnants.Core.Story.StoryDataLoader.LoadFromJson(Read(prefix + "characters.json"), In("missions"), In("dialogue"), In("maps"));
    }

    /// <summary>
    /// The raid model and defense catalogue (raids.json + schematics.defense.json), validated against the colony and
    /// scavenge data; null when raids.json is missing (no raids). Set it on every <c>ColonySim</c> the game makes.
    /// </summary>
    public static RaidData? LoadRaids()
    {
        if (_raidsLoaded) return _raids;
        _raidsLoaded = true;
        _raids = LoadRaidsFresh();
        _raids?.Validate(Load(), LoadScavenge());
        return _raids;
    }

    private static RaidData? LoadRaidsFresh()
    {
        foreach (var dir in CandidateDirectories())
            if (File.Exists(Path.Combine(dir, "raids.json")))
                return RaidDataLoader.LoadFromDirectory(dir);
        var raids = TryReadEmbedded("raids.json");
        return raids is null ? null : RaidDataLoader.LoadFromJson(raids);
    }

    private static string? TryReadEmbedded(string file)
    {
        using var stream = typeof(GameDataPaths).Assembly.GetManifestResourceStream(EmbeddedPrefix + file);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ScavengeData LoadScavengeFresh()
    {
        foreach (var dir in CandidateDirectories())
            if (ScavengeFiles.All(f => File.Exists(Path.Combine(dir, f))))
                return ScavengeDataLoader.LoadFromDirectory(dir);
        return ScavengeDataLoader.LoadFromJson(ReadEmbedded("items.json"), ReadEmbedded("enemies.json"),
            ReadEmbedded("zones.json"), ReadEmbedded("scavenge.json"), ReadEmbedded("perks.json"), ReadEmbedded("cybernetics.json"), ReadEmbedded("electronics.json"),
            ReadEmbedded("schematics.json"), ReadEmbedded("schematics.electronics.json"));
    }

    private static GameData LoadFresh()
    {
        foreach (var dir in CandidateDirectories())
        {
            if (!Files.All(f => File.Exists(Path.Combine(dir, f)))) continue;
            Source = dir;
            return GameDataLoader.LoadFromDirectory(dir);
        }
        Source = "embedded";
        return LoadEmbedded();
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        if (OS.HasFeature("template"))
            yield return Path.Combine(OS.GetExecutablePath().GetBaseDir(), "data");
        else
            yield return Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data"));
    }

    private static GameData LoadEmbedded() =>
        GameDataLoader.LoadFromJson(ReadEmbedded("resources.json"), ReadEmbedded("jobs.json"), ReadEmbedded("buildings.json"), ReadEmbedded("rules.json"),
            ReadEmbedded("recipes.json"), ReadEmbedded("perks.json"), ReadEmbedded("cybernetics.json"), ReadEmbedded("weather.json"),
            ReadEmbedded("food.json"), ReadEmbedded("electronics.json"), ReadEmbedded("schematics.json"), ReadEmbedded("schematics.food.json"),
            ReadEmbedded("schematics.electronics.json"));

    private static string ReadEmbedded(string file)
    {
        using var stream = typeof(GameDataPaths).Assembly.GetManifestResourceStream(EmbeddedPrefix + file)
            ?? throw new GameDataException($"Embedded data file '{file}' is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
