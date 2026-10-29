shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, ambient_light_disabled;

// A restrained pearl highlight over the native HumGen material and directional atlas.
uniform sampler2D hair_albedo : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D hair_flow : hint_default_white, filter_linear_mipmap, repeat_disable;
varying vec3 strand;
varying float pearl_angle;

void fragment() {
    if (texture(hair_albedo, UV).a < .45) discard;
    vec2 flow = texture(hair_flow, UV).rg * 2. - 1.;
    strand = normalize(TANGENT * flow.x + BINORMAL * flow.y);
    pearl_angle = clamp(1. - abs(dot(NORMAL, VIEW)), 0., 1.);
    ALBEDO = vec3(0.);
    SPECULAR = 0.;
    ROUGHNESS = .28;
}

void light() {
    vec3 half_vector = normalize(VIEW + LIGHT);
    float along = dot(strand, half_vector);
    float band = pow(max(1. - along * along, 0.), 40.);
    // Pink -> green -> blue. No blue/pink interpolation and no violet endpoint.
    vec3 pearl = pearl_angle < .5
        ? mix(vec3(1., .88, .86), vec3(.87, 1., .91), pearl_angle * 2.)
        : mix(vec3(.87, 1., .91), vec3(.87, .95, 1.), (pearl_angle - .5) * 2.);
    SPECULAR_LIGHT += .02 * band * pearl * LIGHT_COLOR * ATTENUATION;
}
