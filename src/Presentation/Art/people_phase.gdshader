shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back;

// Siann: a faint subdermal trace over the existing skin response. Orenth:
// a faint surface registration. Mor: an incomplete remembered form.
uniform int mode = 0;
uniform bool head_surface = false;
uniform sampler2D base_map : source_color, hint_default_white;
uniform vec4 base_colour : source_color = vec4(1.0);
varying vec3 local_point;

void vertex() {
    local_point = VERTEX;
}

void fragment() {
    if (mode == 0) {
        float fine = abs(sin(UV.x * 185.0 + sin(UV.y * 93.0) * 1.4));
        float trace = 1.0 - smoothstep(0.025, 0.095, fine);
        bool thin = head_surface || (local_point.y > 1.35 && abs(local_point.x) < 0.14);
        ALBEDO = vec3(0.69, 0.65, 0.80);
        ALPHA = thin ? 0.035 + trace * 0.15 : 0.025;
        ROUGHNESS = 0.31;
        METALLIC = 0.06;
    } else if (mode == 1) {
        ALBEDO = vec3(0.49, 0.51, 0.55);
        float registration = pow(max(0.0, sin(local_point.y * 190.0 + TIME * 1.7)), 16.0);
        ALPHA = 0.018 + registration * 0.035;
        ROUGHNESS = 0.8;
    } else {
        float feet = smoothstep(0.015, 0.23, local_point.y);
        float edge = smoothstep(0.0, 0.42, abs(dot(normalize(NORMAL), normalize(VIEW))));
        ALBEDO = mix(vec3(0.19, 0.22, 0.26), texture(base_map, UV).rgb * base_colour.rgb, 0.45);
        ALPHA = feet * mix(0.06, head_surface ? 0.48 : 0.38, edge);
        ROUGHNESS = 1.0;
        SPECULAR = 0.0;
    }
    ALPHA *= texture(base_map, UV).a * base_colour.a;
}
