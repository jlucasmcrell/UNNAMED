// Otherreach's own sky over Sky3D's atmosphere: the bodies, the world's ring, a neighbouring galaxy and the night glows.
// Included by addons/sky_3d/shaders/SkyMaterial.gdshader and driven by Greybox/CelestialView.cs. Everything here is drawn in the
// sky pass only (never into the radiance cubemap, so the scene's lighting is unchanged), behind terrain and clouds.
// Imagery: NASA/JPL/Space Science Institute (Cassini) and ESA/Hubble - see Environment/sky/SOURCES.md.

group_uniforms otherreach;
uniform vec3 or_sun = vec3(0.0, 1.0, 0.0);      // toward the sun
uniform float or_daylight = 1.0;                 // SkyView's 0 (night) .. 1 (day)
uniform float or_fog_comp = 1.0;                 // 1 / (1 - fog_sky_affect): the sky's fog would otherwise wash the bodies out
uniform float or_gain_day = 1.0;
uniform float or_gain_night = 0.35;
uniform float or_haze = 1.0;                     // extinction toward the horizon

uniform bool or_giant_on = false;
uniform vec3 or_giant_dir = vec3(0.0, 0.3, 1.0);
uniform float or_giant_r = 0.2;                  // sine of the angular radius
uniform vec3 or_giant_axis = vec3(0.0, 1.0, 0.0);
uniform vec3 or_giant_ref = vec3(1.0, 0.0, 0.0);
uniform float or_giant_spin = 0.0;
uniform vec3 or_giant_tint : source_color = vec3(1.0);
uniform sampler2D or_giant_map : source_color, filter_linear_mipmap, repeat_enable;
uniform vec2 or_ring_span = vec2(1.24, 2.27);    // ring inner and outer radius in giant radii (0 = no ring)
uniform sampler2D or_ring_profile : filter_linear_mipmap, repeat_disable;   // linear float (EXR), 4096 samples

uniform bool or_moon1_on = false;
uniform vec3 or_moon1_dir = vec3(0.0, 0.5, -1.0);
uniform float or_moon1_r = 0.03;
uniform vec3 or_moon1_tint : source_color = vec3(1.0);
uniform sampler2D or_moon1_map : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool or_moon2_on = false;
uniform vec3 or_moon2_dir = vec3(0.5, 0.5, -1.0);
uniform float or_moon2_r = 0.02;
uniform vec3 or_moon2_tint : source_color = vec3(1.0);
uniform sampler2D or_moon2_map : hint_default_white, filter_linear_mipmap, repeat_enable;

uniform bool or_star_on = false;
uniform vec3 or_star_dir = vec3(-1.0, 0.15, 0.0);
uniform vec3 or_star_color : source_color = vec3(1.0);
uniform float or_star_size = 0.004;              // sine of the radius of the core
uniform float or_star_power = 4.0;

uniform bool or_arc_on = false;                  // the world's own ring, seen from its surface
uniform vec3 or_arc_axis = vec3(0.0, 0.8, 0.6);  // the world's spin axis in local (east, up, north) space
uniform float or_arc_world = 8.0;                // the world's radius in the units below (1 = the observer's height above nothing)
uniform vec2 or_arc_span = vec2(1.6, 2.6);       // in world radii

uniform bool or_galaxy_on = false;
uniform vec3 or_galaxy_dir = vec3(0.0, 0.7, 0.7);
uniform vec3 or_galaxy_u = vec3(1.0, 0.0, 0.0);  // tangent axes, already divided by the half size
uniform vec3 or_galaxy_v = vec3(0.0, 1.0, 0.0);
uniform float or_galaxy_power = 1.0;
uniform sampler2D or_galaxy_map : source_color, filter_linear_mipmap, repeat_disable;

uniform float or_space_boost = 0.0;              // a darker, richer Milky Way (dark-site grading)
uniform float or_airglow = 0.0;
uniform float or_aurora = 0.0;
uniform vec3 or_aurora_dir = vec3(0.0, 0.0, 1.0);
uniform mat3 or_star_frame = mat3(1.0);           // turns the star map so the night is not Earth's

// Extinction through the air along a direction: thicker and redder toward the horizon.
vec3 or_extinction(vec3 d) {
	float airmass = 1.0 / (max(d.y, 0.0) + 0.035);
	return exp(-airmass * vec3(0.022, 0.040, 0.085) * or_haze) * smoothstep(-0.01, 0.015, d.y);
}

// A sphere seen at unit distance along c with sine-radius r: its coverage (anti-aliased), the surface normal and the hit distance.
float or_sphere(vec3 d, vec3 c, float r, out vec3 n, out float t) {
	float b = dot(d, c);
	float s = sqrt(max(1.0 - b * b, 0.0));
	n = vec3(0.0, 0.0, 1.0); t = 1e9;
	if (b <= 0.0 || s >= r * 1.02) return 0.0;
	float h = sqrt(max(r * r - s * s, 0.0));
	t = b - h;
	n = (d * t - c) / r;
	float px = max(fwidth(s), 1e-5);
	return clamp((r - s) / px + 0.5, 0.0, 1.0);
}

vec2 or_latlon(vec3 n, vec3 axis, vec3 ref, float spin) {
	vec3 e = normalize(ref - axis * dot(ref, axis));
	vec3 f = cross(axis, e);
	float lon = atan(dot(n, f), dot(n, e)) + spin;
	float lat = asin(clamp(dot(n, axis), -1.0, 1.0));
	return vec2(fract(lon * INV_TAU + 0.5), 0.5 - lat * INV_PI);
}

// A rocky or icy moon: Lommel-Seeliger, so it stays bright to the limb as airless bodies do.
vec3 or_moon(vec3 d, vec3 c, float r, sampler2D map, vec3 tint, out float a) {
	vec3 n; float t;
	a = or_sphere(d, c, r, n, t);
	if (a <= 0.0) return vec3(0.0);
	vec3 axis = normalize(vec3(0.15, 1.0, 0.05));
	float albedo = texture(map, or_latlon(n, axis, -c, 0.0)).r;
	float mu0 = dot(n, or_sun);
	float mu = max(dot(n, -d), 0.05);
	float lit = mu0 > 0.0 ? 2.0 * mu0 / (mu0 + mu) : 0.0;
	lit *= smoothstep(-0.02, 0.06, mu0);
	return tint * (0.12 + 0.9 * albedo) * lit + tint * 0.004;   // a faint night side (light from the giant)
}

// The ring profile at radius rr, filtered to the pixel's radial footprint fw so the fine Cassini structure averages instead of
// aliasing into stepped stripes, with the inner and outer edges anti-aliased. fw is fwidth(rr), taken by the caller before any
// branch that can differ between neighbouring pixels.
vec4 or_ring_sample(float rr, float fw, vec2 span) {
	float width = span.y - span.x;
	float lod = log2(max(fw / width * 4096.0, 1.0));
	vec4 p = textureLod(or_ring_profile, vec2(clamp((rr - span.x) / width, 0.0, 1.0), 0.5), lod);
	p.a *= clamp((rr - span.x) / fw + 0.5, 0.0, 1.0) * clamp((span.y - rr) / fw + 0.5, 0.0, 1.0);
	return p;
}

float or_ring_alpha(float rr) {
	if (or_ring_span.y <= 0.0 || rr < or_ring_span.x || rr > or_ring_span.y) return 0.0;
	return textureLod(or_ring_profile, vec2((rr - or_ring_span.x) / (or_ring_span.y - or_ring_span.x), 0.5), 0.0).a;
}

// The shadow a sphere (centre c, radius r) casts on point h, soft over a thin penumbra so its edge does not stair-step.
float or_sphere_shadow(vec3 h, vec3 c, float r, float floor_level) {
	vec3 oc = h - c;
	float b = dot(oc, or_sun), k = b * b - (dot(oc, oc) - r * r);
	return mix(1.0, floor_level, smoothstep(-0.01 * r * r, 0.01 * r * r, k) * step(b, 0.0));
}

// The giant and its ring. Returns premultiplied radiance; a = how much of the sky behind it is hidden.
vec3 or_giant(vec3 d, out float a) {
	a = 0.0;
	vec3 c = or_giant_dir; float r = or_giant_r; vec3 ax = or_giant_axis;
	vec3 n; float tp;
	float disc = or_sphere(d, c, r, n, tp);
	vec3 col = vec3(0.0);
	if (disc > 0.0) {
		vec3 base = texture(or_giant_map, or_latlon(n, ax, or_giant_ref, or_giant_spin)).rgb * or_giant_tint;
		float mu0 = dot(n, or_sun);
		float mu = max(dot(n, -d), 0.02);
		// Minnaert limb darkening and a soft terminator through a deep atmosphere.
		float lit = pow(max(mu0, 0.0), 0.92) * pow(mu, -0.12) * smoothstep(-0.06, 0.12, mu0);
		// The ring's shadow on the cloud tops.
		float dn = dot(or_sun, ax);
		if (abs(dn) > 1e-3) {
			vec3 p = c + n * r;
			float ts = dot(c - p, ax) / dn;
			if (ts > 0.0) lit *= 1.0 - 0.85 * or_ring_alpha(length(p + or_sun * ts - c) / r);
		}
		vec3 limb = vec3(0.55, 0.6, 0.72) * pow(1.0 - mu, 4.0) * smoothstep(-0.1, 0.3, mu0) * 0.25;
		col = (base * lit + limb + base * 0.015) * disc;
		a = disc;
	}
	if (or_ring_span.y > 0.0) {
		float dd = dot(d, ax);
		float tr = dot(c, ax) / (abs(dd) > 1e-4 ? dd : 1e-4);
		vec3 h = d * tr;
		float rr = length(h - c) / r;
		float fw = max(fwidth(rr), 1e-5);
		// A footprint wider than the whole ring is where the ray runs along its plane (edge-on, sub-pixel): nothing to draw there.
		if (abs(dd) > 1e-4 && tr > 0.0 && fw < or_ring_span.y - or_ring_span.x && rr > or_ring_span.x - fw && rr < or_ring_span.y + fw) {
			vec4 prof = or_ring_sample(rr, fw, or_ring_span);
			float ra = prof.a;
			if (ra > 0.0 && (disc <= 0.0 || tr < tp)) {
				vec3 rc = prof.rgb / 0.6;
				// Lit face toward us: reflected light; otherwise only what scatters through.
				float sunSide = dot(or_sun, ax), eyeSide = dot(-d, ax);
				float face = sunSide * eyeSide > 0.0 ? abs(sunSide) * 1.2 + 0.08 : abs(sunSide) * (1.0 - ra) * 1.4;
				// The giant's shadow across the ring.
				float shadow = or_sphere_shadow(h, c, r, 0.03);
				vec3 ring = rc * face * shadow * 0.9;
				col = col * (1.0 - ra) + ring * ra;
				a = a + ra * (1.0 - a);
			}
		}
	}
	return col;
}

// The world's own ring, an arc from horizon to horizon: a plane through the world's centre, which lies or_arc_world below.
vec3 or_arc(vec3 d, out float a) {
	a = 0.0;
	vec3 centre = vec3(0.0, -or_arc_world, 0.0);
	float dd = dot(d, or_arc_axis);
	float t = dot(centre, or_arc_axis) / (abs(dd) > 1e-5 ? dd : 1e-5);
	vec3 h = d * t;
	float rr = length(h - centre) / or_arc_world;
	float fw = max(fwidth(rr), 1e-5);
	// A footprint wider than the whole ring is where the ray runs along its plane (edge-on, sub-pixel): nothing to draw there.
	if (d.y <= 0.0 || abs(dd) < 1e-5 || t <= 0.0 || fw > or_arc_span.y - or_arc_span.x || rr < or_arc_span.x - fw || rr > or_arc_span.y + fw) return vec3(0.0);
	vec4 p = or_ring_sample(rr, fw, or_arc_span);
	a = p.a * 0.92;
	float sunSide = dot(or_sun, or_arc_axis), eyeSide = dot(-d, or_arc_axis);
	// Backlit (sun beyond the far face) the thin rings glow by forward scattering, brightest looking toward the sun.
	float forward = 0.7 + 3.0 * pow(max(dot(d, or_sun), 0.0), 3.0);
	float face = sunSide * eyeSide > 0.0 ? abs(sunSide) * 1.1 + 0.06 : (abs(sunSide) + 0.05) * (1.0 - p.a) * (0.35 + p.a * 2.0) * forward;
	// The world's shadow falls across the ring at night: is the sun hidden behind the world from this point?
	float shadow = or_sphere_shadow(h, centre, or_arc_world, 0.02);
	return p.rgb / 0.6 * face * shadow * 0.85;
}

vec3 or_star(vec3 d, out float a) {
	float cosang = dot(d, or_star_dir);
	float s = sqrt(max(1.0 - cosang * cosang, 0.0)) / or_star_size;
	a = cosang > 0.0 ? clamp(1.5 - s, 0.0, 1.0) : 0.0;
	if (cosang <= 0.0) return vec3(0.0);
	float core = 1.0 / (1.0 + s * s * s * s);
	float glare = exp(-s * 0.35) * 0.08;
	return or_star_color * or_star_power * (core + glare);
}

vec3 or_galaxy(vec3 d) {
	float b = dot(d, or_galaxy_dir);
	if (b <= 0.2) return vec3(0.0);
	vec3 p = d / b - or_galaxy_dir;
	vec2 uv = vec2(dot(p, or_galaxy_u), dot(p, or_galaxy_v)) * 0.5 + 0.5;
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return vec3(0.0);
	vec3 g = texture(or_galaxy_map, uv).rgb;
	return pow(g, vec3(1.2)) * or_galaxy_power;
}

// Triangle-wave noise (after nimitz's auroras), cheap and smooth enough for curtains.
float or_tri(float x) { return clamp(abs(fract(x) - 0.5), 0.01, 0.49); }
vec2 or_tri2(vec2 p) { return vec2(or_tri(p.x) + or_tri(p.y), or_tri(p.y + or_tri(p.x))); }
float or_trinoise(vec2 p, float spd, float time) {
	float z = 1.8, z2 = 2.5, rz = 0.0;
	mat2 m2 = mat2(vec2(0.95534, 0.29552), vec2(-0.29552, 0.95534));
	p = m2 * p * 0.71;   // rotate once
	for (int i = 0; i < 4; i++) {
		vec2 dg = or_tri2(p * 1.85) * 0.75;
		dg = mat2(vec2(cos(time * spd), sin(time * spd)), vec2(-sin(time * spd), cos(time * spd))) * dg;
		p -= dg / z2;
		z2 *= 0.45; z *= 0.42;
		p *= 1.21 + (rz - 1.0) * 0.02;
		rz += or_tri(p.x + or_tri(p.y)) * z;
		p = m2 * -p;
	}
	return clamp(1.0 / pow(rz * 29.0, 1.3), 0.0, 0.55);
}

vec3 or_aurora_glow(vec3 d, float time) {
	// Toward the aurora's side of the sky only, and fading up to the zenith.
	float side = dot(normalize(vec3(d.x, 0.0, d.z)), or_aurora_dir);
	if (d.y <= 0.0 || side < -0.2) return vec3(0.0);
	vec3 acc = vec3(0.0);
	for (int i = 0; i < 18; i++) {
		float of = 0.006 * fract(sin(float(i) * 12.9898) * 43758.5453);
		float pt = ((0.8 + pow(float(i), 1.4) * 0.002)) / (d.y * 2.0 + 0.4);
		pt -= of;
		vec2 bpos = d.xz * pt;
		float rzt = or_trinoise(bpos * 1.1, 0.06, time);
		vec3 c2 = mix(vec3(0.15, 1.0, 0.45), vec3(0.85, 0.25, 0.55), clamp(float(i) / 18.0 * 1.4 - 0.25, 0.0, 1.0));
		acc += c2 * rzt * exp2(-float(i) * 0.065 - 2.5);
	}
	return acc * smoothstep(0.15, 0.8, side) * smoothstep(0.0, 0.06, d.y) * (1.0 - smoothstep(0.25, 0.6, d.y));
}

// Composes everything over the sky Sky3D has drawn so far (its atmosphere, sun, moon and stars).
// space: the star map's share of that colour (so a body hides the stars behind it); moon_mask: Sky3D's moon, which passes in front.
vec3 otherreach_sky(vec3 col, vec3 d, vec3 space, float moon_mask, float horizon_blend, bool cubemap, float time) {
	if (cubemap) return col;
	float night = 1.0 - or_daylight;
	vec3 ext = or_extinction(d);
	float gain = mix(or_gain_night, or_gain_day, or_daylight) * or_fog_comp;
	vec3 add = vec3(0.0);
	float hide = 0.0;
	float front = 1.0 - moon_mask;

	if (or_space_boost > 0.0)
		add += (space * or_space_boost + space * space * or_space_boost * 6.0) * night * front;
	if (or_galaxy_on)
		add += or_galaxy(d) * night * horizon_blend * ext * or_fog_comp * front;
	if (or_star_on) {
		float sa; vec3 s = or_star(d, sa);
		add += s * ext * or_fog_comp * front * (0.35 + 0.65 * night);
	}
	if (or_arc_on) {
		float aa; vec3 arc = or_arc(d, aa);
		add += arc * ext * gain * front * (1.0 - hide);
		hide = max(hide, aa * front);
	}
	if (or_giant_on) {
		float ga; vec3 g = or_giant(d, ga);
		add = add * (1.0 - ga * front) + g * ext * gain * front;
		hide = max(hide, ga * front);
	}
	if (or_moon1_on) {
		float ma; vec3 m = or_moon(d, or_moon1_dir, or_moon1_r, or_moon1_map, or_moon1_tint, ma);
		add = mix(add, m * ext * gain, ma * front);
		hide = max(hide, ma * front);
	}
	if (or_moon2_on) {
		float ma; vec3 m = or_moon(d, or_moon2_dir, or_moon2_r, or_moon2_map, or_moon2_tint, ma);
		add = mix(add, m * ext * gain, ma * front);
		hide = max(hide, ma * front);
	}
	if (or_airglow > 0.0)
		add += vec3(0.18, 0.55, 0.22) * or_airglow * night * exp(-pow((d.y - 0.10) / 0.09, 2.0)) * smoothstep(-0.01, 0.03, d.y) * or_fog_comp;
	if (or_aurora > 0.0 && night > 0.6)
		add += or_aurora_glow(d, time) * or_aurora * smoothstep(0.6, 1.0, night) * or_fog_comp;
	return col - space * hide + add;
}
