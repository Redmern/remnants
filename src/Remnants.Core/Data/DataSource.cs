using System.Reflection;

namespace Remnants.Core.Data;

/// <summary>
/// The data/ folder as a set of files with '/'-separated relative paths ("items.json", "story/missions/act1.json"). Every
/// loader reads through one of these, so the dev folder and an exported build's embedded copy find the same files: there
/// is one discovery rule (the loaders), not one list per source.
/// </summary>
public abstract class DataSource
{
    /// <summary>The file's text, or null when it is not there.</summary>
    public abstract string? TryRead(string path);

    /// <summary>The paths of the .json files directly in <paramref name="folder"/> ("" = the root), sorted by name (ordinal).</summary>
    public abstract IReadOnlyList<string> List(string folder);

    /// <summary>Where this data comes from, for diagnostics and error messages.</summary>
    public abstract string Name { get; }

    public bool Exists(string path) => TryRead(path) is not null;

    public string Read(string path) => TryRead(path) ?? throw new GameDataException($"Missing data file '{path}' in {Name}.");

    /// <summary>
    /// A catalogue split over files: <paramref name="mainFile"/> first, then its extensions <c>&lt;stem&gt;.*.json</c> by
    /// name. None when the main file is missing.
    /// </summary>
    public IReadOnlyList<string> Catalogue(string mainFile)
    {
        if (!Exists(mainFile)) return [];
        var folder = mainFile.Contains('/') ? mainFile[..mainFile.LastIndexOf('/')] : "";
        var stem = Path.GetFileNameWithoutExtension(mainFile) + ".";
        return [mainFile, .. List(folder).Where(p => p != mainFile && FileName(p).StartsWith(stem, StringComparison.Ordinal))];
    }

    /// <summary>The texts of <see cref="Catalogue"/>, in order.</summary>
    public string[] ReadCatalogue(string mainFile) => Catalogue(mainFile).Select(Read).ToArray();

    /// <summary>A sub-folder as its own source ("story" → paths relative to data/story).</summary>
    public DataSource Sub(string folder) => new SubSource(this, folder.TrimEnd('/'));

    public static DataSource FromDirectory(string directory) => new DirectorySource(directory);

    /// <summary>
    /// The copy embedded in <paramref name="assembly"/>: every resource named <paramref name="prefix"/> + a relative path
    /// (see EmbeddedData.props; '\' from a Windows build counts as '/').
    /// </summary>
    public static DataSource FromAssembly(Assembly assembly, string prefix = EmbeddedPrefix) => new AssemblySource(assembly, prefix);

    /// <summary>Files from memory (tests): relative path → text.</summary>
    public static DataSource FromFiles(IReadOnlyDictionary<string, string> files, string name = "memory") => new MemorySource(files, name);

    /// <summary>The resource name prefix EmbeddedData.props gives every data file.</summary>
    public const string EmbeddedPrefix = "Remnants.Data.";

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static IReadOnlyList<string> InFolder(IEnumerable<string> paths, string folder)
    {
        var prefix = folder.Length == 0 ? "" : folder + "/";
        return paths.Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.Ordinal)
                                && !p[prefix.Length..].Contains('/'))
            .OrderBy(FileName, StringComparer.Ordinal).ToList();
    }

    private sealed class DirectorySource(string directory) : DataSource
    {
        public override string Name => $"'{directory}'";

        public override string? TryRead(string path)
        {
            var full = Path.Combine(directory, path);
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        public override IReadOnlyList<string> List(string folder)
        {
            var full = Path.Combine(directory, folder);
            if (!Directory.Exists(full)) return [];
            var prefix = folder.Length == 0 ? "" : folder + "/";
            return Directory.GetFiles(full, "*.json").Select(f => prefix + Path.GetFileName(f)).OrderBy(FileName, StringComparer.Ordinal).ToList();
        }
    }

    private sealed class AssemblySource : DataSource
    {
        private readonly Assembly _assembly;
        private readonly Dictionary<string, string> _resources; // relative path → resource name

        public AssemblySource(Assembly assembly, string prefix)
        {
            _assembly = assembly;
            _resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(n => n[prefix.Length..].Replace('\\', '/'), n => n);
        }

        public override string Name => $"the data embedded in {_assembly.GetName().Name}";

        public override string? TryRead(string path)
        {
            if (!_resources.TryGetValue(path, out var name)) return null;
            using var stream = _assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        public override IReadOnlyList<string> List(string folder) => InFolder(_resources.Keys, folder);
    }

    private sealed class MemorySource(IReadOnlyDictionary<string, string> files, string name) : DataSource
    {
        public override string Name => name;
        public override string? TryRead(string path) => files.GetValueOrDefault(path);
        public override IReadOnlyList<string> List(string folder) => InFolder(files.Keys, folder);
    }

    private sealed class SubSource(DataSource parent, string folder) : DataSource
    {
        public override string Name => $"{parent.Name}/{folder}";
        public override string? TryRead(string path) => parent.TryRead($"{folder}/{path}");

        public override IReadOnlyList<string> List(string sub) =>
            parent.List(sub.Length == 0 ? folder : $"{folder}/{sub}").Select(p => p[(folder.Length + 1)..]).ToList();
    }
}
