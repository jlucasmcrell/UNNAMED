shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D strand_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D strand_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D strand_roughness_map : hint_default_white, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D strand_flow : hint_anisotropy, filter_linear_mipmap_anisotropic, repeat_disable;
uniform bool use_native_normal = false;
uniform bool use_native_roughness = false;
uniform float native_normal_scale = 1.;
uniform float native_metallic = 0.;
uniform float native_opacity = 1.;
uniform float strand_anisotropy = 0.;
uniform float specular_amount = .5;
uniform vec3 strand_colour : source_color = vec3(1.);
uniform bool directional = false;
uniform float strand_roughness = .35;
uniform vec2 grain_range = vec2(.43, .58);

void fragment() {
    vec4 strand = texture(strand_atlas, UV);
    float grain = sqrt(clamp(dot(strand.rgb, vec3(.299, .587, .114)), 0., 1.));
    // A multiplicative dye cannot turn baked black brows/roots white. Preserve their
    // native strand grain and alpha, but bound pigment to the same white reflectance.
    vec3 dye = strand_colour * mix(grain_range.x, grain_range.y, grain);
    ALBEDO = dye;
    ALPHA = strand.a * native_opacity;
    ALPHA_HASH_SCALE = 1.;
    if (use_native_normal) {
        NORMAL_MAP = texture(strand_normal, UV).rgb;
        NORMAL_MAP_DEPTH = native_normal_scale;
    }
    ROUGHNESS = strand_roughness * (use_native_roughness ? texture(strand_roughness_map, UV).g : 1.);
    METALLIC = native_metallic;
    SPECULAR = specular_amount;
    ANISOTROPY = directional ? strand_anisotropy : 0.;
    ANISOTROPY_FLOW = texture(strand_flow, UV).rg * 2. - 1.;
}
