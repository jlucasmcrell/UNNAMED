shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded;

// Orenth: the figure's shadow, drawn by a shadow-only twin that trails the body by an instant (`offset`, mesh space).
instance uniform vec3 offset = vec3(0.0);

void vertex() {
	VERTEX += offset;
}

void fragment() {
	ALBEDO = vec3(0.0);
}
