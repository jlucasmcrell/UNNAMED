// UNNAMED Presentation - the timber building kit assembled on M7's lattice, its collision boxes drawn over it (Phase B, B7)
// Godot presentation only, a harness: nothing here is game state; M7's contract is mirrored for the check, never changed (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// <c>--kit-sheet dir</c>: the Phase-B timber kit (<c>piece_timber_*</c>, one model per M7 piece definition) put together as M7 will place
/// pieces - a 3 m lattice, quarter turns, walls centred on the lattice lines, pads and roofs on the squares - into a two-by-two house with one
/// doorway and its door (and a second door swung open), with each piece's blocking boxes from M7's greybox catalogue (design §4.21) drawn
/// over it in red, so any art outside what bodies collide with shows. Photographed from outside, at the doorway, from above and inside.
/// </summary>
public partial class KitSheet : Node3D
{
    private const float M = 3f;

    // M7's blocking part boxes at rotation 0 (metres: min x, min z, max x, max z, height); pads and roofs block nothing.
    private static readonly Dictionary<string, (float X0, float Z0, float X1, float Z1, float H)[]> Parts = new()
    {
        ["piece_timber_wall"] = new[] { (-1.7f, -0.2f, 1.7f, 0.2f, 3.0f) },
        ["piece_timber_doorway"] = new[] { (-1.7f, -0.2f, -0.8f, 0.2f, 3.0f), (0.8f, -0.2f, 1.7f, 0.2f, 3.0f) },
        ["piece_timber_door"] = new[] { (-0.8f, -0.2f, 0.8f, 0.2f, 2.4f) },
    };

    private readonly ArtLibrary _art;
    private readonly string _directory;
    private readonly Camera3D _camera = new() { Fov = 55, Current = true };
    private readonly List<(string Name, Vector3 Eye, Vector3 Target)> _shots = new();
    private readonly List<string> _notes = new();
    private int _shot, _wait = -1;
    private double _clock;

    public KitSheet(ArtLibrary art, string directory)
    {
        _art = art;
        _directory = directory;
    }

    public override void _Ready()
    {
        Directory.CreateDirectory(_directory);
        DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.62f, 0.68f, 0.74f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.62f, 0.66f), AmbientLightEnergy = 0.6f,
                TonemapMode = Godot.Environment.ToneMapper.Agx, SsaoEnabled = true,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, 35, 0), ShadowEnabled = true, LightEnergy = 1.3f });
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(60, 60) }, Position = new Vector3(3, 0, 3) };
        floor.MaterialOverride = _art.WorldMaterial("material_packed_dirt_ground") ?? new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.4f, 0.33f) };
        AddChild(floor);
        AddChild(_camera);

        // A two-by-two house over the squares (0,0)-(1,1): pads and roofs at the squares' centres, walls on the outer lattice lines.
        for (int i = 0; i < 2; i++)
        for (int k = 0; k < 2; k++)
        {
            Piece("piece_timber_pad", new Vector3(M * i + M / 2, 0, M * k + M / 2), 0);
            Piece("piece_timber_roof", new Vector3(M * i + M / 2, 0, M * k + M / 2), 0);
        }
        for (int i = 0; i < 2; i++)
        {
            // South (z = 0, the door in the first span) and north (z = 6): along x, rotation 0. West (x = 0) and east (x = 6): along z, a quarter turn.
            Piece(i == 0 ? "piece_timber_doorway" : "piece_timber_wall", new Vector3(M * i + M / 2, 0, 0), 0);
            Piece("piece_timber_wall", new Vector3(M * i + M / 2, 0, 2 * M), 0);
            Piece("piece_timber_wall", new Vector3(0, 0, M * i + M / 2), 1);
            Piece(i == 1 ? "piece_timber_doorway" : "piece_timber_wall", new Vector3(2 * M, 0, M * i + M / 2), 1);
        }
        // The south door closed; the east door swung open about its hinge (the leaf's origin is its hinge edge, at x = -0.8).
        Door(new Vector3(M / 2, 0, 0), 0, 0);
        Door(new Vector3(2 * M, 0, M + M / 2), 1, -80);

        _shots.Add(("kit_house_outside", new Vector3(-5.5f, 4.2f, -7.5f), new Vector3(3, 1.6f, 3)));
        _shots.Add(("kit_doorway", new Vector3(1.5f, 1.7f, -3.2f), new Vector3(1.5f, 1.3f, 0)));
        _shots.Add(("kit_open_door", new Vector3(9.5f, 1.9f, 1.5f), new Vector3(6, 1.2f, 4.5f)));
        _shots.Add(("kit_from_above", new Vector3(3.01f, 16f, 3f), new Vector3(3, 0, 3)));
        _shots.Add(("kit_inside", new Vector3(1.0f, 1.65f, 1.0f), new Vector3(5, 1.6f, 5)));
    }

    /// <summary>A kit piece at a lattice anchor and quarter turn (rotation 1 turns +x to -z, as a 90-degree yaw), with its blocking boxes in red.</summary>
    private void Piece(string id, Vector3 anchor, int rotation)
    {
        if (_art.ModelWithLods(id) is not { } model)
        {
            _notes.Add($"{id}: not drawn ({_art.Why(id) ?? "no model"})");
            return;
        }
        model.Position = anchor;
        model.RotationDegrees = new Vector3(0, 90 * rotation, 0);
        AddChild(model);
        foreach (var (x0, z0, x1, z1, h) in Parts.GetValueOrDefault(id) ?? Array.Empty<(float, float, float, float, float)>())
            Box(anchor, rotation, x0, z0, x1, z1, h);
    }

    private void Door(Vector3 anchor, int rotation, float swungDegrees)
    {
        if (_art.ModelWithLods("piece_timber_door") is not { } leaf)
        {
            _notes.Add("piece_timber_door: not drawn");
            return;
        }
        var turn = new Basis(Vector3.Up, Mathf.DegToRad(90 * rotation));
        var hinge = new Node3D { Position = anchor + turn * new Vector3(-0.8f, 0, 0), Basis = turn * new Basis(Vector3.Up, Mathf.DegToRad(swungDegrees)) };
        hinge.AddChild(leaf);
        AddChild(hinge);
        if (swungDegrees == 0)
            Box(anchor, rotation, -0.8f, -0.2f, 0.8f, 0.2f, 2.4f);
    }

    /// <summary>A blocking box's edges, in red, turned with its piece.</summary>
    private void Box(Vector3 anchor, int rotation, float x0, float z0, float x1, float z1, float h)
    {
        var turn = new Basis(Vector3.Up, Mathf.DegToRad(90 * rotation));
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        Vector3 C(float x, float y, float z) => anchor + turn * new Vector3(x, y, z);
        var corners = new[] { C(x0, 0, z0), C(x1, 0, z0), C(x1, 0, z1), C(x0, 0, z1), C(x0, h, z0), C(x1, h, z0), C(x1, h, z1), C(x0, h, z1) };
        int[] edges = { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 };
        foreach (int e in edges)
            mesh.SurfaceAddVertex(corners[e]);
        mesh.SurfaceEnd();
        AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, 0.15f, 0.1f), NoDepthTest = true },
        });
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock < 2.0)
            return;
        if (_shot >= _shots.Count)
        {
            File.WriteAllText(Path.Combine(_directory, "kit_sheet.md"), "# Kit sheet\n\n" + (_notes.Count == 0 ? "Every piece drawn.\n" : string.Join("\n", _notes.Select(n => $"- {n}")) + "\n")
                + string.Join("\n", _shots.Select(s => $"![{s.Name}]({s.Name}.png)")) + "\n");
            GetTree().Quit(0);
            return;
        }
        var shot = _shots[_shot];
        if (_wait < 0)
        {
            _camera.LookAtFromPosition(shot.Eye, shot.Target, Mathf.Abs(shot.Eye.Y - shot.Target.Y) > 10 ? Vector3.Forward : Vector3.Up);
            _wait = 0;
            return;
        }
        if (++_wait < 12)
            return;
        Main.SaveScreenshot(GetViewport(), _directory, shot.Name);
        _shot++;
        _wait = -1;
    }
}
