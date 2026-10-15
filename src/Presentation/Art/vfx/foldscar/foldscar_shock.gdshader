// The Foldscar coming into register: a shell of displaced air thrown out from a stone or the heart. It draws nothing of its own but
// the world behind it bent at its rim, and a faint cold edge. Driven by the scene's AnimationPlayer (fade).
shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never, shadows_disabled, fog_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float fade = 1.0;
uniform float bend = 0.035;
uniform vec3 edge_tint : source_color = vec3(0.80, 0.85, 1.0);
void fragment() {
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    vec2 push = NORMAL.xy * bend * rim * fade;
    vec3 behind = textureLod(screen_tex, SCREEN_UV - push, 0.0).rgb;
    ALBEDO = behind + edge_tint * rim * rim * rim * 0.15 * fade;
    ALPHA = clamp(rim * 1.6, 0.0, 1.0) * fade;
}
