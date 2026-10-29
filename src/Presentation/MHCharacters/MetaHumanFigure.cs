using Godot;
using UNNAMED.Domain;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Art;
using UNNAMED.World.Runtime;
using System.Security.Cryptography;
using System.Text;

namespace UNNAMED.Presentation.MHCharacters;

/// <summary>Native MetaHuman presentation. Simulation still owns movement and equipment.</summary>
public sealed partial class MetaHumanFigure : Figure
{
    public override bool HasFootPlantEvents => _native is not null && _native.Get("has_foot_plants").AsBool();
    public const uint FillLayer = 1u << 19;
    public static string AssetRoot => System.Environment.GetEnvironmentVariable("OTHERREACH_MH_CHARACTERS_ROOT")
        ?? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "mh_characters");
    /// <summary>
    /// The native bundle is installed AND this run draws MetaHumans. <c>--visual people=mpfb</c> draws the MakeHuman comparison cast
    /// instead, and every site that prefers a native body here already falls back to the rigged <see cref="Art.SkinnedFigure"/> when
    /// there is none - which is what draws the people when the MetaHuman bundle is not installed at all.
    /// </summary>
    public static bool Available => VisualOptions.ComparisonCast is null
        && System.IO.File.Exists(System.IO.Path.Combine(AssetRoot, "catalog.json"));
    public static string PlayerModel(string? preset) => preset?.Contains("veth_f", StringComparison.Ordinal) == true ? "mh:ada" : "mh:mateo";
    private Node3D _native = null!;
    private Godot.Collections.Dictionary _configuration = new();
    private Godot.Collections.Dictionary _resources = new();
    private bool _crouched, _airborne, _talking, _downed, _wasAirborne;
    private double _climb;
    private double _airTime, _landing, _reaching, _striking;
    private ActivityKind _fishKind;
    private double _fishing;
    private long _swingAt = -1;
    private double _swinging;
    private bool _swimming;
    private bool _washing;
    private double _washElapsed;
    private string _gait = "walk", _attack = "sword_attack_1";
    private int _attackTurn;
    private CombatPhase _lastPhase = CombatPhase.Idle;
    private CombatStance _stance = CombatStance.AtRest;
    private ArtLibrary _art = null!;
    private ArtBindings _bindings = null!;
    private BoneAttachment3D? _hand, _offHand;
    private Transform3D _grip, _offGrip;
    private WeaponArt? _equipped;
    private Node3D? _held;
    private string? _heldId;
    private FaceDriver? _faceDriver;
    private IdleVariety _idles = null!;
    private string? _idleRole;
    private string? _restPose;
    public override bool SetRestPose(string? clip)
    {
        _restPose = clip is not null && Has(clip) ? clip : null;
        return _restPose is not null;
    }
    private string _identity = "";
    private string? _sex;
    public override void Say(string? line) => _faceDriver?.Say(line);
    public override Vector3? RestContact
    {
        get
        {
            if (_restPose is null) return null;
            var pelvis = _native.Call("bone_position", "pelvis").AsVector3();
            if (_restPose != "lie_to_idle") return pelvis - Vector3.Up * .085f;
            var centre = ((_native.Call("bone_position", "head").AsVector3()) +
                (_native.Call("bone_position", "foot_l").AsVector3()+_native.Call("bone_position", "foot_r").AsVector3())*.5f)*.5f;
            return new Vector3(centre.X,pelvis.Y-.115f,centre.Z);
        }
    }
    public override void SetIdleRole(string? role) => _idleRole = role;

    /// <summary>A climb or a vault just happened (the domain's Climbed event): the body plays the climb pose through once.</summary>
    public void BeginClimb() => _climb = 0.001;
    public override void SetGaze(Vector3? target) => _faceDriver?.Gaze(target is { } at && Head is { } head ? GlobalBasis.Inverse() * (at - head).Normalized() : null);
    private Vector3 _moveDirection;
    private bool _hasDirection, _posed;
    private Vector3 _lastPosition;
    public override void SetMovementDirection(Vector3 worldDirection) { if (worldDirection.LengthSquared() > .001f) _moveDirection = worldDirection.Normalized(); _hasDirection = true; }
    private bool _npc;
    private int _npcPerformanceStage;
    private double _pendingAnimation;
    private VisibleOnScreenNotifier3D? _onScreen;
    public string CharacterId { get; private set; } = "";
    public override float Crouch => _crouched ? 1 : 0;
    public override string ClipState => _native is not null ? _native.Call("clip_state").AsString() : "native base pose";

    public static MetaHumanFigure? Create(ArtLibrary art, ArtBindings bindings, string personId, string? model = null, string? sex = null)
    {
        if (!Available) return null;
        var catalog = Json.ParseString(System.IO.File.ReadAllText(System.IO.Path.Combine(AssetRoot, "catalog.json"))).AsGodotDictionary();
        string id;
        if (model?.StartsWith("mh:", StringComparison.Ordinal) == true) id = model[3..];
        else if (personId == "player") id = "mateo";
        else
        {
            var entries = catalog["characters"].AsGodotDictionary();
            var npcs = catalog["npcs"].AsGodotArray().Select(v => v.AsString())
                .Where(key => Ui.Models.CharacterSex.Matches(sex, entries[key].AsGodotDictionary()["sex"].AsString())).ToArray();
            if (npcs.Length == 0) throw new InvalidDataException("MetaHuman NPC roster is empty");
            var assignments = catalog.ContainsKey("assignments") ? catalog["assignments"].AsGodotDictionary() : new();
            if (bindings.NamedCharacters.TryGetValue(personId, out var named) && !npcs.Contains(named.Native))
                throw new InvalidDataException("Named native identity has the wrong sex: " + personId);
            id = named is not null ? named.Native : assignments.ContainsKey(personId) && npcs.Contains(assignments[personId].AsString()) ? assignments[personId].AsString()
                : npcs[(int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(personId))) % (uint)npcs.Length)];
        }
        var characters = catalog["characters"].AsGodotDictionary();
        if (!characters.ContainsKey(id)) throw new InvalidDataException($"Missing MetaHuman catalog entry {id}");
        var configuration = characters[id].AsGodotDictionary();
        if (bindings.NamedCharacters.TryGetValue(personId, out var look) && look.Hood is { } hood) configuration["hood"] = hood;
        return new MetaHumanFigure { _sex = sex, _identity = personId, CharacterId = id, _configuration = configuration, _resources = ReferenceEquals(art, ArtLibrary.Empty) ? new() : art.MetaHumanResources, _art = art, _bindings = bindings };
    }

    public override void _Ready()
    {
        _native = GD.Load<GDScript>("res://MHCharacters/NativeCharacter.gd").New().As<Node3D>();
        _native.Name = "Native";
        AddChild(_native);
        if (!_native.Call("configure", AssetRoot, _configuration, _resources).AsBool())
            throw new InvalidDataException($"Native MetaHuman {CharacterId} failed to load");
        _native.Connect("foot_planted",Callable.From<string,string,double>(EmitFootPlant));
        PeopleAppearance.EnsureSex(_native, _sex);
        _idles = new IdleVariety(_identity);
        _faceDriver = FaceDriver.Attach(_native, _identity);
        if (_faceDriver is not null) AddChild(_faceDriver);
        // Include new/replaced players and NPCs without changing native mesh buffers or materials.
        foreach (var mesh in _native.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            mesh.Layers |= FillLayer;
        if (_npc)
        {
            _onScreen = new VisibleOnScreenNotifier3D { Aabb = new Aabb(-1, -.2f, -1, 2, 2.6f, 2) };
            AddChild(_onScreen);
            _native.Call("configure_npc_performance", _npcPerformanceStage);
            Play("idle", 0);
            _native.Call("advance_animation", _idles.StartPhase * Length("idle"));
        }
        var skeleton = _native.Call("body_skeleton").As<Skeleton3D>();
        _crouchHead=new CrouchHeadLift { Facing=this,Influence=0 };
        skeleton.AddChild(_crouchHead);
        skeleton.SkeletonUpdated += () => { if(_crouchHead.Influence>0) _native.Call("sync_modified_pose"); };
        _hand = new BoneAttachment3D { BoneName = "hand_r" };
        skeleton.AddChild(_hand);
        _offHand = new BoneAttachment3D { BoneName = "hand_l" };
        skeleton.AddChild(_offHand);
        var rotations = _native.Call("weapon_grip_rotations").AsGodotDictionary();
        _grip = HeldWeapon.ClosedGrip(skeleton, "hand_r", rotations.Keys.ToDictionary(k => k.AsInt32(), k => rotations[k].AsQuaternion()));
        _offGrip = NativeGrip(skeleton, "l");
    }

    public override void SetStance(CombatStance stance) => _stance = stance;
    public override void SetPosture(bool crouched, bool airborne) { _crouched = crouched; _airborne = airborne; }
    public override void SetTalking(bool talking) => _talking = talking;
    public override bool SetDowned(bool downed) { _downed = downed; return Has("death"); }
    public override void Strike() => _striking = .001;
    public override void Interact() => _reaching = .001;

    /// <summary>
    /// The line in the water and the picks in a lock: the cast, the reel and the pick each play once, and the wait between the cast and its
    /// bite has no clip of its own - the body stands. One broken off simply stops where it is.
    /// </summary>
    public override void Fishing(ActivityKind kind)
    {
        if (kind == _fishKind)
            return;
        _fishKind = kind;
        _fishing = kind is ActivityKind.FishCast or ActivityKind.FishReel or ActivityKind.PickLock ? .001 : 0;
    }

    /// <summary>The pick into a node: each swing the simulation counts plays the clip once, so a later start tick is a new swing.</summary>
    public override void Swinging(long startedTick)
    {
        if (startedTick == _swingAt) return;
        _swingAt = startedTick;
        _swinging = .001;
    }

    /// <summary>In the water or out of it: the body's height carries it low in the water, and the pose is the bindings' swim state.</summary>
    public override void Swimming(bool swimming) => _swimming = swimming;

    /// <summary>Washing at a bank for as long as the simulation holds it, from the moment it began: the one clip plays once.</summary>
    public override void Washing(bool washing)
    {
        if (washing == _washing) return;
        _washing = washing;
        _washElapsed = washing ? .001 : 0;
    }
    public override void SetFirstPerson(bool firstPerson) => _native.Call("set_shadow_only", firstPerson);
    private CrouchHeadLift _crouchHead=null!;
    public override void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta)
    {
        if (!_hasDirection && _posed && delta > 0)
            _moveDirection = (feet - _lastPosition).Normalized();
        _lastPosition = feet;
        Position = feet;
        float turn = _posed ? Mathf.Clamp(Mathf.AngleDifference(Rotation.Y, facingRadians), -6f * (float)delta, 6f * (float)delta) : facingRadians;
        Rotation = new Vector3(0, _posed ? Rotation.Y + turn : facingRadians, 0);
        _posed = true;
        if (_npc && GetViewport().GetCamera3D() is { } camera)
        {
            float distance = camera.GlobalPosition.DistanceTo(GlobalPosition + Vector3.Up);
            _native.Call("update_npc_shadows", distance);
            if (_npcPerformanceStage >= 2)
            {
                _pendingAnimation += delta;
                if (distance > 102 || !IsVisibleInTree() || _onScreen?.IsOnScreen() != true) return;
                double interval = distance < 8 ? 1.0 / 30 : distance < 20 ? 1.0 / 15 : 1.0 / 5;
                if (_pendingAnimation < interval) return;
                delta = Math.Min(_pendingAnimation, .25);
                _pendingAnimation = 0;
            }
        }
        Animate(speedMetresPerSecond, delta);
        _native.Call("advance_animation", delta);
        _crouchHead.Crouched=_crouched && ClipState is "crouch_idle" or "crouch_walk" or "crouch_move";
    }
    public void ConfigureNpcPerformance(int stage)
    {
        _npc = true;
        _npcPerformanceStage = stage;
        _pendingAnimation = 0;
        if (_native is not null) _native.Call("configure_npc_performance", stage);
    }
    public override Vector3? Head => IsInsideTree() ? _native.Call("bone_position", "head").AsVector3() + Vector3.Up * .06f : null;
    public override Vector3? Foot(string side) => IsInsideTree() ? _native.Call("bone_position", side == "L" ? "foot_l" : "foot_r").AsVector3() : null;
    public override Vector3? CastPoint => _hand?.GlobalPosition;
    public override void LightInOffHand(long radiusMm) => HandLight.Show(_offHand, radiusMm);
    public override void Hold(string? itemDefId)
    {
        if (_heldId == itemDefId) return;
        _heldId = itemDefId;
        Equip(itemDefId is null ? null : _bindings.Weapons.GetValueOrDefault(itemDefId));
    }

    public void Equip(WeaponArt? weapon)
    {
        if (_equipped == weapon && (weapon is null || _held is not null)) return;
        _equipped = weapon;
        _held?.QueueFree();
        _held = null;
        _native.Call("set_weapon_grip", false);
        if (weapon is null) return;
        var hand = weapon.OffHand ? _offHand : _hand;
        if (hand is null) return;
        _held = HeldWeapon.Mount(_art, weapon.Model, weapon.OffHand ? _offGrip : _grip);
        if (_held is not null)
        {
            hand.AddChild(_held);
            _native.Call("set_weapon_grip", !weapon.OffHand && weapon.Family == "sword");
        }
    }

    private static Transform3D NativeGrip(Skeleton3D skeleton, string side)
    {
        // A rigid socket in the native hand's space, using its own palm landmarks.
        // Reads rest transforms only; creates no bones and changes no pose/rest/bind data.
        var hand = skeleton.GetBoneGlobalRest(skeleton.FindBone("hand_" + side)).AffineInverse();
        Vector3 Point(string bone) => hand * skeleton.GetBoneGlobalRest(skeleton.FindBone(bone + "_" + side)).Origin;
        var middle = Point("middle_01");
        return HeldWeapon.Frame(middle * .7f, Point("index_01") - Point("pinky_01"), middle);
    }

    private bool Has(string state) => _native is not null && _native.Call("has_clip", state).AsBool();
    private double Length(string state) => _native.Call("clip_length", state).AsDouble();
    private void Play(string state, float blend = .2f, float rate = 1, bool restart = false) => _native.Call("play_clip", state, blend, rate, restart);
    private void HoldClip(string state, float through, float blend = .1f) => _native.Call("hold_clip", state, through, blend);
    private float Pace(string state, float fallback)
    {
        if (_configuration.TryGetValue("animation_paces", out var value) && value.AsGodotDictionary().TryGetValue(state, out var pace))
            return Math.Max(.1f, pace.AsSingle());
        return fallback;
    }

    // The existing game's phase/gait selection, feeding only Epic-retargeted native clips.
    // A separate authored right-hand grip blends only while a sword is held.
    private void Animate(float speed, double delta)
    {
        if (!Has("idle")) return;
        // D6: a climb or a vault holds the climb pose for the length of the clip, then the gait resumes.
        if (_climb > 0 && Has("climb"))
        {
            _climb += delta;
            if (_climb < 1.0)
            {
                Play("climb", .08f);
                return;
            }
            _climb = 0;
        }
        if (speed > .25f || _downed || _talking || _airborne || (_stance.Holds != Held.Nothing || _equipped is not null)) _idles.Interrupt();
        if (_downed) { Play("death", .25f); return; }
        if (speed < .25f && _restPose is { } resting)
        {
            _idles.Interrupt();
            if (resting == "lie_to_idle") HoldClip(resting, 0, .3f);
            else Play(_talking && Has("sit_talk") ? "sit_talk" : resting, .3f);
            return;
        }
        if (_striking > 0)
        {
            _striking += delta;
            if (_striking < Length("attack") && Has("attack")) { Play("attack", .1f); return; }
            _striking = 0;
        }
        string? action = _stance switch
        {
            { Casting: true } => "cast",
            { Holds: Held.Sword } => "sword_attack",
            { Holds: Held.Spear } => "spear_thrust",
            { Holds: Held.Bow } => "bow_draw",
            _ => null,
        };
        if (_stance.Phase == CombatPhase.Windup && _lastPhase != CombatPhase.Windup && action is not null)
        {
            var variants = new List<string>();
            for (int i = 1; Has($"{action}_{i}"); i++) variants.Add($"{action}_{i}");
            _attack = variants.Count == 0 ? action : variants[_attackTurn++ % variants.Count];
        }
        _lastPhase = _stance.Phase;
        string? clip = action is not null && _attack.StartsWith(action, StringComparison.Ordinal) ? _attack : action;
        float t = Mathf.Clamp(_stance.Progress, 0, 1);
        float through = _stance.Phase switch
        {
            CombatPhase.Windup => .45f * t,
            CombatPhase.Active => .45f + .25f * t,
            CombatPhase.Recovery => .7f + .3f * t,
            _ => -1,
        };
        if (through >= 0 && clip is not null && Has(clip)) { HoldClip(clip, through); return; }
        if (_stance.Phase == CombatPhase.Dodge && Has("dodge")) { HoldClip("dodge", t, .08f); return; }
        if (_stance.Phase == CombatPhase.Staggered && Has("hit")) { Play("hit", .1f); return; }
        if (_stance.Guarding && _stance.Phase == CombatPhase.Idle)
        {
            string guard = _stance.Holds == Held.Bow ? "bow_ready" : _stance.Holds == Held.Spear ? "spear_ready" : "sword_block";
            if (Has(guard)) { HoldClip(guard, .5f, .15f); return; }
        }
        if (_airborne && Has("fall"))
        {
            _airTime = _wasAirborne ? _airTime + delta : 0;
            _wasAirborne = true;
            if (_airTime < .22 && Has("jump_start")) Play("jump_start", .08f, 1, _airTime == 0);
            else Play("fall", .15f);
            return;
        }
        if (_wasAirborne) { _wasAirborne = false; _landing = Has("land") ? .001 : 0; }
        if (_landing > 0)
        {
            _landing += delta;
            if (_landing < Math.Min(.6, Length("land")) && speed < 1.5f) { Play("land", .08f, 1, _landing - delta <= .001); return; }
            _landing = 0;
        }
        // Fishing and lockpicking have no clip of their own in the art root, so the cast plays the clip the cast state names, the reel and
        // the pick the one interact names, each once; the wait between a cast and its bite is an idle and the line's own status is the HUD's.
        if (_fishing > 0 && _fishKind is ActivityKind.FishCast or ActivityKind.FishReel or ActivityKind.PickLock)
        {
            _fishing += delta;
            string fish = _fishKind switch
            {
                ActivityKind.FishCast => "fish_cast",
                ActivityKind.FishReel => "fish_reel",
                _ => "pick_lock",
            };
            if (!Has(fish))
                fish = _fishKind == ActivityKind.FishCast ? "cast" : "interact";
            if (_fishing < Length(fish) && speed < .25f && Has(fish))
            {
                Play(fish, .15f, 1f, _fishing - delta <= .001);
                return;
            }
            _fishing = 0;
        }
        // A swing at a node has no clip of its own in the art root either, so it plays the clip the mine_swing state names - the sword's
        // overhead chop, which is the honest reuse for a pick - once for each swing the simulation counts.
        if (_swinging > 0)
        {
            _swinging += delta;
            if (_swinging < Length("mine_swing") && speed < .25f && Has("mine_swing"))
            {
                Play("mine_swing", .1f, 1f, _swinging - delta <= .001);
                return;
            }
            _swinging = 0;
        }
        // A wash is a rest at the bank and has no clip of its own either, so it plays the clip the wash state names - the interact's - once.
        if (_washElapsed > 0)
        {
            _washElapsed += delta;
            if (_washElapsed < Length("wash") && speed < .25f && Has("wash"))
            {
                Play("wash", .15f, 1f, _washElapsed - delta <= .001);
                return;
            }
            _washElapsed = 0;
        }
        if (_reaching > 0)
        {
            _reaching += delta;
            if (_reaching < Length("interact") && speed < .25f && Has("interact")) { Play("interact", .15f, 1, _reaching - delta <= .001); return; }
            _reaching = 0;
        }
        if (_talking && speed < .3f && Has("talk")) { Play("talk", .3f); return; }
        // In the water the body swims, and the art root has no swim clip: the swim state bound here is the crouch's low travelling loop, at
        // the pace the simulation swims at - half a walk - and the body's own height, floating its feet at the surface, is the rest of it.
        if (_swimming && Has("swim"))
        {
            Play("swim", .25f, Mathf.Clamp(speed / Pace("swim", .8f), .35f, 2.2f));
            return;
        }
        if (_crouched && Has("crouch_idle"))
        {
            if (speed > .25f && Has("crouch_walk")) Play("crouch_walk", .25f, Mathf.Clamp(speed / Pace("crouch_walk", 1.6f), .35f, 2.2f));
            else Play("crouch_idle", .3f);
            return;
        }
        if (speed > .25f && Has("walk"))
        {
            var local = GlobalBasis.Inverse() * _moveDirection * speed;
            if (_native.Call("move_blend", new Vector2(local.X, local.Z), delta).AsBool()) return;
            string gait = speed > 2.2f && Has("run") ? "run" : "walk";
            Play(gait, .25f, speed / Pace(gait, gait == "walk" ? 1.4f : 3.2f));
            return;
        }
        _gait = "walk";
        string ready = _stance.Holds switch { Held.Sword => "sword_ready", Held.Spear => "spear_ready", Held.Bow => "bow_ready", _ => "idle" };
        if (ready == "idle" && _equipped is null) ready = _idles.Step(delta, _idleRole, Has, Length);
        Play(Has(ready) ? ready : "idle", .4f);
    }

}

