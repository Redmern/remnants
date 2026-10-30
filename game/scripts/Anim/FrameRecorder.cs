using System.Linq;
using Godot;

namespace Remnants.Game.Anim;

/// <summary>
/// Debug: records the viewport as a numbered PNG sequence and (with <c>--gif=PATH</c>, if ffmpeg is on PATH) a GIF.
/// <c>--record=DIR</c> turns it on; <c>--record-every=K</c> keeps every Kth frame (default 2). A demo script calls
/// <see cref="Begin"/> / <see cref="Finish"/> around its moment (<c>--anim-demo</c>); otherwise <c>--record-start=N</c> and
/// <c>--record-frames=M</c> record frames N..N+M of any scene (colony workers). <c>--quit</c> quits when done. Pass Godot's
/// <c>--fixed-fps 30</c> so the clips play at their speed however long a frame takes to save:
/// <code>godot-mono --display-driver wayland --disable-vsync --fixed-fps 30 --resolution 960x540 --path game -- --scavenge --anim-demo=survivor --record=/tmp/anim --gif=/tmp/anim.gif --quit</code>
/// </summary>
public partial class FrameRecorder : Node
{
    private static FrameRecorder? _instance;
    private string _dir = "";
    private int _every = 2, _start = -1, _frames = -1, _frame, _saved;
    private bool _recording, _done;

    public static bool Requested => DebugCapture.Value("--record") is { Length: > 0 };

    public static FrameRecorder? FromCommandLine()
    {
        if (DebugCapture.Value("--record") is not { Length: > 0 } dir) return null;
        _instance = new FrameRecorder
        {
            Name = "FrameRecorder",
            _dir = dir,
            _every = int.TryParse(DebugCapture.Value("--record-every"), out var k) ? Mathf.Max(1, k) : 2,
            _start = int.TryParse(DebugCapture.Value("--record-start"), out var s) ? s : -1,
            _frames = int.TryParse(DebugCapture.Value("--record-frames"), out var f) ? f : -1,
        };
        DirAccess.MakeDirRecursiveAbsolute(dir);
        // A fresh sequence: old frames would end up in the GIF.
        foreach (var old in DirAccess.GetFilesAt(dir).Where(n => n.StartsWith("frame_") && n.EndsWith(".png")))
            DirAccess.RemoveAbsolute($"{dir}/{old}");
        return _instance;
    }

    /// <summary>Starts recording (a demo script at its moment). No-op without <c>--record</c>.</summary>
    public static void Begin()
    {
        if (_instance is { _done: false } r) r._recording = true;
    }

    /// <summary>Stops, writes the GIF and quits if asked.</summary>
    public static void Finish() => _instance?.Done();

    public override void _Process(double delta)
    {
        _frame++;
        if (!_recording && !_done && _start >= 0 && _frame >= _start) _recording = true;
        if (!_recording) return;
        if (_frame % _every == 0)
        {
            GetViewport().GetTexture().GetImage().SavePng($"{_dir}/frame_{_saved:0000}.png");
            _saved++;
        }
        if (_frames > 0 && _saved * _every >= _frames) Done();
    }

    private void Done()
    {
        if (_done) return;
        _done = true;
        _recording = false;
        GD.Print($"[record] {_saved} frames saved to {_dir}");
        if (DebugCapture.Value("--gif") is { Length: > 0 } gif && _saved > 0)
        {
            // Frames come at --fixed-fps (30 unless --record-fps says otherwise).
            var fps = Mathf.Max(1, (int.TryParse(DebugCapture.Value("--record-fps"), out var f) ? f : 30) / _every);
            var exit = OS.Execute("ffmpeg", ["-y", "-loglevel", "error", "-framerate", fps.ToString(), "-i", $"{_dir}/frame_%04d.png",
                "-vf", "scale=720:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=160[p];[b][p]paletteuse=dither=bayer", gif]);
            GD.Print(exit == 0 ? $"[record] GIF saved to {gif}" : $"[record] ffmpeg failed ({exit}); the frames are in {_dir}");
        }
        if (DebugCapture.HasFlag("--quit")) GetTree().Quit();
    }
}
