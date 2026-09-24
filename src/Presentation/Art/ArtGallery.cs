// UNNAMED Presentation - the asset library on one lit floor: every bound model, playing its clips, photographed (Phase-1 asset integration)
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// <c>--art-gallery dir</c>: every model and clip the art bindings name, stood in rows on a floor of a world material, each rigged
/// body playing its clips, photographed from the front and the side - so a wrong axis, a wrong scale or a clip that does not
/// fit its skeleton shows at a glance, and the pictures record what the game draws. Writes <c>gallery.md</c> listing what
/// loaded and every ID that did not, and why. A harness: it may name what it shows.
/// </summary>
public partial class ArtGallery : Node3D
{
    private readonly ArtLibrary _art;
    private readonly ArtBindings _bindings;
    private readonly string _directory;
    private readonly List<(string Name, Vector3 Eye, Vector3 Target)> _shots = new();
    private readonly List<(SkinnedModel Model, string[] States)> _animated = new();
    private readonly List<Figure> _held = new();
    private readonly Camera3D _camera = new() { Fov = 50, Current = true };
    private int _frame;
    private int _shot;

    public ArtGallery(ArtLibrary art, ArtBindings bindings, string directory)
    {
        _art = art;
        _bindings = bindings;
        _directory = directory;
    }

    public override void _Ready()
    {
        Directory.CreateDirectory(_directory);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.72f, 0.75f, 0.78f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.57f, 0.6f), AmbientLightEnergy = 0.6f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 35, 0), ShadowEnabled = true, LightEnergy = 1.3f });
        _art.Withhold(_bindings.Withheld);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(160, 80) }, Position = new Vector3(40, 0, -15),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.44f, 0.36f), Roughness = 1f },
        });
        AddChild(_camera);
        // The world materials, each tried on a tile at z = 8: withheld ones are drawn as their raw colour map, so the picture shows why.
        float tileX = 0;
        foreach (string id in _bindings.Withheld.Keys.Where(k => k.StartsWith("material_", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal))
        {
            string map = Path.Combine(_art.Root ?? string.Empty, "materials", id, id + "_basecolor.png");
            if (!File.Exists(map) || Image.LoadFromFile(map) is not { } image)
                continue;
            AddChild(new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(4, 4) }, Position = new Vector3(tileX, 0.01f, 8),
                MaterialOverride = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(image), Uv1Scale = new Vector3(2, 2, 1) },
            });
            Label(id.Replace("material_", string.Empty), new Vector3(tileX, 0.6f, 10.2f));
            tileX += 4.6f;
        }
        _shots.Add(("materials_withheld", new Vector3(tileX / 2 - 2.3f, 9f, 17f), new Vector3(tileX / 2 - 2.3f, 0, 8)));

        // Row one, at z = 0: the creatures walking, spaced by their size.
        float x = 0;
        foreach (var (defId, creature) in _bindings.Creatures)
        {
            var model = SkinnedModel.Create(_art, creature.Model, creature.Clips);
            if (model is null)
                continue;
            model.Position = new Vector3(x, 0, 0);
            AddChild(model);
            Label(defId, new Vector3(x, 2.6f, 0));
            _animated.Add((model, new[] { "walk", "attack", "idle" }));
            x += 3.2f;
        }
        _shots.Add(("creatures_front", new Vector3(x / 2 - 1.6f, 2.2f, 9f), new Vector3(x / 2 - 1.6f, 0.8f, 0)));
        _shots.Add(("creatures_side", new Vector3(-7f, 2f, 0.5f), new Vector3(x / 2, 0.8f, 0)));

        // Row two, at z = -6: the people and the player.
        x = 0;
        foreach (var (id, figure) in _bindings.People)
        {
            var model = SkinnedModel.Create(_art, figure.Model, figure.Clips);
            if (model is null)
                continue;
            model.Position = new Vector3(x, 0, -6);
            AddChild(model);
            Label(id, new Vector3(x, 2.2f, -6));
            _animated.Add((model, figure.Clips.Keys.ToArray()));
            x += 2.2f;
        }
        _shots.Add(("people_front", new Vector3(x / 2 - 1.1f, 1.6f, 1.5f), new Vector3(x / 2 - 1.1f, 1.0f, -6)));

        // Off to the side, at x = 60: the greybox character holding each bound weapon, at rest and in its attack's windup (the player's
        // body is withheld, so this is what the game draws), then each skinned companion holding its own.
        x = 60;
        foreach (var (held, phase) in new[] { (Held.Sword, CombatPhase.Idle), (Held.Sword, CombatPhase.Windup), (Held.Bow, CombatPhase.Idle),
            (Held.Bow, CombatPhase.Windup), (Held.Spear, CombatPhase.Idle), (Held.Spear, CombatPhase.Active) })
        {
            var holder = new Avatar { Position = new Vector3(x, 0, 0) };
            HeldWeapon.Arm(holder, _art, _bindings);
            holder.SetStance(new CombatStance(phase, 1f, held, false));
            AddChild(holder);
            _held.Add(holder);
            Label($"{held} {phase}".ToLowerInvariant(), new Vector3(x, 2.2f, 0));
            x += 1.6f;
        }
        foreach (var (id, person) in _bindings.People.Where(p => p.Value.Grip is not null))
        {
            if (SkinnedFigure.Create(_art, _bindings, id) is not { } companion)
                continue;
            companion.Equip(_bindings.CompanionWeapon);
            companion.Position = new Vector3(x, 0, 0);
            AddChild(companion);
            _held.Add(companion);
            Label(id, new Vector3(x, 2.2f, 0));
            x += 1.6f;
        }
        _shots.Add(("held_front", new Vector3((60 + x) / 2 - 0.8f, 1.4f, 5.4f), new Vector3((60 + x) / 2 - 0.8f, 1.1f, 0)));
        _shots.Add(("held_close_sword_bow", new Vector3(62.4f, 1.4f, 3.0f), new Vector3(62.4f, 1.1f, 0)));
        _shots.Add(("held_close_spear_companion", new Vector3(67.2f, 1.4f, 3.2f), new Vector3(67.2f, 1.1f, 0)));
        _shots.Add(("held_side", new Vector3(x + 1.8f, 1.4f, 0.4f), new Vector3((60 + x) / 2 - 0.8f, 1.1f, 0)));

        // Row three, at z = -14: every static model the bindings name, each once.
        x = 0;
        foreach (string id in _bindings.StaticModels().Distinct().OrderBy(s => s, StringComparer.Ordinal))
        {
            if (_art.Model(id) is not { } model)
                continue;
            var size = Bounds(model).Size;
            model.Position = new Vector3(x + size.X / 2, 0, -14);
            AddChild(model);
            Label(id, new Vector3(x + size.X / 2, Math.Max(size.Y, 1f) + 0.4f, -14));
            x += Math.Max(size.X, 1.2f) + 1.2f;
        }
        for (float at = 0; at < x; at += 22f)
            _shots.Add(($"models_{(int)(at / 22f):00}", new Vector3(at + 11f, 5f, 2f), new Vector3(at + 11f, 1.5f, -14)));
    }

    public override void _Process(double delta)
    {
        _frame++;
        // Each rigged body cycles through its states, a second and a half each.
        foreach (var (model, states) in _animated)
            model.Play(states[(int)(_clock / 1.5) % states.Length]);
        foreach (var figure in _held)
            figure.Pose(figure.Position, 0, 0, delta);
        _clock += delta;
        if (_clock < 1.0)
            return;
        // A shot: aim, let a few frames draw it, then save the frame that shows it.
        if (_shot < _shots.Count)
        {
            var (name, eye, target) = _shots[_shot];
            if (_aimedAt < 0)
            {
                _camera.Position = eye;
                _camera.LookAt(target, Vector3.Up);
                _aimedAt = _frame;
            }
            else if (_frame - _aimedAt >= 4)
            {
                Main.SaveScreenshot(GetViewport(), _directory, name);
                _shot++;
                _aimedAt = -1;
            }
            return;
        }
        WriteReport();
        GetTree().Quit(0);
    }

    private double _clock;
    private int _aimedAt = -1;

    private void Label(string text, Vector3 at) =>
        AddChild(new Label3D { Text = text, Position = at, PixelSize = 0.006f, FontSize = 40, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });

    private void WriteReport()
    {
        var lines = new List<string> { "# Art gallery", "", $"Asset workspace: {_art.Root ?? "none"}", "", "## Loaded", "" };
        lines.AddRange(_art.Used.Select(id => $"- `{id}`"));
        lines.AddRange(new[] { "", "## Could not be used", "" });
        lines.AddRange(_art.Problems.Count == 0 ? new[] { "None." } : _art.Problems.Select(p => $"- `{p.Key}`: {p.Value}"));
        lines.AddRange(new[] { "", "## Pictures", "" });
        lines.AddRange(_shots.Select(s => $"![{s.Name}]({s.Name}.png)"));
        File.WriteAllText(Path.Combine(_directory, "gallery.md"), string.Join("\n", lines) + "\n");
        GD.Print($"UNNAMED art gallery written to {_directory}: {_art.Used.Count} used, {_art.Problems.Count} problems");
    }

    /// <summary>A model's bounds in its own space, from its meshes.</summary>
    public static Aabb Bounds(Node3D root)
    {
        Aabb? total = null;
        foreach (var mesh in root.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is null)
                continue;
            var box = ArtLibrary.Relative(root, mesh) * mesh.Mesh.GetAabb();
            total = total is { } t ? t.Merge(box) : box;
        }
        return total ?? new Aabb(Vector3.Zero, Vector3.One);
    }
}
