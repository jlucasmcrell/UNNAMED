// Presentation only: the lingering spirit ghost an Orenth leaves behind (owner ruling 2026-10-25). The gameplay that
// decides what it absorbs lives in the simulation; this draws it.
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>A frozen spirit ghost made by <see cref="OrenthPhase.Linger"/>: it fades out over its life, or shatters when hit.</summary>
public partial class OrenthDecoy : Node3D
{
    private const double ShatterSeconds = .5;

    private double _age, _shattered = -1;

    public OrenthDecoy() => Name = "OrenthDecoy";

    /// <summary>How long it holds before it has faded out.</summary>
    public double Seconds { get; set; } = 1;

    /// <summary>False once it has faded out or shattered.</summary>
    public bool Alive => _shattered < 0 && _age < Seconds;

    /// <summary><see cref="Alive"/>, for callers that reach the decoy through Call.</summary>
    public bool IsAlive() => Alive;

    /// <summary>The decoy absorbs a hit: it dissolves from the inside out and drifts away as pale motes.</summary>
    public void Shatter()
    {
        if (!Alive)
            return;
        _shattered = 0;
        AddChild(new CpuParticles3D
        {
            Emitting = true, OneShot = true, Amount = 40, Lifetime = 1.4f, Explosiveness = .7f, Position = Vector3.Up * 1.0f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(.25f, .6f, .15f),
            Direction = Vector3.Up, Spread = 60, InitialVelocityMin = .15f, InitialVelocityMax = .5f, Gravity = Vector3.Up * .15f,
            ScaleAmountMin = .5f, ScaleAmountMax = 1f,
            Mesh = new SphereMesh { Radius = .012f, Height = .024f, RadialSegments = 6, Rings = 3, Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(.82f, .86f, .92f, .7f),
            } },
        });
    }

    public override void _Process(double delta)
    {
        _age += delta;
        float strength;
        float dissolve = 0f;
        if (_shattered >= 0)
        {
            _shattered += delta;
            strength = PeopleLooks.DecoyStrength(0, Seconds);
            dissolve = (float)Math.Clamp(_shattered / ShatterSeconds, 0, 1) * 1.1f;
            if (_shattered > 1.5)
                QueueFree();
        }
        else
        {
            strength = PeopleLooks.DecoyStrength(_age, Seconds);
            if (_age >= Seconds)
                QueueFree();
        }
        foreach (var mesh in GetChildren().OfType<MeshInstance3D>())
            mesh.SetInstanceShaderParameter("dissolve", dissolve);
        foreach (var mesh in GetChildren().OfType<MeshInstance3D>())
            mesh.SetInstanceShaderParameter("strength", strength);
    }
}
