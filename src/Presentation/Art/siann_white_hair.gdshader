shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D strand_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D strand_flow : hint_default_white, filter_linear_mipmap, repeat_disable;
uniform vec3 strand_colour : source_color = vec3(1.);
uniform bool directional = false;
uniform float strand_roughness = .35;
uniform vec2 grain_range = vec2(.43, .58);
uniform float self_light : hint_range(0., .5) = .14;

void fragment() {
    vec4 strand = texture(strand_atlas, UV);
    float grain = sqrt(clamp(dot(strand.rgb, vec3(.299, .587, .114)), 0., 1.));
    // A multiplicative dye cannot turn baked black brows/roots white. Preserve their
    // native strand grain and alpha, but bound pigment to the same white reflectance.
    vec3 dye = strand_colour * mix(grain_range.x, grain_range.y, grain);
    ALBEDO = dye;
    // The cap sits under the cards and takes their shadow; a faint self-light keeps it white instead of grey.
    EMISSION = dye * self_light;
    ALPHA = strand.a;
    ALPHA_SCISSOR_THRESHOLD = .18;
    ROUGHNESS = strand_roughness;
    SPECULAR = .45;
    ANISOTROPY = directional ? .7 : 0.;
    ANISOTROPY_FLOW = texture(strand_flow, UV).rg * 2. - 1.;
}
