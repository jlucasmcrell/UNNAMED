// UNNAMED Presentation - the continuous player camera (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §1-§5, §21, §22)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Settings;

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

    public Camera3D Camera { get; } = new() { Name = "Camera", Fov = new CameraPrefs().Fov, Near = 0.05f };

    /// <summary>Radians about +Y. At 0 the camera looks along -Z.</summary>
    public float Yaw { get; set; }

    /// <summary>Radians; negative looks down.</summary>
    public float Pitch { get; set; } = -0.3f;

    /// <summary>The distance the player chose. The spring arm may hold the camera closer.</summary>
    public float TargetDistance { get; set; } = 3.5f;

    public float Distance { get; private set; } = 3.5f;

    public Shoulder Side { get; set; } = Shoulder.Right;

    /// <summary>How far a giant in the fight lifts the view, in metres: the eye rises by it and the arm grows by a little more, so the
    /// foe's head stays in frame and the camera rides over the ground behind instead of being pressed into it.</summary>
    public float Lift { get; set; }
    private float _lift;

    /// <summary>How far into a crouch the body is drawn (0-1): the eye comes down with it (the owner's M6 playtest).</summary>
    public float Crouch { get; set; }

    /// <summary>A crouched eye's height.</summary>
    public const float CrouchedEyeHeight = 1.05f;

    /// <summary>Radians a pixel of mouse travel turns the camera (Settings, Camera: look sensitivity).</summary>
    public float Sensitivity { get; private set; } = CameraPrefs.BaseSensitivity;

    /// <summary>Moving the mouse forward looks down (Settings, Camera: invert vertical look).</summary>
    public bool InvertY { get; private set; }

    /// <summary>The player's camera settings: look speed, vertical look and the field of view, applied at once.</summary>
    public void Use(CameraPrefs prefs)
    {
        Sensitivity = CameraPrefs.BaseSensitivity * prefs.Sensitivity;
        InvertY = prefs.InvertY;
        Camera.Fov = prefs.Fov;
        _shakeScale = prefs.Shake;
        _bobScale = prefs.Bob;
    }

    // Comfort (Settings, Camera): how much of a shake and of a head bob is felt, 0 to 1.
    private float _shakeScale = 1f, _bobScale = 1f;
    private float _shake, _bobPhase, _clock;
    private Vector3? _lastFeet;

    /// <summary>A blow landed on the character: the view jolts by <paramref name="strength"/> (0-1), scaled by the shake setting, and settles.</summary>
    public void Kick(float strength) => _shake = Mathf.Max(_shake, Mathf.Clamp(strength, 0, 1));

    /// <summary>How far the eye is moved this frame by a shake and a first-person bob, in metres: nothing at all when both settings are 0.</summary>
    public static Vector3 Disturbance(float shake, float shakeScale, float bobPhase, float bobScale, float stride, float clock) =>
        new Vector3(Mathf.Sin(clock * 57f), Mathf.Cos(clock * 43f), 0) * shake * shakeScale * 0.05f
        + new Vector3(0, Mathf.Sin(bobPhase), 0) * bobScale * stride * 0.035f;

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
        Pitch = Mathf.Clamp(Pitch - mouseDelta.Y * Sensitivity * (InvertY ? -1 : 1), -1.35f, 1.2f);
    }

    public void Zoom(float metres) => TargetDistance = Mathf.Clamp(TargetDistance + metres, 0, Cap);

    /// <summary>How far the camera may pull back while building (M7 design §8.11, WP-1G): the whole of a small structure in view.</summary>
    public const float BuildMaxDistance = 9f;

    /// <summary>
    /// How far the zoom may go now: <see cref="MaxDistance"/>, or <see cref="BuildMaxDistance"/> in build mode. Lowering it pulls the
    /// camera in, and the lerp eases it there.
    /// </summary>
    public float Cap
    {
        get => _cap;
        set
        {
            _cap = value;
            TargetDistance = Mathf.Min(TargetDistance, value);
            _restoreDistance = Mathf.Min(_restoreDistance, value);
        }
    }

    private float _cap = MaxDistance;

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

        // Head bob: only at the eye (first person), by how fast the body is going; a shake decays by itself.
        _clock += (float)delta;
        float speed = _lastFeet is { } before && delta > 0 ? new Vector2(feet.X - before.X, feet.Z - before.Z).Length() / (float)delta : 0;
        _lastFeet = feet;
        _bobPhase += speed * 2.2f * (float)delta;
        _shake = Mathf.Max(0, _shake - 3.5f * (float)delta);
        float stride = IsFirstPerson ? Mathf.Clamp(speed / 6f, 0, 1) : 0;
        _lift = Mathf.Lerp(_lift, IsFirstPerson ? 0 : Lift, 1f - Mathf.Exp(-2.5f * (float)delta));
        Position = feet + new Vector3(0, Mathf.Lerp(Avatar.EyeHeight, CrouchedEyeHeight, Crouch) + _lift, 0) + Disturbance(_shake, _shakeScale, _bobPhase, _bobScale, stride, _clock);
        Rotation = new Vector3(0, Yaw, 0);
        // In first person the eye sits just ahead of the head's centre, so looking down shows the torso, not the neck.
        float forward = Mathf.Clamp(1f - Distance / FirstPersonBelow, 0, 1) * 0.1f;
        _pitch.Position = new Vector3(_shoulder, 0, -forward);
        _pitch.Rotation = new Vector3(Pitch, 0, 0);
        _arm.SpringLength = Distance + _lift * 1.5f;
    }
}
