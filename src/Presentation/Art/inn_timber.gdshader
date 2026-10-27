shader_type spatial;

// The kit logs run horizontally on both wall axes. Standard triplanar wood
// puts the grain upright; rotate each wall projection in world metres instead.
uniform sampler2D colour_map : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_map : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_metres = 1.5;
uniform float normal_strength = 1.0;
varying vec3 world_position;
varying vec3 world_normal;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
}

void fragment() {
    vec3 p = world_position / tile_metres;
    vec3 n = normalize(world_normal);
    vec3 weight = pow(abs(n), vec3(16.0));
    weight /= max(dot(weight, vec3(1.0)), 0.0001);
    vec2 ux = p.yz;
    vec2 uy = p.xz;
    vec2 uz = p.yx;
    ALBEDO = texture(colour_map, ux).rgb * weight.x
        + texture(colour_map, uy).rgb * weight.y + texture(colour_map, uz).rgb * weight.z;
    vec3 orm = texture(orm_map, ux).rgb * weight.x
        + texture(orm_map, uy).rgb * weight.y + texture(orm_map, uz).rgb * weight.z;
    AO = orm.r;
    ROUGHNESS = orm.g;
    METALLIC = 0.0;
    vec3 nx = texture(normal_map, ux).xyz * 2.0 - 1.0;
    vec3 ny = texture(normal_map, uy).xyz * 2.0 - 1.0;
    vec3 nz = texture(normal_map, uz).xyz * 2.0 - 1.0;
    nx.xy *= normal_strength;
    ny.xy *= normal_strength;
    nz.xy *= normal_strength;
    vec3 mapped = normalize(vec3(nx.z * sign(n.x), nx.x, nx.y) * weight.x
        + vec3(ny.x, ny.z * sign(n.y), ny.y) * weight.y
        + vec3(nz.y, nz.x, nz.z * sign(n.z)) * weight.z);
    NORMAL = normalize((VIEW_MATRIX * vec4(mapped, 0.0)).xyz);
}
