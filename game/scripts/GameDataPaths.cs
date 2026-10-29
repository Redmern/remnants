using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Remnants.Core.Data;

namespace Remnants.Game;

/// <summary>
/// Finds and loads the game data once. Lookup order:
/// 1. <c>data/</c> next to the executable (exported builds; lets players override balancing),
/// 2. the repo's <c>data/</c> one level above the Godot project (editor and dev runs),
/// 3. the copy embedded in the game assembly at build time (always present).
/// </summary>
public static class GameDataPaths
{
    private static readonly string[] Files = ["resources.json", "jobs.json", "buildings.json", "rules.json"];
    private const string EmbeddedPrefix = "Remnants.Data.";

    private static GameData? _data;

    /// <summary>Where the loaded data came from, for diagnostics.</summary>
    public static string Source { get; private set; } = "";

    public static GameData Load() => _data ??= LoadFresh();

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

    private static GameData LoadEmbedded()
    {
        var assembly = typeof(GameDataPaths).Assembly;
        string Read(string file)
        {
            using var stream = assembly.GetManifestResourceStream(EmbeddedPrefix + file)
                ?? throw new GameDataException($"Embedded data file '{file}' is missing from the build.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return GameDataLoader.LoadFromJson(Read("resources.json"), Read("jobs.json"), Read("buildings.json"), Read("rules.json"));
    }
}
