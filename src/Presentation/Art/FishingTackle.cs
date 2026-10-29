// UNNAMED Presentation - the rod, the line and the float a cast puts in the world (the D20 fishing follow-on's art).
// Drawn as primitives from the figure's own hand: a rod at the shoulder, a line to a float that bobs, and a splash when the bite comes.

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The tackle a fishing cast shows: a hazel rod in the hand, a line out to a float on the water, and a ring on the surface where something
/// has taken it. It is geometry built here rather than a model, because a rod, a line and a float are three primitives - but they are drawn
/// where the cast is, in the hand and on the water, which is what the player sees.
/// </summary>
public partial class FishingTackle : Node3D
{
    private static readonly Vector3 RodButt = new(.12f, 1.05f, .16f);
    private const float RodReach = 2.15f;

    private MeshInstance3D _rod = null!;
    private MeshInstance3D _float = null!;
    private MeshInstance3D _ring = null!;
    private double _bob;
    private bool _cast;
    private bool _biting;

    /// <summary>True once the line is in the water (the cast has landed).</summary>
    public bool LineOut
    {
        set
        {
            if (_cast == value)
                return;
            _cast = value;
            _biting = false;
        }
    }

    /// <summary>True while something is on the line: the float dips and a ring spreads.</summary>
    public bool Biting
    {
        set => _biting = value;
    }

    public override void _Ready()
    {
        _rod = new MeshInstance3D
        {
            Name = "Rod",
            Mesh = new CylinderMesh
            {
                TopRadius = .006f,
                BottomRadius = .022f,
                Height = RodReach,
                RadialSegments = 6,
                Rings = 1,
            },
            MaterialOverride = Mat(new Color(.55f, .42f, .24f)),
            Position = RodButt,
            RotationDegrees = new Vector3(62f, 0f, -8f),
            Visible = false,
        };
        AddChild(_rod);

        _float = new MeshInstance3D
        {
            Name = "Float",
            Mesh = new SphereMesh { Radius = .045f, Height = .11f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = Mat(new Color(.85f, .2f, .16f)),
            Visible = false,
        };
        AddChild(_float);

        _ring = new MeshInstance3D
        {
            Name = "BiteRing",
            Mesh = new TorusMesh { InnerRadius = .18f, OuterRadius = .24f, Rings = 16, RingSegments = 6 },
            MaterialOverride = Mat(new Color(.88f, .9f, .95f, .55f)),
            Visible = false,
        };
        AddChild(_ring);
    }

    private static StandardMaterial3D Mat(Color colour) => new()
    {
        AlbedoColor = colour,
        Roughness = .7f,
        Transparency = colour.A < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
    };

    public override void _Process(double delta)
    {
        _bob += delta * (_biting ? 9 : 1.7);
        bool out_ = _cast;
        _rod.Visible = true;
        _float.Visible = out_;
        _ring.Visible = out_ && _biting;

        // The float rides the water a short cast ahead of the body, dipping when something is on it.
        float dip = _biting ? (float)Mathf.Abs(Mathf.Sin((float)_bob)) * .16f : (float)Mathf.Sin((float)_bob) * .012f;
        var ahead = new Vector3(0f, 0f, -2.6f);
        _float.Position = ahead + new Vector3(0f, .02f - dip, 0f);
        _ring.Position = new Vector3(ahead.X, .01f, ahead.Z);
        _ring.Scale = Vector3.One * (1f + (float)Mathf.Abs(Mathf.Sin((float)_bob * .5f)) * .6f);
    }
}
