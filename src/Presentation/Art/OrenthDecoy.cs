// Presentation only: the lingering spirit ghost an Orenth leaves behind (owner ruling 2026-10-25). The gameplay that
// decides what it absorbs lives in the simulation; this draws it.
using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>A frozen spirit ghost made by <see cref="OrenthPhase.Linger"/>: it fades out over its life, or shatters when hit.</summary>
public partial class OrenthDecoy : Node3D
{
    private const double ShatterSeconds = .35;

    private readonly double _seconds;
    private double _age, _shattered = -1;

    public OrenthDecoy(double seconds) { _seconds = seconds; Name = "OrenthDecoy"; }

    /// <summary>False once it has faded out or shattered.</summary>
    public bool Alive => _shattered < 0 && _age < _seconds;

    /// <summary>The decoy absorbs a hit: it flares, swells slightly and breaks into silver shards.</summary>
    public void Shatter()
    {
        if (!Alive)
            return;
        _shattered = 0;
        var shards = new CpuParticles3D
        {
            Emitting = true, OneShot = true, Amount = 48, Lifetime = .6f, Explosiveness = .95f, Position = Vector3.Up * 1.1f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(.25f, .7f, .15f),
            Direction = Vector3.Up, Spread = 180, InitialVelocityMin = 1.2f, InitialVelocityMax = 2.6f, Gravity = Vector3.Down * 3,
            ScaleAmountMin = .015f, ScaleAmountMax = .04f,
            Mesh = new BoxMesh { Size = Vector3.One, Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(.86f, .9f, .96f),
                EmissionEnabled = true, Emission = new Color(.78f, .82f, .88f),
            } },
        };
        AddChild(shards);
    }

    public override void _Process(double delta)
    {
        _age += delta;
        float strength;
        if (_shattered >= 0)
        {
            _shattered += delta;
            float t = (float)(_shattered / ShatterSeconds);
            strength = Mathf.Lerp(1f, 0f, Mathf.Clamp(t, 0f, 1f));
            foreach (var mesh in GetChildren().OfType<MeshInstance3D>())
                mesh.Scale = Vector3.One * (1f + .12f * t);
            if (_shattered > .7)
                QueueFree();
        }
        else
        {
            strength = PeopleLooks.DecoyStrength(_age, _seconds);
            if (_age >= _seconds)
                QueueFree();
        }
        foreach (var mesh in GetChildren().OfType<MeshInstance3D>())
            mesh.SetInstanceShaderParameter("strength", strength);
    }
}
