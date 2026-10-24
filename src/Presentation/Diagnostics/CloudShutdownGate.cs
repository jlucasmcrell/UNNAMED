using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Diagnostics;

/// <summary>Native renderer regression for the runtime cloud compositor's shutdown ownership.</summary>
public partial class CloudShutdownGate : Node3D
{
    private double _seconds;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Cloud shutdown gate needs the native renderer");
        var world = new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(.4f, .6f, .8f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
        } };
        AddChild(world);
        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-35, -20, 0) };
        AddChild(sun);
        var camera = new Camera3D { Position = new Vector3(0, 2, 5), Current = true };
        AddChild(camera);
        if (!CloudsView.Attach(world, sun, out string? why)) throw new InvalidOperationException(why);
        if (world.Compositor.CompositorEffects.Count != 1)
            throw new InvalidOperationException("Cloud driver registered the compositor effect more than once");
        world.TreeExiting += () => GD.Print("CLOUD_SHUTDOWN_GATE remaining_effects=" + world.Compositor.CompositorEffects.Count);
    }

    public override void _Process(double delta)
    {
        _seconds += delta;
        if (_seconds < 4) return;
        GD.Print("CLOUD_SHUTDOWN_GATE quitting after native frames=" + Engine.GetFramesDrawn());
        SetProcess(false);
        GetTree().Quit();
    }
}
