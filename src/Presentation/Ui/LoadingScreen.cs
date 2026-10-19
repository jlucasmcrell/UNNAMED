// UNNAMED Presentation - the launch screen: a progress bar over the real load stages, and a watchdog that says so when one stalls
// Godot presentation only: this file draws; it never touches the world. Nothing here sends anything anywhere.

using System.Diagnostics;
using Godot;

namespace UNNAMED.Presentation.Ui;

/// <summary>
/// Start builds the whole world on the main thread before the first frame, so the scene tree cannot draw a bar: this paints straight
/// onto the root viewport's canvas and forces a frame at each <see cref="Stage"/>. The watchdog runs on its own thread, because the
/// stall it exists to report is the main thread being blocked: it can only raise a message box, which needs no frame.
/// </summary>
public sealed class LoadingScreen
{
    private const double IdleSeconds = 45;     // no stage for this long and no CPU used: the load is hung, not slow
    private const double SlowSeconds = 240;    // no stage for this long even though the CPU is busy: say so once

    private readonly Viewport _viewport;
    private readonly Rid _canvas;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private System.Threading.Timer? _watchdog;
    private string _stage = "Starting";
    private double _fraction;
    private double _stageAt;
    private TimeSpan _cpuAtStage;
    private bool _warned;
    private bool _ended;

    public LoadingScreen(Viewport viewport)
    {
        _viewport = viewport;
        _canvas = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(_canvas, viewport.FindWorld2D().Canvas);
        RenderingServer.CanvasItemSetZIndex(_canvas, 4000);
        _viewport.Disable3D = true;            // only this is drawn while the world is half built
        _cpuAtStage = Process.GetCurrentProcess().TotalProcessorTime;
        _watchdog = new System.Threading.Timer(_ => Watch(), null, 5000, 5000);
        Stage("Starting", 0);
    }

    /// <summary>The load has reached <paramref name="stage"/>, <paramref name="fraction"/> (0 to 1) of the way through the whole load.</summary>
    public void Stage(string stage, double fraction)
    {
        lock (_gate)
        {
            if (_ended)
                return;
            _stage = stage;
            _fraction = fraction;
            _stageAt = _clock.Elapsed.TotalSeconds;
            _cpuAtStage = Process.GetCurrentProcess().TotalProcessorTime;
            _warned = false;
        }
        GD.Print($"UNNAMED load stage: {stage} ({fraction * 100:0}%) at {_clock.Elapsed.TotalSeconds:0.0} s");
        Paint();
        RenderingServer.ForceDraw();
    }

    /// <summary>The load is over: the bar goes and the world is drawn again.</summary>
    public void End()
    {
        lock (_gate)
        {
            if (_ended)
                return;
            _ended = true;
        }
        _watchdog?.Dispose();
        _watchdog = null;
        RenderingServer.FreeRid(_canvas);
        _viewport.Disable3D = false;
    }

    private void Paint()
    {
        var size = _viewport.GetVisibleRect().Size;
        var heading = HudStyle.Caps;
        var body = HudStyle.Body;
        RenderingServer.CanvasItemClear(_canvas);
        RenderingServer.CanvasItemAddRect(_canvas, new Rect2(Vector2.Zero, size), HudStyle.Coal);
        float scale = Math.Max(1f, size.Y / 1080f);
        float barWidth = Math.Min(size.X * 0.5f, 900f * scale), barHeight = 8f * scale;
        float left = (size.X - barWidth) / 2, top = size.Y * 0.62f;
        heading.DrawString(_canvas, new Vector2(0, size.Y * 0.4f), "OTHERREACH", HorizontalAlignment.Center, size.X, (int)(64 * scale), HudStyle.Bone);
        double fraction;
        string stage;
        lock (_gate)
        {
            fraction = _fraction;
            stage = _stage;
        }
        RenderingServer.CanvasItemAddRect(_canvas, new Rect2(left - 2, top - 2, barWidth + 4, barHeight + 4), HudStyle.Iron);
        RenderingServer.CanvasItemAddRect(_canvas, new Rect2(left, top, barWidth, barHeight), HudStyle.Track);
        RenderingServer.CanvasItemAddRect(_canvas, new Rect2(left, top, (float)(barWidth * Math.Clamp(fraction, 0, 1)), barHeight), HudStyle.Brass);
        body.DrawString(_canvas, new Vector2(left, top + 36 * scale), stage, HorizontalAlignment.Left, barWidth, (int)(22 * scale), HudStyle.Ash);
        body.DrawString(_canvas, new Vector2(left, top + 36 * scale), $"{fraction * 100:0}%", HorizontalAlignment.Right, barWidth, (int)(22 * scale), HudStyle.Muted);
    }

    private void Watch()
    {
        string stage;
        double waited;
        bool idle;
        lock (_gate)
        {
            if (_ended || _warned)
                return;
            waited = _clock.Elapsed.TotalSeconds - _stageAt;
            idle = Process.GetCurrentProcess().TotalProcessorTime - _cpuAtStage < TimeSpan.FromSeconds(0.02 * waited);
            if (waited < (idle ? IdleSeconds : SlowSeconds))
                return;
            _warned = true;
            stage = _stage;
        }
        string log = Main.LogPath;
        string why = idle
            ? $"The game has stopped at \"{stage}\" for {waited:0} seconds and is using no processor time, so it is stuck rather than slow."
            : $"The game has been at \"{stage}\" for {waited:0} seconds. It is still working, but this is much slower than usual.";
        GD.PushError($"UNNAMED load watchdog: {why}");
        OS.Alert($"{why}\n\nIf it does not move on, close the game and start it again. If it stops at the same place, send the log: {log}", "Otherreach is not loading");
    }
}
