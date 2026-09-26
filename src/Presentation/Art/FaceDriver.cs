// UNNAMED Presentation - a character-standard face driven from what its body is doing: blinks, the line on screen spoken as visemes
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Art;

/// <summary>
/// The face channels of a character-standard body (CHARACTER_ASSET_STANDARD.md: ARKit-52 and the Meta visemes as morph targets, on
/// the skin, brows, lashes, teeth, tongue and hair alike): natural blinks, and while the character speaks a line, visemes timed from
/// the line's text (there is no recorded voice: about fifteen letters a second, pauses at punctuation) with the jaw following the
/// vowels. A body without the channels gets no driver.
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

    private static readonly string[] Channels =
    {
        "eyeBlinkLeft", "eyeBlinkRight", "jawOpen", "mouthClose", "browInnerUp", "viseme_sil", "viseme_PP", "viseme_FF", "viseme_TH",
        "viseme_DD", "viseme_kk", "viseme_CH", "viseme_SS", "viseme_nn", "viseme_RR", "viseme_aa", "viseme_E", "viseme_I", "viseme_O",
        "viseme_U",
    };

    /// <summary>A driver for <paramref name="model"/>'s face, or null when no mesh carries the blink channel.</summary>
    public static FaceDriver? Attach(Node model, string seed)
    {
        var driver = new FaceDriver { Name = "Face" };
        foreach (var mesh in model.FindChildren("*", nameof(MeshInstance3D), true, false).Cast<MeshInstance3D>())
        {
            if (mesh.Mesh is not ArrayMesh array || array.GetBlendShapeCount() == 0)
                continue;
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string channel in Channels)
            {
                int i = mesh.FindBlendShapeByName(channel);
                if (i >= 0)
                    index[channel] = i;
            }
            if (index.Count > 0)
                driver._meshes.Add((mesh, index));
        }
        if (!driver._meshes.Any(m => m.Index.ContainsKey("eyeBlinkLeft")))
        {
            driver.Free();
            return null;
        }
        driver._random.Seed = (ulong)seed.GetHashCode();
        driver._blinkIn = driver._random.RandfRange(0.5f, 3f);
        return driver;
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
        foreach (string channel in Channels)
            _targets[channel] = 0;
        Blink(delta);
        Speak(delta);
        // Visemes and the jaw ease in and out (about 60 ms): the mouth moves between shapes, never snaps.
        float rate = 1f - Mathf.Exp(-16f * (float)delta);
        float blinkRate = 1f - Mathf.Exp(-40f * (float)delta);
        foreach (var (channel, target) in _targets)
        {
            float now = _weights.GetValueOrDefault(channel);
            _weights[channel] = Mathf.Lerp(now, target, channel.StartsWith("eyeBlink", StringComparison.Ordinal) ? blinkRate : rate);
        }
        foreach (var (mesh, index) in _meshes)
        {
            foreach (var (channel, i) in index)
                mesh.SetBlendShapeValue(i, _weights.GetValueOrDefault(channel));
        }
    }

    private void Blink(double delta)
    {
        if (_blinking >= 0)
        {
            _blinking += delta;
            float closed = (float)Math.Clamp(1 - Math.Abs(_blinking - 0.07) / 0.07, 0, 1);
            _targets["eyeBlinkLeft"] = _targets["eyeBlinkRight"] = closed;
            if (_blinking > 0.16)
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

    private void Speak(double delta)
    {
        if (_line is null || _spoken < 0)
            return;
        _spoken += delta;
        int at = (int)(_spoken * LettersPerSecond);
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
        _targets[shape] = shape == "viseme_sil" ? 0.6f : 0.85f;
        _targets["jawOpen"] = jaw * 0.6f;
        if (c is '.' or ',' or '!' or '?' or '-' or ';' or ':')
            _spoken += delta * 2.5;
        if (_line.TrimEnd().EndsWith('?'))
            _targets["browInnerUp"] = 0.25f;
    }
}
