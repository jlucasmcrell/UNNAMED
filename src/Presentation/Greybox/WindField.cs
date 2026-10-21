// UNNAMED Presentation - one world wind signal for everything that moves in it (Phase B, B0.5 / B4)
// Godot presentation only (D-11): global shader parameters; vegetation, cloth and particles read them, nothing writes game state

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// The world's wind as three global shader parameters, registered once before any shader that reads them compiles:
/// <c>wind</c> (xyz: the direction it blows towards, unit; w: strength 0-1), <c>wind_gust</c> (0-1: how much it gusts) and
/// <c>wind_time</c> (seconds, advanced here; the tree and foliage shaders below sway on the renderer's own TIME instead, which the
/// motion vectors can see). Every swaying thing - grass, flowers, ferns, shrubs,
/// leaves, branches, trunks, reeds, cloth, smoke - reads the same signal and answers it by its own stiffness, so trees never move like
/// grass. Weather sets the strength and gusts; the direction turns slowly.
/// </summary>
public partial class WindField : Node
{
    private static bool _registered;
    private static WindField? _instance;

    /// <summary>The wind registered and one node advancing its clock, under <paramref name="parent"/> the first time; the same node after.</summary>
    public static WindField Ensure(Node parent)
    {
        Register();
        if (_instance is null || !IsInstanceValid(_instance))
        {
            _instance = new WindField { Name = "Wind" };
            parent.AddChild(_instance);
        }
        return _instance;
    }

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
        // The tree and foliage shaders below also read the strangeness field.
        Otherworld.Register();
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
    /// The tree shader (B4): a lit surface rebuilt from a tree's own material (colour, normal, roughness; its leaves alpha-tested) that
    /// sways with the wind. The whole tree bends with the square of the height, computed from the vertex's place alone so trunk, wood and
    /// leaves stay joined; a stiffness answers the wind (a trunk moves centimetres where grass moves its height); only the foliage surfaces
    /// add a quick flutter of their own.
    /// </summary>
    public const string TreeShader = @"
shader_type spatial;
render_mode cull_back, depth_draw_opaque;
global uniform vec4 wind;
global uniform float wind_gust;
// ow_late is a breath-late sway: the gust that crosses the ground reaches this tree seconds after its neighbours (Old Late).
instance uniform float ow_late = 0.0;
#ifdef OTHERWORLD" + Otherworld.FieldGlsl + @"
// Otherworld (Otherworld.Variant), at full strangeness: the leaves turn toward blue-green, the bark's shade toward violet.
uniform float ow_hue = -40.0;
uniform float ow_violet = 1.0;
varying float strange;
#endif
// One turn an hour: the sway's rates are whole turns per hour, so TIME's hourly rollover never jumps a branch.
const float CYCLE = 6.28318530718 / 3600.0;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic;
uniform bool has_albedo = false;
uniform vec4 albedo_colour : source_color = vec4(1.0);
instance uniform vec4 tree_tint : source_color = vec4(1.0);
uniform bool use_instance_tint = false;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic;
uniform bool has_normal = false;
uniform sampler2D roughness_tex : hint_default_white, filter_linear_mipmap_anisotropic;
uniform bool has_roughness = false;
uniform float roughness_value = 0.9;
uniform bool foliage = false;
uniform float alpha_cut = 0.5;
instance uniform float tree_height = 13.0;
instance uniform float stiffness = 1.0;

void vertex() {
    vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
#ifdef OTHERWORLD
    strange = otherworld_at(origin);
#endif
    // TIME, not a clock of our own: the renderer runs vertex() again at the previous frame's TIME for the motion vectors,
    // and a global uniform has no previous value - swaying leaves had zero motion and TAA smeared and sparkled them.
    float wind_time = TIME - ow_late;
    mat3 basis = mat3(MODEL_MATRIX);
    float scale2 = max(dot(basis[0], basis[0]), 0.0001);
    float h = clamp(VERTEX.y / max(tree_height, 0.01), 0.0, 1.0);
    float phase = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);
    float gust = 0.5 + 0.5 * sin(wind_time * 212.0 * CYCLE - dot(origin.xz, wind.xz) * 0.045);
    float strength = wind.w * mix(1.0, 0.35 + 1.3 * gust, wind_gust);
    float sway = sin(wind_time * 516.0 * CYCLE + phase * 6.2832) * 0.6 + sin(wind_time * 974.0 * CYCLE + phase * 3.0) * 0.4;
    // About a tenth of a metre at the crown of a 13 m tree in a moderate wind, more in the gusts.
    vec3 bend = wind.xyz * strength * h * h * (0.75 + 0.25 * sway) * tree_height * sqrt(scale2) * 0.024 / max(stiffness, 0.05);
    if (foliage) {
        vec3 p = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        float n = sin(wind_time * 2349.0 * CYCLE + p.x * 1.7 + p.y * 1.3) * cos(wind_time * 1891.0 * CYCLE + p.z * 1.9);
        bend += vec3(n, n * 0.4, -n) * 0.045 * strength * h;
    }
    VERTEX += transpose(basis) * bend / scale2;
}

void fragment() {
    vec4 a = has_albedo ? texture(albedo_tex, UV) : vec4(1.0);
    vec4 colour = albedo_colour * (use_instance_tint ? tree_tint : vec4(1.0));
    ALBEDO = a.rgb * colour.rgb;
#ifdef OTHERWORLD
    if (strange > 0.0) {
        float l = dot(ALBEDO, vec3(0.2126, 0.7152, 0.0722));
        ALBEDO = foliage ? max(otherworld_hue(ALBEDO, ow_hue * strange), vec3(0.0))
                         : mix(ALBEDO, ALBEDO * vec3(1.04, 0.9, 1.2), ow_violet * strange * (1.0 - smoothstep(0.05, 0.35, l)));
    }
#endif
    if (foliage) {
        ALPHA = a.a * colour.a;
        ALPHA_SCISSOR_THRESHOLD = alpha_cut;
        // A LEAF TRANSMITS LIGHT, AND THE LEAVES THAT MOST NEED IT ARE THE ONES FACING AWAY. Measured: a birch crown
    // rendered at mean luma 92 against 145 as authored, while albedo, texture and per-instance tint all arrived
    // INTACT (the well-lit band sat at 0.94 of albedo). So the loss was never a gain error - it was POPULATION:
    // the away-facing half of every leaf card receives no sun, and BACKLIGHT is the term that exists to fill it.
    // At olive (0.12, 0.14, 0.06) it filled that half with 12% of a dark green, which is also why the shadowed
    // leaves measured G-dominant. A warm fill on the leaf's own hue instead: per pixel this predicts the crown
    // mean rising from 92 to ~118-135, and the green cast going with it.
    BACKLIGHT = vec3(0.88, 0.81, 0.63);
        if (!FRONT_FACING) NORMAL = -NORMAL;
    }
    ROUGHNESS = has_roughness ? texture(roughness_tex, UV).g : roughness_value;
    if (has_normal) {
        NORMAL_MAP = texture(normal_tex, UV).rgb;
    }
}";

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
const float CYCLE = 6.28318530718 / 3600.0;
#ifdef OTHERWORLD" + Otherworld.FieldGlsl + @"
// Otherworld (ScatterRules' otherflora, at full strangeness; Otherworld.cs scales it by where the plant stands). All zero: as scanned.
// OW_MINERAL (stones only): the seam and the lichen, which need the plant's own frame.
uniform float ow_hue = 0.0;
uniform vec3 ow_backlight = vec3(0.0);
uniform float ow_backlight_on = 0.0;
uniform float ow_pale = 0.0;
uniform float ow_sheen = 0.0;
uniform float ow_late = 0.0;
uniform vec3 ow_glow = vec3(0.0);
uniform bool ow_glow_green = false;
varying float strange;
#ifdef OW_MINERAL
uniform vec3 ow_vein = vec3(0.0);
uniform float ow_vein_on = 0.0;
uniform float ow_iridescence = 0.0;
uniform vec3 ow_lichen = vec3(0.0);
uniform float ow_lichen_on = 0.0;
varying vec3 ow_place;
#endif
#endif
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2D arm_tex : hint_default_white, filter_linear_mipmap;
uniform bool has_normal = false;
uniform bool has_arm = false;
uniform float alpha_cut = 0.18;
uniform float plant_height = 1.0;
uniform float stiffness = 1.0;
uniform float flutter = 0.35;
uniform vec3 backlight_colour : source_color = vec3(0.88, 0.81, 0.63);
uniform float roughness_value = 0.85;
// Grass blades carry normals bent to the sky: both sides shade alike (no dark undersides), unlike a leaf lit through from behind.
uniform bool upright_normals = false;
// Metres from the camera over which the plants drop out one by one (ScatterView: the outer part of the kind's reach); 0 keeps all.
uniform float thin_from = 0.0;
uniform float thin_to = 0.0;
// Opt-in per-plant LOD selection. Negative begin preserves the existing renderer.
uniform float plant_lod_begin = -1.0;
uniform float plant_lod_end = 0.0;
varying vec3 tint;

void vertex() {
    tint = INSTANCE_CUSTOM.rgb;
    vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    mat3 basis = mat3(MODEL_MATRIX);
    float scale2 = max(dot(basis[0], basis[0]), 0.0001);
    float tall = plant_height * sqrt(scale2);
    float h = clamp(COLOR.r, 0.0, 1.0);
    float phase = fract(sin(dot(origin.xz, vec2(12.9898, 78.233))) * 43758.5453);
#ifndef STATIC_FOLIAGE
    float wind_time = TIME;   // see TreeShader: the motion vectors need TIME
#endif
#ifdef OTHERWORLD
    // The place's strangeness (ScatterView puts it in the custom data's alpha), each plant its own share of it, so a stand is not
    // wrong all at once.
    strange = clamp(INSTANCE_CUSTOM.a * otherworld_mix.x * (0.6 + 0.8 * fract(phase * 7.31)), 0.0, 1.0);
#ifdef OW_MINERAL
    ow_place = VERTEX * sqrt(scale2) + origin * 0.37;
#endif
    // A breath late: up to 1.6 s behind the gust its neighbours answer.
#ifndef STATIC_FOLIAGE
    wind_time -= ow_late * strange * 1.6;
#endif
#endif
#ifndef STATIC_FOLIAGE
    float gust = 0.5 + 0.5 * sin(wind_time * 212.0 * CYCLE - dot(origin.xz, wind.xz) * 0.045);
    float sway = sin(wind_time * 917.0 * CYCLE + phase * 6.2832) * 0.65 + sin(wind_time * 1776.0 * CYCLE + phase * 11.0 + COLOR.g * 3.0) * 0.35;
    float amount = wind.w * mix(1.0, 0.35 + 1.3 * gust, wind_gust) * h * h / max(stiffness, 0.05);
    vec3 bend = wind.xyz * amount * (0.7 + 0.3 * sway) * tall * 0.5;
    bend += vec3(sin(wind_time * 2865.0 * CYCLE + phase * 20.0 + COLOR.g * 12.0), 0.0, cos(wind_time * 2464.0 * CYCLE + phase * 17.0 + COLOR.g * 9.0))
            * flutter * 0.05 * wind.w * h * tall;
    // The bend is in the world; the plant is turned and uniformly scaled: carry it back into the model's space.
    VERTEX += transpose(basis) * bend / scale2;
    VERTEX.y -= amount * amount * 0.08 * plant_height;
#endif
#ifndef NO_FOLIAGE_THINNING
    if (plant_lod_begin >= 0.0) {
        float plant_distance = distance(origin, CAMERA_POSITION_WORLD);
        // The same origin/hash on adjacent mesh levels makes selection complementary.
        float variation = phase * 2.0 - 1.0;
        float begin = plant_lod_begin + variation * min(1.0, plant_lod_begin * 0.1);
        float end = plant_lod_end + variation * min(1.0, plant_lod_end * 0.1);
        if ((plant_lod_begin > 0.0 && plant_distance < begin) ||
                (plant_lod_end > 0.0 && plant_distance >= end)) {
            VERTEX = vec3(0.0);
        }
    }
    if (thin_to > 0.0) {
        float keep = 1.0 - smoothstep(thin_from, thin_to, distance(origin, CAMERA_POSITION_WORLD));
        if (fract(sin(dot(origin.xz, vec2(41.37, 17.91))) * 23421.631) >= keep) {
            VERTEX = vec3(0.0);   // every vertex on the origin: the plant draws nothing
        }
    }
#endif
}

void fragment() {
    vec4 a = texture(albedo_tex, UV);
    vec3 colour = a.rgb * tint;
#ifdef OTHERWORLD
    float lift = 0.0;
    if (strange > 0.0) {
        colour = max(otherworld_hue(colour, ow_hue * strange), vec3(0.0));
        float l = dot(colour, vec3(0.2126, 0.7152, 0.0722));
        colour = mix(colour, vec3(0.80, 0.78, 0.72) * (0.55 + l), ow_pale * strange);
        colour *= mix(vec3(1.0), vec3(0.78, 0.9, 1.0), ow_sheen * strange);
#ifdef OW_MINERAL
        if (ow_vein_on > 0.0) {
            // A seam through the stone, in the stone's own frame so it stays put: a thin ridge of a warped wave, only where it is strange enough.
            vec3 p = ow_place * 2.2;
            float n = abs(sin(dot(p, vec3(1.3, 0.7, 1.1)) + 1.4 * sin(p.y * 2.1 + p.x * 0.9) + 0.9 * sin(p.z * 1.9 - p.y)));
            float vein = (1.0 - smoothstep(0.04, 0.12 + 0.1 * strange, n)) * smoothstep(0.3, 0.75, strange);
            vec3 film = 0.5 + 0.5 * cos(6.2832 * (vec3(0.0, 0.33, 0.67) + 1.7 * dot(NORMAL, VIEW)));
            colour = mix(colour, mix(ow_vein, film * 0.7, ow_iridescence) * (0.45 + l), vein);
            lift = vein;
        }
        if (ow_lichen_on > 0.0) {
            float up = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).y;
            vec3 q = ow_place * 3.0;
            float crust = smoothstep(0.35, 0.8, up) * smoothstep(0.15, 0.55, sin(q.x + 2.0 * sin(q.z * 0.8)) * sin(q.z * 1.3 + q.y) * 0.5 + 0.5);
            colour = mix(colour, ow_lichen * (0.6 + l), crust * strange * 0.85);
        }
#endif
    }
#endif
    ALBEDO = colour;
#ifndef OPAQUE_FOLIAGE
    ALPHA = a.a;
    ALPHA_SCISSOR_THRESHOLD = alpha_cut;
#endif
#ifndef NO_FOLIAGE_ARM_MAP
    if (has_arm) {
        vec3 arm = texture(arm_tex, UV).rgb;
        // NO AO FROM THE FILE. ArtLibrary refuses to draw a model's baked occlusion - the generated and scanned
        // models' occlusion channels are black over whole islands, which Godot applies as black patches where
        // Blender shows none. This shader was putting it straight back: AO with AO_LIGHT_AFFECT 0.25 multiplies
        // 75% of the red channel into the light, and a PHOTOGRAMMETRY surface carries far more baked occlusion
        // than a hand-painted card does. Measured cost on scanned grass: the mid-ground went from luma 64.1 to
        // 54.3 when these clumps replaced cards. The renderer's own occlusion does contact shading instead.
        AO = 1.0;
        ROUGHNESS = arm.g;
    } else
#endif
    {
        ROUGHNESS = roughness_value;
    }
    SPECULAR = 0.3;
    BACKLIGHT = backlight_colour;
#ifdef OTHERWORLD
    if (strange > 0.0) {
        ROUGHNESS = mix(ROUGHNESS, 0.3, max(ow_sheen, lift * 0.8) * strange);
        SPECULAR = mix(0.3, 0.6, ow_sheen * strange);
        BACKLIGHT = mix(backlight_colour, ow_backlight, ow_backlight_on * strange);
        // The night glow: on the paler parts (a flower head, a frond's tip) or, for moss, the green ones. The albedo is linear, and a
        // scanned plant's sits at 0.03-0.2, so the masks are set there.
        float key = ow_glow_green ? clamp((a.g - max(a.r, a.b)) * 14.0, 0.0, 1.0) : smoothstep(0.04, 0.22, max(a.r, max(a.g, a.b)));
        EMISSION = ow_glow * key * strange * otherworld_mix.y;
#ifdef OW_MINERAL
        EMISSION += ow_vein * lift * 0.35 * strange * otherworld_mix.y;
#endif
    }
#endif
#ifndef NO_FOLIAGE_NORMAL_MAP
    if (has_normal) {
        NORMAL_MAP = texture(normal_tex, UV).rgb;
    }
#endif
    if (upright_normals && !FRONT_FACING) {
        NORMAL = -NORMAL;
    }
}";
}
