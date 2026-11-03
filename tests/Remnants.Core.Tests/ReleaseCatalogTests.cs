using Remnants.Core.Versions;

namespace Remnants.Core.Tests;

public class ReleaseCatalogTests
{
    private const string Zip = "remnants-windows-x86_64.zip";

    private static string Release(string tag, string published, string assets = $$"""[{"name":"{{Zip}}","browser_download_url":"https://x/TAG.zip","size":100}]""", bool draft = false) =>
        $$"""{"tag_name":"{{tag}}","name":"Build {{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":true,"published_at":"{{published}}","assets":{{assets.Replace("TAG", tag)}}}""";

    [Fact]
    public void Parses_tag_and_zip_asset_of_each_release()
    {
        var result = ReleaseCatalog.Parse($"[{Release("v0.3.2-win-preview", "2026-11-03T11:08:15Z")}]");

        Assert.Null(result.Error);
        var release = Assert.Single(result.Releases);
        Assert.Equal("v0.3.2-win-preview", release.Tag);
        Assert.Equal("https://x/v0.3.2-win-preview.zip", release.ZipUrl);
        Assert.Equal(100, release.ZipSize);
    }

    [Fact]
    public void Orders_newest_first_and_skips_drafts()
    {
        var json = $"[{Release("v0.1.0", "2026-11-02T17:06:47Z")},{Release("v0.3.0", "2026-11-03T08:12:26Z")}," +
                   $"{Release("v0.4.0", "2026-11-04T08:00:00Z", draft: true)},{Release("v0.2.0", "2026-11-02T17:21:25Z")}]";

        var tags = ReleaseCatalog.Parse(json).Releases.Select(r => r.Tag);

        Assert.Equal(new[] { "v0.3.0", "v0.2.0", "v0.1.0" }, tags);
    }

    [Fact]
    public void Empty_list_parses_to_no_releases_without_error()
    {
        var result = ReleaseCatalog.Parse("[]");

        Assert.Null(result.Error);
        Assert.Empty(result.Releases);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"message\":\"API rate limit exceeded\"}")]
    [InlineData("[{\"tag_name\":42}]")]
    [InlineData("[{\"tag_name\":\"v1\",\"assets\":")]
    public void Malformed_json_reports_an_error_instead_of_throwing(string json)
    {
        var result = ReleaseCatalog.Parse(json);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Releases);
    }

    [Fact]
    public void Release_without_a_zip_asset_is_listed_but_not_installable()
    {
        var assets = """[{"name":"notes.txt","browser_download_url":"https://x/notes.txt","size":3}]""";

        var release = Assert.Single(ReleaseCatalog.Parse($"[{Release("v0.1.0", "2026-11-02T17:06:47Z", assets)}]").Releases);

        Assert.Null(release.ZipUrl);
        Assert.False(release.Installable);
    }

    [Fact]
    public void Prefers_the_windows_zip_when_a_release_has_several()
    {
        var assets = """[{"name":"source.zip","browser_download_url":"https://x/source.zip","size":1},""" +
                     """{"name":"remnants-windows-x86_64.zip","browser_download_url":"https://x/win.zip","size":2}]""";

        var release = Assert.Single(ReleaseCatalog.Parse($"[{Release("v0.1.0", "2026-11-02T17:06:47Z", assets)}]").Releases);

        Assert.Equal("https://x/win.zip", release.ZipUrl);
    }

    [Theory]
    [InlineData("v0.3.2-win-preview", "0.3.2", true)]
    [InlineData("v0.3.2", "0.3.2", true)]
    [InlineData("0.3.2", "v0.3.2", true)]
    [InlineData("v0.3.1-win-preview", "0.3.2", false)]
    [InlineData("v0.3.20-win-preview", "0.3.2", false)]
    [InlineData("v0.3.2-win-preview", "", false)]
    public void Matches_a_tag_to_the_game_version(string tag, string version, bool expected) =>
        Assert.Equal(expected, ReleaseCatalog.IsVersion(tag, version));
}
