using System.IO;
using Godot;
using Remnants.Core.Data;
using Remnants.Core.Raids;
using Remnants.Core.Scavenge;

namespace Remnants.Game;

/// <summary>
/// Finds and loads the game data once. Every catalogue reads through one <see cref="DataSource"/>, picked once:
/// 1. <c>data/</c> next to the executable (exported builds; lets players override balancing),
/// 2. the repo's <c>data/</c> one level above the Godot project (editor and dev runs),
/// 3. the copy embedded in the game assembly at build time (always present; see EmbeddedData.props).
/// A folder counts when it has resources.json. The Core loaders decide which files a catalogue needs, so the folder and
/// the embedded copy cannot load different files.
/// </summary>
public static class GameDataPaths
{
    private static DataSource? _source;
    private static GameData? _data;
    private static ScavengeData? _scavenge;
    private static Remnants.Core.Story.StoryData? _story;
    private static Remnants.Core.Trade.TradeRules? _trade;
    private static RaidData? _raids;
    private static bool _raidsLoaded;

    /// <summary>Where the loaded data came from, for diagnostics.</summary>
    public static string Source => Data.Name;

    /// <summary>The data folder in use (see the class summary).</summary>
    public static DataSource Data => _source ??= FindSource();

    public static GameData Load() => _data ??= GameDataLoader.Load(Data);

    /// <summary>Scavenge definitions (items, enemies, zones, rules, loot, salvage).</summary>
    public static ScavengeData LoadScavenge() => _scavenge ??= ScavengeDataLoader.Load(Data);

    /// <summary>Player trading rules (trade.json; defaults without it).</summary>
    public static Remnants.Core.Trade.TradeRules LoadTrade() => _trade ??= Remnants.Core.Trade.TradeRules.Load(Data);

    /// <summary>The story (data/story): missions, dialogue, maps. A Sandbox colony carries it too and simply never uses it.</summary>
    public static Remnants.Core.Story.StoryData LoadStory() => _story ??= Remnants.Core.Story.StoryDataLoader.Load(Data.Sub("story"));

    /// <summary>
    /// The raid model and defense catalogue (raids.json + schematics.defense.json), validated against the colony and
    /// scavenge data; null when raids.json is missing (no raids). Set it on every <c>ColonySim</c> the game makes.
    /// </summary>
    public static RaidData? LoadRaids()
    {
        if (_raidsLoaded) return _raids;
        _raidsLoaded = true;
        _raids = RaidDataLoader.Load(Data);
        _raids?.Validate(Load(), LoadScavenge());
        return _raids;
    }

    private static DataSource FindSource()
    {
        var dir = OS.HasFeature("template")
            ? Path.Combine(OS.GetExecutablePath().GetBaseDir(), "data")
            : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data"));
        return File.Exists(Path.Combine(dir, "resources.json"))
            ? DataSource.FromDirectory(dir)
            : DataSource.FromAssembly(typeof(GameDataPaths).Assembly);
    }
}
