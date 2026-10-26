shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back;

// Mor: an incomplete remembered form.
uniform bool head_surface = false;
uniform sampler2D base_map : source_color, hint_default_white;
uniform vec4 base_colour : source_color = vec4(1.0);
varying vec3 local_point;

void vertex() {
    local_point = VERTEX;
}

void fragment() {
    float feet = smoothstep(0.015, 0.23, local_point.y);
    float edge = smoothstep(0.0, 0.42, abs(dot(normalize(NORMAL), normalize(VIEW))));
    ALBEDO = mix(vec3(0.19, 0.22, 0.26), texture(base_map, UV).rgb * base_colour.rgb, 0.45);
    ALPHA = feet * mix(0.06, head_surface ? 0.48 : 0.38, edge);
    ROUGHNESS = 1.0;
    SPECULAR = 0.0;
    ALPHA *= texture(base_map, UV).a * base_colour.a;
}
