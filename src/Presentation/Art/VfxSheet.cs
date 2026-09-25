// UNNAMED Presentation - the Phase-A flat flipbooks beside Phase B's particle recipes, side by side and in motion (Phase B, B0.7)
// Godot presentation only, a harness: nothing here is game state (D-11)

using Godot;
using UNNAMED.Presentation.Greybox;
using UNNAMED.Presentation.Ui;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// <c>--vfx-sheet dir</c>: on a dim floor, left to right - the Impulse Bolt in flight as Phase A draws it (the pipeline's flipbook on one
/// camera-facing quad) and as a particle recipe draws it (the same quad, smaller, trailing <c>force_bolt_trail</c>), each swinging to and
/// fro; its impact as Phase A draws it (the impact flipbook on one quad) and as recipes draw it (<c>force_bolt_burst</c> with
/// <c>impact_sparks</c>), every 1.4 s; and a torch (<c>torch_flame</c>, <c>embers</c>). Photographed wide and pair by pair, the bursts a
/// tenth of a second in; run it with <c>--write-movie</c> to judge the motion.
/// </summary>
public partial class VfxSheet : Node3D
{
    private const double Cycle = 1.4, BurstAt = 0.12;

    private readonly AssetCatalog _assets;
    private readonly ParticleRecipes _recipes;
    private readonly string _directory;
    private readonly Camera3D _camera = new() { Fov = 45, Current = true };
    private readonly List<(string Name, Vector3 Eye, Vector3 Target, bool OnBurst)> _shots = new();
    private readonly List<(Node3D Node, Vector3 Home)> _swinging = new();
    private readonly List<(MeshInstance3D Quad, Flipbook Book, bool Once)> _books = new();
    private readonly List<GpuParticles3D> _bursts = new();
    private readonly List<string> _notes = new();
    private double _clock, _cycleStart = -10;
    private int _shot, _wait = -1;

    public VfxSheet(AssetCatalog assets, ParticleRecipes recipes, string directory)
    {
        _assets = assets;
        _recipes = recipes;
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
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.10f, 0.11f, 0.13f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.35f, 0.37f, 0.42f), AmbientLightEnergy = 0.6f,
                TonemapMode = Godot.Environment.ToneMapper.Agx, GlowEnabled = true,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0), LightEnergy = 0.35f });
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(60, 30) }, Position = new Vector3(6, 0, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.2f, 0.18f), Roughness = 0.95f },
        });
        AddChild(_camera);

        // 1, 2: the bolt in flight.
        var travel = _assets.Effect("vfx.force.impulse_bolt_travel");
        Station(0, "Phase A: bolt, flat flipbook", () =>
        {
            var bolt = new Node3D();
            if (travel is not null)
                AddBook(bolt, travel, 0.9f, once: false);
            return bolt;
        }, swing: true);
        Station(3, "B0.7: bolt + trail recipe", () =>
        {
            var bolt = new Node3D();
            if (travel is not null)
                AddBook(bolt, travel, 0.45f, once: false);
            if (_recipes.Build("force_bolt_trail") is { } trail)
                bolt.AddChild(trail);
            else
                _notes.Add("force_bolt_trail would not build");
            return bolt;
        }, swing: true);
        // 3, 4: the impact.
        var impact = _assets.Effect("vfx.force.impulse_bolt_impact");
        Station(6, "Phase A: impact, flat flipbook", () =>
        {
            var at = new Node3D();
            if (impact is not null)
                AddBook(at, impact, 1.6f, once: true);
            return at;
        }, swing: false);
        Station(9, "B0.7: burst + sparks recipes", () =>
        {
            var at = new Node3D();
            foreach (string name in new[] { "force_bolt_burst", "impact_sparks" })
            {
                if (_recipes.Build(name) is { } burst)
                {
                    at.AddChild(burst);
                    _bursts.Add(burst);
                }
                else
                {
                    _notes.Add($"{name} would not build");
                }
            }
            return at;
        }, swing: false);
        // 5: a torch.
        Station(12, "B0.7: torch flame + embers", () =>
        {
            var post = new Node3D();
            post.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 1.5f }, Position = new Vector3(0, -0.75f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.17f, 0.1f) },
            });
            foreach (string name in new[] { "torch_flame", "embers" })
            {
                if (_recipes.Build(name) is { } fire)
                    post.AddChild(fire);
            }
            post.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.62f, 0.3f), LightEnergy = 1.2f, OmniRange = 5f, Position = new Vector3(0, 0.3f, 0) });
            return post;
        }, swing: false);

        _shots.Add(("vfx_wide", new Vector3(6, 2.4f, 13), new Vector3(6, 1.2f, 0), false));
        _shots.Add(("vfx_bolt_pair", new Vector3(1.5f, 1.7f, 6.5f), new Vector3(1.5f, 1.4f, 0), false));
        _shots.Add(("vfx_impact_pair", new Vector3(7.5f, 1.7f, 6f), new Vector3(7.5f, 1.4f, 0), true));
        _shots.Add(("vfx_torch", new Vector3(12, 1.9f, 3.2f), new Vector3(12, 1.6f, 0), false));
    }

    private void Station(float x, string label, Func<Node3D> make, bool swing)
    {
        var node = make();
        node.Position = new Vector3(x, 1.4f, 0);
        AddChild(node);
        if (swing)
            _swinging.Add((node, node.Position));
        AddChild(new Label3D
        {
            Text = label, Position = new Vector3(x, 2.6f, 0), PixelSize = 0.003f, FontSize = 36, OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        });
    }

    private void AddBook(Node3D parent, Flipbook book, float size, bool once)
    {
        var quad = ProjectilesView.Quad("Book", book, size);
        parent.AddChild(quad);
        _books.Add((quad, book, once));
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        // The bolts swing to and fro across the view; the impacts fire every cycle.
        foreach (var (node, home) in _swinging)
            node.Position = home + new Vector3(0, 0, MathF.Sin((float)_clock * 2.2f) * 2.5f);
        if (_clock - _cycleStart >= Cycle)
        {
            _cycleStart = _clock;
            foreach (var burst in _bursts)
                ParticleRecipes.Fire(burst);
        }
        double inCycle = _clock - _cycleStart;
        foreach (var (quad, book, once) in _books)
        {
            double t = once ? inCycle : _clock;
            int frame = (int)(t * book.Fps);
            quad.Visible = !once || frame < book.Frames;
            ProjectilesView.Frame(quad, book, once ? Math.Min(frame, book.Frames - 1) : frame % book.Frames);
        }
        if (_clock < 3.0)
            return;
        if (_shot >= _shots.Count)
        {
            // Hold the wide view a few more cycles for the movie, then stop.
            if (_clock > _shotsDone + 6)
            {
                File.WriteAllText(Path.Combine(_directory, "vfx_sheet.md"),
                    "# VFX sheet\n\n" + (_notes.Count == 0 ? "Every recipe built.\n" : string.Join("\n", _notes.Select(n => $"- {n}")) + "\n")
                    + string.Join("\n", _shots.Select(s => $"![{s.Name}]({s.Name}.png)")) + "\n");
                GetTree().Quit(0);
            }
            return;
        }
        var shot = _shots[_shot];
        if (_wait < 0)
        {
            _camera.LookAtFromPosition(shot.Eye, shot.Target, Vector3.Up);
            _wait = 0;
            return;
        }
        // A few frames for the view; a burst's picture is taken a tenth of a second into its cycle.
        if (++_wait < 45 || shot.OnBurst && (inCycle < BurstAt || inCycle > BurstAt + 0.08))
            return;
        Main.SaveScreenshot(GetViewport(), _directory, shot.Name);
        _shot++;
        _wait = -1;
        if (_shot >= _shots.Count)
        {
            _shotsDone = _clock;
            _camera.LookAtFromPosition(_shots[0].Eye, _shots[0].Target, Vector3.Up);
        }
    }

    private double _shotsDone;
}
