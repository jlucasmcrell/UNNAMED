// UNNAMED Presentation - frame-time capture for the Phase-1 performance gate (RISK_REGISTER.md RK-02; PROTOTYPE.md §6.3)
// Godot presentation only

using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation.Perf;

/// <summary>
/// Records every frame: its wall time, the renderer's CPU and GPU time, and memory - and once a second, the worst frame's
/// time in process callbacks, as Godot reports it. Writes <c>frames.csv</c> and a <c>summary.json</c> with, per segment, the frame-time distribution, 1% and
/// 0.1% lows, hitches, RAM and VRAM peaks, and the machine it ran on. The owner reads the numbers; this code draws no
/// conclusion beyond stating whether the 1% low held 60 FPS.
/// </summary>
public sealed class FrameStats
{
    private readonly List<Sample> _samples = new();
    private readonly List<(string Segment, double Ms)> _worstProcess = new();
    private readonly Rid _viewport;
    private long _workingSetBytes;
    private int _frame;
    private bool _skipNext;
    private double _lastProcessMs = double.NaN;
    private bool _screenshotPending;

    public FrameStats(Viewport viewport)
    {
        _viewport = viewport.GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewport, true);
    }

    public string Segment { get; set; } = "default";

    public int Count => _samples.Count;

    /// <summary>Drop the next sample: a screenshot stalls the GPU, and that stall is the tool's, not the game's.</summary>
    public void SkipNext() => _skipNext = _screenshotPending = true;

    public void Record(double delta)
    {
        // Godot's process-time monitor holds the worst frame's process time of the last second, refreshed once a second: each new
        // figure is kept once - but not the first, from before the capture, nor the one that takes in a screenshot's frame.
        double processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
        if (processMs != _lastProcessMs)
        {
            bool first = double.IsNaN(_lastProcessMs);
            if (!first && !_screenshotPending)
                _worstProcess.Add((Segment, processMs));
            if (!first)
                _screenshotPending = false;
            _lastProcessMs = processMs;
        }
        if (_skipNext)
        {
            _skipNext = false;
            return;
        }
        // The process working set is expensive to read; twice a second is plenty for a peak.
        if (_frame++ % 30 == 0)
            _workingSetBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        _samples.Add(new Sample(
            Segment,
            delta * 1000,
            processMs,
            RenderingServer.ViewportGetMeasuredRenderTimeCpu(_viewport),
            RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport),
            (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic),
            _workingSetBytes,
            (long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed)));
    }

    /// <summary>Write the capture. Returns the summary text.</summary>
    public string Write(string directory, IReadOnlyDictionary<string, string> notes)
    {
        Directory.CreateDirectory(directory);
        var csv = new StringBuilder("segment,frame,frame_ms,process_ms,render_cpu_ms,render_gpu_ms,static_mb,working_set_mb,vram_mb\n");
        for (int i = 0; i < _samples.Count; i++)
        {
            var s = _samples[i];
            csv.Append(CultureInfo.InvariantCulture,
                $"{s.Segment},{i},{s.FrameMs:0.###},{s.ProcessMs:0.###},{s.RenderCpuMs:0.###},{s.RenderGpuMs:0.###},{Mb(s.StaticBytes):0.#},{Mb(s.WorkingSetBytes):0.#},{Mb(s.VramBytes):0.#}\n");
        }
        File.WriteAllText(Path.Combine(directory, "frames.csv"), csv.ToString());

        var summary = new Dictionary<string, object>
        {
            ["machine"] = Machine(),
            ["notes"] = notes,
            ["segments"] = _samples.GroupBy(s => s.Segment).ToDictionary(g => g.Key, g => (object)Summarise(g.ToList(),
                _worstProcess.Where(w => w.Segment == g.Key).Select(w => w.Ms).OrderBy(v => v).ToList())),
        };
        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, "summary.json"), json);
        return json;
    }

    private static Dictionary<string, object> Summarise(List<Sample> frames, List<double> worstProcessEachSecond)
    {
        var ms = frames.Select(f => f.FrameMs).OrderBy(v => v).ToList();
        double median = Percentile(ms, 50);
        double Low(double fraction)
        {
            int n = Math.Max(1, (int)Math.Ceiling(ms.Count * fraction));
            return 1000.0 / ms.Skip(ms.Count - n).Average();
        }
        double duration = ms.Sum() / 1000.0;
        return new Dictionary<string, object>
        {
            ["frames"] = frames.Count,
            ["seconds"] = Math.Round(duration, 2),
            ["average_fps"] = Math.Round(frames.Count / duration, 1),
            ["one_percent_low_fps"] = Math.Round(Low(0.01), 1),
            ["point_one_percent_low_fps"] = Math.Round(Low(0.001), 1),
            ["minimum_fps"] = Math.Round(1000.0 / ms[^1], 1),
            ["frame_ms"] = Distribution(ms),
            ["frames_within_16_7_ms_percent"] = Math.Round(100.0 * ms.Count(v => v <= 1000.0 / 60) / ms.Count, 2),
            ["hitches_over_33_ms"] = ms.Count(v => v > 1000.0 / 30),
            ["hitches_over_twice_median"] = ms.Count(v => v > 2 * median),
            ["process_ms_worst_each_second"] = worstProcessEachSecond.Count > 0 ? Distribution(worstProcessEachSecond) : new Dictionary<string, double>(),
            ["render_cpu_ms"] = Distribution(frames.Select(f => f.RenderCpuMs).OrderBy(v => v).ToList()),
            ["render_gpu_ms"] = Distribution(frames.Select(f => f.RenderGpuMs).OrderBy(v => v).ToList()),
            ["static_memory_peak_mb"] = Math.Round(Mb(frames.Max(f => f.StaticBytes)), 1),
            ["working_set_peak_mb"] = Math.Round(Mb(frames.Max(f => f.WorkingSetBytes)), 1),
            ["vram_peak_mb"] = Math.Round(Mb(frames.Max(f => f.VramBytes)), 1),
            ["sustained_60_fps_by_one_percent_low"] = Low(0.01) >= 60,
        };
    }

    private static Dictionary<string, double> Distribution(List<double> sorted) => new()
    {
        ["mean"] = Math.Round(sorted.Average(), 3),
        ["p50"] = Math.Round(Percentile(sorted, 50), 3),
        ["p95"] = Math.Round(Percentile(sorted, 95), 3),
        ["p99"] = Math.Round(Percentile(sorted, 99), 3),
        ["max"] = Math.Round(sorted[^1], 3),
    };

    private static double Percentile(List<double> sorted, double percent) =>
        sorted[Math.Clamp((int)Math.Ceiling(percent / 100 * sorted.Count) - 1, 0, sorted.Count - 1)];

    private static double Mb(long bytes) => bytes / 1024.0 / 1024.0;

    private static Dictionary<string, object> Machine() => new()
    {
        ["host"] = System.Environment.MachineName,
        ["gpu"] = RenderingServer.GetVideoAdapterName(),
        ["gpu_vendor"] = RenderingServer.GetVideoAdapterVendor(),
        ["graphics_api"] = RenderingServer.GetVideoAdapterApiVersion(),
        ["cpu"] = OS.GetProcessorName(),
        ["cpu_threads"] = OS.GetProcessorCount(),
        ["os"] = $"{OS.GetName()} {OS.GetVersion()}",
        ["godot"] = Engine.GetVersionInfo()["string"].AsString(),
        ["renderer"] = ProjectSettings.GetSetting("rendering/renderer/rendering_method").AsString(),
        ["resolution"] = DisplayServer.WindowGetSize().ToString(),
        ["vsync"] = DisplayServer.WindowGetVsyncMode().ToString(),
        ["captured_utc"] = DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture),
    };

    private readonly record struct Sample(
        string Segment, double FrameMs, double ProcessMs, double RenderCpuMs, double RenderGpuMs, long StaticBytes, long WorkingSetBytes, long VramBytes);
}
