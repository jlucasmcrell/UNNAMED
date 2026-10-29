shader_type spatial;
render_mode blend_add, depth_draw_never, cull_back, ambient_light_disabled;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap;
void fragment() {
    ALBEDO = vec3(0.);
    NORMAL_MAP = texture(normal_map, UV).rgb;
    ROUGHNESS = 0.;
    SPECULAR = .5;
    ALPHA = 1.;
}
