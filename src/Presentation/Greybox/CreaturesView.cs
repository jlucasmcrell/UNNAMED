// UNNAMED Presentation - creatures in the greybox: a placeholder body per archetype, with readable tells (M3c, M3d)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain;
using UNNAMED.Domain.Combat;
using UNNAMED.Domain.Spatial;
using UNNAMED.World;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws every creature the simulation reports, between its last two ticks, so a 20 Hz body moves smoothly. Each phase of
/// an attack reads at a glance: the windup rears back and lights the striking part (the tell), the active window lunges,
/// a stagger sways blue, and the dead lie still. A mark over the head says what it knows: "?" when it has noticed
/// something, "!" when it is after the player, "z" asleep. Timing comes from the simulation's phases, never from a clip.
/// </summary>
public partial class CreaturesView : Node3D
{
    private readonly Dictionary<EntityId, CreatureBody> _figures = new();
    private long _tick = -1;
    private Art.ArtLibrary _art = Art.ArtLibrary.Empty;
    private Art.ArtBindings _bindings = Art.ArtBindings.Empty;

    /// <summary>The asset library and its bindings (the Phase-1 asset integration); a creature without a usable model stays greybox.</summary>
    public void Bind(Art.ArtLibrary art, Art.ArtBindings bindings)
    {
        _art = art;
        _bindings = bindings;
    }

    public void Draw(Simulation simulation, double alpha, double delta)
    {
        bool advanced = simulation.WorldTick != _tick;
        _tick = simulation.WorldTick;
        var views = simulation.Creatures;
        foreach (var creature in views)
        {
            if (!_figures.TryGetValue(creature.Id, out var figure))
            {
                var definition = simulation.Setup.Combat.Creatures[creature.DefId];
                var skinned = Art.SkinnedCreature.Create(_art, _bindings, definition);
                string? model = _bindings.Creatures.GetValueOrDefault(creature.DefId)?.Model;
                if (skinned is not null)
                    _art.Coverage.Resolved("creature", creature.DefId, model!);
                else
                    _art.Coverage.Fallback("creature", creature.DefId, model is null ? "no binding: greybox blocks" : $"{_art.Why(model) ?? "not drawn"}: greybox blocks", model);
                figure = (CreatureBody?)skinned ?? Greybox(definition);
                figure.Name = creature.Id.Value;
                AddChild(figure);
                figure.Place(creature.Body);
                _figures[creature.Id] = figure;
            }
            if (advanced)
                figure.Advance(creature.Body);
            figure.Pose(creature, (float)alpha, delta);
        }
        // A creature that came back is a new body; the old one's figure goes.
        if (advanced && _figures.Count > views.Length)
        {
            var live = views.Select(v => v.Id).ToHashSet();
            foreach (var id in _figures.Keys.Where(id => !live.Contains(id)).ToList())
            {
                _figures[id].QueueFree();
                _figures.Remove(id);
            }
        }
    }

    private static CreatureFigure Greybox(CreatureDefinition definition)
    {
        var figure = new CreatureFigure();
        figure.Build(definition);
        return figure;
    }

    /// <summary>After a load or a new game, every creature is placed afresh.</summary>
    public void Reset()
    {
        foreach (var figure in _figures.Values)
            figure.QueueFree();
        _figures.Clear();
        _tick = -1;
    }
}

internal enum BodyPlan
{
    Quadruped,
    Biped,
    Arachnid,
}

/// <summary>
/// Placeholder looks until the asset pipeline delivers real bodies: a body plan, a hide colour and proportions per
/// archetype. Size comes from the creature's authored body radius; the look carries nothing the simulation reads.
/// </summary>
internal sealed record PlaceholderLook(BodyPlan Plan, Color Hide, float Width = 1f, float Height = 1f, float Legs = 1f, bool Blade = false)
{
    public static PlaceholderLook For(CreatureDefinition definition) => definition.Id switch
    {
        "creature.beast.ash_ember_hound" => new(BodyPlan.Quadruped, new Color(0.55f, 0.25f, 0.12f), Width: 0.8f, Legs: 1.15f),
        "creature.beast.bristleback_boar" => new(BodyPlan.Quadruped, new Color(0.33f, 0.25f, 0.19f), Width: 1.45f, Height: 1.3f, Legs: 0.75f),
        "creature.beast.cave_hunting_spider" => new(BodyPlan.Arachnid, new Color(0.17f, 0.15f, 0.14f)),
        "creature.undead.bone_walker_husk" => new(BodyPlan.Biped, new Color(0.76f, 0.72f, 0.62f), Width: 0.8f, Blade: true),
        "creature.construct.animated_armour" => new(BodyPlan.Biped, new Color(0.48f, 0.5f, 0.54f), Width: 1.3f),
        _ => new(definition.Family is "undead" or "construct" ? BodyPlan.Biped : BodyPlan.Quadruped, new Color(0.46f, 0.45f, 0.43f)),
    };
}

/// <summary>A creature as drawn - greybox or from the asset library. Drawn only; it holds a copy of two ticks' bodies, never the truth.</summary>
public abstract partial class CreatureBody : Node3D
{
    public abstract void Place(Body body);

    public abstract void Advance(Body body);

    public abstract void Pose(CreatureView creature, float alpha, double delta);
}

/// <summary>One creature's greybox body. Drawn only; it holds a copy of two ticks' bodies, never the truth.</summary>
internal sealed partial class CreatureFigure : CreatureBody
{
    private static readonly StandardMaterial3D Dead = new() { AlbedoColor = new Color(0.22f, 0.2f, 0.19f) };
    private static readonly StandardMaterial3D Tell = new() { AlbedoColor = new Color(1f, 0.8f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.1f) };
    private static readonly StandardMaterial3D Eyes = new() { AlbedoColor = new Color(0.9f, 0.15f, 0.1f), EmissionEnabled = true, Emission = new Color(0.8f, 0.1f, 0.05f) };
    private static readonly StandardMaterial3D Calm = new() { AlbedoColor = new Color(0.1f, 0.1f, 0.1f) };
    private static readonly StandardMaterial3D Staggered = new() { AlbedoColor = new Color(0.45f, 0.55f, 0.85f) };
    private static readonly Dictionary<Color, StandardMaterial3D> Hides = new();

    private readonly Node3D _torso = new();
    private readonly List<MeshInstance3D> _hide = new();
    private readonly List<MeshInstance3D> _striking = new();
    private readonly List<Node3D> _legs = new();
    private readonly Label3D _mark = new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        FontSize = 72,
        OutlineSize = 14,
        PixelSize = 0.004f,
        Visible = false,
    };
    private StandardMaterial3D _hideMaterial = null!;
    private MeshInstance3D _eyes = null!;
    private Node3D? _strikingLimb;
    private BodyPlan _plan;
    private float _torsoHeight;
    private float _scale = 1;
    private Vector3 _previous;
    private Vector3 _current;
    private float _yaw;
    private float _gait;
    private double _phase;

    /// <summary>Assemble the body for its archetype; called once, before the figure enters the tree.</summary>
    public void Build(CreatureDefinition definition)
    {
        var look = PlaceholderLook.For(definition);
        _plan = look.Plan;
        if (!Hides.TryGetValue(look.Hide, out _hideMaterial!))
            Hides[look.Hide] = _hideMaterial = new StandardMaterial3D { AlbedoColor = look.Hide, Roughness = 0.9f };
        _scale = definition.RadiusMm / 450f;
        AddChild(_torso);
        AddChild(_mark);
        switch (look.Plan)
        {
            case BodyPlan.Biped:
                BuildBiped(look);
                break;
            case BodyPlan.Arachnid:
                BuildArachnid();
                break;
            default:
                BuildQuadruped(look);
                break;
        }
    }

    private MeshInstance3D Part(Node3D parent, Vector3 size, Vector3 at, bool hide = true)
    {
        var part = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = hide ? _hideMaterial : null };
        parent.AddChild(part);
        if (hide)
            _hide.Add(part);
        return part;
    }

    private void BuildQuadruped(PlaceholderLook look)
    {
        float s = _scale, w = look.Width, h = look.Height;
        float leg = 0.44f * look.Legs * s;
        _torsoHeight = leg + 0.11f * h * s;
        _torso.Position = new Vector3(0, _torsoHeight, 0);
        Part(_torso, new Vector3(0.36f * w, 0.34f * h, 0.95f) * s, Vector3.Zero);
        var head = Part(_torso, new Vector3(0.26f * w, 0.26f * h, 0.36f) * s, new Vector3(0, 0.12f * h, 0.6f) * s);
        _striking.Add(Part(head, new Vector3(0.14f * w, 0.12f, 0.2f) * s, new Vector3(0, -0.05f, 0.26f) * s));
        _eyes = Part(head, new Vector3(0.2f * w, 0.04f, 0.02f) * s, new Vector3(0, 0.05f * h, 0.18f) * s, hide: false);
        Part(_torso, new Vector3(0.08f, 0.08f, 0.4f) * s, new Vector3(0, 0.1f * h, -0.62f) * s).RotationDegrees = new Vector3(25, 0, 0);
        foreach (var hip in new[] { new Vector3(0.14f * w, -0.12f * h, 0.36f), new Vector3(-0.14f * w, -0.12f * h, 0.36f),
                     new Vector3(0.14f * w, -0.12f * h, -0.36f), new Vector3(-0.14f * w, -0.12f * h, -0.36f) })
        {
            var joint = new Node3D { Position = hip * s };
            Part(joint, new Vector3(0.08f * s * Math.Max(1, w * 0.8f), leg, 0.08f * s), new Vector3(0, -leg / 2, 0));
            _torso.AddChild(joint);
            _legs.Add(joint);
        }
        _mark.Position = new Vector3(0, _torsoHeight + 0.6f * h * s, 0);
    }

    private void BuildBiped(PlaceholderLook look)
    {
        float w = look.Width;
        _torsoHeight = 0.95f;
        _torso.Position = new Vector3(0, _torsoHeight, 0);
        foreach (float side in new[] { 0.12f * w, -0.12f * w })
        {
            var hip = new Node3D { Position = new Vector3(side, 0, 0) };
            Part(hip, new Vector3(0.15f * w, 0.9f, 0.15f * w), new Vector3(0, -0.45f, 0));
            _torso.AddChild(hip);
            _legs.Add(hip);
        }
        Part(_torso, new Vector3(0.46f * w, 0.62f, 0.28f * w), new Vector3(0, 0.34f, 0));
        var head = Part(_torso, new Vector3(0.26f * Math.Max(1, w * 0.85f), 0.28f, 0.26f * Math.Max(1, w * 0.85f)), new Vector3(0, 0.82f, 0));
        _eyes = Part(head, new Vector3(0.18f, 0.035f, 0.02f), new Vector3(0, 0.03f, 0.135f * Math.Max(1, w * 0.85f)), hide: false);
        for (int i = 0; i < 2; i++)
        {
            float side = (i == 0 ? -1 : 1) * 0.3f * w;
            var shoulder = new Node3D { Position = new Vector3(side, 0.6f, 0) };
            Part(shoulder, new Vector3(0.11f * w, 0.62f, 0.11f * w), new Vector3(0, -0.31f, 0));
            var fist = Part(shoulder, new Vector3(0.14f * w, 0.14f, 0.14f * w), new Vector3(0, -0.66f, 0));
            _torso.AddChild(shoulder);
            _legs.Add(shoulder);
            if (i == 0)
            {
                // The striking arm: its fist (and a husk's blade) lights through the windup.
                _strikingLimb = shoulder;
                _striking.Add(fist);
                if (look.Blade)
                    _striking.Add(Part(shoulder, new Vector3(0.04f, 0.08f, 0.9f), new Vector3(0, -0.7f, 0.42f)));
            }
        }
        _mark.Position = new Vector3(0, 2.2f, 0);
    }

    private void BuildArachnid()
    {
        float s = _scale;
        _torsoHeight = 0.34f * s;
        _torso.Position = new Vector3(0, _torsoHeight, 0);
        Part(_torso, new Vector3(0.5f, 0.32f, 0.6f) * s, new Vector3(0, 0.04f, -0.22f) * s);
        var front = Part(_torso, new Vector3(0.36f, 0.22f, 0.32f) * s, new Vector3(0, 0, 0.24f) * s);
        _eyes = Part(front, new Vector3(0.2f, 0.05f, 0.02f) * s, new Vector3(0, 0.07f, 0.165f) * s, hide: false);
        _striking.Add(Part(front, new Vector3(0.16f, 0.1f, 0.08f) * s, new Vector3(0, -0.08f, 0.18f) * s));
        foreach (float z in new[] { 0.3f, 0.12f, -0.06f, -0.24f })
        {
            foreach (float side in new[] { 1f, -1f })
            {
                var joint = new Node3D { Position = new Vector3(0.17f * side, 0.02f, z) * s, Rotation = new Vector3(0, 0, side * 1.05f) };
                Part(joint, new Vector3(0.05f, 0.62f, 0.05f) * s, new Vector3(0, -0.31f, 0) * s);
                _torso.AddChild(joint);
                _legs.Add(joint);
            }
        }
        _mark.Position = new Vector3(0, 0.95f * s, 0);
    }

    public override void Place(Body body)
    {
        _previous = _current = Feet(body);
        _yaw = Facing(body);
    }

    public override void Advance(Body body)
    {
        _previous = _current;
        _current = Feet(body);
    }

    public override void Pose(CreatureView creature, float alpha, double delta)
    {
        if (creature.Condition == CreatureCondition.Gone)
        {
            Visible = false;
            return;
        }
        Visible = true;
        var feet = _previous.Lerp(_current, alpha);
        float speed = new Vector2(_current.X - _previous.X, _current.Z - _previous.Z).Length() / 0.05f;
        _yaw = Mathf.LerpAngle(_yaw, Facing(creature.Body), 1f - Mathf.Exp(-16f * (float)delta));
        Position = feet;

        if (!creature.Alive)
        {
            // The dead lie still: a beast on its side, a walker face down, a spider on its back.
            (Rotation, Position) = _plan switch
            {
                BodyPlan.Biped => (new Vector3(Mathf.Pi / 2, _yaw, 0), feet + new Vector3(0, 0.16f, 0)),
                BodyPlan.Arachnid => (new Vector3(0, _yaw, Mathf.Pi), feet + new Vector3(0, _torsoHeight + 0.1f * _scale, 0)),
                _ => (new Vector3(0, _yaw, Mathf.Pi / 2), feet + new Vector3(0, 0.18f * _scale, 0)),
            };
            foreach (var part in _hide)
                part.MaterialOverride = Dead;
            _eyes.MaterialOverride = Calm;
            _mark.Visible = false;
            return;
        }
        Rotation = new Vector3(0, _yaw, 0);
        var hide = creature.Phase == CombatPhase.Staggered ? Staggered : _hideMaterial;
        foreach (var part in _hide)
            part.MaterialOverride = hide;
        foreach (var part in _striking)
            part.MaterialOverride = creature.Phase == CombatPhase.Windup ? Tell : hide;
        _eyes.MaterialOverride = creature.Hostile ? Eyes : Calm;
        Mark(creature);
        if (creature.Asleep)
        {
            // Asleep it lies down, legs folded under (a walker sits slumped).
            _torso.Rotation = Vector3.Zero;
            _torso.Position = new Vector3(0, _plan == BodyPlan.Biped ? 0.45f : _torsoHeight * 0.45f, 0);
            for (int i = 0; i < _legs.Count; i++)
            {
                var joint = _legs[i];
                joint.Rotation = _plan switch
                {
                    BodyPlan.Arachnid => new Vector3(0, 0, joint.Rotation.Z),
                    BodyPlan.Biped => new Vector3(i < 2 ? -1.5f : 0, 0, 0),
                    _ => new Vector3(i < 2 ? -1.45f : 1.45f, 0, 0),
                };
            }
            return;
        }

        // The attack reads in the torso: rear back through the windup, lunge through the active window.
        float lean = 0, lunge = 0, sway = 0, raise = 0;
        switch (creature.Phase)
        {
            case CombatPhase.Windup:
                lean = -0.35f;
                lunge = -0.12f;
                raise = -2.3f;
                break;
            case CombatPhase.Active:
                lean = 0.2f;
                lunge = 0.4f;
                raise = 0.9f;
                break;
            case CombatPhase.Recovery:
                lunge = 0.1f;
                raise = 0.3f;
                break;
            case CombatPhase.Staggered:
                sway = Mathf.Sin((float)Time.GetTicksMsec() / 40f) * 0.25f;
                lean = -0.2f;
                break;
        }
        if (_plan == BodyPlan.Biped)
            lean *= 0.35f;
        _torso.Rotation = new Vector3(lean, 0, sway);
        _torso.Position = new Vector3(0, _torsoHeight, lunge * _scale);

        _gait = Mathf.Lerp(_gait, Mathf.Clamp(speed / 4.5f, 0, 1.2f), 1f - Mathf.Exp(-10f * (float)delta));
        _phase += speed * delta * Mathf.Tau / (_plan == BodyPlan.Arachnid ? 0.6 : 1.1);
        float swing = Mathf.Sin((float)_phase) * 0.7f * _gait;
        for (int i = 0; i < _legs.Count; i++)
        {
            var joint = _legs[i];
            // Diagonal pairs step together; a walker's arms swing against its legs.
            float stride = _plan == BodyPlan.Biped ? (i % 2 == 0 ? swing : -swing) : (i % 2 == 0) == (i / 2 % 2 == 0) ? swing : -swing;
            if (_plan == BodyPlan.Arachnid)
                joint.Rotation = new Vector3(0, stride * 0.6f, joint.Rotation.Z);
            else if (joint == _strikingLimb && raise != 0)
                joint.Rotation = new Vector3(raise, 0, 0);
            else
                joint.Rotation = new Vector3(stride, 0, 0);
        }
    }

    private void Mark(CreatureView creature)
    {
        (string text, Color colour) = creature switch
        {
            { Asleep: true } => ("z", new Color(0.75f, 0.8f, 0.9f)),
            { Mind: CreatureMind.Engaged } => ("!", new Color(1f, 0.25f, 0.15f)),
            { Mind: CreatureMind.Suspicious or CreatureMind.Searching } => ("?", new Color(1f, 0.85f, 0.25f)),
            _ => ("", Colors.White),
        };
        _mark.Visible = text != "";
        _mark.Text = text;
        _mark.Modulate = colour;
    }

    private static Vector3 Feet(Body body) => HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);

    private static float Facing(Body body) => Mathf.DegToRad(body.FacingMdeg / 1000f);
}
