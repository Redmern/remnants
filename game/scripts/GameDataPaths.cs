using Godot;
using Remnants.Core.Data;

namespace Remnants.Game;

/// <summary>Finds the repo's data/ folder (one level above the Godot project) and loads it once.</summary>
public static class GameDataPaths
{
    private static GameData? _data;

    public static string DataDirectory =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data"));

    public static GameData Load() => _data ??= GameDataLoader.LoadFromDirectory(DataDirectory);
}
