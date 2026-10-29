shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2D eye_albedo : source_color, filter_linear_mipmap, repeat_disable;

void fragment() {
    ALBEDO = texture(eye_albedo, UV).rgb;
    float radius = length(UV - vec2(0.5));
    float sclera = smoothstep(0.112, 0.125, radius);
    // The cornea catches the real scene lights. Sclera stays softer, without an emissive white veil over the iris.
    ROUGHNESS = mix(0.11, 0.32, sclera);
    SPECULAR = mix(0.65, 0.35, sclera);
    CLEARCOAT = 0.22 * (1.0 - sclera);
    CLEARCOAT_ROUGHNESS = 0.15;
}
