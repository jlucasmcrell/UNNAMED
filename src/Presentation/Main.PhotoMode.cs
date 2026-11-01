using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.Presentation.Ui;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation;

public partial class Main
{
    private Camera3D? _photoCamera;
    private CanvasLayer? _photoHelp;
    private readonly Dictionary<CanvasLayer, bool> _photoLayers = new();
    private readonly Dictionary<Label3D, uint> _photoLabels = new();
    private Simulation? _photoWorld;
    private SpaceId _photoSpace;
    private Vector3 _photoAnchor;
    private bool _photoCapturing;

    private void BeginPhotoMode()
    {
        if (_photoCamera is not null || _session.Simulation is not { } world || world.Combat.Defeated || _lying || _rope is not null) return;
        CloseScreens();
        _photoWorld = world;
        _photoSpace = world.ActiveLayout.SpaceId;
        _photoAnchor = _avatar.Position;
        _photoCamera = new Camera3D { Name = "PhotoCamera", Fov = _camera.Camera.Fov, Near = _camera.Camera.Near,
            Far = _camera.Camera.Far, CullMask = _camera.Camera.CullMask };
        AddChild(_photoCamera);
        _photoCamera.GlobalTransform = _camera.Camera.GlobalTransform;
        _photoCamera.MakeCurrent();
        _photoHelp = new CanvasLayer { Name = "PhotoControls", Layer = 80 };
        AddChild(_photoHelp);
        var moveKeys = string.Join(" / ", new[] { "move_forward", "move_left", "move_back", "move_right" }.Select(HelpPanel.Key));
        var words = UiKit.Words($"PHOTO MODE   ·   Move: {moveKeys}   Up/down: E/Q   Faster: Shift   Lens: wheel\nPicture: {HelpPanel.Key("screenshot")}   Return: {HelpPanel.Key("photo_mode")} / Esc   ·   The world keeps running.", 18);
        _photoHelp.AddChild(words);
        words.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        words.OffsetLeft = 24;
        words.OffsetRight = -24;
        words.OffsetTop = 24;
        words.OffsetBottom = 110;
        words.AddThemeConstantOverride("outline_size", 3);
        words.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, .8f));
        _autorun = false;
        _controller.Steer(_camera, Vector2.Zero, Gait.Run);
        _controller.Guard(false);
        HidePhotoInterface();
    }

    private void EndPhotoMode()
    {
        if (_photoCamera is null) return;
        _photoCamera.QueueFree();
        _photoCamera = null;
        _photoHelp?.QueueFree();
        _photoHelp = null;
        _camera.Camera.MakeCurrent();
        foreach (var (layer, visible) in _photoLayers)
            if (GodotObject.IsInstanceValid(layer)) layer.Visible = visible;
        foreach (var (label, layers) in _photoLabels)
            if (GodotObject.IsInstanceValid(label)) label.Layers = layers;
        _photoLayers.Clear();
        _photoLabels.Clear();
        _photoWorld = null;
        _avatar.SetFirstPerson(_camera.EffectiveDistance < 0.4f);
    }

    private void HidePhotoInterface()
    {
        foreach (var layer in FindChildren("*", "CanvasLayer", true, false).OfType<CanvasLayer>())
        {
            if (layer == _photoHelp) continue;
            _photoLayers.TryAdd(layer, layer.Visible);
            layer.Visible = false;
        }
        foreach (var label in FindChildren("*", "Label3D", true, false).OfType<Label3D>())
        {
            _photoLabels.TryAdd(label, label.Layers);
            label.Layers = 0;
        }
        _avatar.SetFirstPerson(false);
    }

    private bool PhotoMouse(InputEvent input)
    {
        if (_photoCamera is not { } camera) return false;
        if (input is InputEventMouseMotion motion)
        {
            var angles = camera.Rotation;
            angles.Y -= motion.Relative.X * 0.003f;
            angles.X = Mathf.Clamp(angles.X - motion.Relative.Y * 0.003f, -1.5f, 1.5f);
            angles.Z = 0;
            camera.Rotation = angles;
        }
        else if (input is InputEventMouseButton { Pressed: true } button && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            camera.Fov = Mathf.Clamp(camera.Fov + (button.ButtonIndex == MouseButton.WheelUp ? -3 : 3), 20, 110);
        return true;
    }

    private void ReadPhotoInput()
    {
        if (Input.IsActionJustPressed("photo_mode") || Input.IsActionJustPressed("release_mouse")) EndPhotoMode();
        else if (Input.IsActionJustPressed("screenshot")) TakePhoto();
    }

    private void DrawPhotoMode(double delta)
    {
        if (_photoCamera is not { } camera) return;
        if (_photoWorld is not { } world || _session.Simulation != world || world.Combat.Defeated || world.ActiveLayout.SpaceId != _photoSpace)
        {
            EndPhotoMode();
            return;
        }
        var direction = camera.GlobalBasis * new Vector3(Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"), 0,
            Input.GetActionStrength("move_back") - Input.GetActionStrength("move_forward"));
        direction += Vector3.Up * ((Input.IsPhysicalKeyPressed(Key.E) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Q) ? 1 : 0));
        float speed = Input.IsPhysicalKeyPressed(Key.Shift) ? 10 : 3;
        camera.GlobalPosition = _photoAnchor + (camera.GlobalPosition + direction.LimitLength() * speed * (float)Math.Min(delta, .1) - _photoAnchor).LimitLength(25);
        HidePhotoInterface();
    }

    private async void TakePhoto()
    {
        if (_photoCapturing || _photoCamera is null) return;
        _photoCapturing = true;
        if (_photoHelp is not null) _photoHelp.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_photoCamera is not null)
        {
            TakeScreenshot();
            if (_photoHelp is not null) _photoHelp.Visible = true;
        }
        _photoCapturing = false;
    }
}
