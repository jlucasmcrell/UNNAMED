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

    public override string? ClipState => _model.Current;

    /// <summary>The bound person's skinned figure (<c>player</c> or an NPC's ID), or null when the bindings or the asset lack it.</summary>
    public static SkinnedFigure? Create(ArtLibrary art, ArtBindings bindings, string personId)
    {
        if (!bindings.People.TryGetValue(personId, out var person) || SkinnedModel.Create(art, person.Model, person.Clips, personId) is not { } model)
            return null;
        var figure = new SkinnedFigure { Name = personId, _model = model, _art = art, _bindings = bindings, _paces = person.Paces, _phases = person.Phases };
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
        // A rig with finger bones closes the hand that holds a weapon and relaxes the other (Phase B, character fidelity).
        // (A hand bound at its socket - the character standard's SOCK_hand.R - closes the fingers of the hand bone it sits on.)
        static string Bone(string hand) => hand.StartsWith("SOCK_", StringComparison.Ordinal) ? hand[5..] : hand;
        figure._fingers = BodyModifiers.Grip(model.Skeleton, Bone(person.Hand));
        string otherHand = Bone(person.OffHand ?? (person.Hand.EndsWith(".R", StringComparison.Ordinal) ? person.Hand[..^1] + "L" : person.Hand[..^1] + "R"));
        figure._offFingers = BodyModifiers.Grip(model.Skeleton, otherHand);
        figure.Relax();
        // A character-standard face: blinks, and the line on screen spoken (FaceDriver).
        if (FaceDriver.Attach(model, personId) is { } face)
        {
            figure.AddChild(face);
            figure._face = face;
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
    private HandGrip? _fingers;
    private HandGrip? _offFingers;

    /// <summary>How closed an empty hand rests (a relaxed curl, not the rest pose's flat fingers).</summary>
    private const float RelaxedHand = 0.2f;

    private void Relax()
    {
        bool off = _equipped?.OffHand == true && _offHand is not null;
        if (_fingers is not null)
            _fingers.Closed = _held is not null && !off ? 1f : RelaxedHand;
        if (_offFingers is not null)
            _offFingers.Closed = _held is not null && off ? 1f : RelaxedHand;
    }

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

    private FaceDriver? _face;

    public override void Say(string? line) => _face?.Say(line);

    public override Vector3? Head => _model.Skeleton.FindBone("head") is int head and >= 0
        ? _model.Skeleton.GlobalTransform * _model.Skeleton.GetBoneGlobalPose(head).Origin + Vector3.Up * 0.06f
        : null;

    public override Vector3? Foot(string side) => _model.Skeleton.FindBone("foot." + side) is int foot and >= 0
        ? _model.Skeleton.GlobalTransform * _model.Skeleton.GetBoneGlobalPose(foot).Origin
        : null;

    public override void Wear(string? chestItemDefId) =>
        _model.SetOutfit(chestItemDefId is null ? "base" : chestItemDefId[(chestItemDefId.LastIndexOf('.') + 1)..]);

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
        Relax();
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
        Relax();
    }

    public override void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta)
    {
        // Standing, a turn of more than about 20 degrees is made at a walking body's pace rather than snapped round (an in-place walk
        // cycle here slid the feet: measured, tools/visual_review/foot_slide.py).
        float behind = Mathf.Abs(Mathf.AngleDifference(_yaw, facingRadians));
        _turning = speedMetresPerSecond < 0.25f && (behind > 0.35f || (_turning && behind > 0.08f));
        float follow = _turning ? 6f : 14f;
        _yaw = Mathf.LerpAngle(_yaw, facingRadians, 1f - Mathf.Exp(-follow * (float)delta));
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
    private IReadOnlyDictionary<string, float[]> _phases = new Dictionary<string, float[]>();

    // Phase B remediation: the blow a new windup plays (a body with several attack clips takes the next each time), the gait held
    // until the speed clearly leaves it (no flicking between two gaits at a threshold), and a landing played through once.
    private string? _attack;
    private CombatPhase _lastPhase = CombatPhase.Idle;
    private int _attackTurn;
    private string _gait = "walk";
    private bool _wasAirborne;
    private double _landing;
    private double _airTime;
    private bool _turning;

    /// <summary>How long the take-off clip plays before the airborne loop: the simulation's rise is 0.42 s (config.base_speeds).</summary>
    private const double TakeOff = 0.35;

    /// <summary>
    /// The playback-rate range for a gait clip: wide enough that a clip reaches the body's speed (the simulation's speeds are
    /// authoritative; a walk clip measured at 0.79 m/s plays at 2x for the 1.6 m/s walk) - past it the feet would slide.
    /// </summary>
    private const float MinRate = 0.35f, MaxRate = 2.2f;

    /// <summary>The attack clip for a weapon's action: its numbered variants in turn (<c>sword_attack_1</c>, <c>_2</c>...), else the one clip.</summary>
    private string NextAttack(string action)
    {
        var variants = new List<string>();
        for (int i = 1; _model.Has($"{action}_{i}"); i++)
            variants.Add($"{action}_{i}");
        if (variants.Count == 0)
            return action;
        return variants[_attackTurn++ % variants.Count];
    }

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
        string? action = s switch
        {
            { Casting: true } => "cast",
            { Holds: Held.Sword } => "sword_attack",
            { Holds: Held.Spear } => "spear_thrust",
            { Holds: Held.Bow } => "bow_draw",
            _ => null,
        };
        // A fresh windup picks its blow; the same blow plays on through its active window and recovery.
        if (s.Phase == CombatPhase.Windup && _lastPhase != CombatPhase.Windup && action is not null)
            _attack = NextAttack(action);
        _lastPhase = s.Phase;
        string? clip = action is null ? null : _attack is { } chosen && chosen.StartsWith(action, StringComparison.Ordinal) ? chosen : action;
        // An attack's pose follows its phase: the windup to the clip's blow, the active window through it, the recovery after it -
        // at the clip's own marks where the bindings give them (0.45 / 0.7 / 1 otherwise).
        var marks = clip is not null && _phases.TryGetValue(clip, out var p) ? p : new[] { 0.45f, 0.7f, 1f };
        float through = s.Phase switch
        {
            CombatPhase.Windup => marks[0] * t,
            CombatPhase.Active => marks[0] + (marks[1] - marks[0]) * t,
            CombatPhase.Recovery => marks[1] + (marks[2] - marks[1]) * t,
            _ => -1,
        };
        if (through >= 0 && clip is not null && m.Has(clip))
        {
            m.Hold(clip, through);
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
                // Held where the clip's guard is up (its first mark where the bindings give one).
                m.Hold(guard, _phases.TryGetValue(guard, out var g) ? g[0] : 0.5f, 0.15f);
                return;
            }
        }
        // In the air: the take-off played through its first part, then the airborne loop; on landing, the landing played once
        // (walking or running on cuts it short).
        if (_airborne && m.Has("fall"))
        {
            _airTime = _wasAirborne ? _airTime + delta : 0;
            _wasAirborne = true;
            if (_airTime < TakeOff && m.Has("jump_start"))
                m.Play("jump_start", 0.08f, 1f, _airTime == 0);
            else
                m.Play("fall", 0.15f);
            return;
        }
        if (_wasAirborne)
        {
            _wasAirborne = false;
            _landing = m.Has("land") ? 0.001 : 0;
        }
        if (_landing > 0)
        {
            _landing += delta;
            if (_landing < Math.Min(0.6, m.Length("land")) && speed < 1.5f)
            {
                m.Play("land", 0.08f, 1f, _landing - delta <= 0.001);
                return;
            }
            _landing = 0;
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
                m.Play("crouch_walk", 0.25f, Mathf.Clamp(speed / Pace("crouch_walk", 1.6f), MinRate, MaxRate));
            else
                m.Play("crouch_idle", 0.3f);
            return;
        }
        // Locomotion, the clip's pace following the body's (a walk clip is authored at about 1.4 m/s, a run 3.2, a sprint 5). A gait
        // holds until the speed has clearly left it: accelerating or slowing through a threshold never flicks between two clips.
        if (speed > 0.25f && m.Has("walk"))
        {
            _gait = _gait switch
            {
                "sprint" when speed < 4.0f => speed < 2.0f ? "walk" : "run",
                "run" when speed > 4.4f && m.Has("sprint") => "sprint",
                "run" when speed < 2.0f => "walk",
                "walk" when speed > 4.4f && m.Has("sprint") => "sprint",
                "walk" when speed > 2.4f && m.Has("run") => "run",
                _ => _gait,
            };
            if (!m.Has(_gait))
                _gait = "walk";
            float pace = Pace(_gait, _gait switch { "sprint" => 5f, "run" => 3.2f, _ => 1.4f });
            m.Play(_gait, 0.2f, Mathf.Clamp(speed / pace, MinRate, MaxRate));
            return;
        }
        _gait = "walk";
        // Standing with a weapon drawn: its ready stance, not the empty-handed idle.
        string ready = s.Holds switch { Held.Sword => "sword_ready", Held.Spear => "spear_ready", Held.Bow => "bow_ready", _ => "" };
        if (ready.Length > 0 && m.Has(ready))
        {
            m.Play(ready, 0.3f);
            return;
        }
        m.Play("idle", 0.3f);
    }
}
