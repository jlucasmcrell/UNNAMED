using Godot;
using UNNAMED.Presentation.Capture;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation;

public partial class Main
{
    private ClipRecorder? _clipRecorder;
    private Label? _clipStatus;

    private void ToggleClipRecording()
    {
        if (_clipRecorder is { } running)
        {
            running.Stop();
            return;
        }
        try
        {
            _clipRecorder = new ClipRecorder(GetViewport(), ScreenshotsFolder);
            if (_clipStatus is null)
            {
                var layer = new CanvasLayer { Name = "ClipRecording", Layer = 90 };
                var root = new Control { Theme = UiKit.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
                root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                _clipStatus = UiKit.Words(string.Empty, 17, HudStyle.Wound);
                _clipStatus.SetAnchorsPreset(Control.LayoutPreset.TopRight);
                _clipStatus.GrowHorizontal = Control.GrowDirection.Begin;
                _clipStatus.HorizontalAlignment = HorizontalAlignment.Right;
                _clipStatus.OffsetRight = -32; _clipStatus.OffsetTop = 32;
                root.AddChild(_clipStatus); layer.AddChild(root); AddChild(layer);
            }
            _hud.Toast("Clip recording started. Press the clip key again to stop.", 3);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _hud.Toast($"Clip recording could not start: {e.Message}", 6);
        }
    }

    private void UpdateClipRecording()
    {
        if (_clipRecorder is not { } recorder) return;
        if (_session?.Simulation is null) recorder.Stop();
        if (recorder.Finished is { } result)
        {
            recorder.Stop();
            _clipStatus!.Visible = false;
            _clipRecorder = null;
            string message = result.Finalised && result.VideoFrames > 0
                ? $"Clip saved: {System.IO.Path.GetFileName(result.Path)}"
                : "The clip could not be saved.";
            if (result.Problem is { } problem) message += " " + problem;
            _hud?.Toast(message, result.Problem is null ? 4 : 8);
            GD.Print($"CLIP {System.Text.Json.JsonSerializer.Serialize(result)}");
            return;
        }
        _clipStatus!.Visible = true;
        _clipStatus.Text = recorder.Recording ? $"● REC {TimeSpan.FromSeconds(recorder.Seconds):mm\\:ss}" : "Saving clip…";
    }
}
