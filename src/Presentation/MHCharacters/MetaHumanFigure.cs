using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.Presentation.Art;
using System.Security.Cryptography;
using System.Text;

namespace UNNAMED.Presentation.MHCharacters;

/// <summary>Native MetaHuman presentation. Simulation still owns movement and equipment.</summary>
public sealed partial class MetaHumanFigure : Figure
{
    public static string AssetRoot => System.Environment.GetEnvironmentVariable("OTHERREACH_MH_CHARACTERS_ROOT")
        ?? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "mh_characters");
    public static bool Available => System.IO.File.Exists(System.IO.Path.Combine(AssetRoot, "catalog.json"));
    public static string PlayerModel(string? preset) => preset?.Contains("veth_f", StringComparison.Ordinal) == true ? "mh:ada" : "mh:mateo";
    private Node3D _native = null!;
    private Godot.Collections.Dictionary _configuration = new();
    private Godot.Collections.Dictionary _resources = new();
    private bool _crouched;
    private CombatStance _stance = CombatStance.AtRest;
    private ArtLibrary _art = null!;
    private ArtBindings _bindings = null!;
    private BoneAttachment3D? _hand;
    private Node3D? _held;
    private string? _heldId;
    public string CharacterId { get; private set; } = "";
    public override float Crouch => _crouched ? 1 : 0;
    public override string ClipState => "idle (animation handoff pending)";

    public static MetaHumanFigure? Create(ArtLibrary art, ArtBindings bindings, string personId, string? model = null)
    {
        if (!Available) return null;
        var catalog = Json.ParseString(System.IO.File.ReadAllText(System.IO.Path.Combine(AssetRoot, "catalog.json"))).AsGodotDictionary();
        string id;
        if (model?.StartsWith("mh:", StringComparison.Ordinal) == true) id = model[3..];
        else if (personId == "player") id = "mateo";
        else
        {
            var npcs = catalog["npcs"].AsGodotArray();
            if (npcs.Count == 0) throw new InvalidDataException("MetaHuman NPC roster is empty");
            var assignments = catalog.ContainsKey("assignments") ? catalog["assignments"].AsGodotDictionary() : new();
            id = assignments.ContainsKey(personId) ? assignments[personId].AsString()
                : npcs[(int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(personId))) % (uint)npcs.Count)].AsString();
        }
        var characters = catalog["characters"].AsGodotDictionary();
        if (!characters.ContainsKey(id)) throw new InvalidDataException($"Missing MetaHuman catalog entry {id}");
        return new MetaHumanFigure { CharacterId = id, _configuration = characters[id].AsGodotDictionary(), _resources = ReferenceEquals(art, ArtLibrary.Empty) ? new() : art.MetaHumanResources, _art = art, _bindings = bindings };
    }

    public override void _Ready()
    {
        _native = GD.Load<GDScript>("res://MHCharacters/NativeCharacter.gd").New().As<Node3D>();
        _native.Name = "Native";
        AddChild(_native);
        if (!_native.Call("configure", AssetRoot, _configuration, _resources).AsBool())
            throw new InvalidDataException($"Native MetaHuman {CharacterId} failed to load");
        var skeleton = _native.Call("body_skeleton").As<Skeleton3D>();
        _hand = new BoneAttachment3D { BoneName = "hand_r" };
        skeleton.AddChild(_hand);
    }

    public override void SetStance(CombatStance stance) => _stance = stance;
    public override void SetPosture(bool crouched, bool airborne) => _crouched = crouched;
    public override void SetFirstPerson(bool firstPerson) => _native.Call("set_shadow_only", firstPerson);
    public override void Pose(Vector3 feet, float facingRadians, float speedMetresPerSecond, double delta)
    {
        Position = feet;
        Rotation = new Vector3(0, facingRadians, 0);
        // Idle only until the reviewed native AnimationLibrary is handed over. No procedural rig edits.
    }
    public override Vector3? Head => IsInsideTree() ? _native.Call("bone_position", "head").AsVector3() + Vector3.Up * .06f : null;
    public override Vector3? Foot(string side) => IsInsideTree() ? _native.Call("bone_position", side == "L" ? "foot_l" : "foot_r").AsVector3() : null;
    public override Vector3? CastPoint => _hand?.GlobalPosition;
    public override void Hold(string? itemDefId)
    {
        if (_heldId == itemDefId || _hand is null) return;
        _heldId = itemDefId;
        _held?.QueueFree();
        _held = null;
        if (itemDefId is not null && _bindings.Weapons.GetValueOrDefault(itemDefId) is { } weapon)
        {
            _held = HeldWeapon.Mount(_art, weapon.Model, Transform3D.Identity);
            if (_held is not null) _hand.AddChild(_held);
        }
    }
}
