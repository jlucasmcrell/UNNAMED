// UNNAMED Presentation - "plausible but wrong" wildlife (DIRECTION_AND_CONTENT_PLAN.md D.1): the WildMesh coat as scanned, with one or
// two of its properties moved - a hue, a band, a lichen mottle, a sheen, a few marks that glow after dark. OtherFauna.cs sets it up.
// Patterns are laid in the body's own rest pose (CUSTOM0, metres; feet on y = 0, head toward +z), so they ride the animal as it moves.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

global uniform float otherfauna_night;
instance uniform float variant = 0.5;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 albedo_mul : source_color = vec4(1.0);
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float normal_scale = 1.0;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float roughness_mul = 1.0;
uniform float ao_light_affect = 0.0;
uniform float alpha_scissor = 0.0;
// The rest-pose box the body fills (metres), so regions are written as fractions of it.
uniform vec3 body_min = vec3(-0.5, 0.0, -0.5);
uniform vec3 body_size = vec3(1.0);

// The whole coat: a turn of hue (degrees, each animal a little off by hue_jitter), saturation and value.
uniform float hue_shift = 0.0;
uniform float hue_jitter = 0.0;
uniform float saturation = 1.0;
uniform float value = 1.0;
// One hue band moved somewhere else (a red comb to violet): centre and half-width in degrees.
uniform float key_hue = 0.0;
uniform float key_width = 0.0;
uniform float key_shift = 0.0;
uniform float key_saturation = 1.0;
uniform float key_min_saturation = 0.15;
// Bands across the body's length, broken up, from a height fraction upward (the belly stays plain).
uniform vec3 band_tint = vec3(1.0);
uniform float band_strength = 0.0;
uniform float band_spacing = 0.1;
uniform float band_width = 0.35;
uniform float band_wobble = 0.3;
uniform float band_from = 0.0;
// Lichen-like patches.
uniform vec3 mottle_tint = vec3(1.0);
uniform float mottle_strength = 0.0;
uniform float mottle_scale = 0.12;
uniform float mottle_cover = 0.4;
uniform float mottle_from = 0.0;
// A sheen on the dark parts of the coat that turns between two colours with the view.
uniform vec3 sheen_a = vec3(0.1, 0.4, 0.35);
uniform vec3 sheen_b = vec3(0.35, 0.15, 0.5);
uniform float sheen_strength = 0.0;
// Marks that glow at night: round spots on a grid seen from above, inside a region (fractions of the body box), or the whole region
// when glow_size reaches glow_spacing.
uniform vec3 glow_colour = vec3(0.4, 1.0, 0.8);
uniform float glow_strength = 0.0;
uniform float glow_spacing = 0.06;
uniform float glow_size = 0.012;
uniform vec3 glow_from = vec3(0.0);
uniform vec3 glow_to = vec3(1.0);

varying vec3 rest;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

vec3 rgb_to_hsv(vec3 c) {
	vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
	vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
	vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
	float d = q.x - min(q.w, q.y);
	return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-10)), d / (q.x + 1e-10), q.x);
}

vec3 hsv_to_rgb(vec3 c) {
	vec3 p = abs(fract(c.xxx + vec3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
	return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

void vertex() {
	rest = CUSTOM0.xyz;
}

void fragment() {
	vec4 tex = texture(albedo_tex, UV) * albedo_mul;
	vec3 hsv = rgb_to_hsv(tex.rgb);
	float h = hsv.x * 360.0;
	float key = key_width > 0.0 ? 1.0 - smoothstep(key_width * 0.6, key_width, abs(mod(h - key_hue + 180.0, 360.0) - 180.0)) : 0.0;
	key *= smoothstep(key_min_saturation, key_min_saturation + 0.15, hsv.y);
	hsv.x = fract((h + hue_shift + (variant - 0.5) * hue_jitter + key * key_shift) / 360.0 + 1.0);
	hsv.y = clamp(hsv.y * mix(saturation, key_saturation, key), 0.0, 1.0);
	hsv.z *= value;
	vec3 coat = hsv_to_rgb(hsv);
	vec3 at = (rest - body_min) / body_size;
	if (band_strength > 0.0) {
		float wob = (noise(rest * 9.0 + variant * 31.0) - 0.5) * band_wobble;
		float s = fract(rest.z / band_spacing + wob + variant);
		float band = 1.0 - smoothstep(band_width * 0.7, band_width, abs(s - 0.5) * 2.0);
		band *= smoothstep(band_from, band_from + 0.12, at.y) * (1.0 - key);
		coat = mix(coat, coat * band_tint, band * band_strength);
	}
	if (mottle_strength > 0.0) {
		float n = noise(rest / mottle_scale + variant * 17.0) * 0.65 + noise(rest / (mottle_scale * 0.35)) * 0.35;
		float patch = smoothstep(1.0 - mottle_cover, 1.0 - mottle_cover + 0.08, n) * smoothstep(mottle_from, mottle_from + 0.15, at.y);
		coat = mix(coat, mottle_tint * dot(coat, vec3(0.3, 0.59, 0.11)) * 1.6, patch * mottle_strength * (1.0 - key));
	}
	if (sheen_strength > 0.0) {
		float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
		float dark = 1.0 - smoothstep(0.08, 0.3, hsv.z);
		coat = mix(coat, mix(sheen_a, sheen_b, facing), sheen_strength * dark * (1.0 - facing * 0.5));
	}
	ALBEDO = coat;
	if (glow_strength > 0.0 && otherfauna_night > 0.01) {
		vec3 inside = step(glow_from, at) * step(at, glow_to);
		vec3 cell = floor(rest / glow_spacing);
		vec3 centre = (cell + 0.25 + 0.5 * vec3(hash(cell), hash(cell + 7.1), hash(cell + 3.3))) * glow_spacing;
		// Measured across the ground plane only, so a spot shows wherever the coat crosses its column.
		float spot = 1.0 - smoothstep(glow_size * 0.5, glow_size, length(rest.xz - centre.xz));
		EMISSION = glow_colour * glow_strength * otherfauna_night * spot * inside.x * inside.y * inside.z;
	}
	NORMAL_MAP = texture(normal_tex, UV).rgb;
	NORMAL_MAP_DEPTH = normal_scale;
	vec4 orm = texture(orm_tex, UV);
	ROUGHNESS = clamp(orm.g * roughness_mul, 0.0, 1.0);
	AO = orm.r;
	AO_LIGHT_AFFECT = ao_light_affect;
	ALPHA = alpha_scissor > 0.0 ? tex.a : 1.0;
	ALPHA_SCISSOR_THRESHOLD = alpha_scissor;
}
