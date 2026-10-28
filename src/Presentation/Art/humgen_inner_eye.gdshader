shader_type spatial;
render_mode sss_mode_skin;
uniform sampler2D color_map : source_color, filter_linear_mipmap;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap;
uniform sampler2D scatter_map : filter_linear_mipmap;
uniform float roughness = .7487672;
uniform float metallic = .5;
// The creator's iris: the preset's own iris scaled, or (Siann, Kal) replaced by a people's own, a dark pupil, a brighter
// ring round it and a limbal ring at the edge, over the preset's grain. The HumGen iris spans radius .035 - .118 in UV.
uniform float iris_lightness = 1.0;
// How much of the sclera's own warmth is taken out (a Siann's whites are not skin-coloured).
uniform float sclera_pale = 0.0;
uniform bool iris_recolour = false;
uniform vec3 iris_base : source_color = vec3(.44, .43, .48);
uniform vec3 iris_ring : source_color = vec3(.12, .12, .13);
uniform vec2 iris_shade = vec2(.65, .5);
uniform float pupil_radius = .038;
uniform vec3 pupil_colour : source_color = vec3(.02, .02, .03);
void fragment() {
    vec3 albedo = texture(color_map, UV).rgb;
    float radius = length(UV - .5);
    if (radius >= .118) {
        albedo = mix(albedo, vec3(dot(albedo, vec3(.2126, .7152, .0722))), sclera_pale);
    } else {
        if (iris_recolour) {
            float grain = pow(dot(albedo, vec3(.2126, .7152, .0722)), 1. / 2.2);
            float ring = smoothstep(.05, .058, radius) * (1. - smoothstep(.066, .082, radius));
            float limbal = 1. - .65 * smoothstep(.095, .112, radius);
            vec3 iris = (iris_base * (iris_shade.x + iris_shade.y * grain) + iris_ring * ring) * limbal;
            albedo = radius < pupil_radius ? mix(albedo, pupil_colour, .95) : iris;
        } else {
            albedo *= iris_lightness;
        }
    }
    ALBEDO = albedo;
    NORMAL_MAP = texture(normal_map, UV).rgb;
    ROUGHNESS = roughness;
    METALLIC = metallic;
    SPECULAR = 0.;
    vec3 radius_scatter = texture(scatter_map, UV).rgb;
    SSS_STRENGTH = max(radius_scatter.r, max(radius_scatter.g, radius_scatter.b));
}
