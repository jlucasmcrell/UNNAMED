// UNNAMED Presentation - creatures in the greybox: a quadruped mannequin per combatant, with readable tells (M3c)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Domain;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// Draws every creature the simulation reports, between its last two ticks, so a 20 Hz body moves smoothly. Each phase of
/// an attack reads at a glance: the windup rears back and lights the muzzle (the tell), the active window lunges, a stagger
/// sways blue, and the dead lie on their side. Timing comes from the simulation's phases, never from a clip.
/// </summary>
public partial class CreaturesView : Node3D
{
    private readonly Dictionary<EntityId, CreatureFigure> _figures = new();
    private long _tick = -1;

    public void Draw(Simulation simulation, double alpha, double delta)
    {
        bool advanced = simulation.WorldTick != _tick;
        _tick = simulation.WorldTick;
        foreach (var creature in simulation.Creatures)
        {
            if (!_figures.TryGetValue(creature.Id, out var figure))
            {
                figure = new CreatureFigure { Name = creature.Id.Value };
                AddChild(figure);
                figure.Place(creature.Body);
                _figures[creature.Id] = figure;
            }
            if (advanced)
                figure.Advance(creature.Body);
            figure.Pose(creature, (float)alpha, delta);
        }
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

/// <summary>One creature's greybox body. Drawn only; it holds a copy of two ticks' bodies, never the truth.</summary>
internal sealed partial class CreatureFigure : Node3D
{
    private static readonly StandardMaterial3D Fur = new() { AlbedoColor = new Color(0.46f, 0.45f, 0.43f) };
    private static readonly StandardMaterial3D Dead = new() { AlbedoColor = new Color(0.22f, 0.2f, 0.19f) };
    private static readonly StandardMaterial3D Tell = new() { AlbedoColor = new Color(1f, 0.8f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.1f) };
    private static readonly StandardMaterial3D Eyes = new() { AlbedoColor = new Color(0.9f, 0.15f, 0.1f), EmissionEnabled = true, Emission = new Color(0.8f, 0.1f, 0.05f) };
    private static readonly StandardMaterial3D Calm = new() { AlbedoColor = new Color(0.1f, 0.1f, 0.1f) };
    private static readonly StandardMaterial3D Staggered = new() { AlbedoColor = new Color(0.45f, 0.55f, 0.85f) };

    private readonly Node3D _torso = new() { Position = new Vector3(0, 0.55f, 0) };
    private readonly MeshInstance3D _body = new() { Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.34f, 0.95f) } };
    private readonly MeshInstance3D _head = new() { Mesh = new BoxMesh { Size = new Vector3(0.26f, 0.26f, 0.36f) }, Position = new Vector3(0, 0.12f, 0.6f) };
    private readonly MeshInstance3D _muzzle = new() { Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.12f, 0.2f) }, Position = new Vector3(0, -0.05f, 0.26f) };
    private readonly MeshInstance3D _eyes = new() { Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.04f, 0.02f) }, Position = new Vector3(0, 0.05f, 0.18f) };
    private readonly Node3D[] _legs = new Node3D[4];
    private Vector3 _previous;
    private Vector3 _current;
    private float _yaw;
    private float _gait;
    private double _phase;

    public override void _Ready()
    {
        AddChild(_torso);
        _torso.AddChild(_body);
        _torso.AddChild(_head);
        _head.AddChild(_muzzle);
        _head.AddChild(_eyes);
        _torso.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.08f, 0.4f) }, Position = new Vector3(0, 0.1f, -0.62f),
            RotationDegrees = new Vector3(25, 0, 0), MaterialOverride = Fur });
        var hips = new[] { new Vector3(0.14f, -0.12f, 0.36f), new Vector3(-0.14f, -0.12f, 0.36f), new Vector3(0.14f, -0.12f, -0.36f), new Vector3(-0.14f, -0.12f, -0.36f) };
        for (int i = 0; i < 4; i++)
        {
            _legs[i] = new Node3D { Position = hips[i] };
            _legs[i].AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.44f, 0.08f) }, Position = new Vector3(0, -0.22f, 0), MaterialOverride = Fur });
            _torso.AddChild(_legs[i]);
        }
    }

    public void Place(Body body)
    {
        _previous = _current = Feet(body);
        _yaw = Facing(body);
    }

    public void Advance(Body body)
    {
        _previous = _current;
        _current = Feet(body);
    }

    public void Pose(CreatureView creature, float alpha, double delta)
    {
        var feet = _previous.Lerp(_current, alpha);
        float speed = new Vector2(_current.X - _previous.X, _current.Z - _previous.Z).Length() / 0.05f;
        _yaw = Mathf.LerpAngle(_yaw, Facing(creature.Body), 1f - Mathf.Exp(-16f * (float)delta));
        Position = feet;

        if (!creature.Alive)
        {
            Rotation = new Vector3(0, _yaw, Mathf.Pi / 2);
            Position = feet + new Vector3(0, 0.18f, 0);
            _body.MaterialOverride = _head.MaterialOverride = _muzzle.MaterialOverride = Dead;
            _eyes.MaterialOverride = Calm;
            return;
        }
        Rotation = new Vector3(0, _yaw, 0);
        _body.MaterialOverride = _head.MaterialOverride = creature.Phase == CombatPhase.Staggered ? Staggered : Fur;
        _eyes.MaterialOverride = creature.Hostile ? Eyes : Calm;
        _muzzle.MaterialOverride = creature.Phase == CombatPhase.Windup ? Tell : Fur;

        // The attack reads in the torso: rear back through the windup, lunge through the active window.
        float lean = 0, lunge = 0, sway = 0;
        switch (creature.Phase)
        {
            case CombatPhase.Windup:
                lean = -0.35f;
                lunge = -0.12f;
                break;
            case CombatPhase.Active:
                lean = 0.2f;
                lunge = 0.4f;
                break;
            case CombatPhase.Recovery:
                lunge = 0.1f;
                break;
            case CombatPhase.Staggered:
                sway = Mathf.Sin((float)Time.GetTicksMsec() / 40f) * 0.25f;
                lean = -0.2f;
                break;
        }
        _torso.Rotation = new Vector3(lean, 0, sway);
        _torso.Position = new Vector3(0, 0.55f, lunge);

        _gait = Mathf.Lerp(_gait, Mathf.Clamp(speed / 4.5f, 0, 1.2f), 1f - Mathf.Exp(-10f * (float)delta));
        _phase += speed * delta * Mathf.Tau / 1.1;
        float swing = Mathf.Sin((float)_phase) * 0.7f * _gait;
        _legs[0].Rotation = new Vector3(swing, 0, 0);
        _legs[3].Rotation = new Vector3(swing, 0, 0);
        _legs[1].Rotation = new Vector3(-swing, 0, 0);
        _legs[2].Rotation = new Vector3(-swing, 0, 0);
    }

    private static Vector3 Feet(Body body) => HollowView.ToGodot(body.XMm, body.YMm, body.ZMm);

    private static float Facing(Body body) => Mathf.DegToRad(body.FacingMdeg / 1000f);
}
