using Remnants.Core.Versions;

namespace Remnants.Core.Tests;

public sealed class ActiveVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "remnants-active-" + Guid.NewGuid().ToString("N"));

    private string ActiveFile => Path.Combine(_root, ActiveVersion.FileName);

    public ActiveVersionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Written_tag_reads_back()
    {
        Assert.True(ActiveVersion.Write(ActiveFile, "v0.6.0-win-preview"));

        Assert.Equal("v0.6.0-win-preview", ActiveVersion.Read(ActiveFile));
    }

    [Fact]
    public void Switching_back_overwrites_the_tag()
    {
        ActiveVersion.Write(ActiveFile, "v0.6.0");
        ActiveVersion.Write(ActiveFile, "v0.4.0");

        Assert.Equal("v0.4.0", ActiveVersion.Read(ActiveFile));
    }

    [Fact]
    public void Write_leaves_no_temp_files_behind()
    {
        ActiveVersion.Write(ActiveFile, "v0.6.0");

        Assert.Equal(new[] { ActiveFile }, Directory.GetFiles(_root));
    }

    [Fact]
    public void Missing_file_reads_as_null() => Assert.Null(ActiveVersion.Read(ActiveFile));

    [Fact]
    public void Blank_file_reads_as_null()
    {
        File.WriteAllText(ActiveFile, "  \n");

        Assert.Null(ActiveVersion.Read(ActiveFile));
    }

    [Fact]
    public void Unreadable_path_reads_as_null()
    {
        Directory.CreateDirectory(ActiveFile);

        Assert.Null(ActiveVersion.Read(ActiveFile));
    }

    [Fact]
    public void Failed_write_reports_false_instead_of_throwing()
    {
        Directory.CreateDirectory(ActiveFile);

        Assert.False(ActiveVersion.Write(ActiveFile, "v0.6.0"));
        Assert.Empty(Directory.GetFiles(_root));
    }

    private string Versions => Path.Combine(_root, "versions");

    private string OriginalExe => Path.Combine(_root, "install", VersionInstall.ExeName);

    private string Install(string tag)
    {
        var exe = Path.Combine(Versions, VersionInstall.DirName(tag), "remnants", VersionInstall.ExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");
        return exe;
    }

    private BootDecision Boot(string? active, string running, string? runningExe = null) =>
        ActiveVersion.DecideBoot(active, running, Versions, runningExe ?? OriginalExe);

    [Fact]
    public void Boot_redirects_to_the_installed_active_version()
    {
        var exe = Install("v0.6.0");

        Assert.Equal(new BootDecision(exe, null), Boot("v0.6.0", "0.5.2"));
    }

    [Fact]
    public void Boot_redirects_to_an_older_active_version_so_rollback_sticks()
    {
        var exe = Install("v0.4.0");

        Assert.Equal(new BootDecision(exe, null), Boot("v0.4.0", "0.5.2"));
    }

    [Fact]
    public void Boot_continues_when_the_active_version_is_the_running_one()
    {
        Install("v0.5.2-win-preview");

        Assert.Equal(BootDecision.Continue, Boot("v0.5.2-win-preview", "0.5.2"));
    }

    [Fact]
    public void Boot_continues_without_an_active_version() => Assert.Equal(BootDecision.Continue, Boot(null, "0.5.2"));

    [Fact]
    public void Boot_continues_when_the_active_version_is_not_installed() => Assert.Equal(BootDecision.Continue, Boot("v0.6.0", "0.5.2"));

    [Fact]
    public void Boot_continues_when_the_install_has_no_exe()
    {
        Directory.CreateDirectory(Path.Combine(Versions, "v0.6.0"));

        Assert.Equal(BootDecision.Continue, Boot("v0.6.0", "0.5.2"));
    }

    [Theory]
    [InlineData("garbage \u0001\u0002")]
    [InlineData("../../install")]
    public void Boot_continues_on_a_corrupt_tag(string tag)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OriginalExe)!);
        File.WriteAllText(OriginalExe, "");

        Assert.Equal(BootDecision.Continue, Boot(tag, "0.5.2"));
    }

    [Fact]
    public void Boot_continues_when_the_install_folder_cannot_be_listed()
    {
        if (OperatingSystem.IsWindows()) return;
        var exe = Install("v0.6.0");
        var locked = Path.GetDirectoryName(exe)!;
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            Assert.Equal(BootDecision.Continue, Boot("v0.6.0", "0.5.2"));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Handoff_copy_never_redirects_even_when_the_active_tag_differs()
    {
        var running = Install("v0.6.0");
        Install("v0.4.0");

        Assert.Null(Boot("v0.4.0", "0.6.0", running).RedirectExe);
    }

    [Fact]
    public void Handoff_copy_started_by_a_build_that_persists_nothing_becomes_active()
    {
        var running = Install("v0.6.0");
        Install("v0.4.0");

        Assert.Equal(new BootDecision(null, "v0.6.0"), Boot("v0.4.0", "0.6.0", running));
    }

    [Fact]
    public void Handoff_copy_already_active_changes_nothing()
    {
        var running = Install("v0.6.0");

        Assert.Equal(BootDecision.Continue, Boot("v0.6.0", "0.6.0", running));
    }

    [Fact]
    public void Exe_in_a_folder_merely_named_like_the_versions_folder_is_not_a_handoff_copy()
    {
        var exe = Install("v0.6.0");

        Assert.Equal(new BootDecision(exe, null), Boot("v0.6.0", "0.5.2", Path.Combine(_root, "versions2", "v0.5.2", VersionInstall.ExeName)));
    }

    [Fact]
    public void Rollback_then_reboot_of_the_rolled_back_copy_does_not_ping_pong()
    {
        var old = Install("v0.4.0");
        ActiveVersion.Write(ActiveFile, "v0.4.0");

        var original = Boot(ActiveVersion.Read(ActiveFile), "0.5.2");
        var handedOff = Boot(ActiveVersion.Read(ActiveFile), "0.4.0", original.RedirectExe);

        Assert.Equal(old, original.RedirectExe);
        Assert.Equal(BootDecision.Continue, handedOff);
    }
}
