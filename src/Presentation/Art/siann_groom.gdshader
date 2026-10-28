shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// A Siann's native hair cards, brows and lashes: a single colour over the preset's own strand shapes. The atlas is baked
// dark, so the colour replaces it and only its grain and alpha are kept. The cards are masked exactly as the native
// export masks them (the glb's alphaMode MASK); a hashed alpha turned every strand into a field of stray dots.
uniform sampler2D strand_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform vec3 strand_colour : source_color = vec3(.6, .59, .56);
uniform vec2 grain_range = vec2(.8, 1.);
uniform float strand_roughness = .6;
// White strands on a dark atlas have no modelling left, and the cards shadow each other, so the thinnest ones read as
// pencil lines. A little self-light and light bleeding through the card keeps a white head hair rather than ink.
uniform float self_light : hint_range(0., .5) = .2;
uniform float bleed : hint_range(0., .8) = .35;

void fragment() {
    vec4 strand = texture(strand_atlas, UV);
    float grain = sqrt(clamp(dot(strand.rgb, vec3(.299, .587, .114)), 0., 1.));
    vec3 dye = strand_colour * mix(grain_range.x, grain_range.y, grain);
    ALBEDO = dye;
    EMISSION = dye * self_light;
    BACKLIGHT = dye * bleed;
    ALPHA = clamp(strand.a * 1.15, 0., 1.);
    ALPHA_SCISSOR_THRESHOLD = .38;
    ROUGHNESS = strand_roughness;
    SPECULAR = .3;
}
