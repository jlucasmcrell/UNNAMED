shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// A Siann's native hair cards, brows and lashes: a single colour over the preset's own strand shapes at their soft
// native coverage. The atlas is baked dark, so the colour replaces it and only its grain and alpha are kept.
uniform sampler2D strand_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform vec3 strand_colour : source_color = vec3(.6, .59, .56);
uniform vec2 grain_range = vec2(.8, 1.);
uniform float strand_roughness = .6;

void fragment() {
    vec4 strand = texture(strand_atlas, UV);
    float grain = sqrt(clamp(dot(strand.rgb, vec3(.299, .587, .114)), 0., 1.));
    ALBEDO = strand_colour * mix(grain_range.x, grain_range.y, grain);
    ALPHA = clamp(strand.a * 1.15, 0., 1.);
    ALPHA_HASH_SCALE = 1.;
    ROUGHNESS = strand_roughness;
    SPECULAR = .3;
}
