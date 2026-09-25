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
    private readonly List<(SkinnedModel Model, string State, float Fraction)> _held = new();
    // Phase B (B0.4): bodies driven here each frame - figures at a pace, and bodies swung side to side (the spring's test).
    private readonly List<(SkinnedFigure Figure, float Speed)> _walkers = new();
    private readonly List<(Node3D Body, Vector3 At)> _swung = new();
    private readonly List<(string Name, SkinnedFigure Figure, Func<float, float, float> Ground)> _plantChecks = new();
    private readonly List<(string Name, SkinnedFigure Figure, Node3D Marker, LookAtModifier3D? Look)> _lookChecks = new();
    private readonly List<(string Name, SkinnedModel Body)> _tailChecks = new();
    private readonly List<string> _measured = new();
    // A modifier's result exists only while the skeleton updates (Godot discards it after skinning): the bones read are copied here then.
    private readonly Dictionary<(Skeleton3D, string), Transform3D> _watched = new();

    private void Watch(Skeleton3D skeleton, params string[] bones)
    {
        skeleton.SkeletonUpdated += () =>
        {
            foreach (string bone in bones)
            {
                int index = skeleton.FindBone(bone);
                if (index >= 0)
                    _watched[(skeleton, bone)] = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(index);
            }
        };
    }

    private Transform3D Watched(Skeleton3D skeleton, string bone) =>
        _watched.TryGetValue((skeleton, bone), out var pose) ? pose : skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone(bone));
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
            AddRetargetedClips(playerZ - 90f);
            AddModifierProofs(new Vector3(-200f, 3f, -200f));
        }
    }

    /// <summary>
    /// Phase B (B0.4): every clip retargeted onto the player's body from an external pack (<c>anim.player.*.ext_*.glb</c>, made by the
    /// retarget factory), a row a clip on the player's own model, five bodies held 5, 25, 50, 75 and 95 % through, from the front and the side.
    /// </summary>
    private void AddRetargetedClips(float z)
    {
        string model = _bindings.People["player"].Model;
        string folder = _art.Root is { } root ? Path.Combine(root, "animation", "ready", "player") : "";
        if (!Directory.Exists(folder))
            return;
        float[] fractions = { 0.05f, 0.25f, 0.5f, 0.75f, 0.95f };
        const float Spacing = 2.2f;
        foreach (string file in Directory.GetFiles(folder, "anim.player.*.ext_*.glb").Order(StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(file)["anim.".Length..];
            var clips = new Dictionary<string, string> { ["clip"] = id };
            for (int i = 0; i < fractions.Length; i++)
            {
                if (SkinnedModel.Create(_art, model, clips) is not { } body)
                {
                    _notes.Add($"{id}: not drawn on {model} ({_art.Why(id) ?? _art.Why(model) ?? "no model"})");
                    break;
                }
                body.Position = new Vector3(i * Spacing, 0, z);
                AddChild(body);
                Label($"{id.Split('.').Last()} {(int)(fractions[i] * 100)}%{(body.Has("clip") ? "" : " (NO CLIP)")}", new Vector3(i * Spacing, 2.3f, z));
                _held.Add((body, "clip", fractions[i]));
            }
            float middle = 2 * Spacing, rz = z;
            string name = id.Split('.').Last();
            _shots.Add(($"retarget_{name}_front", () => Aim(new Vector3(middle, 1.9f, rz + 9f), new Vector3(middle, 1.0f, rz))));
            _shots.Add(($"retarget_{name}_side", () => Aim(new Vector3(middle + 11f, 1.5f, rz + 2.5f), new Vector3(middle - 1f, 1.0f, rz))));
            z -= 30f;
        }
    }

    /// <summary>
    /// Phase B (B0.4): the engine's skeleton modifiers on our bodies. On a 20-degree side slope, the player's body three times - idle with
    /// no planting (the Phase-A look: a foot in the hill, a foot in the air), idle and walking with its feet planted (TwoBoneIK3D fed by
    /// FootPlanting). Two bodies on the flat turning their heads to a marker (LookAtModifier3D). Two hounds swung side to side, the second
    /// with a springy tail (SpringBoneSimulator3D). Photographed from the front and the side.
    /// </summary>
    private void AddModifierProofs(Vector3 at)
    {
        const float Degrees = 20f;
        float rise = MathF.Tan(Mathf.DegToRad(Degrees));
        float Slope(float x, float z) => at.Y + (x - at.X) * rise;
        var hill = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(10, 9) }, Position = at, RotationDegrees = new Vector3(0, 0, Degrees),
            MaterialOverride = _art.WorldMaterial("material_packed_dirt_ground") ?? new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.4f, 0.33f) },
        };
        AddChild(hill);
        var cases = new (string Name, bool Plant, float Speed)[] { ("no planting", false, 0), ("feet planted", true, 0), ("walking, planted", true, 1.4f) };
        for (int i = 0; i < cases.Length; i++)
        {
            if (SkinnedFigure.Create(_art, _bindings, "player") is not { } figure)
                return;
            // Side by side across the slope, each on its own point of it, facing down the row (+Z): one foot uphill of the other.
            float x = at.X - 2.2f + i * 2.2f;
            figure.Position = new Vector3(x, Slope(x, at.Z), at.Z);
            AddChild(figure);
            if (cases[i].Plant && BodyModifiers.PlantFeet(figure.Skeleton, figure, Slope) is null)
                _notes.Add("foot planting: the player's rig lacks a leg bone");
            Label(cases[i].Name, figure.Position + new Vector3(0, 2.2f, 0));
            _walkers.Add((figure, cases[i].Speed));
            _plantChecks.Add((cases[i].Name, figure, Slope));
            Watch(figure.Skeleton, "foot.L", "foot.R", "hips");
            var feet = figure.Position;
            _shots.Add(($"modifiers_slope_feet_{i + 1}", () => Aim(feet + new Vector3(0.2f, 0.55f, 2.2f), feet + new Vector3(0, 0.25f, 0))));
        }
        _shots.Add(("modifiers_slope_front", () => Aim(at + new Vector3(0, 1.8f, 8.5f), at + new Vector3(0, 0.9f, 0))));

        // The head: two bodies on the flat, a marker to the left of one and up to the right of the other.
        var flat = at + new Vector3(0, 0, -14f);
        for (int i = 0; i < 2; i++)
        {
            if (SkinnedFigure.Create(_art, _bindings, "player") is not { } figure)
                return;
            figure.Position = flat + new Vector3(i * 2.4f, 0, 0);
            AddChild(figure);
            var marker = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.3f, 0.2f), EmissionEnabled = true, Emission = new Color(0.6f, 0.1f, 0.05f) },
                Position = figure.Position + (i == 0 ? new Vector3(1.4f, 1.55f, 1.2f) : new Vector3(-1.2f, 2.3f, 1.4f)),
            };
            AddChild(marker);
            var look = BodyModifiers.LookAt(figure.Skeleton, figure, marker);
            if (look is null)
                _notes.Add("look-at: the player's rig has no head bone");
            _lookChecks.Add((i == 0 ? "looks left" : "looks up, right", figure, marker, look));
            Watch(figure.Skeleton, "head");
            Label(i == 0 ? "looks left" : "looks up, right", figure.Position + new Vector3(0, 2.4f, 0));
            _walkers.Add((figure, 0));
        }
        _shots.Add(("modifiers_look_front", () => Aim(flat + new Vector3(1.2f, 1.7f, 5.5f), flat + new Vector3(1.2f, 1.4f, 0))));

        // The tail: two hounds swung side to side; the second's tail springs.
        var hound = _bindings.Creatures.FirstOrDefault(c => c.Key.Contains("hound", StringComparison.Ordinal));
        if (hound.Value is { } houndLook)
        {
            var yard = at + new Vector3(0, 0, -26f);
            for (int i = 0; i < 2; i++)
            {
                if (SkinnedModel.Create(_art, houndLook.Model, houndLook.Clips) is not { } body)
                    break;
                body.Position = yard + new Vector3(i * 3f, 0, 0);
                AddChild(body);
                if (body.Has("idle"))
                    body.Play("idle");
                if (i == 1 && BodyModifiers.Spring(body.Skeleton, "tail", 0.5f) is null)
                    _notes.Add($"spring: {houndLook.Model} has no tail bone");
                Label(i == 0 ? "tail as clipped" : "tail on a spring", body.Position + new Vector3(0, 1.4f, 0));
                _swung.Add((body, body.Position));
                _tailChecks.Add((i == 0 ? "tail as clipped" : "tail on a spring", body));
                Watch(body.Skeleton, "tail");
            }
            _shots.Add(("modifiers_tail_above", () => Aim(yard + new Vector3(1.5f, 4.5f, 2.5f), yard + new Vector3(1.5f, 0.3f, 0))));
        }
    }

    private void Aim(Vector3 eye, Vector3 target) => _camera.LookAtFromPosition(eye, target, Vector3.Up);

    public override void _Process(double delta)
    {
        _clock += delta;
        foreach (var (_, model, state) in _creatures)
            model.Hold(state, _fraction, 0);
        foreach (var (model, state, fraction) in _held)
            model.Hold(state, fraction, 0);
        foreach (var (figure, speed) in _walkers)
            figure.Pose(figure.Position, 0, speed, delta);
        // Swung side to side and turned with it, fast enough that a springy tail trails.
        foreach (var (body, home) in _swung)
        {
            float t = (float)_clock * 3.2f;
            body.Position = home + new Vector3(MathF.Sin(t) * 0.5f, 0, 0);
            body.Rotation = new Vector3(0, MathF.Sin(t) * 0.7f, 0);
        }
        foreach (var pose in _poses)
        {
            pose.Set(pose.Figure);
            // The body faces the camera's side of the row a little; locomotion plays at its pace, so the picture catches it mid-stride.
            pose.Figure.Pose(pose.Figure.Position, Mathf.DegToRad(30), pose.Speed, delta);
        }
        Measure();
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

    /// <summary>
    /// The modifiers measured, not just photographed (once, four seconds in): each foot's height over the ground under it, the angle between
    /// each looking head's face and its marker, and how far each tail bone's direction swings from the clip's over half a second.
    /// </summary>
    private readonly Dictionary<string, (float Min, float Max)> _tailSwing = new();

    private void Measure()
    {
        foreach (var (name, body) in _tailChecks)
        {
            var skeleton = body.Skeleton;
            int tail = skeleton.FindBone("tail");
            if (tail < 0)
                continue;
            // The tail's direction in the body's own frame (its yaw removed), as an angle about the body's up.
            var along = body.GlobalTransform.Basis.Inverse() * Watched(skeleton, "tail").Basis.Y;
            float angle = Mathf.RadToDeg(MathF.Atan2(along.X, -along.Z));
            var (lo, hi) = _tailSwing.GetValueOrDefault(name, (float.MaxValue, float.MinValue));
            if (_clock > 3.5)
                _tailSwing[name] = (Math.Min(lo, angle), Math.Max(hi, angle));
        }
        if (_measured.Count > 0 || _clock < 4.0)
            return;
        foreach (var (name, figure, ground) in _plantChecks)
        {
            var skeleton = figure.Skeleton;
            var parts = new List<string>();
            foreach (string foot in new[] { "foot.L", "foot.R" })
            {
                var p = Watched(skeleton, foot).Origin;
                parts.Add($"{foot} ankle {p.Y - ground(p.X, p.Z):0.000} m over the ground under it");
            }
            var planting = skeleton.GetChildren().OfType<FootPlanting>().FirstOrDefault();
            _measured.Add($"slope, {name}: {string.Join("; ", parts)}{(planting is not null ? $"; hips dropped {planting.Drop:0.000} m" : "")}");
        }
        foreach (var (name, figure, marker, look) in _lookChecks)
        {
            var skeleton = figure.Skeleton;
            int head = skeleton.FindBone("head");
            if (head < 0 || look is null)
                continue;
            var pose = Watched(skeleton, "head");
            var axis = look.ForwardAxis switch
            {
                SkeletonModifier3D.BoneAxis.PlusX => pose.Basis.X, SkeletonModifier3D.BoneAxis.MinusX => -pose.Basis.X,
                SkeletonModifier3D.BoneAxis.PlusY => pose.Basis.Y, SkeletonModifier3D.BoneAxis.MinusY => -pose.Basis.Y,
                SkeletonModifier3D.BoneAxis.PlusZ => pose.Basis.Z, _ => -pose.Basis.Z,
            };
            float off = Mathf.RadToDeg(axis.Normalized().AngleTo((marker.GlobalPosition - pose.Origin).Normalized()));
            float bodyOff = Mathf.RadToDeg(figure.GlobalTransform.Basis.Z.Normalized().AngleTo((marker.GlobalPosition - pose.Origin).Normalized()));
            _measured.Add($"look, {name}: the face ({look.ForwardAxis}) is {off:0.0} deg off the marker; the body's facing is {bodyOff:0.0} deg off it");
        }
    }

    private void Label(string text, Vector3 at) =>
        AddChild(new Label3D { Text = text, Position = at, PixelSize = 0.004f, FontSize = 36, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });

    private void WriteReport()
    {
        var lines = new List<string> { "# Animation sheet", "", $"Asset workspace: {_art.Root ?? "none"}", "" };
        lines.AddRange(_notes.Count == 0 ? new[] { "Every bound creature and the player drawn." } : _notes.Select(n => $"- {n}"));
        if (_measured.Count > 0 || _tailSwing.Count > 0)
        {
            lines.AddRange(new[] { "", "## Skeleton modifiers, measured (Phase B, B0.4)", "" });
            lines.AddRange(_measured.Select(m => $"- {m}"));
            lines.AddRange(_tailSwing.Select(t => $"- {t.Key}: the tail swings {t.Value.Max - t.Value.Min:0.0} deg in the body's frame ({t.Value.Min:0.0} to {t.Value.Max:0.0})"));
        }
        lines.AddRange(new[] { "", "## Pictures", "" });
        lines.AddRange(_shots.Select(s => $"![{s.Name}]({s.Name}.png)"));
        lines.AddRange(new[] { "", "## Library problems", "" });
        lines.AddRange(_art.Problems.Count == 0 ? new[] { "None." } : _art.Problems.Select(p => $"- `{p.Key}`: {p.Value}"));
        File.WriteAllText(Path.Combine(_directory, "animation_sheet.md"), string.Join("\n", lines) + "\n");
        GD.Print($"UNNAMED animation sheet written to {_directory}: {_shots.Count} pictures");
    }
}
