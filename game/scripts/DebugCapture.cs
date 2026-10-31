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
        if (Anim.FrameRecorder.FromCommandLine() is { } recorder) tree.Root.CallDeferred(Node.MethodName.AddChild, recorder);
        if (HasFlag("--anim-selftest")) tree.Root.CallDeferred(Node.MethodName.AddChild, new Anim.AnimSelfTest { Name = "AnimSelfTest" });
        if (PerfMonitor.FromCommandLine() is { } perf) tree.Root.CallDeferred(Node.MethodName.AddChild, perf);
        if (Hud.Help.HelpSelfTest.FromCommandLine() is { } help) tree.Root.CallDeferred(Node.MethodName.AddChild, help);
        if (FromCommandLine() is { } capture) tree.Root.CallDeferred(Node.MethodName.AddChild, capture);
    }

    public static DebugCapture? FromCommandLine()
    {
        // --fog-demo, --tech-demo, --loot-demo and --scavenge --gear-demo / --medicine-demo save their own stills at the right moment (ScavengeScreen.*Demo).
        if (Value("--screenshot") is not { Length: > 0 } path || HasFlag("--fog-demo") || Value("--tech-demo") is { Length: > 0 } || Value("--loot-demo") is { Length: > 0 }
            || (HasFlag("--gear-demo") || HasFlag("--medicine-demo")) && HasFlag("--scavenge")) return null;
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
