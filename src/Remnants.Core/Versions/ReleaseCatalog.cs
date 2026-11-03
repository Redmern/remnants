using System.Text.Json;

namespace Remnants.Core.Versions;

/// <summary>One published build: its tag and, when it has one, the zip the switcher downloads.</summary>
public sealed record GameRelease(string Tag, string Name, DateTimeOffset PublishedAt, string? ZipUrl, long ZipSize)
{
    public bool Installable => ZipUrl is not null;
}

/// <summary>The releases read from the GitHub API, or why they could not be read.</summary>
public sealed record ReleaseList(IReadOnlyList<GameRelease> Releases, string? Error)
{
    public static ReleaseList Failed(string error) => new(Array.Empty<GameRelease>(), error);
}

/// <summary>
/// Reads the GitHub releases JSON (<c>GET /repos/{owner}/{repo}/releases</c>) into installable builds, newest first,
/// and matches release tags to the game's own version. Never throws on bad input: a malformed body comes back as an error.
/// </summary>
public static class ReleaseCatalog
{
    public const string ReleasesUrl = "https://api.github.com/repos/Redmern/remnants/releases";

    public static ReleaseList Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return ReleaseList.Failed(Message(doc.RootElement));
            var releases = new List<GameRelease>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
                var tag = item.GetProperty("tag_name").GetString();
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : tag;
                var published = item.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetDateTimeOffset()
                    : DateTimeOffset.MinValue;
                var (url, size) = PickZip(item);
                releases.Add(new GameRelease(tag, name, published, url, size));
            }
            return new ReleaseList(releases.OrderByDescending(r => r.PublishedAt).ToList(), null);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return ReleaseList.Failed("Could not read the release list.");
        }
    }

    /// <summary>True when <paramref name="tag"/> (e.g. <c>v0.3.2-win-preview</c>) names the game version <paramref name="version"/> (e.g. <c>0.3.2</c>).</summary>
    public static bool IsVersion(string tag, string version)
    {
        var v = Core(version);
        return v.Length > 0 && Core(tag) == v;
    }

    /// <summary>Whether the Switch button applies to <paramref name="release"/>: not the running version, and downloadable or already on disk.</summary>
    public static bool CanSwitch(GameRelease? release, string currentVersion, bool installed = false) =>
        release is not null && !IsVersion(release.Tag, currentVersion) && (release.Installable || installed);

    /// <summary>The release picker's text for <paramref name="release"/>, marking the running and already-downloaded builds.</summary>
    public static string Label(GameRelease release, string currentVersion, bool installed) =>
        IsVersion(release.Tag, currentVersion) ? $"{release.Tag} (current)"
        : installed ? $"{release.Tag} (installed)"
        : release.Installable ? release.Tag
        : $"{release.Tag} (no download)";

    private static string Core(string tagOrVersion)
    {
        var s = tagOrVersion.Trim().TrimStart('v', 'V');
        var dash = s.IndexOf('-');
        return dash < 0 ? s : s[..dash];
    }

    private static (string? Url, long Size) PickZip(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return (null, 0);
        var zips = assets.EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString(),
                Size: a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0))
            .Where(a => a.Url is not null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Name.Contains("windows", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return zips.Count == 0 ? (null, 0) : (zips[0].Url, zips[0].Size);
    }

    private static string Message(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? $"GitHub: {m.GetString()}"
            : "Could not read the release list.";
}
