// UNNAMED Presentation - water surfaces in the scenery (Phase B, B0.8 / B6): the river round the hollow on the ravine floors
// Godot presentation only (D-11): a surface to look at; no water is simulated, and nothing reads it

using Godot;

namespace UNNAMED.Presentation.Greybox;

/// <summary>
/// <c>--visual water=flow</c>: a river along the ravine's floor on its three sides (scenery beyond the walkable edge, seen from the edge),
/// flowing round the hollow - up the west side, along the north, down the east - drawn with <see cref="FlowWater"/>: normals carried by the
/// current (two phases of procedural ripples advected along the flow and blended so nothing stretches), colour by depth, foam where it is
/// shallow and streaks of it carried downstream, a little refraction and a sky reflection. <c>classic</c> draws Phase A's stream shader on
/// the same strips; <c>boujie</c> is gone (B0.8: rejected, removed). Water inside the hollow needs terrain below a water level: M7's.
/// </summary>
public static class WaterView
{
    /// <summary>The rivers on the ravine floors; null (and why) when the run draws none.</summary>
    public static Node3D? BuildRavineRiver(Node3D parent, Rect2 region, Func<float, float, float> height, out string? why)
    {
        why = null;
        if (VisualOptions.Water is not ("flow" or "classic"))
        {
            why = VisualOptions.Water == "boujie" ? "Boujie Water was rejected in B0.8 and is not in the project" : null;
            return null;
        }
        var root = new Node3D { Name = "RavineRiver" };
        parent.AddChild(root);
        foreach (var (centre, size, flow) in Strips(region))
            Strip(root, flow.Y > 0 ? "west" : flow.Y < 0 ? "east" : "north", centre, size, flow, height);
        return root;
    }

    /// <summary>
    /// The river's strips on the ravine floor - west, north, east: each its centre line 17 m out (the floor runs 8-26 m out), its size
    /// (15 m wide, running 24 m past the corners) and the way the current runs along it. Shared with the mist over them.
    /// </summary>
    public static IEnumerable<(Vector3 Centre, Vector2 Size, Vector2 Flow)> Strips(Rect2 region)
    {
        float x0 = region.Position.X, x1 = region.End.X, z0 = region.Position.Y, z1 = region.End.Y;
        const float Out = 17f, Width = 15f, Past = 24f;
        yield return (new Vector3(x0 - Out, 0, (z0 + z1) / 2), new Vector2(Width, z1 - z0 + 2 * Past), new Vector2(0, 1));
        yield return (new Vector3((x0 + x1) / 2, 0, z1 + Out), new Vector2(x1 - x0 + 2 * Past, Width), new Vector2(1, 0));
        yield return (new Vector3(x1 + Out, 0, (z0 + z1) / 2), new Vector2(Width, z1 - z0 + 2 * Past), new Vector2(0, -1));
    }

    private static void Strip(Node3D root, string name, Vector3 centre, Vector2 size, Vector2 flow, Func<float, float, float> height)
    {
        // The surface stands a little over the floor's mean along the strip: most of the floor is under it, its high spots are banks.
        float level = 0;
        for (int i = -8; i <= 8; i++)
        {
            float along = i / 8f * 0.45f;
            var p = centre + new Vector3(flow.X * along * size.X, 0, flow.Y * along * size.Y);
            level += height(p.X, p.Z) / 17f;
        }
        Material material;
        if (VisualOptions.Water == "flow")
        {
            var flowing = (ShaderMaterial)FlowWater.Duplicate();
            flowing.SetShaderParameter("flow_dir", flow);
            material = flowing;
        }
        else
        {
            material = Palette.StreamWater;
        }
        root.AddChild(new MeshInstance3D
        {
            Name = name, Mesh = new PlaneMesh { Size = size, SubdivideWidth = 32, SubdivideDepth = 32 }, MaterialOverride = material,
            Position = new Vector3(centre.X, level + 1.1f, centre.Z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>The flowing-water surface (B6), shared and duplicated per strip for its current.</summary>
    public static ShaderMaterial FlowWater { get; } = new()
    {
        Shader = new Shader
        {
            Code = @"
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec2 flow_dir = vec2(1.0, 0.0);
uniform float flow_speed = 0.9;
uniform float ripple_scale = 0.55;
uniform float ripple_strength = 0.55;
uniform vec3 shallow_colour : source_color = vec3(0.16, 0.27, 0.24);
uniform vec3 deep_colour : source_color = vec3(0.03, 0.07, 0.08);
uniform float depth_fade_m = 1.6;
uniform float foam_m = 0.12;
uniform float refraction = 0.018;

varying vec3 world_pos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * vnoise(p); p = p * 2.03 + vec2(17.0, 9.0); a *= 0.5; }
    return v;
}
vec2 slope(vec2 p) {
    float e = 0.08;
    float h = fbm(p);
    return vec2(fbm(p + vec2(e, 0.0)) - h, fbm(p + vec2(0.0, e)) - h) / e;
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Two phases of the ripple field carried downstream, each reset as the other peaks, so the pattern flows without stretching.
    vec2 p = world_pos.xz * ripple_scale;
    float t = TIME * flow_speed * 0.25;
    float ph0 = fract(t), ph1 = fract(t + 0.5);
    float w = abs(1.0 - 2.0 * ph0);
    vec2 s = mix(slope(p - flow_dir * ph0 * 2.0), slope(p - flow_dir * ph1 * 2.0 + vec2(3.7, 1.3)), 1.0 - w);
    s += slope(p * 2.7 - flow_dir * TIME * flow_speed * 0.35) * 0.35;
    vec3 n = normalize(vec3(-s.x * ripple_strength, 1.0, -s.y * ripple_strength));
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

    float raw = textureLod(depth_tex, SCREEN_UV, 0.0).r;
    vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
    float depth = max(-view.z / view.w - (-VERTEX.z), 0.0);
    float deep = clamp(depth / depth_fade_m, 0.0, 1.0);

    // What lies under the water, bent a little by the ripples - only where the screen copy holds it (instances fading in or out are
    // not in it: its alpha is 0 there).
    vec2 bent = SCREEN_UV + n.xz * refraction * deep;
    vec4 under = textureLod(screen_tex, bent, 0.0);
    vec3 body = mix(shallow_colour, deep_colour, deep);
    vec3 colour = mix(body, under.rgb * body * 3.0, (1.0 - deep) * 0.6 * under.a);

    // Foam where it is shallow, broken up by noise and drawn out downstream in streaks.
    vec2 streak = world_pos.xz * vec2(0.9, 0.9) - flow_dir * TIME * flow_speed;
    float breakup = vnoise(streak * vec2(1.0 + abs(flow_dir.y) * 3.0, 1.0 + abs(flow_dir.x) * 3.0));
    float foam = (1.0 - smoothstep(0.0, foam_m, depth)) * smoothstep(0.55, 0.85, breakup) * 0.7;
    foam += smoothstep(0.8, 0.95, fbm(streak * 0.6)) * 0.2;
    colour = mix(colour, vec3(0.62, 0.66, 0.65), clamp(foam, 0.0, 1.0));

    ALBEDO = colour;
    ALPHA = clamp(mix(0.55, 0.96, deep) * smoothstep(0.0, 0.06, depth) + foam * 0.3, 0.0, 1.0);
    ROUGHNESS = mix(0.06, 0.5, clamp(foam, 0.0, 1.0));
    SPECULAR = 0.55;
}
",
        },
    };
}
