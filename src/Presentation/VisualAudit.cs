// UNNAMED Presentation - the visual audit: pictures of chosen assets as the game draws them, and alone on a neutral stage, with what was drawn
// Godot presentation only: it moves a camera and reads the scene; it submits no command and changes nothing the game draws (D-11)

using System.Text.Json;
using Godot;
using UNNAMED.Application;
using UNNAMED.Presentation.Player;

namespace UNNAMED.Presentation;

/// <summary>
/// <c>--visual-audit dir</c>: the game booted as a player gets it (the same session, the hollow with its art bindings, NPCs, creatures, the
/// game's lighting and camera settings), the HUD hidden, and a camera stood at each shot's eye looking at its target - a 1920x1080 picture
/// and a JSON dump of what is drawn for the target (mesh by mesh: the level shown at that distance, triangles, each surface's material and
/// textures, greybox or not) and of the renderer settings in effect. Then each sample alone on a neutral grey stage in a 1536x1536 view,
/// loaded the way the game loads it, with the same dump. A harness: it may name what it shows.
/// </summary>
public sealed class VisualAudit
{
    private const int WarmupFrames = 150;
    private const int SettleFrames = 75;
    /// <summary>A shots file's per-shot sky: "hour" (0-24) and "celestial" (a look), pinned as the shot starts.</summary>
    private static readonly Dictionary<string, (string? Hour, string? Look)> SkyFor = new();
    private const int MeasureFrames = 60;   // after settling: the median GPU, CPU and frame time of these (Phase B's comparisons)
    private const float IsoFov = 35f;
    private const float IsoAzimuth = 45f;
    private const float IsoElevation = 35f;

    /// <summary>A picture: the camera at an eye (x, height above the ground, z) looking at a target (the same), and the nodes it is of.
    /// With <see cref="Near"/>, the picture is of whichever figure under that group stands nearest the point when it is taken (a creature
    /// wanders): the eye is then an offset from the figure, its height above the ground, and the target the figure's own height.
    /// <see cref="Frames"/> keeps that many consecutive frames of the measured window (raw/&lt;name&gt;_seq/), with the world running, for
    /// a temporal (flicker) measure; <see cref="Move"/> walks the eye and the target by that offset across those frames. Run it with
    /// Godot's <c>--fixed-fps 60</c> so the wind steps the same amount every frame however long a frame's readback takes.
    /// <see cref="Hide"/> names nodes kept invisible for the shot, so one run can measure a view with and without them.</summary>
    internal sealed record Shot(string Name, string Sample, (float X, float H, float Z) Eye, (float X, float H, float Z) Target, string[] Targets, string Says,
        (string Under, float X, float Z)? Near = null, bool Motion = false, int Frames = 0, (float X, float H, float Z)? Move = null,
        string[]? Hide = null);

    // Gameplay shots at a player's distance: the third-person camera stands 3.5 m behind a body, about 2.65 m up (eye 1.62 m, pitch -0.3).
    /// <summary>The in-world shots (the A/B harness, <see cref="VisualAuditAB"/>, takes the same ones).</summary>
    internal static readonly Shot[] WorldShots =
    {
        new Shot("world_1_longhouse_far", "longhouse", (60.5f, 2.65f, 120f), (44f, 2.2f, 128f), new[] { "longhouse", "door.longhouse_door", "Terrain" },
            "Renn's lodge from the south-east, 18 m from its centre"),
        new Shot("world_1_longhouse_door", "longhouse", (59f, 2.65f, 128f), (52f, 1.6f, 128f), new[] { "longhouse", "door.longhouse_door", "Terrain" },
            "The lodge's east door from 7 m"),
        new Shot("world_2_flora_oak_tree_far", "flora_oak_tree", (98f, 2.65f, 156f), (108f, 6.5f, 158f), new[] { "tree_01", "Terrain" },
            "tree_01 (an oak by the id hash) from 10 m"),
        new Shot("world_2_flora_oak_tree_trunk", "flora_oak_tree", (104.2f, 2.65f, 157f), (108f, 2.2f, 158f), new[] { "tree_01", "Terrain" },
            "tree_01's trunk from 4 m"),
        new Shot("world_3_prop_cart_damaged_merchant", "prop_cart_damaged_merchant", (151.8f, 2.65f, 159f), (157f, 0.7f, 162.2f),
            new[] { "cart_wreck", "Terrain" }, "The merchant cart wreck from 6 m"),
        new Shot("world_4_rock_boulder", "rock_boulder", (15.8f, 2.65f, 85.5f), (10f, 1.0f, 84f), new[] { "rock_rim_01", "Terrain" },
            "rock_rim_01 (bound to rock_boulder) from 6 m"),
        new Shot("world_5_container_chest_iron_banded", "container_chest_iron_banded", (39.2f, 2.3f, 139.5f), (38f, 0.35f, 137f),
            new[] { "container.waystation_chest", "Terrain" }, "The waystation chest from 3 m"),
        new Shot("world_6_npc_veth_magistrate", "npc_veth_magistrate", (49f, 1.75f, 126.2f), (46.5f, 1.45f, 126.2f),
            new[] { "npc.ashen_hollow.renn_vale", "longhouse" }, "Renn Vale inside the lodge from 2.5 m, as the game lights it"),
        new Shot("world_7_establishing_waystation", "establishing", (27.9f, 2.65f, 160.8f), (44f, 1.8f, 134f),
            new[] { "longhouse", "forge", "container.waystation_chest", "rock_waystone", "Player", "Terrain" },
            "The waystation from the spawn stone, 3.5 m behind the body at spawn"),
        new Shot("world_8_establishing_charwood", "establishing", (124f, 2.65f, 158f), (150f, 3f, 164f),
            new[] { "tree_07", "tree_08", "tree_11", "tree_01", "tree_03", "Terrain" }, "Inside the Charwood, looking east"),
    };

    private sealed record Sample(int N, string Id, string Says, Func<Node3D?> Load);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly Node3D _root;
    private readonly GameSession _session;
    private readonly Art.ArtLibrary _art;
    private readonly Art.ArtBindings _bindings;
    private readonly CameraRig _rig;
    private readonly string _raw;
    private readonly List<Shot> _shots = new();
    private readonly List<Sample> _samples = new();
    private readonly List<Dictionary<string, object?>> _log = new();
    private readonly Dictionary<ulong, Dictionary<string, object?>> _textureCache = new();
    private Camera3D? _camera;
    private SubViewport? _stage;
    private Camera3D? _stageCamera;
    private MeshInstance3D? _floor;
    private Node3D? _sample;
    private Figure? _figure;
    private int _frame;
    private int _index;
    private int _wait;
    private readonly List<double> _gpu = new(), _cpu = new(), _wall = new();
    private ulong _last;
    private readonly bool _scatterAb = System.Environment.GetEnvironmentVariable("UNNAMED_SCATTER_AB") == "1";
    private int _scatterStep;
    private static readonly string[] ScatterArms = { "A", "A2", "B", "A3", "B2" };

    // ── THE HAIR A/B: one session, one runtime asset swap, and its own controls ──────────────────────────
    //
    // WHY THIS EXISTS. RUN_COMMANDS.md TRAP 12 measured that two captures of the SAME station with the SAME
    // camera and the SAME sun hour differ by 49-56 % of ALL pixels, so an A/B whose two arms come from two
    // runs carries an unmeasured confound. The fix it prescribes is to capture both arms in ONE session with
    // a runtime asset swap - which is what this is. Nothing else about the run changes: the same figure, the
    // same idle phase, the same lighting, the same camera; only the hair MESH resource on one
    // MeshInstance3D is exchanged.
    //
    // The five arms are ordered so that the comparison carries its own controls:
    //   A   the shipped groom                                     ┐ nothing is swapped between these two, so
    //   A2  the shipped groom again, 135 frames later             ┘ A-vs-A2 is the IDENTICAL-IMAGE CONTROL
    //   B   the staged groom
    //   A3  the shipped groom again, reached by swapping BACK      the swap's own control: A-vs-A3 must be 0
    //   B2  the staged groom again                                 the staged arm's own stability
    //
    // The swap is a Mesh resource assignment, not a file copy: the staged GLB is loaded once from disk with
    // Godot's own glTF importer, gets the same `CharacterMaterials.Upgrade` pass the live figure got, and the
    // mesh is handed to the MeshInstance3D that already carries the shipped groom. Its Skin and Skeleton are
    // left exactly as they are, which is sound only because the splice kept the shipped skeleton and the
    // groom's JOINTS_0 indices were verified to address the same bones in the same order.
    private static readonly string[] HairArms = { "A", "A2", "B", "A3", "B2" };
    private readonly int _settleExtra;
    private bool _entered;
    private readonly string? _enter;
    private readonly string? _hairGlb;
    private readonly string _hairFrom;
    private readonly string _hairTo;
    private readonly string _hairSubject;
    private readonly bool _hairFreeze;
    private bool _hairFrozen;

    /// <summary>
    /// Whether the world is frozen for the captures of an audit that is NOT the hair A/B.
    ///
    /// WHY THIS IS NOT THE HAIR'S PRIVATE TRICK ANY MORE. The hair round proved that `Engine.TimeScale = 0` makes an
    /// identical-image control read zero, and measured a control of 0.49-0.61 with it. **But the freeze was gated on
    /// `_hairGlb`, so every OTHER audit ran in a moving world** - and the controls say exactly what that costs:
    /// * the hair A/B, FROZEN: control **0.49-0.61** on a head crop;
    /// * a skin A/B at the established `face_sel` station, MOVING: median control **2.2411** over ten run pairs;
    /// * the same A/B at a CLOSE station (0.25-0.40 m), MOVING: median control **11.1-13.0**, range 0.48-16.37.
    /// **A comparison has to beat its control, and at close range the control was eleven.** *Nothing measured on those
    /// stations could have been read, and two rounds concluded "indistinguishable" about a shader that a crop shows is
    /// real.* **The freeze is the difference between an admissible A/B and an expensive coin toss.**
    ///
    /// A MOTION shot is the exception and restores the moving world for its own capture: `m_smoke` and `m_grass` exist
    /// to show what moves on its own, and freezing them would make them two identical pictures. `--audit-no-freeze`
    /// restores the old behaviour for the whole run; `--hair-no-freeze` still means what it meant.
    /// </summary>
    private readonly bool _auditFreeze;
    private bool _auditFrozen;
    private readonly List<Dictionary<string, object?>> _hairLog = new();
    private MeshInstance3D? _hairInstance;
    private Mesh? _hairOriginal;
    private Mesh? _hairStaged;
    private int _hairStep;

    /// <param name="shotsFile"><c>--audit-shots file.json</c>: these world pictures in place of the built-in ones, and no isolated samples
    /// (a checkpoint's screenshot list). Each entry: <c>name</c>, <c>says</c>, then either <c>eye</c> and <c>target</c> as [x, height, z],
    /// or <c>near</c> as [group, x, z] with <c>eye</c> an offset [dx, height, dz] from that figure and <c>target</c> [0, height, 0].</param>
    public VisualAudit(Node3D root, GameSession session, Art.ArtLibrary art, Art.ArtBindings bindings, CameraRig rig, string directory,
        string? shotsFile = null)
    {
        _root = root;
        _session = session;
        _art = art;
        _bindings = bindings;
        _rig = rig;
        Directory = directory;
        _raw = Path.Combine(directory, "raw");
        System.IO.Directory.CreateDirectory(_raw);
        // Godot's launch options own the window size.
        // Measured, not paced (the per-shot GPU and frame times are Phase B's comparisons): no vsync, no cap.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        // The hair A/B's switches come from the command line directly, in the shape VisualAuditAB uses for its
        // own tuning switches, so that `--visual-audit` needs no change to Main.cs's option table. With none of
        // them given, every line below is dead and the run behaves exactly as it did before.
        var userArgs = OS.GetCmdlineUserArgs();
        string? UserArg(string name) => Array.IndexOf(userArgs, name) is var i and >= 0 && i + 1 < userArgs.Length
            ? userArgs[i + 1] : null;
        _enter = UserArg("--enter");
    _settleExtra = int.TryParse(UserArg("--settle"), out int settle) ? Math.Max(0, settle) : 0;
    _hairGlb = UserArg("--hair-glb");
        _hairFrom = UserArg("--hair-material-from") ?? "Human.elvs_hazel_hair";
        _hairTo = UserArg("--hair-material-to") ?? "Human.o4saken_long01";
        _hairSubject = UserArg("--hair-subject") ?? "Player";
        // THE WORLD IS FROZEN FOR THE HAIR A/B, AND THE FIRST RUN SHOWED WHY IT HAS TO BE.
        //
        // With the world running, the identical-image control (arm A against arm A2, 135 frames later, with
        // nothing swapped between them) read mean |d| 4.70 with 23.3 % of pixels past 8 - the SAME magnitude as
        // the comparison it was there to police (A vs B: 6.12 / 26.1 %). Every pixel of the figure had moved, so
        // no part of the frame difference could be attributed to the groom and the run was inadmissible by the
        // standard the brief sets. The cause is that the player's idle clip, the region's ambient life and TAA
        // history all advance with time, and TRAP 12 measured the same thing from outside.
        //
        // `Engine.TimeScale = 0` makes every `_Process(delta)` receive delta 0, so the simulation stops ticking
        // and the figure holds one pose, while the harness's own frame counters (which count frames, not time)
        // carry on. The scene tree is NOT paused: pausing would stop `Main._Process`, which is what drives this
        // audit. `--hair-no-freeze` restores the moving world, and the control then reports what it reports.
        _hairFreeze = UserArg("--hair-no-freeze") is null;

        // AND THE SAME FREEZE NOW COVERS EVERY AUDIT, not only the hair A/B - see the field's own note for the three
        // controls that measure what running unfrozen costs. Default on; `--audit-no-freeze` opts out.
        _auditFreeze = UserArg("--audit-no-freeze") is null;

        if (shotsFile is not null)
        {
            _shots.AddRange(LoadShots(shotsFile));
            return;
        }
        _shots.AddRange(WorldShots);

        _samples.AddRange(new[]
        {
            new Sample(1, "longhouse", "longhouse (ArtLibrary.ModelWithLods, as Fitting.Building loads it)", () => _art.ModelWithLods("longhouse")),
            new Sample(2, "flora_oak_tree", "flora_oak_tree (ArtLibrary.ModelWithLods, as Fitting.Structure loads it)", () => _art.ModelWithLods("flora_oak_tree")),
            new Sample(3, "prop_cart_damaged_merchant", "prop_cart_damaged_merchant (ModelWithLods)", () => _art.ModelWithLods("prop_cart_damaged_merchant")),
            new Sample(4, "rock_boulder", "rock_boulder (ModelWithLods; the game refuses it at the rim rocks)", () => _art.ModelWithLods("rock_boulder")),
            new Sample(5, "container_chest_iron_banded", "container_chest_iron_banded (ModelWithLods, as Fitting.Site loads it)",
                () => _art.ModelWithLods("container_chest_iron_banded")),
            new Sample(6, "npc_veth_magistrate", "Renn Vale (SkinnedFigure.Create: the rigged model and its idle clip)",
                () => Art.SkinnedFigure.Create(_art, _bindings, "npc.ashen_hollow.renn_vale")),
        });
    }

    public string Directory { get; }

    /// <summary>Advance one frame: null while working, then "done" or "failed".</summary>
    public string? Update()
    {
        _frame++;
        try
        {
            // A minimised window draws nothing, so every capture after it would be the last frame drawn again. Restore it and
            // wait rather than photograph a stale frame (a whole flicker sequence came back as one picture repeated).
            if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Minimized)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                return null;
            }
            if (_frame < WarmupFrames)
                return null;
            if (_index < _shots.Count)
            {
                WorldShot(_shots[_index]);
                return null;
            }
            if (_index < _shots.Count + _samples.Count)
            {
                Isolated(_samples[_index - _shots.Count]);
                return null;
            }
            Write("visual_audit_log.json", new Dictionary<string, object?>
            {
                ["pictures"] = _log,
                ["hair_ab"] = _hairGlb is null ? null : new Dictionary<string, object?>
                {
                    ["arms"] = HairArms,
                    ["staged_glb"] = _hairGlb,
                    ["material_from"] = _hairFrom,
                    ["material_to"] = _hairTo,
                    ["subject"] = _hairSubject,
                    ["captures"] = _hairLog,
                    ["note"] = "A and A2 are the identical-image control: nothing is swapped between them. "
                             + "A3 is the shipped groom reached by swapping back, which is the swap's own control.",
                },
                ["art_problems"] = _art.Problems,
                ["art_used"] = _art.Used.OrderBy(u => u, StringComparer.Ordinal).ToList(),
                ["measurement_control"] = MeasurementControl(),
            });
            GD.Print($"UNNAMED visual audit written to {Directory}");
            return "done";
        }
        catch (Exception e)
        {
            GD.PushError($"UNNAMED visual audit failed: {e}");
            return "failed";
        }
    }

    // ── in the world ────────────────────────────────────────────────────────

    private void WorldShot(Shot shot)
    {
        if (_hiddenFor != shot.Name)
            HideFor(shot);
        if (_camera is null)
        {
            // A camera like the player's: its field of view, near and far planes, in the same viewport and world.
            _camera = new Camera3D { Name = "AuditCamera", Fov = _rig.Camera.Fov, Near = _rig.Camera.Near, Far = _rig.Camera.Far,
                Attributes = _rig.Camera.Attributes, Environment = _rig.Camera.Environment };
            _root.AddChild(_camera);
            Greybox.StartCamera.Track(_root, _camera);
        }
        foreach (var layer in _root.FindChildren("*", nameof(CanvasLayer), true, false).Cast<CanvasLayer>())
            layer.Visible = false;
        var eye = Ground(shot.Eye);
        var target = Ground(shot.Target);
        if (shot.Move is { } move)
        {
            // Still through the settle, then a straight walk across the kept frames.
            float t = Math.Clamp((_wait - SettleFrames) / (float)Math.Max(shot.Frames, 1), 0f, 1f);
            eye = Ground((shot.Eye.X + move.X * t, shot.Eye.H + move.H * t, shot.Eye.Z + move.Z * t));
            target = Ground((shot.Target.X + move.X * t, shot.Target.H + move.H * t, shot.Target.Z + move.Z * t));
        }
        if (shot.Near is { } near)
        {
            var group = _root.GetNodeOrNull(near.Under);
            var figure = group is Art.SkinnedFigure person ? person : group?.GetChildren().OfType<Node3D>()
                .MinBy(n => new Vector2(n.GlobalPosition.X - near.X, n.GlobalPosition.Z - near.Z).LengthSquared());
            if (figure is null)
                throw new InvalidOperationException($"{shot.Name}: nothing under {near.Under}");
            var at = figure.GlobalPosition;
            eye = Ground((at.X + shot.Eye.X, shot.Eye.H, at.Z + shot.Eye.Z));
            target = at + new Vector3(0, shot.Target.H, 0);
            shot = shot with { Targets = new[] { figure.Name.ToString() } };
        }
        if (_hairGlb is not null)
        {
            // Hair A/B framing: the shot's eye and target are OFFSETS from the subject figure's own origin, so a
            // shot file says "0.55 m out and 0.62 m up, looking at head height" and the figure's world position is
            // read from the live scene. That matters here because the head must fill the frame: at the audit's
            // vertical fov of 75 and a 1080-px frame the focal length is 704 px, so a 0.32 m head at 0.85 m is
            // 0.32/0.85*704 = 265 px across - enough to judge the shape of a groom.
            var subject = _root.GetNodeOrNull(_hairSubject) as Node3D
                ?? throw new InvalidOperationException($"{shot.Name}: no Node3D called {_hairSubject} to frame");
            var at = subject.GlobalPosition;
            eye = at + new Vector3(shot.Eye.X, shot.Eye.H, shot.Eye.Z);
            target = at + new Vector3(shot.Target.X, shot.Target.H, shot.Target.Z);
            shot = shot with { Targets = new[] { _hairSubject } };
        }
        if (_hairGlb is not null && !_hairFrozen)
        {
            // Frozen once, before the first capture of the first station, so that every one of the five arms of
            // every station is shot in a world that is not moving.
            _hairFrozen = true;
            if (_hairFreeze)
            {
                Engine.TimeScale = 0.0;
                _auditFrozen = true;
                GD.Print("UNNAMED hair A/B: Engine.TimeScale = 0 - the world is frozen for every capture, so the "
                         + "identical-image control can read zero and the comparison is admissible");
            }
        }
        // THE SAME FREEZE FOR EVERY OTHER AUDIT, and the motion shot is the one exception. A motion shot captures its
        // own first frame and compares it with its last, so it exists to show what MOVES - frozen, those two frames
        // are the same picture and the shot is worthless. So a motion shot restores the moving world for its own
        // capture and the next ordinary shot freezes again.
        if (_hairGlb is null && _auditFreeze)
        {
            bool wantFrozen = !shot.Motion && shot.Frames == 0;
            if (wantFrozen && !_auditFrozen)
            {
                Engine.TimeScale = 0.0;
                _auditFrozen = true;
                GD.Print("UNNAMED audit: the world is frozen for the captures (Engine.TimeScale = 0) - the identical-image "
                         + "control can now read zero and a comparison is admissible. --audit-no-freeze restores motion");
            }
            else if (!wantFrozen && _auditFrozen)
            {
                Engine.TimeScale = 1.0;
                _auditFrozen = false;
                GD.Print("UNNAMED audit: this shot is a MOTION shot, so the world is running for it and will be frozen again after");
            }
        }
        _camera.Current = true;
        _camera.LookAtFromPosition(eye, target, Vector3.Up);
        if (_wait == 0)
        {
            // A capture that must stand inside a space (the Barrowworks ring) asks for the portal once, and then waits
            // longer than the settle so the interior has been built and lit before the picture is taken.
            if (_enter is { Length: > 0 } portal && _entered == false)
            {
                _entered = true;
                _session.Submit(new UNNAMED.World.Runtime.EnterSpaceCommand(_session.Simulation!.PlayerId, portal));
                GD.Print($"UNNAMED audit: stepped through {portal} before {shot.Name}");
                _wait = -240;
                return;
            }
            if (SkyFor.TryGetValue(shot.Name, out var sky))
            {
                if (sky.Hour is { } hour)
                    VisualOptions.Pin("hour", hour);
                if (sky.Look is { } look)
                    VisualOptions.Pin("celestial", look);
            }
            var scatter = _root.GetNodeOrNull<Greybox.ScatterView>("Scatter");
            if (_scatterAb) scatter?.SetResidencyModeForCapture(ScatterArms[_scatterStep] is "B" or "B2");
            scatter?.PrepareResidencyForCapture(eye);
        }
        if (++_wait < SettleFrames + _settleExtra)
            return;
        var viewportRid = _root.GetViewport().GetViewportRid();
        if (_wait == SettleFrames + _settleExtra)
        {
            RenderingServer.ViewportSetMeasureRenderTime(viewportRid, true);
            // A motion shot keeps the first frame too, so what moves on its own (the wind) shows against the last.
            // A headless run has no rendering device and the viewport hands back no image at all: the picture is
            // skipped and the dump is still written. Null only headless, so a windowed run is unchanged.
            if (shot.Motion)
                _root.GetViewport().GetTexture().GetImage()?.SavePng(Path.Combine(_raw, shot.Name + "_first.png"));
            _gpu.Clear();
            _cpu.Clear();
            _wall.Clear();
            _last = Time.GetTicksUsec();
            return;
        }
        ulong now = Time.GetTicksUsec();
        _wall.Add((now - _last) / 1000.0);
        _last = now;
        _gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewportRid));
        _cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewportRid));
        if (shot.Frames > 0 && _wait - SettleFrames <= shot.Frames)
        {
            string sequence = Path.Combine(_raw, shot.Name + "_seq");
            System.IO.Directory.CreateDirectory(sequence);
            // DDS, not PNG: uncompressed, so a frame is written in milliseconds rather than half a second, and lossless.
            _root.GetViewport().GetTexture().GetImage()?.SaveDds(Path.Combine(sequence, $"f{_wait - SettleFrames - 1:000}.dds"));
        }
        if (_wait < SettleFrames + Math.Max(MeasureFrames, shot.Frames))
            return;
        _wait = 0;
        if (_hairGlb is not null)
        {
            // One shot, five captures, one session: A / A2 (nothing swapped between them - the identical-image
            // control), B, then A3 (reached by swapping BACK - the swap's own control) and B2.
            EnsureHair();
            Capture(shot, eye, target, "__" + HairArms[_hairStep]);
            _hairStep++;
            if (_hairStep < HairArms.Length)
            {
                SetHair(HairArms[_hairStep] is "B" or "B2");
                return;
            }
            _hairStep = 0;
            SetHair(staged: false);
            _index++;
            return;
        }
        Capture(shot, eye, target, _scatterAb ? "__" + ScatterArms[_scatterStep] : "");
        if (_scatterAb && ++_scatterStep < ScatterArms.Length) return;
        _scatterStep = 0;
        _index++;
    }

    private readonly List<Node3D> _hidden = new();
    private string? _hiddenFor;

    /// <summary>Show what the last shot hid, and hide this shot's <see cref="Shot.Hide"/> nodes.</summary>
    private void HideFor(Shot shot)
    {
        foreach (var node in _hidden)
            node.Visible = true;
        _hidden.Clear();
        foreach (string name in shot.Hide ?? Array.Empty<string>())
        {
            var node = _root.FindChild(name, true, false) as Node3D
                ?? throw new InvalidOperationException($"{shot.Name}: no Node3D called {name} to hide");
            node.Visible = false;
            _hidden.Add(node);
        }
        _hiddenFor = shot.Name;
    }

    /// <summary>One frame's picture and its dump, named with <paramref name="suffix"/> when a shot has several.</summary>
    private void Capture(Shot shot, Vector3 eye, Vector3 target, string suffix)
    {
        // Null under --headless (there is no rendering device): the picture is skipped and the numbers-only dump
        // written, which is what a clock march wants - it reads the environment, not pixels. Never null in a window.
        var image = _root.GetViewport().GetTexture().GetImage();
        var viewport = _root.GetViewport();
        var window = _root.GetTree().Root;
        var clientSize = DisplayServer.WindowGetSize();
        var screen = DisplayServer.WindowGetCurrentScreen();
        var screenSize = DisplayServer.ScreenGetSize(screen);
        var viewportTexture = viewport.GetTexture();
        image?.SavePng(Path.Combine(_raw, shot.Name + suffix + ".png"));
        var dump = new Dictionary<string, object?>
        {
            ["shot"] = shot.Name,
            ["sample"] = shot.Sample,
            ["says"] = shot.Says,
            ["image_size"] = new[] { image?.GetWidth() ?? 0, image?.GetHeight() ?? 0 },
            ["display_sizes_px"] = new Dictionary<string, object?>
            {
                ["window_client"] = new[] { clientSize.X, clientSize.Y },
                ["root_window"] = new[] { window.Size.X, window.Size.Y },
                ["screen_id"] = screen,
                ["screen"] = new[] { screenSize.X, screenSize.Y },
                ["viewport_texture"] = new[] { viewportTexture.GetWidth(), viewportTexture.GetHeight() },
            },
            ["camera"] = CameraDump(_camera),
            ["target_point"] = V(target),
            ["distance_to_target_point_m"] = Math.Round(eye.DistanceTo(target), 2),
            ["targets"] = shot.Targets.Select(t => TargetDump(t, _camera.GlobalPosition)).ToList(),
            ["renderer"] = RendererDump(_root.GetViewport(), image),
            ["measured"] = new Dictionary<string, object?>
            {
                ["frames"] = _wall.Count,
                ["gpu_ms_median"] = Math.Round(Median(_gpu), 3),
                ["cpu_ms_median"] = Math.Round(Median(_cpu), 3),
                ["frame_ms_median"] = Math.Round(Median(_wall), 3),
                ["frame_ms_p95"] = Math.Round(Percentile(_wall, 0.95), 3),
                ["vram_mb"] = Math.Round(Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0, 1),
                ["draw_calls"] = Math.Round(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame), 0),
                ["primitives"] = Math.Round(Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame), 0),
                // The main thread's own share of a frame (scripts and the simulation), beside the renderer's cpu/gpu above.
                ["process_ms"] = Math.Round(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000, 3),
                ["physics_process_ms"] = Math.Round(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000, 3),
                ["objects"] = Math.Round(Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame), 0),
            },
            ["visual_options"] = VisualOptions.All.ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        if (_root.GetNodeOrNull<Greybox.ScatterView>("Scatter") is { ResidencyEnabled: true } scatter)
            dump["scatter_residency"] = new
            {
                enabled = true, ready = scatter.ResidencyReady, nodes = scatter.GetChildCount(),
                worst_chunk_ms = scatter.WorstChunkMs, resident_counts = scatter.Counts,
            };
        if (_scatterAb) dump["scatter_ab_arm"] = ScatterArms[_scatterStep];
        if (_hairGlb is not null)
        {
            // WHICH ARM WAS ACTUALLY ON THE FIGURE, read off the live resource rather than assumed from the
            // schedule. A sibling round's "counterbalanced" pair silently used the identical arrangement twice
            // because the tool permuted labels independently of argument order; the only defence is to record
            // the state that was true when the shutter opened.
            string now = _hairInstance?.Mesh is { } m && ReferenceEquals(m, _hairStaged) ? _hairFrom
                : _hairInstance?.Mesh is { } o && ReferenceEquals(o, _hairOriginal) ? _hairTo : "unknown";
            dump["hair_arm"] = new Dictionary<string, object?>
            {
                ["suffix"] = suffix,
                ["material_on_figure"] = now,
                ["staged_glb"] = _hairGlb,
                ["hair_triangles"] = _hairInstance?.Mesh is { } mesh ? Triangles(mesh) : 0,
                ["hair_surfaces"] = _hairInstance?.Mesh?.GetSurfaceCount() ?? 0,
                ["skin_resource_shared"] = true,
            };
            _hairLog.Add(new Dictionary<string, object?>
            {
                ["png"] = shot.Name + suffix + ".png", ["arm"] = suffix.TrimStart('_'), ["material_on_figure"] = now,
                ["hair_triangles"] = _hairInstance?.Mesh is { } mm ? Triangles(mm) : 0,
                ["frame_ms_median"] = Math.Round(Median(_wall), 3),
            });
            GD.Print($"UNNAMED hair A/B {shot.Name}{suffix}: figure is wearing {now}, "
                     + $"{(_hairInstance?.Mesh is { } m2 ? Triangles(m2) : 0)} hair triangles, frame {Median(_wall):0.00} ms");
        }
        Write(shot.Name + suffix + ".json", dump);
        _log.Add(new Dictionary<string, object?> { ["png"] = shot.Name + suffix + ".png", ["says"] = shot.Says, ["eye"] = V(eye), ["target"] = V(target),
            ["gpu_ms_median"] = Math.Round(Median(_gpu), 3), ["frame_ms_median"] = Math.Round(Median(_wall), 3) });
        GD.Print($"UNNAMED visual audit {shot.Name}{suffix}: gpu {Median(_gpu):0.00} ms, cpu {Median(_cpu):0.00} ms, frame {Median(_wall):0.00} ms");
    }

    /// <summary>The live hair surface and the two meshes it is exchanged between, found once from the scene.</summary>
    private void EnsureHair()
    {
        if (_hairInstance is not null)
            return;
        _hairInstance = FindByMaterial(_root, _hairTo)
            ?? throw new InvalidOperationException($"no surface under the root carries the material {_hairTo}");
        _hairOriginal = _hairInstance.Mesh;
        _hairStaged = LoadStagedHair();
        GD.Print($"UNNAMED hair A/B: arm A {_hairTo} {Triangles(_hairOriginal!)} triangles -> "
                 + $"arm B {_hairFrom} {Triangles(_hairStaged!)} triangles, on {_hairInstance.GetPath()}");
    }

    /// <summary>Put one of the two meshes on the live surface. The Skin and the Skeleton are never touched.</summary>
    private void SetHair(bool staged)
    {
        if (_hairInstance is null)
            throw new InvalidOperationException("SetHair before EnsureHair");
        var want = staged ? _hairStaged! : _hairOriginal!;
        if (!ReferenceEquals(_hairInstance.Mesh, want))
            _hairInstance.Mesh = want;
    }

    /// <summary>The staged GLB's groom mesh, put through the same material pass the live figure got.</summary>
    private Mesh LoadStagedHair()
    {
        var document = new GltfDocument();
        var state = new GltfState();
        Error error = document.AppendFromFile(_hairGlb!, state);
        if (error != Error.Ok)
            throw new InvalidOperationException($"the staged hair GLB would not load: {error}");
        Node? scene = document.GenerateScene(state);
        if (scene is null)
            throw new InvalidOperationException("the staged hair GLB generated no scene");
        // The live figure's materials went through CharacterMaterials.Upgrade when the figure was created; the
        // staged mesh must get the same pass or arm B would be judged with the importer's defaults instead of
        // the `hair` kind. The material is named `Human.elvs_hazel_hair`, so the `_hair` suffix reaches it.
        int upgraded = Art.CharacterMaterials.Upgrade(scene);
        var mesh = FindByMaterial(scene, _hairFrom)?.Mesh;
        GD.Print($"UNNAMED hair A/B: staged scene gave {upgraded} upgraded surface(s); groom surface {_hairFrom} "
                 + (mesh is null ? "NOT FOUND" : $"found, {Triangles(mesh)} triangles"));
        scene.Free();
        return mesh ?? throw new InvalidOperationException($"the staged GLB has no surface with material {_hairFrom}");
    }

    private static MeshInstance3D? FindByMaterial(Node node, string materialName)
    {
        if (node is MeshInstance3D instance && instance.Mesh is not null)
            for (int s = 0; s < instance.Mesh.GetSurfaceCount(); s++)
            {
                var material = instance.Mesh.SurfaceGetMaterial(s);
                if (material is not null && material.ResourceName == materialName)
                    return instance;
            }
        foreach (var child in node.GetChildren())
            if (FindByMaterial(child, materialName) is { } hit)
                return hit;
        return null;
    }

    /// <summary>
    /// The shots in a <c>--audit-shots</c> file: <b>either a list of shots, or a single shot written as a bare
    /// object</b> - and every failure names the file and the shot it is about.
    ///
    /// WHY BOTH SHAPES ARE TAKEN. This read <c>document.RootElement.EnumerateArray()</c> unconditionally, so a
    /// one-shot file written as a bare <c>{ … }</c> threw
    /// <c>InvalidOperationException: The requested operation requires an element of type 'Array', but the target
    /// element has type 'Object'</c> - <b>a message that names neither the file nor the fault</b>, shown to whoever
    /// launched the run as *"Otherreach cannot start"*. Measured: of the eight per-station files a sibling round
    /// had staged, <c>ab_03</c>, <c>ab_04</c>, <c>ab_06</c>, <c>ab_07</c> and <c>ab_08</c> were bare objects while
    /// <c>ab_02</c>, <c>ab_09</c> and <c>ab_10</c> were lists - so <b>every other capture in its loop died before
    /// the first frame and the loop looked like it was failing at random</b>. *A one-shot file is the natural thing
    /// for a person or a script to write, so the reader takes it.*
    /// </summary>
    private static IEnumerable<Shot> LoadShots(string file)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(file));
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            throw new InvalidOperationException($"the shots file {file} could not be read: {e.Message}");
        }

        using (document)
        {
            var entries = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().ToList(),
                JsonValueKind.Object => new List<JsonElement> { document.RootElement },
                _ => throw new InvalidOperationException(
                    $"the shots file {file} must hold a list of shots, or one shot as an object; its root is {document.RootElement.ValueKind}"),
            };
            if (entries.Count == 0)
                throw new InvalidOperationException($"the shots file {file} holds no shots");

            var shots = new List<Shot>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"the shots file {file} holds a {entry.ValueKind} where a shot object was expected");
                string name = entry.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String
                    ? nm.GetString()!
                    : $"<shot {shots.Count + 1} of {entries.Count}>";

                static (float, float, float) Triple(JsonElement parent, string key, string shot, string source)
                {
                    if (!parent.TryGetProperty(key, out var e))
                        throw new InvalidOperationException($"shot '{shot}' in {source} has no \"{key}\"");
                    if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3)
                        throw new InvalidOperationException($"shot '{shot}' in {source} needs \"{key}\" to be three numbers [x, y, z]");
                    return (e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
                }

                (string, float, float)? near = null;
                if (entry.TryGetProperty("near", out var n))
                    near = (n[0].GetString()!, n[1].GetSingle(), n[2].GetSingle());
                var targets = entry.TryGetProperty("targets", out var t) && t.ValueKind == JsonValueKind.Array
                    ? t.EnumerateArray().Select(x => x.GetString()!).ToArray()
                    : Array.Empty<string>();
                // A shot may set the sky's hour and look for itself (SkyFor), so one run photographs a place at every hour and sky.
                SkyFor[name] = (entry.TryGetProperty("hour", out var hr) ? hr.ToString() : null,
                    entry.TryGetProperty("celestial", out var cl) ? cl.GetString() : null);
                shots.Add(new Shot(name, "checkpoint", Triple(entry, "eye", name, file), Triple(entry, "target", name, file),
                    targets,
                    entry.TryGetProperty("says", out var says) ? says.GetString()! : string.Empty,
                    near, entry.TryGetProperty("motion", out var m) && m.ValueKind == JsonValueKind.True,
                    entry.TryGetProperty("frames", out var f) ? f.GetInt32() : 0,
                    entry.TryGetProperty("move", out _) ? Triple(entry, "move", name, file) : null,
                    entry.TryGetProperty("hide", out var hide) ? hide.EnumerateArray().Select(x => x.GetString()!).ToArray() : null));
            }
            return shots;
        }
    }

    private static double Median(List<double> values) => Percentile(values, 0.5);

    private static double Percentile(List<double> values, double q)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
    }

    private Vector3 Ground((float X, float H, float Z) at)
    {
        var terrain = _session.Setup.Layout.Space.Terrain;
        float ground = terrain.HeightAtMm((long)(at.X * 1000), (long)(at.Z * 1000)) / 1000f;
        return new Vector3(at.X, ground + at.H, at.Z);
    }

    /// <summary>A target by its node name, searched under the hollow, the items, the NPCs and the scene root.</summary>
    private Node3D? FindTarget(string name)
    {
        string alt = name.Replace('.', '_');
        foreach (var parent in new[] { "Hollow", "Items", "Npcs", "Creatures", "Crafting" }.Select(p => _root.GetNodeOrNull(p)).Append(_root))
        {
            if (parent is null)
                continue;
            foreach (var child in parent.GetChildren())
            {
                if (child is Node3D node && (child.Name == name || child.Name == alt))
                    return node;
            }
        }
        return null;
    }

    private Dictionary<string, object?> TargetDump(string name, Vector3 cameraAt)
    {
        var node = FindTarget(name);
        var result = new Dictionary<string, object?> { ["target"] = name, ["found"] = node is not null };
        string? bound = name switch
        {
            "longhouse" => _bindings.Buildings.GetValueOrDefault("longhouse_")?.Model,
            "forge" => _bindings.Buildings.GetValueOrDefault("forge_")?.Model,
            "door.longhouse_door" => _bindings.Doors.GetValueOrDefault("door.longhouse")?.Model,
            _ when name.StartsWith("container.", StringComparison.Ordinal) => _bindings.Containers.GetValueOrDefault(name)?.Model,
            _ when name.StartsWith("npc.", StringComparison.Ordinal) => _bindings.People.GetValueOrDefault(name)?.Model,
            _ => _bindings.Structure(name)?.Model,
        };
        result["bound_model"] = bound;
        result["art_problems"] = _art.Problems.Where(p => (bound is not null && p.Key.StartsWith(bound, StringComparison.Ordinal)) || p.Key.EndsWith(" " + name, StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value);
        if (node is null)
            return result;
        result["node_path"] = node.GetPath().ToString();
        result["node_class"] = node.GetClass();
        result["global_position"] = V(node.GlobalPosition);
        result["global_scale"] = V(node.GlobalTransform.Basis.Scale);
        var meshes = MeshesUnder(node, cameraAt);
        result["meshes"] = meshes;
        result["greybox_fallback"] = meshes.Count > 0 && meshes.Where(m => (bool)m["drawn"]!).All(m => (bool)m["greybox"]!);
        result["drawn_triangles"] = meshes.Where(m => (bool)m["drawn"]!).Sum(m => (int)m["triangles"]!);
        result["drawn_lod_levels"] = meshes.Where(m => (bool)m["drawn"]!).Select(m => m["lod_level"]).Distinct().ToList();
        return result;
    }

    /// <summary>Every mesh under a node: whether it is drawn at this camera distance, which level of its chain it is, and its surfaces.</summary>
    private List<Dictionary<string, object?>> MeshesUnder(Node3D node, Vector3 cameraAt)
    {
        var all = (node is MeshInstance3D self ? new[] { self } : Array.Empty<MeshInstance3D>())
            .Concat(node.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>()).ToList();
        var begins = all.Select(m => m.VisibilityRangeBegin).Distinct().OrderBy(b => b).ToList();
        var list = new List<Dictionary<string, object?>>();
        foreach (var mesh in all)
        {
            var aabb = mesh.GlobalTransform * mesh.GetAabb();
            float distance = cameraAt.DistanceTo(aabb.GetCenter());
            bool inRange = (mesh.VisibilityRangeBegin <= 0 || distance >= mesh.VisibilityRangeBegin)
                && (mesh.VisibilityRangeEnd <= 0 || distance < mesh.VisibilityRangeEnd);
            bool drawn = mesh.Mesh is not null && mesh.IsVisibleInTree() && inRange && mesh.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            var entry = new Dictionary<string, object?>
            {
                ["path"] = node.GetPathTo(mesh).ToString(),
                ["mesh_class"] = mesh.Mesh?.GetClass(),
                ["mesh_name"] = mesh.Mesh?.ResourceName,
                ["drawn"] = drawn,
                ["visible_in_tree"] = mesh.IsVisibleInTree(),
                ["cast_shadow"] = mesh.CastShadow.ToString(),
                ["distance_to_aabb_centre_m"] = Math.Round(distance, 2),
                ["visibility_range"] = new[] { mesh.VisibilityRangeBegin, mesh.VisibilityRangeEnd },
                ["visibility_range_margins"] = new[] { mesh.VisibilityRangeBeginMargin, mesh.VisibilityRangeEndMargin },
                ["lod_level"] = begins.Count > 1 ? begins.IndexOf(mesh.VisibilityRangeBegin) : 0,
                ["greybox"] = mesh.Mesh is PrimitiveMesh,
                ["global_scale"] = V(mesh.GlobalTransform.Basis.Scale),
                ["aabb_size_m"] = V(aabb.Size),
                ["triangles"] = mesh.Mesh is { } m ? Triangles(m) : 0,
                ["material_override"] = mesh.MaterialOverride?.GetClass(),
                ["surfaces"] = mesh.Mesh is null ? new List<Dictionary<string, object?>>()
                    : Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(s => SurfaceDump(mesh, s)).ToList(),
            };
            if (mesh is { Skeleton: { IsEmpty: false } skeleton })
                entry["skeleton"] = skeleton.ToString();
            list.Add(entry);
        }
        return list;
    }

    private static int Triangles(Mesh mesh)
    {
        int total = 0;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            total += SurfaceTriangles(mesh, s);
        return total;
    }

    private static int SurfaceTriangles(Mesh mesh, int s)
    {
        if (mesh is ArrayMesh array)
        {
            int indices = array.SurfaceGetArrayIndexLen(s);
            return (indices > 0 ? indices : array.SurfaceGetArrayLen(s)) / 3;
        }
        var arrays = mesh.SurfaceGetArrays(s);
        var index = arrays[(int)Mesh.ArrayType.Index];
        if (index.VariantType != Variant.Type.Nil && index.AsInt32Array().Length > 0)
            return index.AsInt32Array().Length / 3;
        return arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length / 3;
    }

    private Dictionary<string, object?> SurfaceDump(MeshInstance3D mesh, int s)
    {
        var material = mesh.GetActiveMaterial(s);
        var entry = new Dictionary<string, object?>
        {
            ["surface"] = s,
            ["triangles"] = SurfaceTriangles(mesh.Mesh!, s),
            ["material_class"] = material?.GetClass(),
            ["material_name"] = material?.ResourceName,
        };
        if (material is BaseMaterial3D b)
        {
            entry["shading_mode"] = b.ShadingMode.ToString();
            entry["albedo_color"] = b.AlbedoColor.ToHtml();
            entry["albedo_texture"] = TextureDump(b.AlbedoTexture);
            entry["texture_filter"] = b.TextureFilter.ToString();
            entry["texture_repeat"] = b.TextureRepeat;
            entry["normal_enabled"] = b.NormalEnabled;
            entry["normal_scale"] = b.NormalScale;
            entry["normal_texture"] = TextureDump(b.NormalTexture);
            entry["roughness"] = b.Roughness;
            entry["roughness_texture"] = TextureDump(b.RoughnessTexture);
            // WHICH CHANNEL THE ROUGHNESS COMES FROM, because this stretch shipped a fix for it and then could not
            // prove it at runtime. `ArtLibrary` sets RoughnessTextureChannel = Green on every imported material -
            // Godot defaults it to RED while glTF packs roughness in GREEN, and 1,916 of 1,980 shipped materials are
            // true ORM with the SAME image in the occlusion and metallicRoughness slots, so the default made every
            // imported asset read its occlusion map as roughness. The fix was verified COMPILED by the assembly's
            // timestamp and its code path verified RUNNING by the occlusion counter beside it, but nothing printed
            // the channel itself. One line closes that, and a value in a log is evidence where an inference is not.
            entry["roughness_texture_channel"] = b.RoughnessTextureChannel.ToString();
            entry["metallic"] = b.Metallic;
            entry["metallic_specular"] = b.MetallicSpecular;
            entry["metallic_texture"] = TextureDump(b.MetallicTexture);
            // EMISSION, FOR THE SAME REASON THE CHANNEL ABOVE IS PRINTED. The creature round measured
            // `emissiveFactor: absent` and `emissiveTexture: absent` on all six bodies and then added a map to the
            // hound, but nothing could say whether the ENGINE had it: a glTF emissive that the importer drops, or
            // that a later pass clears, looks identical in the file and in the frame. `emission_enabled` plus a
            // non-null `emission_texture` is the runtime answer, and it is the difference between "we set it in the
            // GLB" and "the game is drawing it".
            entry["emission_enabled"] = b.EmissionEnabled;
            entry["emission"] = b.Emission.ToHtml();
            entry["emission_energy_multiplier"] = b.EmissionEnergyMultiplier;
            entry["emission_texture"] = TextureDump(b.EmissionTexture);
            entry["ao_enabled"] = b.Get("ao_enabled").AsBool();
            entry["ao_texture"] = TextureDump(b.Get("ao_texture").As<Texture2D>());
            if (material is OrmMaterial3D orm)
                entry["orm_texture"] = TextureDump(orm.OrmTexture);
            entry["transparency"] = b.Transparency.ToString();
            entry["alpha_scissor_threshold"] = b.AlphaScissorThreshold;
            entry["alpha_antialiasing_mode"] = b.Get("alpha_antialiasing_mode").AsInt32();
            entry["cull_mode"] = b.CullMode.ToString();
            entry["uv1_scale"] = V(b.Uv1Scale);
            entry["uv1_triplanar"] = b.Uv1Triplanar;
            entry["uv1_world_triplanar"] = b.Uv1WorldTriplanar;
            entry["vertex_color_use_as_albedo"] = b.VertexColorUseAsAlbedo;
            entry["specular_mode"] = b.SpecularMode.ToString();
            entry["diffuse_mode"] = b.DiffuseMode.ToString();
            entry["heightmap_enabled"] = b.Get("heightmap_enabled").AsBool();
            entry["detail_enabled"] = b.Get("detail_enabled").AsBool();
        }
        else if (material is ShaderMaterial shader)
        {
            entry["shader"] = shader.Shader?.ResourcePath;
        }
        return entry;
    }

    private Dictionary<string, object?>? TextureDump(Texture2D? texture)
    {
        if (texture is null)
            return null;
        if (_textureCache.TryGetValue(texture.GetInstanceId(), out var cached))
            return cached;
        var image = texture.GetImage();
        var entry = new Dictionary<string, object?>
        {
            ["class"] = texture.GetClass(),
            ["size"] = new[] { texture.GetWidth(), texture.GetHeight() },
            ["format"] = image?.GetFormat().ToString(),
            ["has_mipmaps"] = image?.HasMipmaps(),
            ["mipmap_count"] = image?.GetMipmapCount(),
            ["resource_path"] = texture.ResourcePath,
            ["resource_name"] = texture.ResourceName,
        };
        _textureCache[texture.GetInstanceId()] = entry;
        return entry;
    }

    /// <summary>The texture dump run on two textures of known make, one with mipmaps and one without: the measurement is read the same way.</summary>
    private Dictionary<string, object?> MeasurementControl()
    {
        var with = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        with.GenerateMipmaps();
        var without = Image.CreateEmpty(64, 64, false, Image.Format.Rgb8);
        return new Dictionary<string, object?>
        {
            ["image_texture_made_with_mipmaps"] = TextureDump(ImageTexture.CreateFromImage(with)),
            ["image_texture_made_without_mipmaps"] = TextureDump(ImageTexture.CreateFromImage(without)),
        };
    }

    private static Dictionary<string, object?> CameraDump(Camera3D camera) => new()
    {
        ["position"] = V(camera.GlobalPosition),
        ["fov_deg"] = camera.Fov,
        ["keep_aspect"] = camera.KeepAspect.ToString(),
        ["near"] = camera.Near,
        ["far"] = camera.Far,
        ["attributes"] = camera.Attributes?.GetClass(),
        ["environment"] = camera.Environment?.GetClass(),
    };

    /// <summary>The renderer in effect: the project's rendering settings, the viewport's, the world environment and the sun.</summary>
    private Dictionary<string, object?> RendererDump(Viewport viewport, Image? capturedImage)
    {
        var settings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in ProjectSettings.Singleton.GetPropertyList())
        {
            string name = property["name"].AsString();
            if (name.StartsWith("rendering/", StringComparison.Ordinal))
                settings[name] = Str(ProjectSettings.GetSetting(name));
        }
        string project = Godot.FileAccess.GetFileAsString("res://project.godot");
        var environment = _root.FindChildren("*", nameof(WorldEnvironment), true, false).Cast<WorldEnvironment>().FirstOrDefault()?.Environment;
        var sun = _root.FindChildren("*", nameof(DirectionalLight3D), true, false).Cast<DirectionalLight3D>().ToList();
        var lights = _root.FindChildren("*", nameof(Light3D), true, false).Cast<Light3D>().ToList();
        return new Dictionary<string, object?>
        {
            ["rendering_method"] = RenderingServer.GetCurrentRenderingMethod(),
            ["rendering_driver"] = RenderingServer.GetCurrentRenderingDriverName(),
            ["adapter"] = RenderingServer.GetVideoAdapterName(),
            ["project_godot_has_rendering_section"] = project.Contains("[rendering]", StringComparison.Ordinal),
            ["viewport"] = new Dictionary<string, object?>
            {
                ["size"] = V2(viewport.GetVisibleRect().Size),
                ["size_space"] = "logical visible rect; canvas_items may use a larger native render target",
                ["render_target_size"] = new[] { capturedImage?.GetWidth() ?? 0, capturedImage?.GetHeight() ?? 0 },
                ["render_target_size_source"] = "unresized renderer texture readback, also saved as PNG; zero in headless mode",
                ["viewport_texture_reported_size"] = new[] { viewport.GetTexture().GetWidth(), viewport.GetTexture().GetHeight() },
                ["msaa_3d"] = viewport.Msaa3D.ToString(),
                ["screen_space_aa"] = viewport.ScreenSpaceAA.ToString(),
                ["use_taa"] = viewport.UseTaa,
                ["use_debanding"] = viewport.UseDebanding,
                ["scaling_3d_mode"] = viewport.Scaling3DMode.ToString(),
                ["scaling_3d_scale"] = viewport.Scaling3DScale,
                ["texture_mipmap_bias"] = viewport.TextureMipmapBias,
                ["anisotropic_filtering_level"] = Str(viewport.Get("anisotropic_filtering_level")),
                ["mesh_lod_threshold"] = viewport.MeshLodThreshold,
                ["use_occlusion_culling"] = viewport.UseOcclusionCulling,
                ["positional_shadow_atlas_size"] = viewport.PositionalShadowAtlasSize,
            },
            ["environment"] = environment is null ? null : new Dictionary<string, object?>
            {
                ["summary"] = new Dictionary<string, object?>
                {
                    ["background_mode"] = environment.BackgroundMode.ToString(),
                    ["sky_material"] = environment.Sky?.SkyMaterial?.GetClass(),
                    ["ambient_light_source"] = environment.AmbientLightSource.ToString(),
                    ["ambient_light_energy"] = environment.AmbientLightEnergy,
                    ["ambient_light_sky_contribution"] = environment.AmbientLightSkyContribution,
                    ["reflected_light_source"] = environment.ReflectedLightSource.ToString(),
                    ["tonemap_mode"] = environment.TonemapMode.ToString(),
                    ["tonemap_exposure"] = environment.TonemapExposure,
                    ["tonemap_white"] = environment.TonemapWhite,
                    ["ssao_enabled"] = environment.SsaoEnabled,
                    ["ssil_enabled"] = environment.SsilEnabled,
                    ["sdfgi_enabled"] = environment.SdfgiEnabled,
                    ["glow_enabled"] = environment.GlowEnabled,
                    ["fog_enabled"] = environment.FogEnabled,
                    ["fog_density"] = environment.FogDensity,
                    ["volumetric_fog_enabled"] = environment.VolumetricFogEnabled,
                    ["adjustment_enabled"] = environment.AdjustmentEnabled,
                    ["voxel_gi_nodes"] = _root.FindChildren("*", nameof(VoxelGI), true, false).Count,
                    ["lightmap_gi_nodes"] = _root.FindChildren("*", nameof(LightmapGI), true, false).Count,
                    ["reflection_probes"] = _root.FindChildren("*", nameof(ReflectionProbe), true, false).Count,
                },
                ["all_properties"] = Props(environment),
                ["sky_material_properties"] = environment.Sky?.SkyMaterial is { } sky ? Props(sky) : null,
            },
            ["lights"] = lights.Select(l => new Dictionary<string, object?>
            {
                ["path"] = l.GetPath().ToString(),
                ["class"] = l.GetClass(),
                ["energy"] = l.LightEnergy,
                ["shadow_enabled"] = l.ShadowEnabled,
                ["rotation_deg"] = V(l.GlobalRotationDegrees),
                ["directional_shadow_mode"] = l is DirectionalLight3D d ? d.DirectionalShadowMode.ToString() : null,
                ["all_properties"] = Props(l),
            }).ToList(),
            ["directional_lights"] = sun.Count,
            ["project_rendering_settings"] = settings,
        };
    }

    // ── alone on the stage ──────────────────────────────────────────────────

    private void Isolated(Sample sample)
    {
        if (_stage is null)
            BuildStage();
        if (_sample is null)
        {
            if (sample.Load() is not { } model)
            {
                Write($"iso_{sample.N}_{sample.Id}.json", new Dictionary<string, object?> { ["sample"] = sample.Id, ["loaded"] = false, ["art_problems"] = _art.Problems });
                _index++;
                return;
            }
            _sample = new Node3D { Name = sample.Id };
            _sample.AddChild(model);
            _stage!.AddChild(_sample);
            _figure = model as Figure;
            Pose();
            var bounds = Art.ArtGallery.Bounds(_sample);
            // Stood on the floor, centred.
            model.Position = new Vector3(-bounds.GetCenter().X, -bounds.Position.Y, -bounds.GetCenter().Z);
            bounds = Art.ArtGallery.Bounds(_sample);
            float radius = bounds.Size.Length() / 2;
            float az = Mathf.DegToRad(IsoAzimuth), el = Mathf.DegToRad(IsoElevation);
            var direction = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            var centre = bounds.GetCenter();
            float distance = FitDistance(bounds, direction);
            _stageCamera!.Far = distance * 6;
            _stageCamera.Near = Math.Max(0.02f, distance / 500);
            _stageCamera.LookAtFromPosition(centre + direction * distance, centre, Vector3.Up);
            _floor!.Scale = Vector3.One * Math.Max(40f, radius * 60);
            return;
        }
        Pose();
        if (++_wait < SettleFrames + _settleExtra)
            return;
        _wait = 0;
        _index++;
        var image = _stage!.GetTexture().GetImage();
        image.SavePng(Path.Combine(_raw, $"iso_{sample.N}_{sample.Id}.png"));
        var boundsNow = Art.ArtGallery.Bounds(_sample);
        var meshes = MeshesUnder(_sample, _stageCamera!.GlobalPosition);
        Write($"iso_{sample.N}_{sample.Id}.json", new Dictionary<string, object?>
        {
            ["sample"] = sample.Id,
            ["says"] = sample.Says,
            ["loaded"] = true,
            ["image_size"] = new[] { image.GetWidth(), image.GetHeight() },
            ["stage"] = new Dictionary<string, object?>
            {
                ["camera_fov_deg"] = IsoFov, ["azimuth_deg"] = IsoAzimuth, ["elevation_deg"] = IsoElevation,
                ["camera"] = CameraDump(_stageCamera), ["bounds_size_m"] = V(boundsNow.Size), ["bounds_centre"] = V(boundsNow.GetCenter()),
            },
            ["meshes"] = meshes,
            ["drawn_triangles"] = meshes.Where(m => (bool)m["drawn"]!).Sum(m => (int)m["triangles"]!),
            ["all_triangles_by_lod"] = meshes.GroupBy(m => m["lod_level"]).ToDictionary(g => $"lod{g.Key}", g => g.Sum(m => (int)m["triangles"]!)),
            ["renderer_viewport"] = new Dictionary<string, object?>
            {
                ["msaa_3d"] = _stage.Msaa3D.ToString(), ["screen_space_aa"] = _stage.ScreenSpaceAA.ToString(), ["use_taa"] = _stage.UseTaa,
                ["scaling_3d_mode"] = _stage.Scaling3DMode.ToString(), ["scaling_3d_scale"] = _stage.Scaling3DScale,
            },
            ["art_problems"] = _art.Problems.Where(p => p.Key.StartsWith(sample.Id, StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value),
        });
        _log.Add(new Dictionary<string, object?> { ["png"] = $"iso_{sample.N}_{sample.Id}.png", ["says"] = sample.Says });
        _sample.QueueFree();
        _sample = null;
        _figure = null;
    }

    /// <summary>
    /// How far along a direction the camera stands so every corner of the bounds is inside the square view, with 8 % to spare (the neutral
    /// Blender render uses the same rule).
    /// </summary>
    private static float FitDistance(Aabb bounds, Vector3 direction)
    {
        var right = Vector3.Up.Cross(direction).Normalized();
        var up = direction.Cross(right).Normalized();
        float t = Mathf.Tan(Mathf.DegToRad(IsoFov / 2));
        float distance = 0;
        for (int i = 0; i < 8; i++)
        {
            var q = bounds.GetEndpoint(i) - bounds.GetCenter();
            distance = Math.Max(distance, q.Dot(direction) + Math.Max(Math.Abs(q.Dot(right)), Math.Abs(q.Dot(up))) / t);
        }
        return distance * 1.08f;
    }

    private void Pose()
    {
        if (_figure is null)
            return;
        _figure.SetStance(CombatStance.AtRest);
        _figure.SetTalking(false);
        _figure.Pose(_figure.Position, 0f, 0f, 1 / 60.0);
    }

    /// <summary>A neutral stage in a world of its own: a mid-grey floor, a key and a fill light, flat grey ambient, the game's viewport settings.</summary>
    private void BuildStage()
    {
        var main = _root.GetViewport();
        _stage = new SubViewport
        {
            Name = "AuditStage", Size = new Vector2I(1536, 1536), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = main.Msaa3D, ScreenSpaceAA = main.ScreenSpaceAA, UseTaa = main.UseTaa, Scaling3DMode = main.Scaling3DMode, Scaling3DScale = main.Scaling3DScale,
            TransparentBg = false,
        };
        _root.AddChild(_stage);
        _stage.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.5f, 0.5f, 0.5f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.5f, 0.5f, 0.5f), AmbientLightEnergy = 0.6f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Agx,
            },
        });
        // Key from the camera's left and above; fill from the other side, no shadow.
        _stage.AddChild(new DirectionalLight3D { Name = "Key", RotationDegrees = new Vector3(-50, -20, 0), LightEnergy = 1.6f, ShadowEnabled = true });
        _stage.AddChild(new DirectionalLight3D { Name = "Fill", RotationDegrees = new Vector3(-25, 150, 0), LightEnergy = 0.45f, ShadowEnabled = false });
        _floor = new MeshInstance3D
        {
            Name = "Floor", Mesh = new PlaneMesh { Size = new Vector2(1, 1) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.5f, 0.5f), Roughness = 1f },
        };
        _stage.AddChild(_floor);
        _stageCamera = new Camera3D { Name = "StageCamera", Fov = IsoFov, Current = true };
        _stage.AddChild(_stageCamera);
    }

    // ── writing ─────────────────────────────────────────────────────────────

    private void Write(string name, object value) => File.WriteAllText(Path.Combine(_raw, name), JsonSerializer.Serialize(value, Json));

    private static SortedDictionary<string, string> Props(GodotObject o)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in o.GetPropertyList())
        {
            long usage = property["usage"].AsInt64();
            string name = property["name"].AsString();
            if ((usage & (long)PropertyUsageFlags.Storage) == 0 || name is "script" or "resource_local_to_scene")
                continue;
            d[name] = Str(o.Get(name));
        }
        return d;
    }

    private static string Str(Variant v) => v.VariantType switch
    {
        Variant.Type.Nil => "null",
        Variant.Type.Object => v.AsGodotObject() is { } g ? g.GetClass() + (g is Resource { ResourcePath.Length: > 0 } r ? " " + r.ResourcePath : "") : "null",
        _ => v.Obj?.ToString() ?? "null",
    };

    private static float[] V(Vector3 v) => new[] { (float)Math.Round(v.X, 3), (float)Math.Round(v.Y, 3), (float)Math.Round(v.Z, 3) };

    private static float[] V2(Vector2 v) => new[] { v.X, v.Y };
}
