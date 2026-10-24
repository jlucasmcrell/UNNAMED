shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back;

// An older Orenth's phase scar: one iris (the figure's left) is missing a segment, drawn as sclera.
varying float left_eye;

void vertex() {
	left_eye = step(0.0, VERTEX.x);
}

void fragment() {
	vec2 d = UV - 0.5;
	float r = length(d);
	float a = atan(d.y, d.x);
	float segment = step(0.035, r) * step(r, 0.118) * step(-0.4, a) * step(a, 0.3) * step(0.5, left_eye);
	ALBEDO = vec3(0.86, 0.82, 0.78);
	ROUGHNESS = 0.2;
	ALPHA = segment;
}
