namespace Remnants.Core.Versions;

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

    private static bool IsFileError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
