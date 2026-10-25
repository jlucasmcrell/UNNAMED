shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_lambert, specular_disabled;

// Orenth: a spirit echo of this person - a second registration of the same skinned surface in its own clothes, hair and
// skin, paled toward silver (never violet: violet belongs to the Rift). Lit like the scene but with no specular: an unlit
// ghost is pushed by the frontier's exposure to a flat white that hides the clothes. Trailing copies sit `offset` behind (mesh space)
// and smear along it; the body's depth hides all but what leaves its silhouette. Edges and feet dissolve, so it reads as
// a spirit rather than a solid. OrenthPhase and OrenthDecoy drive offset, strength and dissolve per frame.
uniform sampler2D base_map : source_color, hint_default_white;
uniform vec4 base_colour : source_color = vec4(1.0);
instance uniform vec3 offset = vec3(0.0);
instance uniform float strength = 0.0;
instance uniform float dissolve = 0.0;

const vec3 SPIRIT = vec3(0.80, 0.84, 0.90);

varying vec3 rest;

float noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float n = dot(i, vec3(1.0, 57.0, 113.0));
	vec4 h = fract(sin(vec4(n, n + 1.0, n + 57.0, n + 58.0)) * 43758.5453);
	vec4 g = fract(sin(vec4(n + 113.0, n + 114.0, n + 170.0, n + 171.0)) * 43758.5453);
	vec4 m = mix(h, g, f.z);
	return mix(mix(m.x, m.y, f.x), mix(m.z, m.w, f.x), f.y);
}

void vertex() {
	rest = VERTEX;
	// The side trailing the motion stretches further back: a little motion left behind.
	vec3 back = length(offset) > 1e-4 ? normalize(offset) : vec3(0.0);
	VERTEX += offset + back * length(offset) * 0.35 * max(dot(NORMAL, back), 0.0);
}

void fragment() {
	vec4 base = texture(base_map, UV) * base_colour;
	float facing = abs(dot(NORMAL, VIEW));
	float wisp = noise(rest * 22.0 + vec3(0.0, TIME * 0.6, 0.0)) * 0.6 + noise(rest * 61.0 - vec3(0.0, TIME * 1.1, 0.0)) * 0.4;
	// Dissolving rim: the edge thins into noise instead of ending in a hard silhouette.
	float body = smoothstep(0.05, 0.55, facing + (wisp - 0.5) * 0.45);
	float feet = smoothstep(0.05, 0.6, rest.y);
	float alive = smoothstep(dissolve - 0.08, dissolve + 0.08, wisp);
	vec3 pale = mix(base.rgb, vec3(dot(base.rgb, vec3(0.299, 0.587, 0.114))), 0.3);
	ALBEDO = mix(pale * 1.1, SPIRIT, 0.28);
	ROUGHNESS = 1.0;
	EMISSION = SPIRIT * (0.04 + 0.1 * (1.0 - facing)) * strength;
	ALPHA = strength * base.a * mix(0.55, 1.0, body) * body * feet * alive;
}
