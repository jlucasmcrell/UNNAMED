// UNNAMED Presentation - the player's full-body greybox mannequin (CAMERA_PERSPECTIVE_AND_PRESENTATION.md §7, §8, §22)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Greybox;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Player;

/// <summary>
/// One full body, used by every camera distance: first person hides only the head (it keeps casting its shadow) and
/// leaves the torso and legs visible when looking down.
/// Greybox proof motion only - a procedural walk cycle and combat poses - until Animation Wave 0's rig is handed over. The
/// poses follow the simulation's attack phases (windup, active window, recovery), so a clip that replaces them is timed
/// to the same numbers.
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
    private readonly MeshInstance3D _sword = Part(new BoxMesh { Size = new Vector3(0.04f, 0.05f, 0.85f) }, Palette.Metal, new Vector3(0, -0.3f, 0.36f));
    private readonly MeshInstance3D _bow = Part(new BoxMesh { Size = new Vector3(0.03f, 1.15f, 0.05f) }, Palette.Leather, new Vector3(0, -0.3f, 0.04f));
    // Along the forearm, from behind the elbow to 1.6 m past the hand: a level forearm holds it level.
    private readonly MeshInstance3D _spear = Part(new BoxMesh { Size = new Vector3(0.035f, 2.1f, 0.035f) }, Palette.Shaft, new Vector3(0, -0.85f, 0.05f));
    private readonly MeshInstance3D _working = Part(new SphereMesh { Radius = 0.07f, Height = 0.14f },
        new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.75f, 1f), EmissionEnabled = true, Emission = new Color(0.4f, 0.6f, 1f) },
        new Vector3(0, -0.34f, 0.04f));
    private CombatStance _stance = CombatStance.AtRest;

    /// <summary>What the body and sleeves are made of; set before the figure enters the tree (an NPC's own clothes, M4).</summary>
    public StandardMaterial3D Clothing { get; init; } = Palette.Cloth;
    private double _phase;
    private float _yaw;
    private bool _crouched;
    private bool _airborne;

    /// <summary>How far into a crouch the body is drawn, 0 standing to 1 crouched, eased so a stance change is not a snap.</summary>
    public float Crouch { get; private set; }

    public override void _Ready()
    {
        AddChild(_hips);
        _hips.AddChild(Part(new CapsuleMesh { Radius = 0.17f, Height = 0.62f }, Clothing, new Vector3(0, 0.28f, 0)));
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
            shoulder.AddChild(Part(new CapsuleMesh { Radius = 0.055f, Height = 0.32f }, Clothing, new Vector3(0, -0.15f, 0)));
            shoulder.AddChild(elbow);
            elbow.AddChild(Part(new CapsuleMesh { Radius = 0.05f, Height = 0.3f }, Palette.Skin, new Vector3(0, -0.14f, 0)));
        }
        _rightElbow.AddChild(_sword);
        _spear.AddChild(Part(new BoxMesh { Size = new Vector3(0.07f, 0.24f, 0.02f) }, Palette.Metal, new Vector3(0, -1.17f, 0)));
        _rightElbow.AddChild(_spear);
        _leftElbow.AddChild(_bow);
        _leftElbow.AddChild(_working);
    }

    /// <summary>What the body is doing in combat this frame: a phase, how far through it, and what it holds.</summary>
    public void SetStance(CombatStance stance) => _stance = stance;

    public void SetFirstPerson(bool firstPerson)
    {
        foreach (var part in _head)
            part.CastShadow = firstPerson ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.On;
    }

    /// <summary>
    /// Crouched or standing, and in the air or not (the owner's M6 playtest): the simulation's posture. The jump's height is already in
    /// the feet it is posed at; this only bends the legs.
    /// </summary>
    public void SetPosture(bool crouched, bool airborne)
    {
        _crouched = crouched;
        _airborne = airborne;
    }

    /// <summary>Place the body where the simulation (plus prediction) says, turned towards its facing, and animate its gait.</summary>
    public void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta)
    {
        // Turn smoothly towards the authoritative facing: the facing itself is state; the turn is only drawing.
        _yaw = Mathf.LerpAngle(_yaw, facingRadians, 1f - Mathf.Exp(-14f * (float)delta));
        Rotation = new Vector3(0, _yaw, 0);

        Position = feet;
        Crouch = Mathf.MoveToward(Crouch, _crouched ? 1f : 0f, (float)delta * 5f);

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
        _hips.Position = new Vector3(0, 0.95f + bob - 0.33f * Crouch, 0);
        _hips.Rotation = new Vector3(0.25f * Crouch, 0, 0);
        // Crouched, the thighs come forward and the knees bend so the feet stay under the body; in the air, the knees tuck.
        float thigh = _airborne ? -0.7f : -1.0f * Crouch, knee = _airborne ? 1.2f : 1.6f * Crouch;
        if (_airborne || Crouch > 0)
        {
            _leftHip.Rotation = new Vector3(thigh + swing * (1 - Crouch) * 0.5f, 0, 0);
            _rightHip.Rotation = new Vector3(thigh - swing * (1 - Crouch) * 0.5f, 0, 0);
            _leftKnee.Rotation = new Vector3(knee, 0, 0);
            _rightKnee.Rotation = new Vector3(knee, 0, 0);
        }
        Fight();
    }

    /// <summary>
    /// Combat poses over the walk. Rotation about X swings a limb: negative raises it forward. A sword rises through the
    /// windup and falls through the active window; a bow is held out and drawn through the windup and loosed at release;
    /// a spear (M3f) is held level in both hands, drawn back through the windup and driven forward through the active window.
    /// A working (M3e) gathers in the open left hand through its tell - the glow is the tell - and is thrown at release.
    /// </summary>
    private void Fight()
    {
        var s = _stance;
        _sword.Visible = s.Holds == Held.Sword;
        _spear.Visible = s.Holds == Held.Spear;
        _bow.Visible = s.Holds == Held.Bow && !s.Casting;
        _working.Visible = s.Casting && s.Phase is CombatPhase.Windup or CombatPhase.Active;
        float t = Mathf.Clamp(s.Progress, 0, 1);
        // Held out, the bow stands upright against the raised arm; at rest it hangs along it.
        _bow.Rotation = new Vector3(s.Phase is CombatPhase.Windup or CombatPhase.Active ? 1.5f : 0, 0, 0);
        switch (s.Phase)
        {
            case CombatPhase.Windup when s.Casting:
                _leftShoulder.Rotation = new Vector3(-1.1f - 0.3f * t, 0, 0.05f);
                _leftElbow.Rotation = new Vector3(-0.9f * (1 - t), 0, 0);
                _working.Scale = Vector3.One * (0.5f + t);
                break;
            case CombatPhase.Active or CombatPhase.Recovery when s.Casting:
                _leftShoulder.Rotation = new Vector3(-1.5f * (s.Phase == CombatPhase.Active ? 1 : 1 - t), 0, 0.05f);
                _leftElbow.Rotation = Vector3.Zero;
                break;
            case CombatPhase.Windup when s.Holds == Held.Bow:
                _leftShoulder.Rotation = new Vector3(-1.5f, 0, 0.05f);
                _leftElbow.Rotation = Vector3.Zero;
                _rightShoulder.Rotation = new Vector3(-1.5f, 0, -0.1f);
                _rightElbow.Rotation = new Vector3(-2.4f * t, 0, 0);
                break;
            case CombatPhase.Active or CombatPhase.Recovery when s.Holds == Held.Bow:
                _leftShoulder.Rotation = new Vector3(-1.5f * (s.Phase == CombatPhase.Active ? 1 : 1 - t), 0, 0.05f);
                _rightShoulder.Rotation = new Vector3(-0.9f * (s.Phase == CombatPhase.Active ? 1 : 1 - t), 0, -0.1f);
                break;
            case CombatPhase.Windup when s.Holds == Held.Spear:
                HoldSpear(Mathf.Lerp(-0.5f, -0.15f, t), Mathf.Lerp(-0.95f, -1.35f, t));
                break;
            case CombatPhase.Active when s.Holds == Held.Spear:
                HoldSpear(Mathf.Lerp(-0.15f, -1.25f, t), Mathf.Lerp(-1.35f, -0.25f, t));
                break;
            case CombatPhase.Recovery when s.Holds == Held.Spear:
                HoldSpear(Mathf.Lerp(-1.25f, -0.5f, t), Mathf.Lerp(-0.25f, -0.95f, t));
                break;
            case CombatPhase.Windup:
                _rightShoulder.Rotation = new Vector3(-2.7f * t, 0, -0.15f);
                _rightElbow.Rotation = new Vector3(-0.6f * t, 0, 0);
                break;
            case CombatPhase.Active:
                _rightShoulder.Rotation = new Vector3(Mathf.Lerp(-2.7f, -0.5f, t), 0, -0.15f);
                _rightElbow.Rotation = new Vector3(-0.2f, 0, 0);
                break;
            case CombatPhase.Recovery:
                _rightShoulder.Rotation = new Vector3(Mathf.Lerp(-0.5f, 0, t), 0, -0.1f);
                break;
            case CombatPhase.Dodge:
                _hips.Position = new Vector3(0, 0.72f, 0);
                _hips.Rotation = new Vector3(0.35f, 0, 0);
                break;
            case CombatPhase.Staggered:
                _hips.Rotation = new Vector3(-0.3f, 0, Mathf.Sin((float)Time.GetTicksMsec() / 45f) * 0.12f);
                _leftShoulder.Rotation = new Vector3(0.6f, 0, 0.6f);
                _rightShoulder.Rotation = new Vector3(0.6f, 0, -0.6f);
                break;
            default:
                if (s.Guarding)
                {
                    _rightShoulder.Rotation = new Vector3(-1.2f, 0.3f, -0.1f);
                    _rightElbow.Rotation = new Vector3(-1.1f, 0, 0);
                    _leftShoulder.Rotation = new Vector3(-1.1f, -0.3f, 0.1f);
                    _leftElbow.Rotation = new Vector3(-1.3f, 0, 0);
                }
                else if (s.Holds == Held.Spear)
                {
                    HoldSpear(-0.5f, -0.95f);
                }
                break;
        }
    }

    /// <summary>Both hands on the shaft: the right drives it, the left steadies it ahead. The two angles keep it level.</summary>
    private void HoldSpear(float shoulder, float elbow)
    {
        _rightShoulder.Rotation = new Vector3(shoulder, 0, -0.1f);
        _rightElbow.Rotation = new Vector3(elbow, 0, 0);
        _leftShoulder.Rotation = new Vector3(-0.9f, 0, 0.35f);
        _leftElbow.Rotation = new Vector3(-0.5f, 0, 0);
    }

    private static MeshInstance3D Part(Mesh mesh, Material material, Vector3 position) =>
        new() { Mesh = mesh, MaterialOverride = material, Position = position };
}

public enum Held
{
    Nothing,
    Sword,
    Bow,
    Spear,
}

/// <summary>A frame's combat pose: the phase from the simulation, how far through it (0-1), what the hands hold, the guard, and whether it is a working.</summary>
public readonly record struct CombatStance(CombatPhase Phase, float Progress, Held Holds, bool Guarding, bool Casting = false)
{
    public static CombatStance AtRest => new(CombatPhase.Idle, 0, Held.Nothing, false);
}
