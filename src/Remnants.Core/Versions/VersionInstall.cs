using System.IO.Compression;

namespace Remnants.Core.Versions;

/// <summary>
/// One downloaded build on disk: a folder per release tag holding the extracted zip. Extraction goes through a staging
/// folder so a half-finished install never looks installed, and rejects zips without the game exe or with entries that
/// would land outside the folder.
/// </summary>
public static class VersionInstall
{
    public const string ExeName = "remnants.exe";

    private static readonly char[] Unsafe = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }).ToArray();

    /// <summary>A single path segment for <paramref name="tag"/>: no separators, no <c>..</c>.</summary>
    public static string DirName(string tag)
    {
        var parts = tag.Split('/', '\\').Where(p => p.Length > 0 && p != "." && p != "..");
        var name = string.Join('_', parts);
        name = new string(name.Select(c => Array.IndexOf(Unsafe, c) >= 0 ? '_' : c).ToArray()).Trim('.', ' ');
        return name.Length == 0 ? "unknown" : name;
    }

    public static bool IsInstalled(string dir) => FindExe(dir) is not null;

    /// <summary>The game exe inside <paramref name="dir"/> (the zip wraps it in a top folder), or null.</summary>
    public static string? FindExe(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, ExeName, SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault()
            : null;

    /// <summary>Extracts <paramref name="zipPath"/> into <paramref name="dir"/>, replacing it. Throws <see cref="InvalidDataException"/> for a bad zip.</summary>
    public static void Extract(string zipPath, string dir)
    {
        var full = Path.GetFullPath(dir);
        var staging = full + ".partial";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var root = staging + Path.DirectorySeparatorChar;
                foreach (var entry in zip.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.Ordinal))
                        throw new InvalidDataException($"Zip entry '{entry.FullName}' points outside the install folder.");
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
            if (FindExe(staging) is null) throw new InvalidDataException($"The download has no {ExeName}.");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
            Directory.Move(staging, full);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }
}
