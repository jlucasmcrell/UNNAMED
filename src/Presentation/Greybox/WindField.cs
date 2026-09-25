// UNNAMED Presentation - one world wind signal for everything that moves in it (Phase B, B0.5 / B4)
// Godot presentation only (D-11): global shader parameters; vegetation, cloth and particles read them, nothing writes game state

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The world's wind as three global shader parameters, registered once before any shader that reads them compiles:
/// <c>wind</c> (xyz: the direction it blows towards, unit; w: strength 0-1), <c>wind_gust</c> (0-1: how much it gusts) and
/// <c>wind_time</c> (seconds, advanced here so every reader shares one clock). Every swaying thing - grass, flowers, ferns, shrubs,
/// leaves, branches, trunks, reeds, cloth, smoke - reads the same signal and answers it by its own stiffness, so trees never move like
/// grass. Weather sets the strength and gusts; the direction turns slowly.
/// </summary>
public partial class WindField : Node
{
    private static bool _registered;

    public static Vector3 Direction { get; private set; } = new Vector3(0.8f, 0, 0.6f).Normalized();
    public static float Strength { get; private set; } = 0.35f;
    public static float Gust { get; private set; } = 0.55f;

    /// <summary>Register the parameters (idempotent). Call before building anything whose shader declares them.</summary>
    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        // Registered here, not in the project settings, so nothing else declares them (listing them is editor-only).
        RenderingServer.GlobalShaderParameterAdd("wind", RenderingServer.GlobalShaderParameterType.Vec4, Pack());
        RenderingServer.GlobalShaderParameterAdd("wind_gust", RenderingServer.GlobalShaderParameterType.Float, Gust);
        RenderingServer.GlobalShaderParameterAdd("wind_time", RenderingServer.GlobalShaderParameterType.Float, 0f);
    }

    /// <summary>Set the wind (the weather's input): direction in the ground plane, strength and gustiness 0-1.</summary>
    public static void Set(Vector3 direction, float strength, float gust)
    {
        Register();
        Direction = new Vector3(direction.X, 0, direction.Z).Normalized();
        Strength = Math.Clamp(strength, 0f, 1f);
        Gust = Math.Clamp(gust, 0f, 1f);
        RenderingServer.GlobalShaderParameterSet("wind", Pack());
        RenderingServer.GlobalShaderParameterSet("wind_gust", Gust);
    }

    private static Vector4 Pack() => new(Direction.X, Direction.Y, Direction.Z, Strength);

    private double _time;

    public override void _Process(double delta)
    {
        _time += delta;
        RenderingServer.GlobalShaderParameterSet("wind_time", (float)_time);
    }

    /// <summary>
    /// The foliage shader: an alpha-tested leaf/blade material that bends with the wind. The plant's own vertex colours carry the bend
    /// (red: the height fraction from the root, 0-1; green: a phase so the leaves do not move as one), the instance's custom data its tint.
    /// The bend grows with the square of the height and with the plant's size in the world, a slow gust travels across the ground in the
    /// wind's direction, each instance takes its phase from where it stands, and a stiffness separates grass from nettles from ferns.
    /// </summary>
    public const string FoliageShader = @"
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;
global uniform vec4 wind;
global uniform float wind_gust;
global uniform float wind_time;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2D arm_tex : hint_default_white, filter_linear_mipmap;
uniform bool has_normal = false;
uniform bool has_arm = false;
uniform float alpha_cut = 0.4;
uniform float plant_height = 1.0;
uniform float stiffness = 1.0;
uniform float flutter = 0.35;
uniform vec3 backlight_colour : source_color = vec3(0.10, 0.12, 0.04);
uniform float roughness_value = 0.85;
varying vec3 tint;

void vertex() {
    tint = INSTANCE_CUSTOM.rgb;
    vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    mat3 basis = mat3(MODEL_MATRIX);
    float scale2 = max(dot(basis[0], basis[0]), 0.0001);
    float tall = plant_height * sqrt(scale2);
    float h = clamp(COLOR.r, 0.0, 1.0);
    float phase = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);
    float gust = 0.5 + 0.5 * sin(wind_time * 0.37 - dot(origin.xz, wind.xz) * 0.045);
    float sway = sin(wind_time * 1.6 + phase * 6.2832) * 0.65 + sin(wind_time * 3.1 + phase * 11.0 + COLOR.g * 3.0) * 0.35;
    float amount = wind.w * mix(1.0, 0.35 + 1.3 * gust, wind_gust) * h * h / max(stiffness, 0.05);
    vec3 bend = wind.xyz * amount * (0.7 + 0.3 * sway) * tall * 0.5;
    bend += vec3(sin(wind_time * 5.0 + phase * 20.0 + COLOR.g * 12.0), 0.0, cos(wind_time * 4.3 + phase * 17.0 + COLOR.g * 9.0))
            * flutter * 0.05 * wind.w * h * tall;
    // The bend is in the world; the plant is turned and uniformly scaled: carry it back into the model's space.
    VERTEX += transpose(basis) * bend / scale2;
    VERTEX.y -= amount * amount * 0.08 * plant_height;
}

void fragment() {
    vec4 a = texture(albedo_tex, UV);
    ALBEDO = a.rgb * tint;
    ALPHA = a.a;
    ALPHA_SCISSOR_THRESHOLD = alpha_cut;
    if (has_arm) {
        vec3 arm = texture(arm_tex, UV).rgb;
        AO = arm.r;
        AO_LIGHT_AFFECT = 0.25;
        ROUGHNESS = arm.g;
    } else {
        ROUGHNESS = roughness_value;
    }
    SPECULAR = 0.3;
    BACKLIGHT = backlight_colour;
    if (has_normal) {
        NORMAL_MAP = texture(normal_tex, UV).rgb;
    }
}";
}
