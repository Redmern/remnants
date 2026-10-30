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

    public static string? Value(string name) =>
        OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith($"{name}="))?[(name.Length + 1)..];

    private static bool _attached;

    /// <summary>Once per run: puts the capture (if asked for) on the tree root, so it survives scene changes.</summary>
    public static void Attach(SceneTree tree)
    {
        if (_attached) return;
        _attached = true;
        if (FromCommandLine() is { } capture) tree.Root.CallDeferred(Node.MethodName.AddChild, capture);
    }

    public static DebugCapture? FromCommandLine()
    {
        // --fog-demo saves its own still at the ambush (ScavengeScreen.FogDemo).
        if (Value("--screenshot") is not { Length: > 0 } path || HasFlag("--fog-demo")) return null;
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
