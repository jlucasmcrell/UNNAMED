// Retain the approved body's painted grey base layer when changing its skin response.
// Separate warm skin from the original grey base layer; geometry is untouched.
uniform sampler2D base_map : source_color, hint_default_white;
uniform vec4 base_colour : source_color = vec4(1.0);
uniform bool preserve_base_layer = false;

float base_layer_mask(vec3 source) {
    bool skin = source.r > source.g * 1.04 && source.r > source.b * 1.08;
    return preserve_base_layer && !skin ? 1.0 : 0.0;
}
