shader_type spatial;
render_mode cull_back;
#include "base_layer.gdshaderinc"
uniform sampler2D stone_colour : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D stone_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D stone_orm : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_scanned_stone = false;
uniform float stone_tiles = 5.5;
varying vec3 local_point;

void vertex() {
    local_point = VERTEX;
}

void fragment() {
    NORMAL_MAP = vec3(0.5, 0.5, 1.0);
    NORMAL_MAP_DEPTH = 0.0;
    float strata = sin(local_point.y * 68.0 + sin(local_point.x * 21.0) * 1.4 + local_point.z * 17.0);
    float fracture = abs(sin(local_point.x * 31.0 + local_point.y * 13.0 + sin(local_point.z * 47.0)));
    float vein = 1.0 - smoothstep(0.025, 0.09, fracture);
    ALBEDO = mix(vec3(0.19, 0.205, 0.21), vec3(0.31, 0.29, 0.265), strata * 0.5 + 0.5);
    ALBEDO = mix(ALBEDO, vec3(0.47, 0.52, 0.51), vein * 0.7);
    ROUGHNESS = mix(0.89, 0.36, vein);
    METALLIC = vein * 0.18;
    SPECULAR = 0.32;
    if (use_scanned_stone) {
        vec2 stone_uv = UV * stone_tiles;
        vec3 scanned = texture(stone_colour, stone_uv).rgb;
        float lightness = dot(scanned, vec3(0.2126, 0.7152, 0.0722));
        ALBEDO = mix(vec3(lightness), scanned, 0.25) * vec3(0.78, 0.83, 0.86);
        ALBEDO = mix(ALBEDO, vec3(0.47, 0.52, 0.51), vein * 0.20);
        vec3 orm = texture(stone_orm, stone_uv).rgb;
        AO = orm.r;
        ROUGHNESS = clamp(orm.g, 0.4, 0.95);
        METALLIC = vein * 0.10;
        NORMAL_MAP = texture(stone_normal, stone_uv).rgb;
        NORMAL_MAP_DEPTH = 0.65;
    }
    vec3 source = texture(base_map, UV).rgb * base_colour.rgb;
    float cloth = base_layer_mask(source);
    ALBEDO = mix(ALBEDO, source, cloth);
    ROUGHNESS = mix(ROUGHNESS, 0.9, cloth);
    METALLIC *= 1.0 - cloth;
    NORMAL_MAP_DEPTH *= 1.0 - cloth;
}
