namespace Remnants.Core.Versions;

/// <summary>What a booting build should do: start another exe and quit, and/or record a tag as the active version.</summary>
public sealed record BootDecision(string? RedirectExe, string? PersistTag)
{
    public static readonly BootDecision Continue = new(null, null);
}

/// <summary>
/// The release tag the player last switched to, kept in a small text file so the next boot runs that build. Reads and
/// writes never throw: a version file that cannot be used must not stop the game from starting.
/// </summary>
public static class ActiveVersion
{
    public const string FileName = "active_version.txt";

    /// <summary>The persisted tag, or null when the file is missing, blank or unreadable.</summary>
    public static string? Read(string path)
    {
        try
        {
            var tag = File.ReadAllText(path).Trim();
            return tag.Length == 0 ? null : tag;
        }
        catch (Exception e) when (IsFileError(e))
        {
            return null;
        }
    }

    /// <summary>Persists <paramref name="tag"/> through a temp file so a crash cannot leave a half-written one. False when it failed.</summary>
    public static bool Write(string path, string tag)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, tag);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (IsFileError(e))
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (IsFileError(cleanup)) { }
            return false;
        }
    }

    /// <summary>
    /// Decides what a booting build does. A copy inside <paramref name="versionsRoot"/> was started by a switch, so it never
    /// redirects, whatever the file says: that rules out ping-pong, and builds from before this file existed hand off without
    /// persisting anything, so such a copy records itself as active. Any other build redirects to the active version when that
    /// is a different version with an exe on disk, and otherwise carries on.
    /// </summary>
    public static BootDecision DecideBoot(string? activeTag, string runningVersion, string versionsRoot, string runningExe)
    {
        if (HandoffTag(versionsRoot, runningExe) is { } own)
            return activeTag is not null && VersionInstall.DirName(activeTag) == own ? BootDecision.Continue : new BootDecision(null, own);
        if (activeTag is null || ReleaseCatalog.IsVersion(activeTag, runningVersion)) return BootDecision.Continue;
        try
        {
            return VersionInstall.FindExe(Path.Combine(versionsRoot, VersionInstall.DirName(activeTag))) is { } exe
                ? new BootDecision(exe, null)
                : BootDecision.Continue;
        }
        catch (Exception e) when (IsFileError(e))
        {
            return BootDecision.Continue;
        }
    }

    /// <summary>The install folder name when <paramref name="exe"/> sits inside one under <paramref name="versionsRoot"/>, else null.</summary>
    private static string? HandoffTag(string versionsRoot, string exe)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(versionsRoot)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(exe);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        var end = full.IndexOf(Path.DirectorySeparatorChar, root.Length);
        return end < 0 ? null : full[root.Length..end];
    }

    private static bool IsFileError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
