using Remnants.Core.Versions;

namespace Remnants.Core.Tests;

public sealed class ActiveVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "remnants-active-" + Guid.NewGuid().ToString("N"));

    private string _file => Path.Combine(_root, ActiveVersion.FileName);

    public ActiveVersionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Written_tag_reads_back()
    {
        Assert.True(ActiveVersion.Write(_file, "v0.6.0-win-preview"));

        Assert.Equal("v0.6.0-win-preview", ActiveVersion.Read(_file));
    }

    [Fact]
    public void Switching_back_overwrites_the_tag()
    {
        ActiveVersion.Write(_file, "v0.6.0");
        ActiveVersion.Write(_file, "v0.4.0");

        Assert.Equal("v0.4.0", ActiveVersion.Read(_file));
    }

    [Fact]
    public void Write_leaves_no_temp_files_behind()
    {
        ActiveVersion.Write(_file, "v0.6.0");

        Assert.Equal(new[] { _file }, Directory.GetFiles(_root));
    }

    [Fact]
    public void Missing_file_reads_as_null() => Assert.Null(ActiveVersion.Read(_file));

    [Fact]
    public void Blank_file_reads_as_null()
    {
        File.WriteAllText(_file, "  \n");

        Assert.Null(ActiveVersion.Read(_file));
    }

    [Fact]
    public void Unreadable_path_reads_as_null()
    {
        Directory.CreateDirectory(_file);

        Assert.Null(ActiveVersion.Read(_file));
    }

    [Fact]
    public void Failed_write_reports_false_instead_of_throwing()
    {
        Directory.CreateDirectory(_file);

        Assert.False(ActiveVersion.Write(_file, "v0.6.0"));
        Assert.Empty(Directory.GetFiles(_root));
    }
}
