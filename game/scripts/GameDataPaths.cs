using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Remnants.Core.Data;
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
    private static readonly string[] Files = ["resources.json", "jobs.json", "buildings.json", "rules.json", "recipes.json", "perks.json"];
    private static readonly string[] ScavengeFiles = ["items.json", "enemies.json", "zones.json", "scavenge.json"];
    private const string EmbeddedPrefix = "Remnants.Data.";

    private static GameData? _data;
    private static ScavengeData? _scavenge;

    /// <summary>Where the loaded data came from, for diagnostics.</summary>
    public static string Source { get; private set; } = "";

    public static GameData Load() => _data ??= LoadFresh();

    /// <summary>Scavenge definitions (items, enemies, zones, rules), found the same way as the colony data.</summary>
    public static ScavengeData LoadScavenge() => _scavenge ??= LoadScavengeFresh();

    private static ScavengeData LoadScavengeFresh()
    {
        foreach (var dir in CandidateDirectories())
            if (ScavengeFiles.All(f => File.Exists(Path.Combine(dir, f))))
                return ScavengeDataLoader.LoadFromDirectory(dir);
        return ScavengeDataLoader.LoadFromJson(ReadEmbedded("items.json"), ReadEmbedded("enemies.json"),
            ReadEmbedded("zones.json"), ReadEmbedded("scavenge.json"));
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
            ReadEmbedded("recipes.json"), ReadEmbedded("perks.json"));

    private static string ReadEmbedded(string file)
    {
        using var stream = typeof(GameDataPaths).Assembly.GetManifestResourceStream(EmbeddedPrefix + file)
            ?? throw new GameDataException($"Embedded data file '{file}' is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
