shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D strand_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D strand_flow : hint_default_white, filter_linear_mipmap, repeat_disable;
uniform vec3 strand_colour : source_color = vec3(1.);
uniform bool directional = false;
uniform float strand_roughness = .35;

void fragment() {
    vec4 strand = texture(strand_atlas, UV);
    float grain = sqrt(clamp(dot(strand.rgb, vec3(.299, .587, .114)), 0., 1.));
    // A multiplicative dye cannot turn baked black brows/roots white. Preserve their
    // native strand grain and alpha, but bound pigment to the same white reflectance.
    ALBEDO = strand_colour * mix(.43, .58, grain);
    ALPHA = strand.a;
    ALPHA_SCISSOR_THRESHOLD = .55;
    ROUGHNESS = strand_roughness;
    SPECULAR = .45;
    ANISOTROPY = directional ? .7 : 0.;
    ANISOTROPY_FLOW = texture(strand_flow, UV).rg * 2. - 1.;
}
