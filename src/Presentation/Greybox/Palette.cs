// UNNAMED Presentation - greybox materials
// Godot presentation only: no gameplay state lives here (D-11)

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>Flat greybox materials, one per kind of thing, so the hollow reads at a glance.</summary>
public static class Palette
{
    public static StandardMaterial3D Terrain { get; } = Flat(new Color(0.36f, 0.42f, 0.27f), roughness: 1f);
    public static StandardMaterial3D Wood { get; } = Flat(new Color(0.42f, 0.30f, 0.20f));
    public static StandardMaterial3D Roof { get; } = Flat(new Color(0.30f, 0.22f, 0.16f));
    public static StandardMaterial3D Door { get; } = Flat(new Color(0.55f, 0.22f, 0.16f));
    public static StandardMaterial3D Rock { get; } = Flat(new Color(0.45f, 0.45f, 0.47f));
    public static StandardMaterial3D Trunk { get; } = Flat(new Color(0.33f, 0.24f, 0.17f));
    public static StandardMaterial3D Canopy { get; } = Flat(new Color(0.20f, 0.33f, 0.18f));
    public static StandardMaterial3D Cliff { get; } = Flat(new Color(0.32f, 0.30f, 0.29f));
    public static StandardMaterial3D Skin { get; } = Flat(new Color(0.78f, 0.63f, 0.52f));
    public static StandardMaterial3D Cloth { get; } = Flat(new Color(0.28f, 0.33f, 0.45f));
    public static StandardMaterial3D Leather { get; } = Flat(new Color(0.35f, 0.25f, 0.18f));
    public static StandardMaterial3D Proxy { get; } = Flat(new Color(0.55f, 0.52f, 0.45f));
    public static StandardMaterial3D Metal { get; } = Flat(new Color(0.62f, 0.60f, 0.56f), roughness: 0.4f);
    public static StandardMaterial3D Ore { get; } = Flat(new Color(0.55f, 0.32f, 0.22f), roughness: 0.6f);
    public static StandardMaterial3D WorkedOut { get; } = Flat(new Color(0.40f, 0.38f, 0.37f));
    public static StandardMaterial3D AshBark { get; } = Flat(new Color(0.78f, 0.74f, 0.64f));
    public static StandardMaterial3D Leaves { get; } = Flat(new Color(0.36f, 0.48f, 0.26f));
    public static StandardMaterial3D Hearth { get; } = Flat(new Color(0.38f, 0.34f, 0.32f));
    public static StandardMaterial3D Iron { get; } = Flat(new Color(0.18f, 0.18f, 0.20f), roughness: 0.35f);
    public static StandardMaterial3D Shaft { get; } = Flat(new Color(0.60f, 0.50f, 0.36f));

    /// <summary>An arrow's fletching: pale, so an arrow stuck in bark or earth can be found by eye.</summary>
    public static StandardMaterial3D Fletching { get; } = Flat(new Color(0.92f, 0.90f, 0.84f));

    public static StandardMaterial3D Embers { get; } = new()
    {
        AlbedoColor = new Color(1f, 0.45f, 0.1f),
        EmissionEnabled = true,
        Emission = new Color(1f, 0.35f, 0.05f),
    };

    /// <summary>
    /// The fold that holds Tavar (M6, content bible §8: "keep the weirdness restrained - slight image doubling; delayed shadow;
    /// displaced audio; inconsistent reflections"): a refractive shimmer over the world behind it (two drifting noise fields warp the
    /// screen sample, and a second, more-displaced ghost sample is blended faintly under the first for the doubling), a faint violet
    /// fresnel rim, and drifting motes; its own alpha and colour fade out approaching the ground and the top of its footprint so it
    /// reads as a standing effect rather than a hard-edged debug volume. Unshaded: it draws its own screen sample, not lit geometry.
    /// </summary>
    public static ShaderMaterial FoldscarBarrier { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_draw_opaque, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec3 edge_colour : source_color = vec3(0.62, 0.42, 0.95);
uniform vec3 core_colour : source_color = vec3(0.22, 0.15, 0.38);
uniform float distortion_amount : hint_range(0.0, 0.05) = 0.014;
uniform float scroll_speed = 0.045;
uniform float top_fade : hint_range(0.0, 1.0) = 0.60;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void fragment() {
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.2);

    vec2 uv_a = UV * vec2(2.4, 1.6) + vec2(TIME * scroll_speed, -TIME * scroll_speed * 0.5);
    vec2 uv_b = UV * vec2(3.6, 2.4) + vec2(-TIME * scroll_speed * 0.6, TIME * scroll_speed * 0.35);
    float shimmer = vnoise(uv_a * 5.0) * 0.6 + vnoise(uv_b * 8.0) * 0.4;

    vec2 offset = (vec2(vnoise(uv_a * 6.0 + 2.0), vnoise(uv_b * 7.0 + 9.0)) - 0.5) * distortion_amount;
    vec3 behind = textureLod(screen_tex, SCREEN_UV + offset, 0.0).rgb;
    // A second, more-displaced sample ghosted faintly under the first: the bible's ""slight image doubling"".
    vec3 ghost = textureLod(screen_tex, SCREEN_UV + offset * 2.6 + vec2(0.006, -0.004), 1.5).rgb;

    float bottom_fade = smoothstep(0.0, 0.10, UV.y);
    float top_alpha = 1.0 - smoothstep(top_fade, 1.0, UV.y);
    float mask = bottom_fade * top_alpha;

    vec2 mote_uv = UV * vec2(7.0, 11.0) + vec2(shimmer * 0.4, -TIME * 0.085);
    float motes = pow(clamp(vnoise(mote_uv) - 0.55, 0.0, 1.0) * 2.2, 3.0);

    vec3 tint = mix(core_colour, edge_colour, clamp(fresnel + shimmer * 0.2, 0.0, 1.0));
    vec3 warped = mix(behind, ghost, 0.35);
    vec3 colour = mix(warped, tint, 0.35 + fresnel * 0.45) + edge_colour * motes * 1.4;

    ALBEDO = colour;
    ALPHA = clamp(0.22 + fresnel * 0.5 + shimmer * 0.1 + motes * 0.5, 0.0, 0.92) * mask;
}
",
        },
    };

    /// <summary>
    /// The fold as B0.6 draws it (<c>--visual foldscar=proof</c>): the bible's restrained weirdness and nothing else - the world behind
    /// seen a little wrong (a slow warp, and the same view doubled a hair to one side), strongest where the fold faces the eye and thinning
    /// to nothing at its silhouette, so it has no edge to read as a volume; a ragged, drifting top and a foot that fades into the ground;
    /// the faintest cool cast and a few slow motes. No violet rim: the Foldscar is wrong, not magical.
    /// </summary>
    public static ShaderMaterial FoldscarFold { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_back, unshaded, depth_draw_never, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float height_m = 2.6;
uniform float warp_amount = 0.010;
uniform float doubling = 0.010;
varying vec3 local;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void vertex() {
    local = VERTEX;
}

void fragment() {
    float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float body = smoothstep(0.08, 0.75, facing);
    float around = atan(local.z, local.x);
    float h = local.y / height_m + 0.5;
    float ragged = vnoise(vec2(around * 2.5 + TIME * 0.03, TIME * 0.07));
    float top = 1.0 - smoothstep(0.45 + 0.3 * ragged, 0.98, h);
    float foot = smoothstep(0.0, 0.12, h);

    vec2 p = vec2(around * 1.7, h * 2.2);
    vec2 warp = (vec2(vnoise(p * 3.0 + vec2(TIME * 0.05, 0.0)), vnoise(p * 3.0 + vec2(7.3, -TIME * 0.04))) - 0.5) * warp_amount * body;
    vec4 behind = textureLod(screen_tex, SCREEN_UV + warp, 0.0);
    // The doubling: the same view again, a hair to one side and drifting slowly back and forth.
    vec2 side = vec2(doubling * (0.6 + 0.4 * sin(TIME * 0.21)), doubling * 0.15);
    vec4 ghost = textureLod(screen_tex, SCREEN_UV + warp * 1.6 + side * body, 0.0);
    // What reaches the frame after the screen copy (a fading instance: the scatter's far chunks, a figure at its range) is not in it -
    // the copy holds nothing there (alpha 0). Draw only over what the copy holds, so those stay as they were drawn.
    float held = smoothstep(0.4, 0.9, behind.a);
    vec3 colour = mix(behind.rgb, mix(behind.rgb, ghost.rgb, 0.42), smoothstep(0.4, 0.9, ghost.a));
    colour *= mix(vec3(1.0), vec3(0.94, 0.93, 1.03), body);

    float motes = pow(clamp(vnoise(vec2(around * 9.0, h * 14.0 - TIME * 0.12)) - 0.62, 0.0, 1.0) * 2.6, 3.0);
    colour += vec3(0.55, 0.50, 0.80) * motes * 0.35;

    ALBEDO = colour;
    ALPHA = clamp(body * top * foot + motes * 0.25 * top * foot, 0.0, 1.0) * held;
}
",
        },
    };

    /// <summary>A Quiet Stone turned into line (M6): a pale band round its top.</summary>
    public static StandardMaterial3D Aligned { get; } = new()
    {
        AlbedoColor = new Color(0.85f, 0.88f, 0.95f),
        EmissionEnabled = true,
        Emission = new Color(0.35f, 0.38f, 0.48f),
    };

    /// <summary>
    /// A Quiet Stone drawn from the asset library, turned into line (Phase A): a pale rim of light round the stone's own silhouette - a
    /// shell grown a centimetre off its surface, brightest where the surface turns away from the eye - leaving its face its own.
    /// </summary>
    public static ShaderMaterial AlignedGlow { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled;
uniform vec3 glow : source_color = vec3(0.70, 0.80, 1.0);
uniform float strength = 0.8;
void vertex() { VERTEX += NORMAL * 0.012; }
void fragment() {
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
    ALBEDO = glow * rim * strength;
}
",
        },
    };

    /// <summary>
    /// The stream (presentation only - the full water system is later): animated fake-normal ripples (two scrolling wave fields turned
    /// into a bump via a finite-difference slope, so a plain low-poly plane still shows motion), a shallow/deep colour gradient and a
    /// soft edge where it meets the banks (both read from the depth prepass: the gap between the water surface and whatever the camera
    /// sees behind it), a fresnel sky tint at grazing angles, and roughness that varies a little with the ripple so the sun's specular
    /// sparkles rather than sitting as one flat highlight.
    /// </summary>
    public static ShaderMaterial StreamWater { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
uniform vec3 shallow_colour : source_color = vec3(0.18, 0.36, 0.34);
uniform vec3 deep_colour : source_color = vec3(0.035, 0.09, 0.13);
uniform float depth_fade_m : hint_range(0.05, 4.0) = 0.9;
uniform float edge_fade_m : hint_range(0.005, 1.0) = 0.12;
uniform float ripple_speed = 0.6;
uniform float ripple_scale = 2.4;
uniform float ripple_strength : hint_range(0.0, 0.2) = 0.035;
uniform float roughness_base : hint_range(0.0, 1.0) = 0.12;

varying vec2 world_xz;

void vertex() {
    world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

float wave_h(vec2 p) {
    float a = sin((p.x * 1.00 + p.y * 0.35 + TIME * ripple_speed) * ripple_scale);
    float b = sin((p.x * -0.60 + p.y * 0.80 - TIME * ripple_speed * 0.8) * ripple_scale * 1.7);
    return a * 0.6 + b * 0.4;
}

void fragment() {
    float e = 0.05;
    float h0 = wave_h(world_xz);
    float slope_x = (wave_h(world_xz + vec2(e, 0.0)) - h0) / e;
    float slope_z = (wave_h(world_xz + vec2(0.0, e)) - h0) / e;
    vec3 world_normal = normalize(vec3(-slope_x * ripple_strength, 1.0, -slope_z * ripple_strength));
    NORMAL = normalize((VIEW_MATRIX * vec4(world_normal, 0.0)).xyz);

    float raw_depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
    vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw_depth);
    vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
    view.xyz /= view.w;
    float scene_depth = -view.z;
    float surface_depth = -VERTEX.z;
    float water_depth = max(scene_depth - surface_depth, 0.0);

    float depth_t = clamp(water_depth / depth_fade_m, 0.0, 1.0);
    vec3 tint = mix(shallow_colour, deep_colour, depth_t);
    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
    tint = mix(tint, vec3(0.75, 0.82, 0.85), fresnel * 0.35);
    float edge = clamp(water_depth / edge_fade_m, 0.0, 1.0);

    ALBEDO = tint;
    ALPHA = mix(0.35, 0.88, depth_t) * edge;
    ROUGHNESS = clamp(roughness_base + abs(slope_x + slope_z) * 0.6, 0.03, 0.5);
    SPECULAR = 0.5;
}
",
        },
    };

    private static StandardMaterial3D Flat(Color color, float roughness = 0.9f) => new() { AlbedoColor = color, Roughness = roughness };
}
