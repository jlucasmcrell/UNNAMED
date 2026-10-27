shader_type spatial;
render_mode sss_mode_skin;
uniform sampler2D color_map : source_color, filter_linear_mipmap;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap;
uniform sampler2D scatter_map : filter_linear_mipmap;
uniform float roughness = .7487672;
uniform float metallic = .5;
void fragment() {
    ALBEDO = texture(color_map, UV).rgb;
    NORMAL_MAP = texture(normal_map, UV).rgb;
    ROUGHNESS = roughness;
    METALLIC = metallic;
    SPECULAR = 0.;
    vec3 radius = texture(scatter_map, UV).rgb;
    SSS_STRENGTH = max(radius.r, max(radius.g, radius.b));
}
