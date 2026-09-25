// UNNAMED Presentation - the navigation grid, shown (M7, F2's navigation stage)
// Godot presentation only: it reads the simulation's navigation view and holds no gameplay state (D-11, D-13)

using Godot;
using UNNAMED.Domain.Spatial;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// F2's navigation stage (M7 design §8.14): the lattice nodes a person cannot stand on within 24 m of the viewer, as flat 0.25 m quads just
/// above the ground, and every gate, red while shut and green while open, read as it is drawn. The nodes are read from
/// <see cref="NavigationView.Grid"/> - the grid the simulation plans on, never one of its own - and redrawn when the grid changes,
/// after the viewer moves 2 m, and at most twice a second. What it drew is kept, so a scripted run can check it.
/// </summary>
public partial class NavigationOverlay : Node3D
{
    public const float RadiusM = 24f;
    private static readonly NavAgent Person = new(0, true);
    private static readonly Color Unwalkable = new(0.95f, 0.25f, 0.2f, 0.55f);
    public static readonly Color Shut = new(0.9f, 0.1f, 0.1f, 0.5f);
    public static readonly Color Open = new(0.15f, 0.85f, 0.25f, 0.5f);

    private readonly MultiMeshInstance3D _nodes = new() { Name = "Nodes" };
    private readonly Dictionary<string, MeshInstance3D> _gates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StandardMaterial3D> _gateMaterials = new(StringComparer.Ordinal);
    private TerrainGrid _terrain = null!;
    private NavGrid? _drawnFor;
    private Vector3 _drawnAt = new(float.NaN, 0, float.NaN);
    private double _since = double.MaxValue;

    /// <summary>Every node looked at in the last redraw, with whether a person may stand there, in the order drawn.</summary>
    public IReadOnlyList<(long I, long J, bool Walkable)> Sampled { get; private set; } = Array.Empty<(long, long, bool)>();

    /// <summary>How many quads the last redraw drew: one per unwalkable node sampled.</summary>
    public int Quads => _nodes.Multimesh?.VisibleInstanceCount ?? 0;

    /// <summary>The colour each gate was last drawn in.</summary>
    public IReadOnlyDictionary<string, Color> GateColours => _gateMaterials.ToDictionary(g => g.Key, g => g.Value.AlbedoColor, StringComparer.Ordinal);

    public void Bind(TerrainGrid terrain) => _terrain = terrain;

    public override void _Ready()
    {
        Visible = false;
        var quad = new QuadMesh { Size = new Vector2(0.22f, 0.22f), Orientation = PlaneMesh.OrientationEnum.Y };
        quad.Material = new StandardMaterial3D
        {
            AlbedoColor = Unwalkable, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _nodes.Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = quad };
        _nodes.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        AddChild(_nodes);
    }

    /// <summary>Hide it, and forget what was drawn, so showing it again draws afresh.</summary>
    public void Close()
    {
        Visible = false;
        _drawnFor = null;
    }

    /// <summary>Draw the grid around <paramref name="viewer"/>, redrawing the nodes only when they could have changed.</summary>
    public void Draw(NavigationView view, Vector3 viewer, double delta, bool now = false)
    {
        if (!Visible)
            return;
        DrawGates(view);
        _since += delta;
        bool moved = float.IsNaN(_drawnAt.X) || new Vector2(viewer.X - _drawnAt.X, viewer.Z - _drawnAt.Z).Length() > 2f;
        if (!now && ReferenceEquals(view.Grid, _drawnFor) && !moved)
            return;
        if (!now && _since < 0.5 && ReferenceEquals(view.Grid, _drawnFor))
            return;
        _since = 0;
        _drawnFor = view.Grid;
        _drawnAt = viewer;
        DrawNodes(view, viewer);
    }

    private void DrawNodes(NavigationView view, Vector3 viewer)
    {
        var grid = view.Grid;
        var open = view.Gates.ToDictionary(g => g.Key, g => g.Open, StringComparer.Ordinal);
        bool IsOpen(NavInput gate) => gate.GateKey is { } key && open.GetValueOrDefault(key);
        long cx = (long)Math.Round(viewer.X * 1000), cz = (long)Math.Round(viewer.Z * 1000);
        long reach = (long)(RadiusM * 1000);
        var sampled = new List<(long, long, bool)>();
        var transforms = new List<Transform3D>();
        for (long j = grid.NodeOf(cz - reach); j <= grid.NodeOf(cz + reach); j++)
        {
            for (long i = grid.NodeOf(cx - reach); i <= grid.NodeOf(cx + reach); i++)
            {
                long x = grid.CentreOf(i), z = grid.CentreOf(j);
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > reach * reach)
                    continue;
                bool walkable = grid.Walkable(i, j, Person, IsOpen);
                sampled.Add((i, j, walkable));
                if (!walkable)
                    transforms.Add(new Transform3D(Basis.Identity, new Vector3(x / 1000f, _terrain.HeightAtMm(x, z) / 1000f + 0.05f, z / 1000f)));
            }
        }
        var multimesh = _nodes.Multimesh;
        if (multimesh.InstanceCount < transforms.Count)
            multimesh.InstanceCount = transforms.Count;
        for (int n = 0; n < transforms.Count; n++)
            multimesh.SetInstanceTransform(n, transforms[n]);
        multimesh.VisibleInstanceCount = transforms.Count;
        Sampled = sampled;
    }

    private void DrawGates(NavigationView view)
    {
        foreach (var gate in view.Gates)
        {
            if (!_gates.TryGetValue(gate.Key, out var mesh))
            {
                var material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                mesh = GateMesh(gate.Footprint, material);
                mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _gates[gate.Key] = mesh;
                _gateMaterials[gate.Key] = material;
                AddChild(mesh);
            }
            _gateMaterials[gate.Key].AlbedoColor = gate.Open ? Open : Shut;
        }
    }

    /// <summary>A gate's footprint as a thin slab just above the ground, so it reads through the door or the fold it stands for.</summary>
    private MeshInstance3D GateMesh(Blocker footprint, StandardMaterial3D material)
    {
        var (x, z) = Footprints.Center(footprint);
        float y = _terrain.HeightAtMm(x, z) / 1000f + 0.08f;
        PrimitiveMesh mesh = footprint switch
        {
            BoxBlocker box => new BoxMesh { Size = new Vector3((box.MaxXMm - box.MinXMm) / 1000f + 0.1f, 0.06f, (box.MaxZMm - box.MinZMm) / 1000f + 0.1f) },
            CircleBlocker circle => new CylinderMesh { TopRadius = circle.RadiusMm / 1000f, BottomRadius = circle.RadiusMm / 1000f, Height = 0.06f },
            _ => new BoxMesh { Size = new Vector3(0.5f, 0.06f, 0.5f) },
        };
        mesh.Material = material;
        return new MeshInstance3D { Mesh = mesh, Position = new Vector3(x / 1000f, y, z / 1000f) };
    }
}
