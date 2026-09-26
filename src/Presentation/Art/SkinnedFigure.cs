// UNNAMED Presentation - a person drawn with the asset library's skinned body and clips (the Phase-1 asset integration)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The character or an NPC as the asset pipeline made them, playing its clips by what the simulation says: idle, walk, run and sprint
/// by speed; an attack, a draw or a working held at the point its phase has reached (the simulation's timing, never the clip's); a
/// guard, a stagger, a conversation, a fall, a reach. Crouched, a body with crouch clips plays them; otherwise the crouch, and always a
/// jump's tuck, are bent in on top (<see cref="PostureModifier"/>). The weapon
/// in hand is its own model, held by its grip socket on the hand's grip frame (<see cref="HeldWeapon"/>).
/// </summary>
public sealed partial class SkinnedFigure : Figure
{
    private SkinnedModel _model = null!;

    /// <summary>The body's skeleton, for the skeleton modifiers (Phase B's foot planting and look-at).</summary>
    public Skeleton3D Skeleton => _model.Skeleton;
    private PostureModifier? _posture;
    private ArtLibrary _art = null!;
    private ArtBindings _bindings = null!;
    private BoneAttachment3D? _hand;
    private BoneAttachment3D? _offHand;
    private Node3D? _held;
    private string? _heldId;
    private WeaponArt? _equipped;
    private CombatStance _stance = CombatStance.AtRest;
    private bool _crouched;
    private bool _airborne;
    private bool _talking;
    private bool _downed;
    private float _crouch;
    private float _tuck;
    private float _yaw;
    private double _striking;
    private double _reaching;

    public override float Crouch => _crouch;

    /// <summary>The bound person's skinned figure (<c>player</c> or an NPC's ID), or null when the bindings or the asset lack it.</summary>
    public static SkinnedFigure? Create(ArtLibrary art, ArtBindings bindings, string personId)
    {
        if (!bindings.People.TryGetValue(personId, out var person) || SkinnedModel.Create(art, person.Model, person.Clips, personId) is not { } model)
            return null;
        var figure = new SkinnedFigure { Name = personId, _model = model, _art = art, _bindings = bindings, _paces = person.Paces };
        figure.AddChild(model);
        var posture = new PostureModifier();
        if (posture.Bind(model.Skeleton, "pelvis", new[] { "thigh_l", "thigh_r" }, new[] { "calf_l", "calf_r" })
            || posture.Bind(model.Skeleton, "hips", new[] { "thigh.L", "thigh.R" }, new[] { "shin.L", "shin.R" }))
        {
            model.Skeleton.AddChild(posture);
            figure._posture = posture;
        }
        else
        {
            posture.Free();
        }
        figure._hand = model.Attach(person.Hand);
        figure._grip = person.Grip ?? SocketGrip(model, person.Hand);
        if (person.OffHand is { } off)
        {
            figure._offHand = model.Attach(off);
            figure._offGrip = person.OffGrip ?? SocketGrip(model, off);
        }
        return figure;
    }

    /// <summary>
    /// A rig's own hand socket (<c>SOCK_hand_r</c> beside <c>hand_r</c>: WAVE_0_MODULAR_ASSET_STANDARD.md's equipment socket), as a grip
    /// frame in the hand bone's space - where the bindings give no grip of their own. The glTF import makes a socket under a bone a bone of
    /// its own, or a node under the hand's attachment; either is read. Null when the rig has none.
    /// </summary>
    private static Transform3D? SocketGrip(SkinnedModel model, string hand)
    {
        var skeleton = model.Skeleton;
        int bone = skeleton.FindBone(hand), socket = skeleton.FindBone("SOCK_" + hand);
        if (bone >= 0 && socket >= 0)
            return skeleton.GetBoneGlobalRest(bone).AffineInverse() * skeleton.GetBoneGlobalRest(socket);
        if (bone >= 0 && model.FindChild("SOCK_" + hand, true, false) is Node3D node && node.GetParent() is BoneAttachment3D attachment
            && attachment.BoneName == hand)
            return node.Transform;
        return null;
    }

    private Transform3D? _grip;
    private Transform3D? _offGrip;

    public override void SetStance(CombatStance stance) => _stance = stance;

    public override void SetFirstPerson(bool firstPerson) => _model.SetShadowOnly(firstPerson);

    public override void SetPosture(bool crouched, bool airborne)
    {
        _crouched = crouched;
        _airborne = airborne;
    }

    public override void SetTalking(bool talking) => _talking = talking;

    public override bool SetDowned(bool downed)
    {
        _downed = downed;
        return true;
    }

    public override void Strike() => _striking = 0.001;

    public override void Interact() => _reaching = 0.001;

    public override Vector3? CastPoint => _hand is { } hand && hand.IsInsideTree() ? hand.GlobalPosition : null;

    /// <summary>The weapon for an item definition on its hand bone (the bindings' <c>weapons</c>), or empty hands.</summary>
    public override void Hold(string? itemDefId)
    {
        if (itemDefId == _heldId)
            return;
        _heldId = itemDefId;
        Equip(itemDefId is null ? null : _bindings.Weapons.GetValueOrDefault(itemDefId));
    }

    /// <summary>A weapon by its binding (a companion's own, which is no item of the character's), or empty hands. The same one again is kept.</summary>
    public void Equip(WeaponArt? weapon)
    {
        if (weapon == _equipped && (weapon is null || _held is not null))
            return;
        _equipped = weapon;
        _held?.QueueFree();
        _held = null;
        bool off = weapon?.OffHand == true && _offHand is not null;
        if (weapon is null)
            return;
        if ((off ? _offHand : _hand) is not { } bone || (off ? _offGrip : _grip) is not { } grip)
        {
            _art.Coverage.Fallback("held", $"{Name}:{weapon.Model}", (off ? _offHand : _hand) is null
                ? "the rig has no such hand bone: the hand is empty" : "no grip frame bound and no hand socket in the rig: the hand is empty", weapon.Model);
            return;
        }
        if (HeldWeapon.Mount(_art, weapon.Model, grip) is not { } model)
            return;
        model.Name = "Held";
        bone.AddChild(model);
        _held = model;
    }

    public override void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta)
    {
        _yaw = Mathf.LerpAngle(_yaw, facingRadians, 1f - Mathf.Exp(-14f * (float)delta));
        Rotation = new Vector3(0, _yaw, 0);
        Position = feet;
        _crouch = Mathf.MoveToward(_crouch, _crouched ? 1f : 0f, (float)delta * 5f);
        _tuck = Mathf.MoveToward(_tuck, _airborne ? 1f : 0f, (float)delta * 8f);
        Animate(speedMetresPerSecond, delta);
        if (_posture is not null)
        {
            // A crouch clip already holds the head under the crouch height; the bend is for what has none (a body without
            // crouch clips, a blow struck crouched).
            _posture.Crouch = _model.Current is "crouch_idle" or "crouch_walk" ? 0 : _crouch;
            _posture.Tuck = _tuck;
        }
    }

    /// <summary>The pace a gait clip was authored at: the bindings' (a retargeted clip's own), or the game's clips' default.</summary>
    private float Pace(string state, float fallback) => _paces.GetValueOrDefault(state, fallback);

    private IReadOnlyDictionary<string, float> _paces = new Dictionary<string, float>();

    private void Animate(float speed, double delta)
    {
        var m = _model;
        if (_downed)
        {
            m.Play("death", 0.25f);
            return;
        }
        if (_striking > 0)
        {
            _striking += delta;
            if (_striking < m.Length("attack") && m.Has("attack"))
            {
                m.Play("attack", 0.1f);
                return;
            }
            _striking = 0;
        }
        var s = _stance;
        float t = Mathf.Clamp(s.Progress, 0, 1);
        // An attack's pose follows its phase: the windup to 45% of the clip, the active window to 70%, the recovery to the end.
        float through = s.Phase switch
        {
            CombatPhase.Windup => 0.45f * t,
            CombatPhase.Active => 0.45f + 0.25f * t,
            CombatPhase.Recovery => 0.7f + 0.3f * t,
            _ => -1,
        };
        string? action = s switch
        {
            { Casting: true } => "cast",
            { Holds: Held.Sword } => "sword_attack",
            { Holds: Held.Spear } => "spear_thrust",
            { Holds: Held.Bow } => "bow_draw",
            _ => null,
        };
        if (through >= 0 && action is not null && m.Has(action))
        {
            m.Hold(action, through);
            return;
        }
        if (s.Phase == CombatPhase.Staggered && m.Has("hit"))
        {
            m.Play("hit", 0.1f);
            return;
        }
        if (s.Guarding && s.Phase == CombatPhase.Idle)
        {
            string guard = s.Holds == Held.Bow ? "bow_ready" : s.Holds == Held.Spear ? "spear_ready" : "sword_block";
            if (m.Has(guard))
            {
                m.Hold(guard, 0.5f, 0.15f);
                return;
            }
        }
        // A reach plays through once, standing (walking on cuts it short).
        if (_reaching > 0)
        {
            _reaching += delta;
            if (_reaching < m.Length("interact") && speed < 0.25f && m.Has("interact"))
            {
                m.Play("interact", 0.15f, 1f, _reaching - delta <= 0.001);
                return;
            }
            _reaching = 0;
        }
        if (_talking && speed < 0.3f && m.Has("talk"))
        {
            m.Play("talk", 0.3f);
            return;
        }
        // Crouched, a body with crouch clips plays them (a crouched walk is authored at 1.6 m/s, the simulation's crouched pace).
        if (_crouched && m.Has("crouch_idle"))
        {
            if (speed > 0.25f && m.Has("crouch_walk"))
                m.Play("crouch_walk", 0.25f, Mathf.Clamp(speed / Pace("crouch_walk", 1.6f), 0.6f, 1.6f));
            else
                m.Play("crouch_idle", 0.3f);
            return;
        }
        // Locomotion, the clip's pace following the body's (a walk clip is authored at about 1.4 m/s, a run 3.2, a sprint 5).
        if (speed > 0.25f && m.Has("walk"))
        {
            (string gait, float pace) = speed switch
            {
                > 4.2f when m.Has("sprint") => ("sprint", Pace("sprint", 5f)),
                > 2.2f when m.Has("run") => ("run", Pace("run", 3.2f)),
                _ => ("walk", Pace("walk", 1.4f)),
            };
            m.Play(gait, 0.2f, Mathf.Clamp(speed / pace, 0.6f, 1.6f));
            return;
        }
        m.Play("idle", 0.3f);
    }
}
