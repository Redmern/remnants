using System.Linq;
using Godot;

namespace Remnants.Game;

/// <summary>
/// Debug aid: <c>godot-mono --path game -- --colony --screenshot=/tmp/shot.png [--frames=90] [--quit]</c>
/// saves the viewport as PNG after N frames and optionally quits. Needs a real display (not --headless).
/// </summary>
public partial class DebugCapture : Node
{
    private string _path = "";
    private int _frames = 90;
    private bool _quit;

    public static bool HasFlag(string flag) => OS.GetCmdlineUserArgs().Contains(flag);

    private static string? Value(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"{name}="))?[(name.Length + 1)..];

    public static DebugCapture? FromCommandLine()
    {
        if (Value("--screenshot") is not { Length: > 0 } path) return null;
        return new DebugCapture
        {
            Name = "DebugCapture",
            _path = path,
            _frames = int.TryParse(Value("--frames"), out var f) ? f : 90,
            _quit = HasFlag("--quit"),
        };
    }

    public override void _Process(double delta)
    {
        if (--_frames > 0) return;
        SetProcess(false);
        var error = GetViewport().GetTexture().GetImage().SavePng(_path);
        GD.Print(error == Error.Ok ? $"Screenshot saved to {_path}" : $"Screenshot failed: {error}");
        if (_quit) GetTree().Quit();
    }
}
