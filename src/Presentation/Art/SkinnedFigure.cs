// UNNAMED Presentation - a person drawn with the asset library's skinned body and clips (the Phase-1 asset integration)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain;
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
    public override bool HasFootPlantEvents => _model.HasFootPlantEvents;
    private SkinnedModel _model = null!;

    /// <summary>The body's skeleton, for the skeleton modifiers (Phase B's foot planting and look-at).</summary>
    public Skeleton3D Skeleton => _model.Skeleton;
    public string ModelId => _person.Model;
    public bool ForceLod0 { set => _model.ForceLod0 = value; }
    internal void ReviewAllLods(bool all) => _model.ReviewAllLods(all);
    public float StatureScale => _uniformScale;
    private PostureModifier? _posture;
    private ArtLibrary _art = null!;
    private ArtBindings _bindings = null!;
    private PersonArt _person = null!;
    private WornOutfit? _worn;
    private BoneAttachment3D? _hand;
    private BoneAttachment3D? _offHand;
    private Node3D? _held;
    private string? _heldId;
    private WeaponArt? _equipped;
    private CombatStance _stance = CombatStance.AtRest;
    private bool _crouched;
    private bool _airborne;
    private bool _gliding;
    private bool _talking;
    private bool _downed;
    private float _crouch;
    private float _tuck;
    private float _yaw;
    private Vector3 _moveDirection;
    private Vector3 _previousPosition;
    private bool _posed, _hasDirection;
    private IdleVariety _idles = null!;
    private string? _idleRole;
    private string? _restPose;
    public override bool SetRestPose(string? clip)
    {
        _restPose = clip is not null && _model.Has(clip) ? clip : null;
        return _restPose is not null;
    }
    public override Vector3? RestContact
    {
        get
        {
            if (_restPose is null) return null;
            Vector3 At(string name)
            {
                int bone=BoneNames.Find(Skeleton,name);
                return bone>=0?Skeleton.GlobalTransform*Skeleton.GetBoneGlobalPose(bone).Origin:GlobalPosition;
            }
            var pelvis=At("pelvis");
            if (_restPose!="get_up") return pelvis-Vector3.Up*(.085f*_uniformScale);
            var centre=(At("head")+(At("foot.L")+At("foot.R"))*.5f)*.5f;
            return new Vector3(centre.X,pelvis.Y-.115f*_uniformScale,centre.Z);
        }
    }
    public override void SetIdleRole(string? role) => _idleRole = role;
    public override void SetGaze(Vector3? target) => _face?.Gaze(target is { } at && Head is { } head ? GlobalBasis.Inverse() * (at - head).Normalized() : null);
    public override void SetMovementDirection(Vector3 direction) { if (direction.LengthSquared() > .001f) _moveDirection = direction.Normalized(); _hasDirection = true; }
    private double _striking;
    private double _reaching;

    public override float Crouch => _crouch;

    public override string? ClipState => _model.Current;

    /// <summary>
    /// The bound person's skinned figure (<c>player</c> or an NPC's ID), or null when the bindings or the asset lack it. A player's chosen
    /// <paramref name="people"/> replaces the binding's, so the people's look (Kal stone and wings, Siann opal, Orenth phase) is drawn.
    /// </summary>
    public static SkinnedFigure? Create(ArtLibrary art, ArtBindings bindings, string personId, string? body = null, PersonArt? appearance = null, string? people = null)
    {
        var person = appearance ?? bindings.People.GetValueOrDefault(personId);
        if (person is null)
            return null;
        if (people is not null)
            person = person with { PeopleId = people };
        // Another body on the same binding (the creator's, WP2): the same rig family, so the clips, hands and grips hold.
        if (body is not null)
            person = person with { Model = body, UniformScale = ArtBindings.ComparisonBodyScale(person.Model, body, person.UniformScale) };
        if (SkinnedModel.Create(art, person.Model, person.Clips, personId) is not { } model)
            return null;
        if (!float.IsFinite(person.UniformScale) || person.UniformScale <= 0)
            throw new InvalidDataException($"Invalid uniform stature for {personId}");
        model.Scale = Vector3.One * person.UniformScale;
        var phases = person.Phases.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (model.MotionActionPhases is { } nativePhases)
            foreach (var pair in nativePhases) phases[pair.Key] = pair.Value;
        var figure = new SkinnedFigure { Name = personId, _model = model, _art = art, _bindings = bindings, _paces = model.MotionPaces ?? person.Paces, _phases = phases, _uniformScale = person.UniformScale };
        figure._person = person;
        figure._idles = new IdleVariety(personId);
        model.IdleStartPhase = figure._idles.StartPhase;
        figure.AddChild(model);
        model.FootPlanted+=figure.EmitFootPlant;
        figure._crouchHead=new CrouchHeadLift { Facing=figure,Influence=0 };
        model.Skeleton.AddChild(figure._crouchHead);
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
        if (person.OffHand is { } off)
        {
            figure._offHand = model.Attach(off);
        }
        // A rig with finger bones closes the hand that holds a weapon and relaxes the other (Phase B, character fidelity).
        // (A hand bound at its socket - the character standard's SOCK_hand.R - closes the fingers of the hand bone it sits on.)
        static string Bone(string hand) => hand.StartsWith("SOCK_", StringComparison.Ordinal) ? hand[5..] : hand;
        figure._fingers = BodyModifiers.Grip(model.Skeleton, Bone(person.Hand));
        if (figure._fingers is not null) figure._fingers.AuthoredRotations = model.WeaponGrip;
        string otherHand = Bone(person.OffHand ?? BoneNames.Opposite(person.Hand));
        figure._offFingers = BodyModifiers.Grip(model.Skeleton, otherHand);
        figure._grip = person.Grip ?? SocketGrip(model, person.Hand, figure._fingers);
        if (person.OffHand is { } offName) figure._offGrip = person.OffGrip ?? SocketGrip(model, offName, figure._offFingers);
        figure.Relax();
        // A character-standard face: blinks, and the line on screen spoken (FaceDriver).
        if (FaceDriver.Attach(model, personId) is { } face)
        {
            figure.AddChild(face);
            figure._face = face;
        }
        // A MetaHuman's corrective helper joints, then its garments' springs: they correct and swing the pose the clips, IK and
        // look-at made, so they stay the skeleton's last modifiers (CHARACTER_ANIMATION_STRATEGY.md 4.4), including over the ones
        // the figure's owner adds once this returns.
        CorrectiveJoints.Load(art, person.Model, model.Skeleton);
        Outfit.Dress(art, model, person.OutfitBody ?? person.Model, person.Outfit);
        PeopleAppearance.Apply(model, person.PeopleId, art, person.Model);
        if (VisualOptions.ComparisonCast is not null)
            foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
                mesh.Layers |= MHCharacters.MetaHumanFigure.FillLayer;
        CharacterMaterials.ShareLodBox(model.Skeleton);
        Callable.From(() => KeepLast(model.Skeleton)).CallDeferred();
        return figure;
    }

    private static void KeepLast(Skeleton3D skeleton)
    {
        foreach (var node in skeleton.GetChildren().Where(n => n is CorrectiveJoints).Concat(skeleton.GetChildren().Where(n => n is SpringBoneSimulator3D)).ToList())
            skeleton.MoveChild(node, -1);
    }

    /// <summary>
    /// A rig's own hand socket (<c>SOCK_hand_r</c> beside <c>hand_r</c>: WAVE_0_MODULAR_ASSET_STANDARD.md's equipment socket), as a grip
    /// frame in the hand bone's space - where the bindings give no grip of their own. The glTF import makes a socket under a bone a bone of
    /// its own, or a node under the hand's attachment; either is read. Null when the rig has none.
    /// </summary>
    internal static Transform3D? SocketGrip(SkinnedModel model, string hand, HandGrip? fingers)
    {
        var skeleton = model.Skeleton;
        hand = BoneNames.Resolve(skeleton, hand) ?? hand;
        int bone = skeleton.FindBone(hand), socket = skeleton.FindBone("SOCK_" + hand);
        if (bone >= 0 && socket >= 0)
            return skeleton.GetBoneGlobalRest(bone).AffineInverse() * skeleton.GetBoneGlobalRest(socket);
        if (bone >= 0 && model.FindChild("SOCK_" + hand, true, false) is Node3D node && node.GetParent() is BoneAttachment3D attachment
            && attachment.BoneName == hand)
            return node.Transform;
        // The comparison rig has named native fingers but no equipment socket. Use its own
        // palm landmarks, exactly as the MetaHuman grip frame does; no rest or bind changes.
        if (VisualOptions.ComparisonCast is not null && bone >= 0)
        {
            string side = BoneNames.Side(hand);
            var inverse = skeleton.GetBoneGlobalRest(bone).AffineInverse();
            Vector3 Point(string name) => inverse * skeleton.GetBoneGlobalRest(BoneNames.Find(skeleton, name + "." + side)).Origin;
            if (new[] { "middle_01", "index_01", "pinky_01" }.All(n => BoneNames.Find(skeleton, n + "." + side) >= 0))
            {
                var middle = Point("middle_01");
                if(fingers is not null && side=="R")
                {
                    return HeldWeapon.ClosedGrip(skeleton,hand,fingers.ClosedRotations(skeleton));
                }
                return HeldWeapon.Frame(middle*.7f,Point("index_01")-Point("pinky_01"),middle);
            }
        }
        return null;
    }

    private Transform3D? _grip;
    private Transform3D? _offGrip;
    private HandGrip? _fingers;
    private HandGrip? _offFingers;

    /// <summary>How closed an empty hand rests (a relaxed curl, not the rest pose's flat fingers).</summary>
    private const float RelaxedHand = 0.32f;

    private void Relax()
    {
        bool off = _equipped?.OffHand == true && _offHand is not null;
        if (_fingers is not null)
        {
            _fingers.UseAuthored = _equipped?.Family == "sword";
            _fingers.Closed = _held is not null && !off ? 1f : _fingers.AuthoredRotations is null ? RelaxedHand : 0f;
        }
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

    public override void SetGliding(bool gliding) => _gliding = gliding;
    public override void SetTalking(bool talking) { _talking = talking; _model.ForceLod0 = talking; }

    public override bool SetDowned(bool downed)
    {
        _downed = downed;
        return true;
    }

    /// <summary>
    /// Lying, or getting up (the opening): its <c>get_up</c> clip where the bindings give one, held at the point; until the animation lane
    /// delivers it, the <c>death</c> clip run backwards from where it lies. Null is on one's feet.
    /// </summary>
    public override bool SetWaking(float? through)
    {
        _waking = through;
        return _model.Has("get_up") || _model.Has("death");
    }

    /// <summary>How long getting up takes on this body, in seconds.</summary>
    public override double GetUpSeconds => _model.Has("get_up") ? _model.Length("get_up") : _model.Has("death") ? Math.Min(2.0, _model.Length("death")) : 0;

    private float? _waking;

    private ActivityKind _fishKind;
    private double _fishing;

    public override void Strike() => _striking = 0.001;

    public override void Interact() => _reaching = 0.001;

    /// <summary>
    /// The line in the water: the cast and the reel each play once, and the wait between them has no clip of its own - the body stands.
    /// A cast broken off, or a line reeled early, simply stops where it is.
    /// </summary>
    public override void Fishing(ActivityKind kind)
    {
        if (kind == _fishKind)
            return;
        _fishKind = kind;
        _fishing = kind is ActivityKind.FishCast or ActivityKind.FishReel ? 0.001 : 0;
    }

    public override Vector3? CastPoint => _hand is { } hand && hand.IsInsideTree() ? hand.GlobalPosition : null;

    public override void LightInOffHand(long radiusMm) => HandLight.Show(_offHand, radiusMm);

    private FaceDriver? _face;

    public override void Say(string? line) => _face?.Say(line);

    public override Vector3? Head => BoneNames.Find(_model.Skeleton, "head") is int head and >= 0
        ? _model.Skeleton.GlobalTransform * _model.Skeleton.GetBoneGlobalPose(head).Origin + Vector3.Up * 0.06f
        : null;

    public override Vector3? Foot(string side) => BoneNames.Find(_model.Skeleton, "foot." + side) is int foot and >= 0
        ? _model.Skeleton.GlobalTransform * _model.Skeleton.GetBoneGlobalPose(foot).Origin
        : null;

    public override void Wear(string? chestItemDefId) =>
        _model.SetOutfit(chestItemDefId is null ? "base" : chestItemDefId[(chestItemDefId.LastIndexOf('.') + 1)..]);

    public override void WearEquipment(WornOutfit worn)
    {
        if (_person.EquipmentOutfits.Count == 0)
        {
            base.WearEquipment(worn);
            return;
        }
        if (_worn == worn)
            return;
        _worn = worn;
        Outfit.Clear(_model);
        var garments = worn.Garments(_person.Outfit, _person.EquipmentOutfits);
        int dressed = Outfit.Dress(_art, _model, _person.OutfitBody ?? _person.Model, garments);
        if (OS.GetEnvironment("UNNAMED_OUTFIT_REPORT") == "1")
            GD.Print($"UNNAMED outfit: {_person.Model} chest={worn.Chest ?? "default"} feet={worn.Feet ?? "default"} loaded={dressed}/{garments.Count} [{string.Join(", ", garments)}]");
        CharacterMaterials.ShareLodBox(_model.Skeleton);
        KeepLast(_model.Skeleton);
    }

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
        if (HeldWeapon.Mount(_art, weapon.Model, grip, VisualOptions.ComparisonCast is not null ? 1 / _uniformScale : 1) is not { } model)
            return;
        model.Name = "Held";
        bone.AddChild(model);
        _held = model;
        _haft = weapon.Family == "polearm" && !off ? Haft(model) : null;
        Relax();
    }

    // A polearm in both hands (Phase B demo): the other hand on the haft ahead of the holding one, by the engine's two-bone IK on the
    // other arm, while the body stands to it or strikes with it. The two-handed clips' hands hold nothing (the source rigs carried no
    // weapon), so their hands came apart on the haft.
    private Vector3? _haft;
    private TwoBoneIK3D? _haftIk;
    private Node3D? _haftTarget, _haftPole;
    private float _haftWeight;
    private float _offSide;

    /// <summary>Where the other hand holds a polearm, in the weapon's own space: along its long axis from the grip toward the head.</summary>
    private static Vector3 Haft(Node3D model)
    {
        var box = ArtGallery.Bounds(model);
        var grip = model.FindChild(HeldWeapon.GripSocket, true, false) is Node3D socket ? ArtLibrary.Relative(model, socket).Origin : Vector3.Zero;
        int axis = box.Size.X >= box.Size.Y && box.Size.X >= box.Size.Z ? 0 : box.Size.Y >= box.Size.Z ? 1 : 2;
        float low = box.Position[axis], high = box.End[axis];
        float toward = high - grip[axis] >= grip[axis] - low ? 1f : -1f;
        var along = Vector3.Zero;
        along[axis] = toward * Mathf.Min(0.45f, 0.5f * Mathf.Abs((toward > 0 ? high : low) - grip[axis]));
        return grip + along;
    }

    /// <summary>The other arm's IK, made the first time a polearm is held (null on a rig without the arm's bones).</summary>
    private TwoBoneIK3D? HaftIk()
    {
        if (_haftIk is not null)
            return _haftIk;
        string main = _hand?.BoneName.StartsWith("SOCK_", StringComparison.Ordinal) == true ? _hand.BoneName[5..] : _hand?.BoneName ?? "";
        string side = BoneNames.Side(main) == "R" ? "L" : "R";
        var skeleton = _model.Skeleton;
        if (BoneNames.Resolve(skeleton, "upper_arm." + side) is not { } upper || BoneNames.Resolve(skeleton, "forearm." + side) is not { } fore
            || BoneNames.Resolve(skeleton, "hand." + side) is not { } offHand)
            return null;
        _offSide = side == "L" ? 1f : -1f;   // the body faces +Z: its left is +X
        _haftTarget = new Node3D { Name = "HaftTarget" };
        _haftPole = new Node3D { Name = "HaftElbow" };
        var ik = new TwoBoneIK3D { Name = "HaftIK", Influence = 0 };
        skeleton.AddChild(ik);
        skeleton.AddChild(_haftTarget);
        skeleton.AddChild(_haftPole);
        ik.SetSettingCount(1);
        ik.SetRootBoneName(0, upper);
        ik.SetMiddleBoneName(0, fore);
        ik.SetEndBoneName(0, offHand);
        ik.SetTargetNode(0, ik.GetPathTo(_haftTarget));
        ik.SetPoleNode(0, ik.GetPathTo(_haftPole));
        return _haftIk = ik;
    }

    private void HoldTheHaft(double delta)
    {
        bool wanted = _haft is not null && _held is not null && _model.Current is { } clip
                      && (clip.StartsWith("spear", StringComparison.Ordinal) || clip.StartsWith("attack", StringComparison.Ordinal));
        if (!wanted && _haftIk is null)
            return;
        if (HaftIk() is not { } ik)
            return;
        _haftWeight = Mathf.MoveToward(_haftWeight, wanted ? 1f : 0f, (float)delta * 7f);
        ik.Influence = _haftWeight;
        if (_haftWeight <= 0 || _haft is not { } haft || _held is not { } held || !held.IsInsideTree())
            return;
        _haftTarget!.GlobalPosition = held.GlobalTransform * haft;
        // The elbow down and out to the other side, from the chest.
        var skeleton = _model.Skeleton;
        int chest = BoneNames.Find(skeleton, "chest");
        var at = chest >= 0 ? skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(chest).Origin : GlobalPosition + Vector3.Up * 1.3f;
        _haftPole!.GlobalPosition = at + GlobalTransform.Basis.X * (0.5f * _offSide) + Vector3.Down * 0.6f;
    }

    private CrouchHeadLift _crouchHead=null!;
    public override void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta)
    {
        if (!_hasDirection && _posed && delta > 0) _moveDirection = (feet - _previousPosition).Normalized();
        _previousPosition = feet;
        if (!_posed) _yaw = facingRadians;
        _posed = true;
        // Standing, a turn of more than about 20 degrees is made at a walking body's pace rather than snapped round (an in-place walk
        // cycle here slid the feet: measured, tools/visual_review/foot_slide.py).
        float behind = Mathf.Abs(Mathf.AngleDifference(_yaw, facingRadians));
        _turning = speedMetresPerSecond < 0.25f && (behind > 0.35f || (_turning && behind > 0.08f));
        float follow = _turning ? 6f : 14f;
        _yaw = VisualOptions.ComparisonCast is not null
            ? _yaw + Mathf.Clamp(Mathf.AngleDifference(_yaw, facingRadians), -6f * (float)delta, 6f * (float)delta)
            : Mathf.LerpAngle(_yaw, facingRadians, 1f - Mathf.Exp(-follow * (float)delta));
        Rotation = new Vector3(0, _yaw, 0);
        Position = feet;
        _crouch = Mathf.MoveToward(_crouch, _crouched ? 1f : 0f, (float)delta * 5f);
        _tuck = Mathf.MoveToward(_tuck, _airborne ? 1f : 0f, (float)delta * 8f);
        Animate(speedMetresPerSecond, delta);
        _crouchHead.Crouched=_crouched && _model.Current is "crouch_idle" or "crouch_walk" or "crouch_move";
        HoldTheHaft(delta);
        if (_posture is not null)
        {
            // A crouch clip already holds the head under the crouch height; the bend is for what has none (a body without
            // crouch clips, a blow struck crouched).
            _posture.Crouch = _model.Current is "crouch_idle" or "crouch_walk" or "captive_kneel" ? 0 : _crouch;
            _posture.Tuck = _tuck;
        }
    }

    /// <summary>The pace a gait clip was authored at: the bindings' (a retargeted clip's own), or the game's clips' default.</summary>
    private float Pace(string state, float fallback) => _paces.GetValueOrDefault(state, fallback) * _uniformScale;

    private float _uniformScale = 1;
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
    // Coming to rest: a gait that has just ended settles through the "stop" clip once (-1 = not stopping).
    private double _stopping = -1;
    private bool _wasGaiting;
    private bool _turning;

    /// <summary>How long the take-off clip plays before the airborne loop: the simulation's rise is 0.42 s (config.base_speeds).</summary>
    private const double TakeOff = 0.35;

    /// <summary>
    /// The playback-rate range for a gait clip: wide enough that a clip reaches the body's speed (the simulation's speeds are
    /// authoritative; a walk clip measured at 0.79 m/s plays at 2x for the 1.6 m/s walk) - past it the feet would slide.
    /// </summary>
    /// <remarks>
    /// READ THIS BEFORE TOUCHING THE CEILING. A clip's <c>pace</c> (art_bindings.json, measured by
    /// tools/asset_pipeline/charprod2/clip_pace.py as the planted foot's own speed) IS the ground speed at which the boot
    /// would sit still, so the rate that locks the foot is exactly <c>speed / pace</c>, and any clamp below it leaves the
    /// boot travelling at <c>speed - rate * pace</c> in the world. Measured 2026-09-29 (docs/phase_b/RUN_CYCLE_ROUND.md):
    /// the production player's run clip has a pace of 2.37 m/s while the run is 3.17 m/s, so at the 2.2 ceiling the boot
    /// could not be slower than 3.17 - 2.2 * 2.37 = -2.04 m/s; at the 8 m/s the sprint reaches, foot_slide.py reads a
    /// 3.57 m/s median at the stillest moment - 45 % of the body's own speed, and the worst figure the project's own
    /// instrument has ever recorded. Raising this ceiling was TRIED and measured this round (to 4f, with the paces
    /// re-derived from content/config/base_speeds.yaml): the foot's own speed at the 5th percentile went from 0.237 to
    /// 0.291 of the body's, and the share of the cycle spent near-planted from 17.4 % to 10.0 % - the right direction, but
    /// the clip's cadence is then over-driven instead, so it is a trade and not a fix. The real levers, in order, are the
    /// clip's own pace (re-derive it, as the <c>standard</c> cast already was: run 2.975 against a 3.2 m/s run) and the
    /// sprint speed itself, which at 8 m/s no 3.47 m/s clip can follow at any plausible cadence.
    /// </remarks>
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
        if (speed > .25f || _downed || _talking || _airborne || (_stance.Holds != Held.Nothing || _equipped is not null)) _idles.Interrupt();
        if (_waking is { } waking)
        {
            if (m.Has("get_up"))
                m.Hold("get_up", waking, 0.05f);
            else
                m.Hold("death", 1f - waking, 0.05f);
            return;
        }
        if (_downed)
        {
            m.Play("death", 0.25f);
            return;
        }
        if (speed < .25f && _restPose is { } resting)
        {
            _idles.Interrupt();
            // The native get-up starts on the back; lie_to_idle starts face-down.
            if (resting == "get_up") m.Hold(resting, 0, .3f);
            else if (resting == "captive_kneel") m.Hold(resting, .5f, .3f);
            else m.Play(_talking && m.Has("sit_talk") ? "sit_talk" : resting, .3f);
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
        // Aloft on spread wings (Kal): a held pose - the body hangs under the wings - never a gait or the fall's flailing. A body with its
        // own glide clip plays it; any other holds its idle still.
        if (_gliding)
        {
            _wasAirborne = true;
            if (m.Has("glide"))
                m.Play("glide", 0.25f);
            else if (m.Has("idle"))
                m.Hold("idle", 0.5f, 0.25f);
            return;
        }
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
        // Fishing has no clip of its own in the art root, so the cast plays the clip the cast state names and the reel the one interact
        // names, each once; the wait between them is an idle and the line's own status is the HUD's.
        if (_fishing > 0 && _fishKind is ActivityKind.FishCast or ActivityKind.FishReel)
        {
            _fishing += delta;
            string fish = _fishKind == ActivityKind.FishCast ? "fish_cast" : "fish_reel";
            if (!m.Has(fish))
                fish = _fishKind == ActivityKind.FishCast ? "cast" : "interact";
            if (_fishing < m.Length(fish) && speed < 0.25f && m.Has(fish))
            {
                m.Play(fish, 0.15f, 1f, _fishing - delta <= 0.001);
                return;
            }
            _fishing = 0;
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
            var local = GlobalBasis.Inverse() * _moveDirection * speed;
            if (m.MoveBlend(new Vector2(local.X, local.Z), delta, _paces, _uniformScale)) return;
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
            _wasGaiting = true;      // so that when this gait ends the body settles instead of snapping
            _stopping = -1;
            return;
        }
        _gait = "walk";
        // Coming to rest from a gait: settle through the "stop" clip once, then stand. Without it a walk
        // at about 1.4 m/s cut straight into the idle stance over 0.3 s - the legs went from a stride to
        // standing with nothing in between, which is the snap a stop clip exists to remove. The clip is
        // authored short (charstd/clip_stop.py: a gait eased into the standing pose over about half a
        // second) and played once at the rig's own rate.
        if (_wasGaiting && m.Has("stop"))
            _stopping = 0;
        _wasGaiting = false;
        if (_stopping >= 0)
        {
            _stopping += delta;
            if (_stopping < m.Length("stop"))
            {
                m.Play("stop", 0.08f, 1f, _stopping - delta <= 0.001);
                return;
            }
            _stopping = -1;
        }
        // Turning in place: a step, not a slide. Pose() owns the body's rotation, so the clip carries only
        // the footwork - which is how this is a turn state at all, without the double-turn that a clip
        // yawing its own root would produce on top of the paced follow above.
        if (_turning && m.Has("turn"))
        {
            m.Play("turn", 0.15f);
            return;
        }
        // An equipped weapon alone is not a fight: use its ready stance only while combat is active.
        string ready = s.Holds switch { Held.Sword => "sword_ready", Held.Spear => "spear_ready", Held.Bow => "bow_ready", _ => "" };
        if (s.Ready && ready.Length > 0 && m.Has(ready))
        {
            m.Play(ready, 0.3f);
            return;
        }
        m.Play(_equipped is null ? _idles.Step(delta, _idleRole, m.Has, m.Length) : "idle", .4f);
    }
}

