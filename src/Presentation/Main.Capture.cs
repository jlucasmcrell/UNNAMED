// Opt-in shared capture handshake. Normal launches never enter this path.
using System.Globalization;
using System.Text.Json;
using Godot;

namespace UNNAMED.Presentation;

public partial class Main
{
    private JsonElement? _captureRequest;
    private string? _captureDirectory;
    private bool _captureReady, _capturePublishing, _captureRunning;
    private Vector3? _captureLastCamera;
    private int _captureStable;

    private void ConfigureCapture()
    {
        if (!_options.TryGetValue("--capture-session", out string? file)) return;
        var request = JsonDocument.Parse(File.ReadAllText(file)).RootElement.Clone();
        if (request.GetProperty("protocol").GetInt32() != 1 ||
            !Guid.TryParse(request.GetProperty("token").GetString(), out _))
            throw new InvalidDataException("Invalid capture protocol/token");
        double x = request.GetProperty("x").GetDouble(), z = request.GetProperty("z").GetDouble();
        if (!double.IsFinite(x) || !double.IsFinite(z)) throw new InvalidDataException("Invalid capture placement");
        _captureDirectory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        _captureRequest = request;
        // NewGame's supported spawn override, never a scripted walk or a state teleport.
        _options["--stand"] = string.Create(CultureInfo.InvariantCulture, $"{x},{z}");
    }

    private void CaptureView()
    {
        if (_captureRequest is not { } request) return;
        _camera.Yaw = Mathf.DegToRad((float)request.GetProperty("yaw").GetDouble());
        _camera.Pitch = (float)request.GetProperty("pitch").GetDouble();
        _camera.TargetDistance = (float)request.GetProperty("distance").GetDouble();
    }

    private bool CaptureSignal(string name)
    {
        string path = Path.Combine(_captureDirectory!, name + ".json");
        if (!File.Exists(path)) return false;
        var signal = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        return signal.GetProperty("token").GetString() == _captureRequest!.Value.GetProperty("token").GetString();
    }

    private async void TickCapture()
    {
        if (_captureRequest is not { } request || _session?.Simulation is null) return;
        if (CaptureSignal("stop")) { GetTree().Quit(); return; }
        if (_captureReady)
        {
            if (!_captureRunning && CaptureSignal("begin"))
            {
                _captureRunning = true;
                WriteCapture("running", new { token = request.GetProperty("token").GetString(), utc = DateTimeOffset.UtcNow });
            }
            return;
        }
        if (_capturePublishing || _veil is { Visible: true } || GetViewport().Disable3D ||
            GetViewport().GetCamera3D() != _camera.Camera || Screen is not null) return;
        Vector3 camera = _camera.Camera.GlobalPosition;
        _captureStable = _captureLastCamera is { } last && last.DistanceTo(camera) < 0.002f ? _captureStable + 1 : 0;
        _captureLastCamera = camera;
        if (_captureStable < 3) return; // Geometric convergence, not a world-load timer.
        _capturePublishing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Vector3 feet = _avatar.Position;
        double dx = feet.X - request.GetProperty("x").GetDouble(), dz = feet.Z - request.GetProperty("z").GetDouble();
        if (Math.Sqrt(dx * dx + dz * dz) > 0.5)
        {
            WriteCapture("failed", new { reason = "Spawn differs from requested placement", actual = new[] { feet.X, feet.Y, feet.Z } });
            GetTree().Quit(1);
            return;
        }
        var size = GetViewport().GetVisibleRect().Size;
        WriteCapture("gameplay_ready", new {
            protocol = 1, token = request.GetProperty("token").GetString(), pid = System.Environment.ProcessId,
            hwnd = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle), utc = DateTimeOffset.UtcNow,
            width = (int)size.X, height = (int)size.Y, world_tick = _session.Simulation.WorldTick,
            player = new[] { feet.X, feet.Y, feet.Z }, camera = new[] { camera.X, camera.Y, camera.Z },
            loading = false, camera_current = true, simulation_held = true
        });
        _captureReady = true;
    }

    private void WriteCapture(string name, object value)
    {
        string path = Path.Combine(_captureDirectory!, name + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
