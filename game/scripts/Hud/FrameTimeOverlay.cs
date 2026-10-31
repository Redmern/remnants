using System;
using Godot;

namespace Remnants.Game.Hud;

/// <summary>
/// Debug: a small frame-time readout in the top-right corner (F3, Settings → Display, or <c>--frame-overlay</c>): the last
/// frame, the mean / worst over the last second, the frames over 16.7 ms in that second and a running count since it
/// opened, and a 2-second graph with the 16.7 ms line. Frame time is wall-clock between frames (what a player feels), so
/// a hitch from a HUD rebuild shows as a spike. Drawn in code, one node, no allocations per frame.
/// </summary>
public partial class FrameTimeOverlay : CanvasLayer
{
    private const double Budget = 1000.0 / 60;
    private const int History = 120;

    private static FrameTimeOverlay? _instance;

    private readonly Graph _graph = new();

    public static void Attach(SceneTree tree)
    {
        if (_instance is not null) return;
        _instance = new FrameTimeOverlay { Name = "FrameTimeOverlay", Visible = GameSettings.ShowFrameTime || DebugCapture.HasFlag("--frame-overlay") };
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    public static void Toggle(SceneTree tree)
    {
        Attach(tree);
        SetShown(!_instance!.Visible);
    }

    public static void SetShown(bool on)
    {
        GameSettings.ShowFrameTime = on;
        if (_instance is null) return;
        _instance.Visible = on;
        _instance._graph.Reset();
    }

    public FrameTimeOverlay()
    {
        Layer = 127;
        ProcessMode = ProcessModeEnum.Always;
        _graph.MouseFilter = Control.MouseFilterEnum.Ignore;
        _graph.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _graph.OffsetLeft = -232;
        _graph.OffsetRight = -8;
        _graph.OffsetTop = 8;
        _graph.OffsetBottom = 86;
        AddChild(_graph);
    }

    public override void _Process(double delta)
    {
        if (Visible) _graph.Push(delta * 1000);
    }

    private sealed partial class Graph : Control
    {
        private readonly double[] _ms = new double[History];
        private int _at;
        private int _count;
        private long _over, _total;
        private string _line1 = "", _line2 = "";
        private double _lastText;
        private ulong _lastStamp;

        public void Reset()
        {
            Array.Clear(_ms);
            _at = _count = 0;
            _over = _total = 0;
            _lastStamp = 0;
        }

        public void Push(double processMs)
        {
            // Wall-clock between frames, not the process delta (which is capped and smoothed).
            var now = Time.GetTicksUsec();
            var ms = _lastStamp == 0 ? processMs : (now - _lastStamp) / 1000.0;
            _lastStamp = now;
            _ms[_at] = ms;
            _at = (_at + 1) % History;
            _count = Math.Min(_count + 1, History);
            _total++;
            if (ms > Budget + 0.5) _over++;
            var clock = now / 1e6;
            if (clock - _lastText >= 0.25)
            {
                _lastText = clock;
                double sum = 0, worst = 0;
                int n = 0, overSecond = 0;
                for (var i = 0; i < Math.Min(_count, 60); i++)
                {
                    var v = _ms[(_at - 1 - i + History) % History];
                    sum += v;
                    worst = Math.Max(worst, v);
                    if (v > Budget + 0.5) overSecond++;
                    n++;
                }
                var mean = n == 0 ? 0 : sum / n;
                _line1 = $"{ms,5:0.0} ms   mean {mean:0.0}   max {worst:0.0}";
                _line2 = $"{(mean > 0 ? 1000 / mean : 0):0} fps   >16.7: {overSecond}/s   total {_over}/{_total}";
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var font = Fonts.Numbers;
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.62f));
            var top = 30f;
            var h = Size.Y - top - 4;
            const double scale = 40; // the graph's top is 40 ms
            var budgetY = top + h - (float)(Budget / scale) * h;
            DrawLine(new Vector2(0, budgetY), new Vector2(Size.X, budgetY), Palette.Warn with { A = 0.6f }, 1);
            var w = Size.X / History;
            for (var i = 0; i < _count; i++)
            {
                var v = _ms[(_at - _count + i + History) % History];
                var bar = (float)Math.Min(v / scale, 1) * h;
                var color = v > 33.4 ? Palette.Danger : v > Budget + 0.5 ? Palette.Warn : Palette.Good;
                DrawRect(new Rect2((History - _count + i) * w, top + h - bar, Math.Max(1, w - 0.5f), bar), color);
            }
            DrawString(font, new Vector2(6, 13), _line1, HorizontalAlignment.Left, -1, 13, Palette.Text);
            DrawString(font, new Vector2(6, 26), _line2, HorizontalAlignment.Left, -1, 12, Palette.TextMuted);
        }
    }
}
