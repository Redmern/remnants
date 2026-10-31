using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Remnants.Game;

/// <summary>
/// Debug aid: <c>-- --perf-log [--perf-seconds=N] [--perf-every=S]</c> logs Godot's performance monitors (frame time,
/// draw calls, primitives, objects in frame, node and object counts, static / managed / video memory) every few seconds
/// as <c>[perf]</c> lines, and a summary after N seconds (then quits). The first two seconds are left out as warm-up.
/// Run with <c>--disable-vsync</c> to see the uncapped frame time. Numbers in docs/PERFORMANCE.md.
/// </summary>
public partial class PerfMonitor : Node
{
    private const double WarmUp = 2;
    private double _seconds, _every = 5, _clock, _nextLog;
    private readonly List<double> _frames = new();
    private readonly List<double> _window = new();
    private readonly List<double> _draws = new();
    private double _firstStatic = -1, _firstManaged = -1, _firstNodes = -1;

    private static PerfMonitor? _active;
    private readonly Dictionary<string, double> _spent = new();

    /// <summary>
    /// Times a block for the hitch lines (<c>using var _ = PerfMonitor.Measure("sync");</c>): the frame's time per label
    /// is printed with each hitch. Free when the monitor is off.
    /// </summary>
    public static Span Measure(string label) => new(_active is null ? null : label);

    public readonly struct Span : IDisposable
    {
        private readonly string? _label;
        private readonly long _start;

        public Span(string? label)
        {
            _label = label;
            _start = label is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_label is null || _active is not { } monitor) return;
            monitor._spent[_label] = monitor._spent.GetValueOrDefault(_label) + System.Diagnostics.Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        }
    }

    public static PerfMonitor? FromCommandLine()
    {
        if (!DebugCapture.HasFlag("--perf-log")) return null;
        return new PerfMonitor
        {
            Name = "PerfMonitor",
            ProcessMode = ProcessModeEnum.Always,
            _seconds = double.TryParse(DebugCapture.Value("--perf-seconds"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0,
            _hitches = DebugCapture.HasFlag("--perf-hitches"),
            _every = double.TryParse(DebugCapture.Value("--perf-every"), System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : 5,
        };
    }

    public override void _Ready()
    {
        _active = this;
        _nextLog = WarmUp + _every;
    }

    public override void _ExitTree() => _active = null;

    private ulong _lastUsec;
    private (int, int, int) _gcs;
    private long _tick;
    private bool _hitches;

    public override void _Process(double delta)
    {
        // Wall clock between frames: Godot's delta is smoothed and would hide hitches.
        var now = Time.GetTicksUsec();
        var ms = _lastUsec == 0 ? delta * 1000 : (now - _lastUsec) / 1000.0;
        _lastUsec = now;
        _clock += ms / 1000;
        var gcs = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var tick = Colony.ColonySession.Current?.Sim.State.Tick ?? -1;
        // --perf-hitches: one line per frame over the 60 Hz budget, with the GCs and colony ticks since the last frame.
        if (_hitches && _clock >= WarmUp && ms > 1000 / 60.0)
            GD.Print($"[perf] hitch t={_clock:F2}s {ms:F1}ms gc0+{gcs.Item1 - _gcs.Item1} gc1+{gcs.Item2 - _gcs.Item2} gc2+{gcs.Item3 - _gcs.Item3} " +
                $"ticks+{tick - _tick} draws={Monitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0} " +
                $"pipelines={Monitor(Performance.Monitor.PipelineCompilationsDraw) + Monitor(Performance.Monitor.PipelineCompilationsSpecialization):F0}" +
                string.Concat(_spent.Select(kv => $" {kv.Key}={kv.Value:F1}ms")));
        _spent.Clear();
        _gcs = gcs;
        _tick = tick;
        if (_clock < WarmUp) return;
        _frames.Add(ms);
        _window.Add(ms);
        _draws.Add(Monitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        if (_firstStatic < 0)
        {
            _firstStatic = Mb(Monitor(Performance.Monitor.MemoryStatic));
            _firstManaged = Mb(GC.GetTotalMemory(false));
            _firstNodes = Monitor(Performance.Monitor.ObjectNodeCount);
        }
        if (_clock >= _nextLog)
        {
            _nextLog += _every;
            GD.Print($"[perf] t={_clock:F0}s {Line(_window)} draws={Monitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0} " +
                $"prims={Monitor(Performance.Monitor.RenderTotalPrimitivesInFrame):F0} objs={Monitor(Performance.Monitor.RenderTotalObjectsInFrame):F0} " +
                $"nodes={Monitor(Performance.Monitor.ObjectNodeCount):F0} objects={Monitor(Performance.Monitor.ObjectCount):F0} " +
                $"orphans={Monitor(Performance.Monitor.ObjectOrphanNodeCount):F0} static={Mb(Monitor(Performance.Monitor.MemoryStatic)):F0}MB " +
                $"managed={Mb(GC.GetTotalMemory(false)):F0}MB vram={Mb(Monitor(Performance.Monitor.RenderVideoMemUsed)):F0}MB " +
                $"process={Monitor(Performance.Monitor.TimeProcess) * 1000:F2}ms");
            _window.Clear();
        }
        if (_seconds > 0 && _clock >= _seconds + WarmUp)
        {
            SetProcess(false);
            GD.Print($"[perf] summary {_clock - WarmUp:F0}s: {Line(_frames)} draws mean={_draws.Average():F0} max={_draws.Max():F0} " +
                $"nodes {_firstNodes:F0}->{Monitor(Performance.Monitor.ObjectNodeCount):F0} " +
                $"static {_firstStatic:F0}->{Mb(Monitor(Performance.Monitor.MemoryStatic)):F0}MB managed {_firstManaged:F0}->{Mb(GC.GetTotalMemory(false)):F0}MB");
            GetTree().Quit();
        }
    }

    private static double Monitor(Performance.Monitor m) => Performance.GetMonitor(m);
    private static double Mb(double bytes) => bytes / (1024 * 1024);

    private static string Line(List<double> ms)
    {
        if (ms.Count == 0) return "frames=0";
        var sorted = ms.OrderBy(x => x).ToList();
        // Frames over a 60 Hz budget (16.7 ms) and two of them: what a player sees as hitches.
        return $"fps={1000 / ms.Average():F0} frame_ms mean={ms.Average():F2} p95={sorted[(int)(sorted.Count * 0.95)]:F2} " +
            $"p99={sorted[(int)(sorted.Count * 0.99)]:F2} max={sorted[^1]:F2} over16.7={ms.Count(x => x > 1000 / 60.0)} over20={ms.Count(x => x > 20)} over33={ms.Count(x => x > 1000 / 30.0)}";
    }
}
