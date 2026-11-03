using System.IO.Compression;
using Remnants.Core.Versions;

namespace Remnants.Core.Tests;

public sealed class VersionInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "remnants-versions-" + Guid.NewGuid().ToString("N"));

    public VersionInstallTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string MakeZip(params string[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(name);
        }
        return path;
    }

    [Fact]
    public void Extracted_release_is_installed_and_its_exe_found()
    {
        var dir = Path.Combine(_root, VersionInstall.DirName("v0.3.2-win-preview"));
        Assert.False(VersionInstall.IsInstalled(dir));

        VersionInstall.Extract(MakeZip("remnants/remnants.exe", "remnants/remnants.console.exe", "remnants/remnants.pck"), dir);

        Assert.True(VersionInstall.IsInstalled(dir));
        Assert.Equal(Path.Combine(dir, "remnants", "remnants.exe"), VersionInstall.FindExe(dir));
    }

    [Fact]
    public void Zip_without_the_game_exe_is_rejected_and_leaves_nothing_installed()
    {
        var dir = Path.Combine(_root, "v1");

        Assert.Throws<InvalidDataException>(() => VersionInstall.Extract(MakeZip("readme.txt"), dir));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Entries_escaping_the_install_folder_are_rejected()
    {
        var dir = Path.Combine(_root, "v1");

        Assert.Throws<InvalidDataException>(() => VersionInstall.Extract(MakeZip("remnants/remnants.exe", "../evil.txt"), dir));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Corrupt_zip_is_rejected()
    {
        var bad = Path.Combine(_root, "bad.zip");
        File.WriteAllText(bad, "not a zip");

        Assert.Throws<InvalidDataException>(() => VersionInstall.Extract(bad, Path.Combine(_root, "v1")));
    }

    [Theory]
    [InlineData("v0.3.2-win-preview", "v0.3.2-win-preview")]
    [InlineData("../../saves", "saves")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    public void Install_folder_name_is_a_single_safe_segment(string tag, string expected) =>
        Assert.Equal(expected, VersionInstall.DirName(tag));
}
