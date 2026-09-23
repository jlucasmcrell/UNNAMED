// UNNAMED Presentation - the player's full-body greybox mannequin (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §7, §8, §22)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Greybox;

namespace UNNAMED.Presentation.Player;

/// <summary>
/// One full body, used by every camera distance: first person hides only the head (it keeps casting its shadow) and
/// leaves the torso and legs visible when looking down.
/// Greybox proof motion only - a procedural walk cycle - until Animation Wave 0's rig is handed over.
/// </summary>
public partial class Avatar : Node3D
{
    public const float EyeHeight = 1.62f;

    private readonly Node3D _hips = new() { Position = new Vector3(0, 0.95f, 0) };
    private readonly Node3D _leftHip = new() { Position = new Vector3(0.11f, 0, 0) };
    private readonly Node3D _rightHip = new() { Position = new Vector3(-0.11f, 0, 0) };
    private readonly Node3D _leftKnee = new() { Position = new Vector3(0, -0.45f, 0) };
    private readonly Node3D _rightKnee = new() { Position = new Vector3(0, -0.45f, 0) };
    private readonly Node3D _leftShoulder = new() { Position = new Vector3(0.23f, 0.47f, 0) };
    private readonly Node3D _rightShoulder = new() { Position = new Vector3(-0.23f, 0.47f, 0) };
    private readonly Node3D _leftElbow = new() { Position = new Vector3(0, -0.3f, 0) };
    private readonly Node3D _rightElbow = new() { Position = new Vector3(0, -0.3f, 0) };
    private readonly List<MeshInstance3D> _head = new();
    private double _phase;
    private double _jumpTime = -1;
    private float _yaw;

    public const double JumpSeconds = 0.55;

    public override void _Ready()
    {
        AddChild(_hips);
        _hips.AddChild(Part(new CapsuleMesh { Radius = 0.17f, Height = 0.62f }, Palette.Cloth, new Vector3(0, 0.28f, 0)));
        var head = Part(new SphereMesh { Radius = 0.12f, Height = 0.26f }, Palette.Skin, new Vector3(0, 0.68f, 0.01f));
        _hips.AddChild(head);
        // A nose, so facing reads in greybox.
        var nose = Part(new BoxMesh { Size = new Vector3(0.04f, 0.05f, 0.05f) }, Palette.Skin, new Vector3(0, 0, 0.12f));
        head.AddChild(nose);
        _head.AddRange(new[] { head, nose });

        foreach (var (hip, knee) in new[] { (_leftHip, _leftKnee), (_rightHip, _rightKnee) })
        {
            _hips.AddChild(hip);
            hip.AddChild(Part(new CapsuleMesh { Radius = 0.075f, Height = 0.48f }, Palette.Leather, new Vector3(0, -0.225f, 0)));
            hip.AddChild(knee);
            knee.AddChild(Part(new CapsuleMesh { Radius = 0.065f, Height = 0.46f }, Palette.Leather, new Vector3(0, -0.22f, 0)));
            knee.AddChild(Part(new BoxMesh { Size = new Vector3(0.1f, 0.06f, 0.24f) }, Palette.Leather, new Vector3(0, -0.46f, 0.05f)));
        }
        foreach (var (shoulder, elbow) in new[] { (_leftShoulder, _leftElbow), (_rightShoulder, _rightElbow) })
        {
            _hips.AddChild(shoulder);
            shoulder.AddChild(Part(new CapsuleMesh { Radius = 0.055f, Height = 0.32f }, Palette.Cloth, new Vector3(0, -0.15f, 0)));
            shoulder.AddChild(elbow);
            elbow.AddChild(Part(new CapsuleMesh { Radius = 0.05f, Height = 0.3f }, Palette.Skin, new Vector3(0, -0.14f, 0)));
        }
    }

    public void SetFirstPerson(bool firstPerson)
    {
        foreach (var part in _head)
            part.CastShadow = firstPerson ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.On;
    }

    /// <summary>A cosmetic hop. Phase 1's jump carries no gameplay (PROTOTYPE.md §3: never load-bearing).</summary>
    public void Hop()
    {
        if (_jumpTime < 0)
            _jumpTime = 0;
    }

    /// <summary>Place the body where the simulation (plus prediction) says, turned towards its facing, and animate its gait.</summary>
    public void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta)
    {
        // Turn smoothly towards the authoritative facing: the facing itself is state; the turn is only drawing.
        _yaw = Mathf.LerpAngle(_yaw, facingRadians, 1f - Mathf.Exp(-14f * (float)delta));
        Rotation = new Vector3(0, _yaw, 0);

        float hop = 0;
        if (_jumpTime >= 0)
        {
            _jumpTime += delta;
            double t = _jumpTime / JumpSeconds;
            hop = t >= 1 ? 0 : (float)(4 * 0.45 * t * (1 - t));
            if (t >= 1)
                _jumpTime = -1;
        }
        Position = feet + new Vector3(0, hop, 0);

        float stride = Mathf.Clamp(speedMetresPerSecond / 3.2f, 0, 1.6f);
        _phase += speedMetresPerSecond * delta * Mathf.Tau / 1.5;
        float swing = Mathf.Sin((float)_phase) * 0.55f * stride;
        _leftHip.Rotation = new Vector3(swing, 0, 0);
        _rightHip.Rotation = new Vector3(-swing, 0, 0);
        // A leg swung back (positive rotation about X, since the body faces +Z) lifts its heel.
        _leftKnee.Rotation = new Vector3(Mathf.Max(0, swing) * 1.2f, 0, 0);
        _rightKnee.Rotation = new Vector3(Mathf.Max(0, -swing) * 1.2f, 0, 0);
        _leftShoulder.Rotation = new Vector3(-swing * 0.8f, 0, 0.08f);
        _rightShoulder.Rotation = new Vector3(swing * 0.8f, 0, -0.08f);
        _leftElbow.Rotation = new Vector3(-0.25f - 0.2f * stride, 0, 0);
        _rightElbow.Rotation = new Vector3(-0.25f - 0.2f * stride, 0, 0);
        float bob = stride > 0 ? Mathf.Abs(Mathf.Cos((float)_phase)) * 0.04f * stride : Mathf.Sin((float)Time.GetTicksMsec() / 700f) * 0.005f;
        _hips.Position = new Vector3(0, 0.95f + bob, 0);
    }

    private static MeshInstance3D Part(Mesh mesh, Material material, Vector3 position) =>
        new() { Mesh = mesh, MaterialOverride = material, Position = position };
}
