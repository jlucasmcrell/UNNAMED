// UNNAMED Presentation - the continuous player camera (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §1-§5, §21, §22)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Player;

public enum Shoulder
{
    Right,
    Left,
    Center,
}

/// <summary>
/// One camera on a spectrum: the wheel moves it continuously from a distant third person through close shoulder to
/// first person at the eye. A spring arm keeps it out of walls and roofs, so indoors it compresses on its own; it never
/// switches the player into first person (§4 "do not force a perspective change"). It changes nothing but the view.
/// </summary>
public partial class CameraRig : Node3D
{
    public const float MaxDistance = 6f;

    /// <summary>At or below this chosen distance the player is in first person: the body faces where the camera looks.</summary>
    public const float FirstPersonBelow = 0.35f;

    private const float ShoulderOffset = 0.45f;

    private readonly Node3D _pitch = new() { Name = "Pitch" };
    private readonly SpringArm3D _arm = new() { Name = "Arm" };
    private float _shoulder;
    private float _restoreDistance = 3.5f;

    public Camera3D Camera { get; } = new() { Name = "Camera", Fov = 75, Near = 0.05f };

    /// <summary>Radians about +Y. At 0 the camera looks along -Z.</summary>
    public float Yaw { get; set; }

    /// <summary>Radians; negative looks down.</summary>
    public float Pitch { get; set; } = -0.3f;

    /// <summary>The distance the player chose. The spring arm may hold the camera closer.</summary>
    public float TargetDistance { get; set; } = 3.5f;

    public float Distance { get; private set; } = 3.5f;

    public Shoulder Side { get; set; } = Shoulder.Right;

    public float Sensitivity { get; set; } = 0.0025f;

    public bool IsFirstPerson => TargetDistance <= FirstPersonBelow;

    /// <summary>How far the camera actually is from the eye once collision has had its say.</summary>
    public float EffectiveDistance => Distance <= 0.001f ? 0 : _arm.GetHitLength();

    /// <summary>Where the camera looks, flattened onto the ground.</summary>
    public Vector3 GroundForward => new(-Mathf.Sin(Yaw), 0, -Mathf.Cos(Yaw));

    public Vector3 GroundRight => new(Mathf.Cos(Yaw), 0, -Mathf.Sin(Yaw));

    public override void _Ready()
    {
        AddChild(_pitch);
        _pitch.AddChild(_arm);
        _arm.AddChild(Camera);
        _arm.CollisionMask = HollowView.CameraCollisionLayer;
        _arm.Shape = new SphereShape3D { Radius = 0.2f };
        _arm.Margin = 0.05f;
        Camera.Current = true;
    }

    public void Look(Vector2 mouseDelta)
    {
        Yaw = Mathf.Wrap(Yaw - mouseDelta.X * Sensitivity, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch - mouseDelta.Y * Sensitivity, -1.35f, 1.2f);
    }

    public void Zoom(float metres) => TargetDistance = Mathf.Clamp(TargetDistance + metres, 0, MaxDistance);

    /// <summary>Jump straight to first person, or back to the last third-person distance.</summary>
    public void ToggleFirstPerson()
    {
        if (IsFirstPerson)
        {
            TargetDistance = _restoreDistance;
        }
        else
        {
            _restoreDistance = TargetDistance;
            TargetDistance = 0;
        }
    }

    public void SwapShoulder() => Side = Side switch
    {
        Shoulder.Right => Shoulder.Left,
        Shoulder.Left => Shoulder.Center,
        _ => Shoulder.Right,
    };

    /// <summary>Follow the body's drawn position this frame.</summary>
    public void Follow(Vector3 feet, double delta)
    {
        float blend = 1f - Mathf.Exp(-10f * (float)delta);
        Distance = Mathf.Abs(Distance - TargetDistance) < 0.005f ? TargetDistance : Mathf.Lerp(Distance, TargetDistance, blend);
        // No shoulder offset near the eye: the offset fades in with distance so the zoom stays seamless.
        float side = Side switch { Shoulder.Right => 1f, Shoulder.Left => -1f, _ => 0f };
        _shoulder = Mathf.Lerp(_shoulder, side * ShoulderOffset * Mathf.Clamp(Distance / 2f, 0, 1), blend);

        Position = feet + new Vector3(0, Avatar.EyeHeight, 0);
        Rotation = new Vector3(0, Yaw, 0);
        // In first person the eye sits just ahead of the head's centre, so looking down shows the torso, not the neck.
        float forward = Mathf.Clamp(1f - Distance / FirstPersonBelow, 0, 1) * 0.1f;
        _pitch.Position = new Vector3(_shoulder, 0, -forward);
        _pitch.Rotation = new Vector3(Pitch, 0, 0);
        _arm.SpringLength = Distance;
    }
}
