// A column of pale light standing up out of a stone or the heart as it comes into register, rippling upward, then gone.
// Driven by the scene's AnimationPlayer (intensity).
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
uniform float intensity = 0.0;
uniform vec3 tint : source_color = vec3(0.72, 0.78, 1.0);
void fragment() {
    float y = 1.0 - UV.y;
    float facing = pow(clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 1.5);
    float along = smoothstep(0.0, 0.04, y) * pow(1.0 - y, 1.8);
    float ripple = 0.6 + 0.25 * sin(y * 23.0 - TIME * 9.0 + sin(UV.x * 37.7) * 1.5) + 0.15 * sin(y * 61.0 - TIME * 15.0 + UV.x * 18.85);
    ALBEDO = tint * intensity * facing * along * ripple;
}
