shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// Siann pigment over the native strand atlas and its full normal/roughness/flow inputs.
// Alpha hashing uses the same TAA-filtered opacity and ordinary mips as other native grooms.
uniform sampler2D strand_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D strand_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D strand_roughness_map : hint_default_white, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D strand_flow : hint_anisotropy, filter_linear_mipmap_anisotropic, repeat_disable;
uniform bool use_native_normal = false;
uniform bool use_native_roughness = false;
uniform bool directional = false;
uniform float native_normal_scale = 1.;
uniform float native_metallic = 0.;
uniform float native_opacity = 1.;
uniform float strand_anisotropy = 0.;
uniform vec3 strand_colour : source_color = vec3(.6, .59, .56);
uniform vec2 grain_range = vec2(.8, 1.);
uniform float strand_roughness = .6;
uniform float specular_amount : hint_range(0., 1.) = .45;
// The pearl of the people reaches the hair: a faint cool sheen along the edge of each card in daylight (the review,
// 2026-10-28 asked for the opal to read on hair highlights too).
uniform float pearl_rim : hint_range(0., .6) = .25;

void fragment() {
    vec4 strand = texture(strand_atlas, UV);
    float grain = sqrt(clamp(dot(strand.rgb, vec3(.299, .587, .114)), 0., 1.));
    vec3 dye = strand_colour * mix(grain_range.x, grain_range.y, grain);
    float rim = pow(1. - clamp(dot(NORMAL, VIEW), 0., 1.), 2.);
    ALBEDO = dye * mix(vec3(1.), vec3(.95, .98, 1.03), rim * pearl_rim);
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
