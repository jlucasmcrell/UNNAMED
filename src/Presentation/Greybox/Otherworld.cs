// UNNAMED Presentation - how far from Earth a place looks: the strangeness field the plant, tree and rock shaders read (art direction D.1)
// Godot presentation only (D-11): global shader parameters, set from data; nothing here is read by the simulation

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// "Plausible but wrong", as a gradient. The scanned plants and rocks keep their own maps; the shaders shift one or two properties
/// (hue, translucency, a night glow, a late sway, a vein) by a strangeness that is low in the villages, higher on the open moor and
/// highest at the Foldscar. The field is three global shader parameters, so every shader works it out from where it stands:
/// <c>otherworld_calm</c> (xy, zw: the two villages' centres), <c>otherworld_fold</c> (xy: the Foldscar's heart; z, w: full strength
/// within, none beyond, metres) and <c>otherworld_mix</c> (x: the run's strength, 0 for off; y: night 0-1; z: strangeness at a village
/// centre; w: in the open). <c>--visual otherworld=off</c> compiles the layer out, so off draws as before; subtle is the default (owner, 2026-10-17).
/// </summary>
public static class Otherworld
{
    private static bool _registered;
    private static Vector4 _mix;

    /// <summary>Register the parameters (idempotent): the wind's shaders declare them, so <see cref="WindField.Register"/> calls this.</summary>
    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        RenderingServer.GlobalShaderParameterAdd("otherworld_calm", RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        RenderingServer.GlobalShaderParameterAdd("otherworld_fold", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 1, 2));
        RenderingServer.GlobalShaderParameterAdd("otherworld_mix", RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
    }

    /// <summary>The run's strength: 0 off, 0.7 subtle, 1 bold (<c>--visual otherworld</c>).</summary>
    public static float Strength => VisualOptions.Otherworld switch
    {
        "subtle" => 0.7f,
        "bold" => 1f,
        _ => 0f,
    };

    /// <summary>
    /// A wind shader's code for this run: as it always was when the run's strength is 0, so off costs nothing; with the otherworld layer
    /// (<c>OTHERWORLD</c>) when a concept is on, and its seam and lichen (<c>OW_MINERAL</c>) only for stones, the one use that needs a
    /// per-vertex position carried to every fragment.
    /// </summary>
    public static string Variant(string code, bool mineral = false)
    {
        if (Strength <= 0)
            return code;
        int line = code.IndexOf("shader_type spatial;", StringComparison.Ordinal) + "shader_type spatial;".Length;
        return code.Insert(line, mineral ? "\n#define OTHERWORLD\n#define OW_MINERAL" : "\n#define OTHERWORLD");
    }

    /// <summary>Set the field from the scatter rules' <c>otherworld</c> block.</summary>
    public static void Set(Art.OtherworldField field)
    {
        Register();
        RenderingServer.GlobalShaderParameterSet("otherworld_calm", new Vector4(field.Calm[0].X, field.Calm[0].Z, field.Calm[^1].X, field.Calm[^1].Z));
        RenderingServer.GlobalShaderParameterSet("otherworld_fold", new Vector4(field.Fold.X, field.Fold.Z, field.FoldFullM, field.FoldNoneM));
        _mix = new Vector4(Strength, _mix.Y, field.Village, field.Wild);
        RenderingServer.GlobalShaderParameterSet("otherworld_mix", _mix);
        GD.Print($"UNNAMED otherworld: {VisualOptions.Otherworld} (strength {Strength:0.0}), village {field.Village:0.00}, open {field.Wild:0.00}, Foldscar at {field.Fold.X:0},{field.Fold.Z:0}");
    }

    /// <summary>How dark it is (1 - the sky's daylight), for the night glow; SkyView's air sets it as the clock runs.</summary>
    public static void Night(float night)
    {
        Register();
        _mix.Y = Mathf.Clamp(night, 0f, 1f);
        RenderingServer.GlobalShaderParameterSet("otherworld_mix", _mix);
    }

    /// <summary>
    /// The strangeness at a point in GLSL, shared by the shaders: the higher of the open ground's (rising from the village value to the
    /// open value between 45 and 170 m from the nearer village) and the Foldscar's (1 at its heart, nothing past its reach), times the
    /// run's strength.
    /// </summary>
    public const string FieldGlsl = @"
global uniform vec4 otherworld_calm;
global uniform vec4 otherworld_fold;
global uniform vec4 otherworld_mix;
float otherworld_at(vec3 p) {
    if (otherworld_mix.x == 0.0) return 0.0;
    float calm = min(distance(p.xz, otherworld_calm.xy), distance(p.xz, otherworld_calm.zw));
    float open = mix(otherworld_mix.z, otherworld_mix.w, smoothstep(45.0, 170.0, calm));
    float fold = 1.0 - smoothstep(otherworld_fold.z, otherworld_fold.w, distance(p.xz, otherworld_fold.xy));
    return max(open, fold) * otherworld_mix.x;
}
vec3 otherworld_hue(vec3 c, float degrees) {
    // Rotation about the grey axis: keeps the scan's luminance and detail, turns only its hue. Negative turns green toward teal and
    // blue, positive toward gold and bronze (the rules' convention; the rotation itself runs red to green to blue).
    float a = radians(-degrees);
    vec3 k = vec3(0.57735);
    return c * cos(a) + cross(k, c) * sin(a) + k * dot(k, c) * (1.0 - cos(a));
}
";
}
