// UNNAMED Presentation - the start screen's establishing shot: a slow drift over the hollow behind the menu (Phase B, B11)
// Godot presentation only (D-11): a camera; the world behind it is the one the game builds anyway

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Behind the start screen's choice, instead of whatever the game camera happens to face (a close stone, the player's legs): a camera that
/// drifts slowly along the hollow's north-west rise, looking across the waystation to the Charwood and the ravine's far face - the Phase-B
/// world, seen as a place. Freed the moment a world is started or loaded; the game camera takes over.
/// </summary>
public partial class StartCamera : Camera3D
{
    // Heights are metres above the ground under each point (the rise is well above the hollow's floor).
    private static readonly Vector3 From = new(12f, 7f, 186f), To = new(58f, 6f, 190f), Look = new(95f, 1f, 120f);
    private const double Seconds = 90;
    private double _clock;

    /// <summary>The ground's height at (x, z): the shot is placed over it.</summary>
    public Func<float, float, float> Ground { get; init; } = (_, _) => 0;

    public override void _Ready()
    {
        Fov = 50;
        Far = 2000;
        MakeCurrent();
        Place();
        Track(this, this);
    }

    /// <summary>Point Terrain3D at <paramref name="camera"/>: it draws its clipmap round the camera it tracks, which is not the current one by itself.</summary>
    public static void Track(Node from, Camera3D camera)
    {
        foreach (var terrain in from.GetTree().Root.FindChildren("*", "Terrain3D", true, false))
            terrain.Call("set_camera", camera);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        Place();
    }

    private void Place()
    {
        // Out and back along the rise, eased at each end, so the shot never jumps.
        float t = (float)(0.5 - 0.5 * Math.Cos(_clock / Seconds * Math.Tau));
        var eye = From.Lerp(To, t);
        var at = Look + new Vector3(0, 0, -10f * t);
        LookAtFromPosition(eye + new Vector3(0, Ground(eye.X, eye.Z), 0), at + new Vector3(0, Ground(at.X, at.Z), 0), Vector3.Up);
    }
}
