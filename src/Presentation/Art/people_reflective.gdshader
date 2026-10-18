shader_type spatial;
render_mode cull_back;
#include "region_mask.gdshaderinc"
#include "base_layer.gdshaderinc"
uniform vec4 colour : source_color = vec4(0.67, 0.70, 0.73, 1.0);
uniform float metal = 1.0;
uniform float rough = 0.12;

void vertex() {
    region_mask(VERTEX, UV2);
}

void fragment() {
    ALBEDO = colour.rgb;
    METALLIC = metal;
    ROUGHNESS = rough;
    vec3 source = texture(base_map, UV).rgb * base_colour.rgb;
    float cloth = base_layer_mask(source);
    ALBEDO = mix(ALBEDO, source, cloth);
    METALLIC *= 1.0 - cloth;
    ROUGHNESS = mix(ROUGHNESS, 0.9, cloth);
}
