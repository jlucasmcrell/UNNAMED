// UNNAMED Presentation - a character-standard face driven from what its body is doing: blinks, the line on screen spoken as visemes
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The face channels of a character-standard body (CHARACTER_ASSET_STANDARD.md: ARKit-52 and the Meta visemes as morph targets, on
/// the skin, brows, lashes, teeth, tongue and hair alike): natural blinks, and while the character speaks a line, visemes timed from
/// recorded playback and voice envelope, or the line's text when no recording exists. A body without the channels gets no driver.
/// </summary>
public sealed partial class FaceDriver : Node
{
    private const float LettersPerSecond = 15f;

    private readonly List<(MeshInstance3D Mesh, Dictionary<string, int> Index)> _meshes = new();
    private readonly Dictionary<string, float> _weights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _targets = new(StringComparer.Ordinal);
    private readonly RandomNumberGenerator _random = new();
    private string? _line;
    private double _spoken = -1;
    private double _blinkIn;
    private double _blinking = -1;
    private string _seed = "";
    private float _peak;
    private double _peakAt = -1;
    private readonly Dictionary<string, float> _arkit = new(StringComparer.Ordinal);
    private double _since, _gazeIn, _expressionTime;
    private Vector2 _saccade;
    private Vector3? _gaze;
    private Skeleton3D? _nativeRig;
    private Node3D? _nativeBody, _eyeTarget;
    // The head's forward in its own rest frame: gaze is aimed from where the head points, not from the level body,
    // or a clip that tips the head down rolls the eyes up under the lids to hold a level target.
    private int _headBone = -1;
    private Vector3 _headAxis;
    public int NativeEyeBindings { get; private set; }
    public bool NativeJawBound { get; private set; }
    public void Gaze(Vector3? localDirection) => _gaze = localDirection;
    public float BlinkWeight => _weights.GetValueOrDefault("eyeBlinkLeft");
    public float JawWeight => _weights.GetValueOrDefault("jawOpen");
    public bool GentleRest { get; set; }
    /// <summary>Presentation mood supplied by a dialogue performance or creator expression preview.</summary>
    public string? Mood { get; set; }
    private float _restingLid = .17f;
    public Func<(double Position, double Duration, float Jaw)?>? RecordedSpeech { get; set; }
    private FacialPerformance? _performance;
    private double _performanceTime;
    public void PlayPerformance(string? path)
    {
        _performance = path is null ? null : FacialPerformance.Load(path);
        _performanceTime = 0;
    }
    // Opt-in native FACS inspection; ordinary speech leaves this unset.
    internal float? ReviewJawOpen { get; set; }

    private static readonly string[] Channels =
    {
        "eyeBlinkLeft", "eyeBlinkRight", "eyeSquintLeft", "eyeSquintRight", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookInRight", "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight", "eyeLookDownLeft", "eyeLookDownRight", "browDownLeft", "browDownRight", "mouthSmileLeft", "mouthSmileRight", "jawOpen", "mouthClose", "browInnerUp", "viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH",
        "viseme_DD", "viseme_kk", "viseme_CH", "viseme_SS", "viseme_nn", "viseme_RR", "viseme_aa", "viseme_E", "viseme_I", "viseme_O",
        "viseme_U", "mouthFrownLeft", "mouthFrownRight", "browOuterUpLeft", "browOuterUpRight", "eyeWideLeft", "eyeWideRight",
        "cheekPuff", "cheekSquintLeft", "cheekSquintRight", "jawForward", "jawLeft", "jawRight",
        "mouthDimpleLeft", "mouthDimpleRight", "mouthFunnel", "mouthLeft", "mouthRight", "mouthLowerDownLeft", "mouthLowerDownRight",
        "mouthPressLeft", "mouthPressRight", "mouthPucker", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
        "mouthStretchLeft", "mouthStretchRight", "mouthUpperUpLeft", "mouthUpperUpRight", "noseSneerLeft", "noseSneerRight", "tongueOut",
    };

    /// <summary>
    /// A face with ARKit shapes but no visemes (a MetaHuman, baked from its RigLogic poses) speaks through these: each viseme as a blend
    /// of ARKit mouth shapes, the jaw still driven by jawOpen. Rough phonetics, not a performance - the line on screen moving the mouth.
    /// </summary>
    private static readonly Dictionary<string, (string Shape, float Weight)[]> VisemeAsArkit = new(StringComparer.Ordinal)
    {
        ["viseme_PP"] = new[] { ("mouthPressLeft", 0.55f), ("mouthPressRight", 0.55f), ("mouthClose", 0.35f) },
        ["viseme_FF"] = new[] { ("mouthRollLower", 0.55f), ("mouthUpperUpLeft", 0.2f), ("mouthUpperUpRight", 0.2f) },
        ["viseme_TH"] = new[] { ("tongueOut", 0.25f), ("mouthStretchLeft", 0.1f), ("mouthStretchRight", 0.1f) },
        ["viseme_DD"] = new[] { ("mouthStretchLeft", 0.15f), ("mouthStretchRight", 0.15f), ("mouthShrugUpper", 0.15f) },
        ["viseme_kk"] = new[] { ("mouthStretchLeft", 0.2f), ("mouthStretchRight", 0.2f) },
        ["viseme_CH"] = new[] { ("mouthFunnel", 0.4f), ("mouthPucker", 0.2f), ("mouthShrugUpper", 0.15f) },
        ["viseme_SS"] = new[] { ("mouthStretchLeft", 0.35f), ("mouthStretchRight", 0.35f), ("mouthSmileLeft", 0.1f), ("mouthSmileRight", 0.1f) },
        ["viseme_nn"] = new[] { ("mouthClose", 0.1f), ("mouthStretchLeft", 0.1f), ("mouthStretchRight", 0.1f) },
        ["viseme_RR"] = new[] { ("mouthPucker", 0.35f), ("mouthFunnel", 0.2f) },
        ["viseme_aa"] = new[] { ("mouthStretchLeft", 0.1f), ("mouthStretchRight", 0.1f), ("mouthLowerDownLeft", 0.2f), ("mouthLowerDownRight", 0.2f) },
        ["viseme_E"] = new[] { ("mouthStretchLeft", 0.35f), ("mouthStretchRight", 0.35f), ("mouthSmileLeft", 0.2f), ("mouthSmileRight", 0.2f) },
        ["viseme_I"] = new[] { ("mouthSmileLeft", 0.35f), ("mouthSmileRight", 0.35f), ("mouthStretchLeft", 0.2f), ("mouthStretchRight", 0.2f) },
        ["viseme_O"] = new[] { ("mouthFunnel", 0.6f), ("mouthPucker", 0.2f) },
        ["viseme_U"] = new[] { ("mouthPucker", 0.7f), ("mouthFunnel", 0.2f) },
    };

    /// <summary>
    /// The combination correctives a MetaHuman face is baked with (asset_work/metahuman/tools/bl_assemble.py): each shape is what RigLogic
    /// does to the mouth for its parts together beyond their linear sum, so it plays at the product of its parts' weights (a left/right
    /// pair counts as their mean).
    /// </summary>
    private static readonly Dictionary<string, string[][]> Combinations = new(StringComparer.Ordinal)
    {
        ["jawOpen_mouthFunnel"] = new[] { new[] { "jawOpen" }, new[] { "mouthFunnel" } },
        ["jawOpen_mouthPucker"] = new[] { new[] { "jawOpen" }, new[] { "mouthPucker" } },
        ["jawOpen_mouthStretch"] = new[] { new[] { "jawOpen" }, new[] { "mouthStretchLeft", "mouthStretchRight" } },
        ["jawOpen_mouthSmile"] = new[] { new[] { "jawOpen" }, new[] { "mouthSmileLeft", "mouthSmileRight" } },
        ["jawOpen_mouthLowerDown"] = new[] { new[] { "jawOpen" }, new[] { "mouthLowerDownLeft", "mouthLowerDownRight" } },
        ["mouthFunnel_mouthPucker"] = new[] { new[] { "mouthFunnel" }, new[] { "mouthPucker" } },
    };

    /// <summary>The face channels <paramref name="mesh"/> carries, by blend shape index; null when it carries none.</summary>
    private static Dictionary<string, int>? Bind(MeshInstance3D mesh)
    {
        if (mesh.Mesh is not ArrayMesh array || array.GetBlendShapeCount() == 0)
            return null;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string channel in Channels)
        {
            int i = ShapeIndex(mesh, channel);
            if (i >= 0)
                index[channel] = i;
        }
        // No visemes of its own but the ARKit mouth: bind those shapes, keyed "arkit:<name>", for the viseme blends above.
        if (!index.ContainsKey("viseme_aa") && ShapeIndex(mesh, "mouthPucker") >= 0)
        {
            foreach (string shape in VisemeAsArkit.Values.SelectMany(v => v).Select(v => v.Shape).Distinct())
            {
                int i = ShapeIndex(mesh, shape);
                if (i >= 0 && !index.ContainsKey(shape))
                    index["arkit:" + shape] = i;
            }
            foreach (string combination in Combinations.Keys)
            {
                int i = mesh.FindBlendShapeByName(combination);
                if (i >= 0)
                    index["combo:" + combination] = i;
            }
        }
        return index.Count > 0 ? index : null;
    }

    /// <summary>Bind again after meshes were put on or taken off the face's body (the creator's hairstyles carry the face's shapes).</summary>
    public void Rebind(Node model)
    {
        _meshes.Clear();
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
            if (Bind(mesh) is { } index)
                _meshes.Add((mesh, index));
    }

    /// <summary>A driver for <paramref name="model"/>'s face, or null when no mesh carries the blink channel.</summary>
    public static FaceDriver? Attach(Node model, string seed)
    {
        var driver = new FaceDriver { Name = "Face" };
        // What was SEEN, not just what matched. "0 meshes with channels" is the same line whether the meshes were never found or were
        // found under names this list does not contain, and those are different bugs with different fixes.
        int found = 0, shapes = 0;
        var sample = new List<string>();
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            found++;
            if (mesh.Mesh is not ArrayMesh array || array.GetBlendShapeCount() == 0)
                continue;
            int count = array.GetBlendShapeCount();
            shapes += count;
            if (sample.Count < 8)
            {
                var names = new List<string>();
                for (int k = 0; k < Mathf.Min(count, 3); k++)
                    names.Add(array.GetBlendShapeName(k));
                sample.Add($"{mesh.Name}:{count}[{string.Join(",", names)}]");
            }
            if (Bind(mesh) is { } index)
                driver._meshes.Add((mesh, index));
        }
        if (!driver._meshes.Any(m => m.Index.ContainsKey("eyeBlinkLeft")))
        {
            // Logged, not silent. This returned null quietly for a whole investigation while three separate frame measurements reported
            // "the character never blinks" - and the asset does carry eyeBlinkLeft, so whether Attach ever found it is the first thing
            // that has to be on the record rather than inferred.
            GD.Print($"UNNAMED face: {seed} NOT ATTACHED - {found} MeshInstance3D found, {shapes} blend shapes total, " +
                     $"{driver._meshes.Count} mesh(es) matched a channel; sample {string.Join(" | ", sample)}");
            driver.Free();
            return null;
        }
        if (model is Node3D body && ArtLibrary.Find<Skeleton3D>(model) is { } rig
            && rig.FindBone("Hips") >= 0 && rig.FindBone("Jaw") >= 0 && rig.FindBone("LeftEye") >= 0 && rig.FindBone("RightEye") >= 0)
        {
            driver._nativeRig = rig;
            driver._nativeBody = body;
        }
        if (driver._meshes.Any(m => m.Mesh.FindBlendShapeByName("eyeBlink_L") >= 0)) driver._restingLid = .22f;
        ulong stableSeed = 14695981039346656037UL;
        foreach (char c in seed) stableSeed = unchecked((stableSeed ^ c) * 1099511628211UL);
        driver._random.Seed = stableSeed;
        driver._blinkIn = driver._random.RandfRange(0.5f, 3f);
        driver._seed = seed;
        GD.Print($"UNNAMED face: {seed} attached - {driver._meshes.Count} mesh(es), " +
                 $"{driver._meshes.Sum(m => m.Index.Count)} channel bindings");
        return driver;
    }

    public override void _Ready()
    {
        if (_nativeRig is not { } rig || _nativeBody is not { } body) return;
        rig.AddChild(new HumGenJaw { Name = "FACSJaw", OpenWeight = () => JawWeight });
        NativeJawBound = true;
        // HumGen eye morphs move the lids; its separate eyeballs need their own bones.
        // These facial channels also run when the optional full-body IK is disabled.
        _eyeTarget = new Node3D { Name = "FacialEyeTarget" };
        body.AddChild(_eyeTarget);
        _headBone = rig.FindBone("Head");
        if (_headBone >= 0)
            _headAxis = (rig.GetBoneGlobalRest(_headBone).Basis.Inverse() * (rig.GlobalBasis.Inverse() * body.GlobalBasis.Z)).Normalized();
        UpdateNativeEyes(Vector2.Zero);
        rig.AddChild(new HumGenHeadLevel { Name = "HeadLevel", Body = body });
        var eyes = new HumGenEyes { Name = "FACSEyes", Body = body, WorldTarget = () => _eyeTarget.GlobalPosition };
        rig.AddChild(eyes);
        NativeEyeBindings = eyes.BoundCount;
    }

    private void UpdateNativeEyes(Vector2 look)
    {
        if (_eyeTarget is null || _nativeRig is not { } rig || _nativeBody is not { } body) return;
        int left = rig.FindBone("LeftEye"), right = rig.FindBone("RightEye");
        if (left < 0 || right < 0) return;
        var direction = new Vector3(Mathf.Tan(look.X * .65f), Mathf.Tan(look.Y * .5f), 1).Normalized();
        var centre = (rig.GetBoneGlobalPose(left).Origin + rig.GetBoneGlobalPose(right).Origin) * .5f;
        var ahead = body.GlobalBasis * direction;
        if (_headBone >= 0)
            ahead = new Quaternion(body.GlobalBasis.Z.Normalized(), HumGenHeadLevel.Level((rig.GlobalBasis * (rig.GetBoneGlobalPose(_headBone).Basis * _headAxis)).Normalized(), Vector3.Up)) * ahead;
        _eyeTarget.GlobalPosition = rig.GlobalTransform * centre + ahead * 2;
    }

    private static int ShapeIndex(MeshInstance3D mesh, string channel)
    {
        int exact = mesh.FindBlendShapeByName(channel);
        if (exact >= 0) return exact;
        // Paid HumGen FACS uses _L/_R for these channels, while eye gaze already
        // uses the ARKit Left/Right spelling. Keep every mesh's native names.
        string alias = channel.EndsWith("Left", StringComparison.Ordinal) ? channel[..^4] + "_L"
            : channel.EndsWith("Right", StringComparison.Ordinal) ? channel[..^5] + "_R" : channel;
        return alias == channel ? -1 : mesh.FindBlendShapeByName(alias);
    }

    /// <summary>The line this character is saying now (a new text starts it from its first letter), or null when silent.</summary>
    public void Say(string? line)
    {
        if (line == _line)
            return;
        _line = line;
        _spoken = line is null ? -1 : 0;
    }

    public override void _Process(double delta)
    {
        if (_meshes.Count == 0 || !_meshes.Any(m => GodotObject.IsInstanceValid(m.Mesh) && m.Mesh.IsVisibleInTree())) return;
        if (GetViewport().GetCamera3D() is { } camera && camera.GlobalPosition.DistanceSquaredTo(_meshes[0].Mesh.GlobalPosition) > 625) return;
        foreach (string channel in Channels)
            _targets[channel] = 0;
        RestAndGaze(delta);
        Expression();
        if (Mood != "sleep") Blink(delta);
        if (_performance is { } performance)
        {
            performance.Sample(_performanceTime, _targets);
            UpdateNativeEyes(new Vector2((_targets["eyeLookOutLeft"] + _targets["eyeLookInRight"] - _targets["eyeLookInLeft"] - _targets["eyeLookOutRight"]) * .5f,
                (_targets["eyeLookUpLeft"] + _targets["eyeLookUpRight"] - _targets["eyeLookDownLeft"] - _targets["eyeLookDownRight"]) * .5f));
            _performanceTime += delta;
            if (_performanceTime > performance.Seconds) _performance = null;
        }
        Speak(delta);
        if (ReviewJawOpen is { } jaw) _targets["jawOpen"] = Mathf.Clamp(jaw, 0, 1);
        // Visemes and the jaw ease in and out (about 60 ms): the mouth moves between shapes, never snaps.
        float rate = 1f - Mathf.Exp(-16f * (float)delta);
        float blinkRate = 1f - Mathf.Exp(-40f * (float)delta);
        foreach (var (channel, target) in _targets)
        {
            float now = _weights.GetValueOrDefault(channel);
            _weights[channel] = Mathf.Lerp(now, target, channel.StartsWith("eyeBlink", StringComparison.Ordinal) ? blinkRate : rate);
        }
        // The ARKit mouth a viseme stands for, where a face has no visemes of its own (summed over the visemes easing in and out).
        _arkit.Clear();
        foreach (var (viseme, blend) in VisemeAsArkit)
        {
            float w = _weights.GetValueOrDefault(viseme);
            if (w <= 0.001f)
                continue;
            foreach (var (shape, weight) in blend)
                _arkit[shape] = _arkit.GetValueOrDefault(shape) + w * weight;
        }
        foreach (var (mesh, index) in _meshes)
        {
            foreach (var (channel, i) in index)
            {
                if (channel.StartsWith("combo:", StringComparison.Ordinal))
                    continue;
                float value = channel.StartsWith("arkit:", StringComparison.Ordinal) ? _arkit.GetValueOrDefault(channel[6..]) + _weights.GetValueOrDefault(channel[6..]) : _weights.GetValueOrDefault(channel);
                // mouthClose is both a channel and part of the PP blend on such a face.
                if (channel == "mouthClose" && !index.ContainsKey("viseme_PP"))
                    value += _arkit.GetValueOrDefault("mouthClose");
                mesh.SetBlendShapeValue(i, Mathf.Clamp(value, 0f, 1f));
            }
            foreach (var (channel, i) in index)
            {
                if (!channel.StartsWith("combo:", StringComparison.Ordinal))
                    continue;
                float weight = 1f;
                foreach (string[] part in Combinations[channel[6..]])
                    weight *= part.Average(shape => Mathf.Clamp(shape == "jawOpen" ? _weights.GetValueOrDefault(shape) : _arkit.GetValueOrDefault(shape), 0f, 1f));
                mesh.SetBlendShapeValue(i, weight);
            }
        }
        // The blink is the one face channel a blind review can score from three metres, and "the character never blinks" has been
        // reported from frame measurements rather than from the driver. This prints what the driver actually set, so the next claim
        // about it is read off the weight instead of inferred from pixels: every 5 s, the peak eyeBlink weight since the last line.
        _peak = Mathf.Max(_peak, _weights.GetValueOrDefault("eyeBlinkLeft"));
        _since += delta;
        if (_since >= 5 && System.Environment.GetEnvironmentVariable("OTHERREACH_RCD3_REVIEW") == "1")
        {
            GD.Print($"UNNAMED face: {_seed} peak eyeBlink {_peak:0.000} over {_since:0.0}s ({_meshes.Count} mesh(es))");
            _peak = 0;
            _since = 0;
        }
    }

    private void RestAndGaze(double delta)
    {
        _expressionTime += delta;
        _gazeIn -= delta;
        if (_gazeIn <= 0)
        {
            _saccade = new Vector2(_random.RandfRange(-.07f, .07f), _random.RandfRange(-.035f, .035f));
            _gazeIn = _random.RandfRange(.4f, 1.8f);
        }
        var look = _saccade;
        if (_gaze is { Z: > .2f } gaze)
            look += new Vector2(Mathf.Clamp(Mathf.Atan2(gaze.X, gaze.Z) / .65f, -.45f, .45f),
                Mathf.Clamp(Mathf.Atan2(gaze.Y, gaze.Z) / .5f, -.3f, .3f));
        UpdateNativeEyes(look);
        _targets["eyeLookOutLeft"] = _targets["eyeLookInRight"] = Math.Max(0, look.X);
        _targets["eyeLookInLeft"] = _targets["eyeLookOutRight"] = Math.Max(0, -look.X);
        _targets["eyeLookUpLeft"] = _targets["eyeLookUpRight"] = Math.Max(0, look.Y);
        _targets["eyeLookDownLeft"] = _targets["eyeLookDownRight"] = Math.Max(0, -look.Y);
        // A lid rest removes the constant wide-eyed expression: the upper lid just covers the top of the iris, as in
        // the HumGen renders, and follows the eye down (and lifts as it looks up). Blinks take precedence.
        bool wide = VisualOptions.All.GetValueOrDefault("lids") == "wide";
        _targets["eyeSquintLeft"] = _targets["eyeSquintRight"] = wide ? .07f : .12f;
        _targets["eyeBlinkLeft"] = _targets["eyeBlinkRight"] = wide ? .035f : Mathf.Clamp(_restingLid + Math.Max(0, -look.Y) * .35f - Math.Max(0, look.Y) * .2f, 0, .45f);
        float expression = .02f + .015f * Mathf.Sin((float)_expressionTime * .37f);
        _targets["browDownLeft"] = _targets["browDownRight"] = GentleRest ? 0 : expression;
        _targets["mouthSmileLeft"] = _targets["mouthSmileRight"] = _line is null ? (GentleRest ? .11f : 0) + expression * .5f : 0;
    }

    private void Blink(double delta)
    {
        if (_blinking >= 0)
        {
            _blinking += delta;
            float closed = (float)Math.Clamp(_blinking < .045 ? _blinking / .045 : _blinking < .095 ? 1 : 1 - (_blinking - .095) / .075, 0, 1);
            _targets["eyeBlinkLeft"] = _targets["eyeBlinkRight"] = closed;
            if (_blinking > .19)
            {
                _blinking = -1;
                // Mostly every few seconds, now and then a quick double blink.
                _blinkIn = _random.Randf() < 0.15f ? 0.25f : _random.RandfRange(2.2f, 5.5f);
            }
            return;
        }
        _blinkIn -= delta;
        if (_blinkIn <= 0)
            _blinking = 0;
    }

    private void Expression()
    {
        // Speech and blinks layer over the expression; never freeze a face into a pose.
        string mood = Mood ?? (_line?.TrimEnd().EndsWith('?') == true ? "curious" : "neutral");
        switch (mood)
        {
            case "sleep":
                _targets["eyeBlinkLeft"] = _targets["eyeBlinkRight"] = 1f;
                break;
            case "warm": case "happy":
                _targets["mouthSmileLeft"] = _targets["mouthSmileRight"] = .35f;
                _targets["eyeSquintLeft"] = _targets["eyeSquintRight"] = .18f;
                break;
            case "sad": case "worried":
                _targets["browInnerUp"] = .35f;
                _targets["mouthFrownLeft"] = _targets["mouthFrownRight"] = .25f;
                break;
            case "stern": case "angry":
                _targets["browDownLeft"] = _targets["browDownRight"] = .35f;
                _targets["eyeSquintLeft"] = _targets["eyeSquintRight"] = .22f;
                break;
            case "surprised":
                _targets["browInnerUp"] = .45f;
                _targets["browOuterUpLeft"] = _targets["browOuterUpRight"] = .3f;
                _targets["eyeWideLeft"] = _targets["eyeWideRight"] = .22f;
                break;
            case "curious":
                _targets["browInnerUp"] = .25f;
                _targets["browOuterUpRight"] = .15f;
                break;
        }
    }

    private void Speak(double delta)
    {
        if (_line is null)
            return;
        var recording = RecordedSpeech?.Invoke();
        if (recording is null && _spoken < 0) return;
        if (recording is null) _spoken += delta;
        int at = recording is { } voice ? Audio.VoiceRules.SpeechLetter(voice.Position, voice.Duration, _line.Length)
            : (int)(_spoken * LettersPerSecond);
        if (at >= _line.Length)
        {
            _spoken = -1;   // the line is said; the face rests until the next one
            return;
        }
        char c = char.ToLowerInvariant(_line[at]);
        char next = at + 1 < _line.Length ? char.ToLowerInvariant(_line[at + 1]) : ' ';
        (string shape, float jaw) = c switch
        {
            'a' => ("viseme_aa", 0.55f),
            'e' => ("viseme_E", 0.35f),
            'i' or 'y' => ("viseme_I", 0.25f),
            'o' => ("viseme_O", 0.45f),
            'u' or 'w' => ("viseme_U", 0.25f),
            'p' or 'b' or 'm' => ("viseme_PP", 0f),
            'f' or 'v' => ("viseme_FF", 0.08f),
            't' when next == 'h' => ("viseme_TH", 0.12f),
            't' or 'd' => ("viseme_DD", 0.15f),
            'k' or 'g' or 'c' or 'q' => ("viseme_kk", 0.2f),
            's' when next == 'h' => ("viseme_CH", 0.12f),
            'j' => ("viseme_CH", 0.12f),
            's' or 'z' or 'x' => ("viseme_SS", 0.08f),
            'n' or 'l' => ("viseme_nn", 0.15f),
            'r' => ("viseme_RR", 0.18f),
            'h' => ("viseme_aa", 0.2f),
            _ => ("viseme_sil", 0f),
        };
        // Punctuation holds the mouth shut a little longer; a question lifts the brows.
        _targets[shape] = shape == "viseme_sil" ? 0.6f : 0.85f * (recording is { } sounding ? Mathf.Clamp(sounding.Jaw * 8, 0, 1) : 1);
        // The recording supplies energy, while the phoneme supplies articulation:
        // a loud M/B/P must still close the jaw rather than leave it hanging open.
        _targets["jawOpen"] = recording is { } voiced ? voiced.Jaw * (jaw / .55f) : jaw * .6f;
        if (c is '.' or ',' or '!' or '?' or '-' or ';' or ':')
            _spoken += delta * 2.5;
        if (_line.TrimEnd().EndsWith('?'))
            _targets["browInnerUp"] = 0.25f;
    }
}
