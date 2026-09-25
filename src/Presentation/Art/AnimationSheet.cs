// UNNAMED Presentation - every bound creature and the player's body in every state the game shows, photographed (Phase A's rig check)
// Godot presentation only, a harness: nothing here is game state (D-11)

using Godot;
using UNNAMED.Presentation.Player;
using UNNAMED.World.Runtime;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// <c>--anim-sheet dir</c>: the rigs as the game draws them, state by state, on a lit floor. Each bound creature's body (the game's
/// <see cref="SkinnedModel"/>, its six clips retargeted as the game retargets them) stands six times in a row - idle, walk, run, attack, hit,
/// death - held at a point through each clip, and is photographed three times (20 %, 50 % and 85 % through), so a foot through the floor, a
/// limb bent about a point off the body or a clip that does not fit its skeleton shows at a glance. Then the player's body, driven through the
/// game's own figure (<see cref="SkinnedFigure"/>: the stance, the posture, the downed state, the weapon in hand) in every state the game asks
/// of it, from the front, the side and the back; and in first person with each weapon. A harness: it may name what it shows.
/// </summary>
public partial class AnimationSheet : Node3D
{
    private static readonly string[] CreatureStates = { "idle", "walk", "run", "attack", "hit", "death" };
    private static readonly float[] Fractions = { 0.2f, 0.5f, 0.85f };

    private sealed record Pose(string Name, SkinnedFigure Figure, Action<SkinnedFigure> Set, float Speed);

    private readonly ArtLibrary _art;
    private readonly ArtBindings _bindings;
    private readonly string _directory;
    private readonly Camera3D _camera = new() { Fov = 40, Current = true, Near = 0.05f };
    private readonly List<(string Name, Action Setup)> _shots = new();
    private readonly List<(string Creature, SkinnedModel Model, string State)> _creatures = new();
    private readonly List<Pose> _poses = new();
    private readonly List<string> _notes = new();
    private int _shot;
    private int _wait = -1;
    private double _clock;
    private double _clockAtShot;
    private float _fraction = 0.5f;

    /// <summary>How far into a reach (its clip is 1.4 s) its picture is taken: the middle of its hold at full stretch.</summary>
    private const double ReachSeconds = 0.7;

    public AnimationSheet(ArtLibrary art, ArtBindings bindings, string directory)
    {
        _art = art;
        _bindings = bindings;
        _directory = directory;
    }

    public override void _Ready()
    {
        Directory.CreateDirectory(_directory);
        DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
        _art.Withhold(_bindings.Withheld);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.62f, 0.66f, 0.70f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.62f, 0.66f), AmbientLightEnergy = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Agx, SsaoEnabled = true,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), ShadowEnabled = true, LightEnergy = 1.2f });
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(800, 800) }, Position = new Vector3(40, 0, -200) };
        floor.MaterialOverride = _art.WorldMaterial("material_packed_dirt_ground") ?? new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.38f, 0.33f) };
        AddChild(floor);
        AddChild(_camera);

        // The creatures: a row each, six states along +X, facing a little towards the camera.
        float z = 0;
        foreach (var (defId, creature) in _bindings.Creatures)
        {
            var first = SkinnedModel.Create(_art, creature.Model, creature.Clips);
            if (first is null)
            {
                _notes.Add($"{defId}: {creature.Model} not drawn ({_art.Why(creature.Model) ?? "no model"})");
                continue;
            }
            var size = ArtGallery.Bounds(first).Size;
            first.Free();
            float spacing = Math.Max(1.2f, Math.Max(size.X, size.Z) * 1.25f + 0.4f);
            for (int i = 0; i < CreatureStates.Length; i++)
            {
                var model = SkinnedModel.Create(_art, creature.Model, creature.Clips)!;
                model.Position = new Vector3(i * spacing, 0, z);
                model.Rotation = new Vector3(0, Mathf.DegToRad(60), 0);
                AddChild(model);
                Label($"{CreatureStates[i]}{(model.Has(CreatureStates[i]) ? "" : " (NO CLIP)")}", new Vector3(i * spacing, size.Y + 0.35f, z));
                _creatures.Add((defId, model, CreatureStates[i]));
            }
            float middle = (CreatureStates.Length - 1) * spacing / 2, row = z, height = size.Y;
            float distance = Math.Max(5f, CreatureStates.Length * spacing * 0.8f);
            string name = defId.Split('.').Last();
            foreach (float fraction in Fractions)
            {
                _shots.Add(($"creature_{name}_{(int)(fraction * 100):00}", () =>
                {
                    _fraction = fraction;
                    Aim(new Vector3(middle, height * 0.9f + distance * 0.18f, row + distance), new Vector3(middle, height * 0.45f, row));
                }));
            }
            // Far enough apart that the row before stands behind the camera.
            z -= 40f;
        }

        // The player: the game's figure, posed through its own interface, each state the game shows.
        z -= 6;
        if (_bindings.People.ContainsKey("player"))
        {
            string? Weapon(string family) => _bindings.Weapons.FirstOrDefault(w => w.Value.Family == family).Key;
            string? sword = Weapon("sword"), bow = Weapon("bow"), spear = Weapon("polearm");
            var poses = new (string Name, string? Hold, Action<SkinnedFigure> Set, float Speed)[]
            {
                ("idle", null, _ => { }, 0),
                ("walk", null, _ => { }, 1.4f),
                ("run", null, _ => { }, 3.2f),
                ("sprint", null, _ => { }, 5f),
                ("crouch", null, f => f.SetPosture(true, false), 0),
                ("jump tuck", null, f => f.SetPosture(false, true), 0),
                ("sword held", sword, f => f.SetStance(new CombatStance(CombatPhase.Idle, 0, Held.Sword, false)), 0),
                ("sword windup", sword, f => f.SetStance(new CombatStance(CombatPhase.Windup, 0.9f, Held.Sword, false)), 0),
                ("sword strike", sword, f => f.SetStance(new CombatStance(CombatPhase.Active, 0.5f, Held.Sword, false)), 0),
                ("sword recovery", sword, f => f.SetStance(new CombatStance(CombatPhase.Recovery, 0.5f, Held.Sword, false)), 0),
                ("sword guard", sword, f => f.SetStance(new CombatStance(CombatPhase.Idle, 0, Held.Sword, true)), 0),
                ("bow held", bow, f => f.SetStance(new CombatStance(CombatPhase.Idle, 0, Held.Bow, false)), 0),
                ("bow aimed", bow, f => f.SetStance(new CombatStance(CombatPhase.Idle, 0, Held.Bow, true)), 0),
                ("bow drawing", bow, f => f.SetStance(new CombatStance(CombatPhase.Windup, 0.6f, Held.Bow, false)), 0),
                ("bow loosed", bow, f => f.SetStance(new CombatStance(CombatPhase.Active, 0.5f, Held.Bow, false)), 0),
                ("spear guard", spear, f => f.SetStance(new CombatStance(CombatPhase.Idle, 0, Held.Spear, true)), 0),
                ("spear windup", spear, f => f.SetStance(new CombatStance(CombatPhase.Windup, 0.9f, Held.Spear, false)), 0),
                ("spear thrust", spear, f => f.SetStance(new CombatStance(CombatPhase.Active, 0.5f, Held.Spear, false)), 0),
                ("cast", null, f => f.SetStance(new CombatStance(CombatPhase.Windup, 0.9f, Held.Nothing, false, true)), 0),
                ("hit", sword, f => f.SetStance(new CombatStance(CombatPhase.Staggered, 0.5f, Held.Sword, false)), 0),
                ("dead", null, f => f.SetDowned(true), 0),
                ("walk with sword", sword, f => f.SetStance(new CombatStance(CombatPhase.Idle, 0, Held.Sword, false)), 1.4f),
                ("interact", null, _ => { }, 0),
                ("crouched walk", sword, f => f.SetPosture(true, false), 1.6f),
            };
            const float Spacing = 1.5f;
            for (int i = 0; i < poses.Length; i++)
            {
                if (SkinnedFigure.Create(_art, _bindings, "player") is not { } figure)
                {
                    _notes.Add($"player: {_bindings.People["player"].Model} not drawn ({_art.Why(_bindings.People["player"].Model) ?? "no model"})");
                    break;
                }
                int row = i / 11, column = i % 11;
                figure.Position = new Vector3(column * Spacing, 0, z - row * 30f);
                AddChild(figure);
                figure.Hold(poses[i].Hold);
                Label(poses[i].Name, new Vector3(column * Spacing, 2.15f, z - row * 30f));
                _poses.Add(new Pose(poses[i].Name, figure, poses[i].Set, poses[i].Speed));
            }
            float playerZ = z;
            for (int row = 0; row < (poses.Length + 10) / 11; row++)
            {
                float rz = playerZ - row * 30f, middle = 5 * Spacing;
                int r = row;
                _shots.Add(($"player_row{r + 1}_front", () => Aim(new Vector3(middle, 2.2f, rz + 11f), new Vector3(middle, 1.0f, rz))));
                _shots.Add(($"player_row{r + 1}_side", () => Aim(new Vector3(middle + 13f, 1.6f, rz + 3.2f), new Vector3(middle - 2f, 1.0f, rz))));
                _shots.Add(($"player_row{r + 1}_back", () => Aim(new Vector3(middle, 2.2f, rz - 11f), new Vector3(middle, 1.0f, rz))));
            }
            // Close-ups of the hands: the sword, the bow and the spear held, each from a metre and a half.
            foreach (string pose in new[] { "sword held", "sword strike", "bow drawing", "spear thrust", "crouch", "crouched walk", "dead" })
            {
                if (_poses.FirstOrDefault(p => p.Name == pose) is not { } p)
                    continue;
                var at = p.Figure.Position;
                _shots.Add(($"player_close_{pose.Replace(' ', '_')}", () => Aim(at + new Vector3(1.3f, 1.5f, 1.6f), at + new Vector3(0, 1.0f, 0))));
            }
            // A reach is a one-shot: started as its picture is set up, photographed at full stretch (ReachSeconds in).
            if (_poses.FirstOrDefault(p => p.Name == "interact") is { } reach)
            {
                var at = reach.Figure.Position;
                _shots.Add(("player_close_interact", () =>
                {
                    reach.Figure.Interact();
                    Aim(at + new Vector3(1.6f, 1.4f, 0.9f), at + new Vector3(0, 0.8f, 0));
                }));
            }
            // First person: the eye inside the head, the body shadow-only as the game draws it, each weapon in hand.
            foreach (string pose in new[] { "sword held", "bow aimed", "spear guard", "sword strike" })
            {
                if (_poses.FirstOrDefault(p => p.Name == pose) is not { } p)
                    continue;
                var figure = p.Figure;
                _shots.Add(($"player_first_person_{pose.Replace(' ', '_')}", () =>
                {
                    foreach (var other in _poses)
                        other.Figure.SetFirstPerson(other.Figure == figure);
                    // Along the body's facing (a figure faces its +Z), from just ahead of the head's centre, as the camera rig puts the eye.
                    var forward = figure.GlobalTransform.Basis.Z.Normalized();
                    var eye = figure.Position + new Vector3(0, Avatar.EyeHeight, 0) + forward * 0.1f;
                    Aim(eye, eye + forward + new Vector3(0, -0.35f, 0));
                }));
            }
            _shots.Add(("player_first_person_off", () =>
            {
                foreach (var other in _poses)
                    other.Figure.SetFirstPerson(false);
                Aim(new Vector3(5 * Spacing, 2.2f, playerZ + 11f), new Vector3(5 * Spacing, 1.0f, playerZ));
            }));
        }
    }

    private void Aim(Vector3 eye, Vector3 target) => _camera.LookAtFromPosition(eye, target, Vector3.Up);

    public override void _Process(double delta)
    {
        _clock += delta;
        foreach (var (_, model, state) in _creatures)
            model.Hold(state, _fraction, 0);
        foreach (var pose in _poses)
        {
            pose.Set(pose.Figure);
            // The body faces the camera's side of the row a little; locomotion plays at its pace, so the picture catches it mid-stride.
            pose.Figure.Pose(pose.Figure.Position, Mathf.DegToRad(30), pose.Speed, delta);
        }
        if (_clock < 3.0)
            return;
        if (_shot >= _shots.Count)
        {
            WriteReport();
            GetTree().Quit(0);
            return;
        }
        if (_wait < 0)
        {
            _shots[_shot].Setup();
            _wait = 0;
            _clockAtShot = _clock;
            return;
        }
        // A few frames for the new view (and the held poses) to draw; the dead and the drawn bow need their clip's time, given at the start.
        // The reach waits until it is at full stretch.
        if (++_wait < 8 || _shots[_shot].Name == "player_close_interact" && _clockAtShot + ReachSeconds > _clock)
            return;
        Main.SaveScreenshot(GetViewport(), _directory, _shots[_shot].Name);
        _shot++;
        _wait = -1;
    }

    private void Label(string text, Vector3 at) =>
        AddChild(new Label3D { Text = text, Position = at, PixelSize = 0.004f, FontSize = 36, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });

    private void WriteReport()
    {
        var lines = new List<string> { "# Animation sheet", "", $"Asset workspace: {_art.Root ?? "none"}", "" };
        lines.AddRange(_notes.Count == 0 ? new[] { "Every bound creature and the player drawn." } : _notes.Select(n => $"- {n}"));
        lines.AddRange(new[] { "", "## Pictures", "" });
        lines.AddRange(_shots.Select(s => $"![{s.Name}]({s.Name}.png)"));
        lines.AddRange(new[] { "", "## Library problems", "" });
        lines.AddRange(_art.Problems.Count == 0 ? new[] { "None." } : _art.Problems.Select(p => $"- `{p.Key}`: {p.Value}"));
        File.WriteAllText(Path.Combine(_directory, "animation_sheet.md"), string.Join("\n", lines) + "\n");
        GD.Print($"UNNAMED animation sheet written to {_directory}: {_shots.Count} pictures");
    }
}
