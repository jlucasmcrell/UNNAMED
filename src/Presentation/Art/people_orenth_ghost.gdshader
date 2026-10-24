shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_disabled;

// Orenth: a second registration of the same skinned surface, trailing the body by `offset` (mesh space). The real
// body's depth hides everything but the part that leaves its silhouette, so what shows is a faint trailing outline.
// OrenthPhase drives offset and strength per frame.
uniform sampler2D base_map : source_color, hint_default_white;
uniform vec4 base_colour : source_color = vec4(1.0);
instance uniform vec3 offset = vec3(0.0);
instance uniform float strength = 0.0;

void vertex() {
	VERTEX += offset;
}

void fragment() {
	vec4 base = texture(base_map, UV) * base_colour;
	float rim = 1.0 - abs(dot(NORMAL, VIEW));
	ALBEDO = mix(base.rgb, vec3(0.62, 0.66, 0.72), 0.35);
	ALPHA = strength * base.a * mix(0.35, 1.0, rim * rim);
}
