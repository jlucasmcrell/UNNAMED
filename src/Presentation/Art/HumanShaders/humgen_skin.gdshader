/*
MIT License
Copyright (c) 2025 MatMADNESS

Permission is hereby granted, free of charge, to use, copy, modify, and
distribute this software as permitted under the MIT License.

This Shader is part of the Human Shaders collection by MatMADNESS
and is built upon https://github.com/RustyRoboticsBV/GodotStandardLightShader
*/

shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx, sss_mode_skin;

#include "res://Art/region_mask.gdshaderinc"

group_uniforms albedo;
uniform vec4 albedo : source_color = vec4(1.);
uniform sampler2D texture_albedo : source_color, filter_linear_mipmap, repeat_enable;

group_uniforms roughness;
uniform float roughness : hint_range(0.0, 1.0) = 0.4;
uniform float specular : hint_range(0.0, 1.0, 0.01) = 0.5;
uniform bool double_specularity = true;
// HumGen's exported roughness/normal already contain its authored detail.
uniform bool native_humgen = false;
uniform bool native_specular_map = false;
uniform sampler2D native_rough_spec_cavity : filter_linear_mipmap;
uniform vec2 native_specular_range = vec2(0.073, 0.7);
uniform vec3 native_sss_radius = vec3(1., .2, .1);
uniform float native_sss_scale = .01;
varying float native_specular;

uniform sampler2D texture_roughness : hint_roughness_r, filter_linear_mipmap, repeat_enable;

group_uniforms metallic;
uniform float metallic : hint_range(0.0, 1.0, 0.01);
uniform sampler2D texture_metallic : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 metallic_texture_channel;

group_uniforms normal;
uniform sampler2D texture_normal : hint_roughness_normal, filter_linear_mipmap, repeat_enable;
uniform float normal_strength : hint_range(-16.0, 16.0) = 1.0;

group_uniforms micro_detail;
uniform bool use_micro_detail = false;
uniform float micro_detail_scale : hint_range(0.0, 200.0) = 25.0;
uniform float micro_normal_strength : hint_range(-1.0, 1.0) = 0.3;
uniform float micro_ao_strength : hint_range(-1.0, 1.0) = 0.4;
uniform sampler2D texture_micro_detail : hint_roughness_normal, filter_linear_mipmap_anisotropic, repeat_enable;
//uniform float soften_normal : hint_range(0.0, 1.0);

group_uniforms ambient_occlusion;
uniform bool use_ambient_occlusion = false;
uniform sampler2D ambient_occlusion_texture : hint_roughness_normal, filter_linear_mipmap, repeat_enable;
uniform float ao_strength : hint_range(0.0, 2.0) = 0.5;
uniform float ao_block_light : hint_range(0.0, 2.0) = 0.5;

group_uniforms subsurface_scattering;
uniform bool use_subsurface_scattering = true;
uniform bool use_noise = true;
uniform float subsurface_scattering_strength : hint_range(0.0, 1.0, 0.01) = 0.4;
uniform float skin_smoothness : hint_range(0.0, 10.0) = 5.0;
uniform float skin_fallof_smoothness : hint_range(0.7, 2.0, 0.01) = 1.0;
uniform bool tinted_shadow_penumbra = false;
uniform bool old_lightwarp_fallof = false;
uniform sampler2D lightwarp : source_color, filter_linear, repeat_disable;
uniform sampler2D texture_sss_noise : filter_linear, repeat_enable, source_color;


group_uniforms translucency;
uniform bool translucency = false;
uniform float translucency_strength : hint_range(0.0, 2.0, 0.01) = 0.75;
uniform sampler2D texture_translucency : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D texture_translucency_color : source_color, filter_linear, repeat_disable;

// Kal calcification (Otherreach addition): a scanned stone set baked into this body's own UV space by triplanar
// projection (rgb colour, a height; stone_rm r roughness, g the joint/age mask, b bare skin). All bare skin carries the
// stone's crack network and relief in the person's own hue (stone_base); toward the joints, as far as coverage reaches,
// it thickens into the stone itself: its colour, matte roughness and full relief. No subsurface where it is stone.
group_uniforms kal_stone;
uniform bool use_stone = false;
uniform sampler2D stone_rgbh : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D stone_rm : hint_default_black, filter_linear_mipmap, repeat_disable;
uniform float stone_coverage : hint_range(0.0, 1.0) = 0.55;
uniform float stone_breakup : hint_range(0.0, 1.5) = 0.9;
uniform float stone_soft : hint_range(0.005, 0.5) = 0.035;
uniform float stone_base : hint_range(0.0, 1.0) = 0.45;
uniform float stone_relief : hint_range(0.0, 40.0) = 14.0;
uniform float stone_base_relief : hint_range(0.0, 40.0) = 7.0;
uniform vec2 stone_crack = vec2(0.16, 0.3);
uniform float stone_crack_dark : hint_range(0.0, 1.0) = 0.55;
uniform float stone_cavity : hint_range(0.0, 1.0) = 0.4;
uniform float stone_skin_blend : hint_range(0.0, 1.0) = 0.35;
uniform float stone_match : hint_range(0.0, 1.0) = 0.6;
// the darkest the matched stone may go: a dark base body's slate stays dark grey, so the face and the eyes in it still read
uniform float stone_floor : hint_range(0.0, 0.6) = 0.26;
uniform float stone_glint : hint_range(0.0, 1.0) = 0.6;
uniform float stone_normal_y : hint_range(-1.0, 1.0) = 1.0;
uniform vec3 stone_tint : source_color = vec3(1.0);
// how much of the tint's hue the whole stone takes, beyond the skin tone it already scales
uniform float stone_tint_hue : hint_range(0.0, 2.0) = 1.0;

// Siann: white mineral base and restrained grazing pink/green/blue pearl. No violet.
group_uniforms siann_opal;
uniform bool use_opal = false;
uniform sampler2D opal_mask : hint_default_black, filter_linear_mipmap, repeat_disable;
uniform float opal_pale : hint_range(0.0, 1.0) = 0.6;
uniform float opal_sheen : hint_range(0.0, 1.0) = 0.85;
uniform float opal_traces : hint_range(0.0, 1.0) = 0.4;
uniform vec3 opal_pearl : source_color = vec3(0.84, 0.85, 0.86);

group_uniforms u_v;
uniform vec3 uv1_scale = vec3(1.);
uniform vec3 uv1_offset = vec3(0.);
uniform vec3 uv2_scale = vec3(1.);
uniform vec3 uv2_offset = vec3(0.);

varying mat3 TBN;
varying vec4 micro_detail;
varying vec3 final_albedo;
varying float final_metallic;
varying float ao;
varying float linear_depth;
varying float smooth_depth;
varying float sss_tex;
varying float stone;
varying float plain;

void vertex() {
	region_mask(VERTEX, UV2);  // before UV2 becomes the detail layer's coordinates
	UV = UV * uv1_scale.xy + uv1_offset.xy;
	// Detail UV Layer: UV2
	UV2 = UV2 * uv2_scale.xy + uv2_offset.xy;
}

vec3 normal_blending(vec3 b, vec3 d) {
	return vec3(b.xy + d.xy - 0.5, b.z);
}

vec3 overlay(vec3 base, vec3 blend){
	vec3 limit = step(0.5, base);
	return mix(2.0 * base * blend, 1.0 - 2.0 * (1.0 - base) * (1.0 - blend), limit);
}

vec3 custom_normal(vec3 normal_map){
	normal_map = mix(vec3(0.5,0.5,1.0), normal_map, normal_strength);
	vec3 normal_tangent = vec3(
		normal_map.r * 2.0 - 1.0,  // X-axis (unchanged)
		(1.0 - normal_map.g) * 2.0 - 1.0,  // Y-axis (inverted)
		1   // Z-axis (solid blue)
		);
	return normalize(TBN * normal_tangent);
}

float opal_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = fract(sin(dot(i, vec2(127.1, 311.7))) * 43758.5453);
	float b = fract(sin(dot(i + vec2(1.0, 0.0), vec2(127.1, 311.7))) * 43758.5453);
	float c = fract(sin(dot(i + vec2(0.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
	float d = fract(sin(dot(i + vec2(1.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float ScreenSpaceDither12(vec2 vScreenPos, float time){
	float vDither = dot( vec2( 171.0, 231.0 ), vScreenPos.xy + time);
	return fract(vDither / 53.2);
}
void fragment() {
	vec2 base_uv = UV;
	TBN = mat3(TANGENT, -BINORMAL, NORMAL);

	//Raw depth value:
	float depth_raw = FRAGCOORD.z;

	//Convert to linear:
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth_raw);
	vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	view.xyz /= view.w;

	// reverse
	linear_depth = clamp(1. + (view.z / 1.0), 0., 1.);
	smooth_depth = smoothstep(0.0, 0.6, linear_depth);

	sss_tex = texture(texture_translucency, base_uv).b;

	//float fresnel = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0 );

	micro_detail = texture(texture_micro_detail, base_uv * micro_detail_scale);
	ao = texture(texture_normal, base_uv).b;
	ao *= texture(ambient_occlusion_texture, base_uv).r;

	float micro_overlay = mix(0.5, micro_detail.b, micro_ao_strength * 2.);
	vec4 albedo_tex = texture(texture_albedo, base_uv);
	if (use_micro_detail){
		albedo_tex = vec4(overlay(albedo_tex.rgb, vec3(micro_overlay)), albedo_tex.w);
	}

	final_albedo = albedo.rgb * albedo_tex.rgb;

	float metallic_tex = dot(texture(texture_metallic, base_uv), metallic_texture_channel);
	final_metallic = metallic_tex * metallic;
	ALBEDO = final_albedo * (1.0 - final_metallic);


	//METALLIC = metallic_tex * metallic;

	//float spec = max(dot(NORMAL, LIGHT_VERTEX * LIGHT_VERTEX), 0.0);

	//ALBEDO = vec3(1.);
	SPECULAR = clamp(specular, 0.,1.);
	native_specular = SPECULAR;
	if (native_specular_map) {
		native_specular = clamp((texture(native_rough_spec_cavity, base_uv).r - native_specular_range.x)
			/ (native_specular_range.y - native_specular_range.x), 0., 1.);
		SPECULAR = native_specular;
	}
	if (use_ambient_occlusion){
		SPECULAR *= ao;
	}

	vec4 roughness_texture_channel = vec4(0.0, 1.0, 0.0, 0.0);
	float roughness_tex = dot(texture(texture_roughness, base_uv), roughness_texture_channel);
	ROUGHNESS = clamp(roughness_tex * roughness, 0.,1.);

	// Normal Map: Enabled
	vec3 micro_normal = mix(vec3(.5,.5,1.), vec3(micro_detail.xy, 1.), micro_normal_strength);

	//Normal Map (For Specular):
	vec3 normal_final = texture(texture_normal, base_uv).rgb;
	normal_final = normal_blending(normal_final, micro_normal);
	NORMAL_MAP = normal_final;
	NORMAL_MAP_DEPTH = normal_strength;

	stone = 0.0;
	if (use_stone){
		vec4 rgbh = texture(stone_rgbh, base_uv);
		vec3 rmb = texture(stone_rm, base_uv).rgb;
		vec2 px = 1.5 / vec2(textureSize(stone_rgbh, 0));
		float t = 1.0 - stone_coverage;
		// breakup grows with the mask, so the faint zones (cheek, brow) appear only at an older coverage
		float joint = smoothstep(t - stone_soft, t + stone_soft, rmb.g + (rgbh.a - 0.5) * stone_breakup * smoothstep(0.25, 0.7, rmb.g)) * step(0.02, rmb.g);
		stone = rmb.b * mix(stone_base, 1.0, joint);
		float crack = 1.0 - smoothstep(stone_crack.x, stone_crack.y, rgbh.a);
		float hx = texture(stone_rgbh, base_uv + vec2(px.x, 0.)).a - texture(stone_rgbh, base_uv - vec2(px.x, 0.)).a;
		float hy = texture(stone_rgbh, base_uv + vec2(0., px.y)).a - texture(stone_rgbh, base_uv - vec2(0., px.y)).a;
		vec2 slope = vec2(hx, hy) * mix(stone_base_relief, stone_relief, joint) * rmb.b;
		float luma = max(dot(ALBEDO, vec3(.299, .587, .114)), .02);
		vec3 rock = rgbh.rgb * stone_tint;
		// the stone's own colour at roughly this skin's lightness: neither a black blot nor a white chalk patch
		rock *= mix(1.0, clamp(max(luma, stone_floor) * 1.05 / max(dot(rock, vec3(.299, .587, .114)), .02), .5, 8.0), stone_match);
		float rock_luma = dot(rock, vec3(.299, .587, .114));
		// the skin's own hue carrying the stone's tone; toward the joints, the stone's own colour
		vec3 mineral = mix(ALBEDO * clamp(rock_luma / max(luma, .05), .55, 1.35), mix(rock, rock_luma * ALBEDO / luma, stone_skin_blend), joint);
		mineral *= mix(1.0 - stone_cavity, 1.0, rgbh.a) * (1.0 - crack * stone_crack_dark);
		mineral *= mix(vec3(1.0), stone_tint / max(dot(stone_tint, vec3(.299, .587, .114)), .02), stone_tint_hue);
		ALBEDO = mix(ALBEDO, mineral, stone);
		ROUGHNESS = mix(ROUGHNESS, max(rmb.r, .78), stone);
		SPECULAR = mix(SPECULAR, .3, stone);
		if (stone_glint > 0.0){
			vec2 cell = floor(base_uv * vec2(textureSize(stone_rgbh, 0)) * 1.5);
			float spark = pow(fract(sin(dot(cell, vec2(12.9898, 78.233))) * 43758.5453), 60.0) * stone_glint * stone;
			ROUGHNESS = mix(ROUGHNESS, 0.06, clamp(spark * 6.0, 0., 1.));
			SPECULAR = mix(SPECULAR, 1.0, clamp(spark * 6.0, 0., 1.));
		}
		NORMAL_MAP = mix(NORMAL_MAP, normalize(vec3(-slope.x, -slope.y * stone_normal_y, 1.0)) * .5 + .5, min(stone * 1.6, 1.0));
	}

	plain = stone * .8;
	if (use_opal){
		vec2 om = texture(opal_mask, base_uv).rg;
		float luma = dot(ALBEDO, vec3(.299, .587, .114));
		// White mineral base retains luminance texture/pores but removes brown socket pigment.
		ALBEDO = mix(ALBEDO, opal_pearl * clamp(.94 + (luma - .5) * .12, .87, .99), opal_pale);
		float nv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
		float grazing = pow(1.0 - nv, 2.5);
		// Ordered pink -> green -> blue; never interpolate blue directly into pink (violet).
		float phase = clamp((1.0 - nv) * .9 + opal_noise(base_uv * 18.0) * .1, 0.0, 1.0);
		vec3 film = phase < .5 ? mix(vec3(1., .88, .86), vec3(.87, 1., .91), phase * 2.)
		    : mix(vec3(.87, 1., .91), vec3(.87, .95, 1.), (phase - .5) * 2.);
		ALBEDO *= mix(vec3(1.), film, opal_sheen * om.r * grazing);

		ROUGHNESS = mix(ROUGHNESS, ROUGHNESS * 0.85, om.r * 0.4);
	}

	// Ambient Occlusion
	if (use_ambient_occlusion){
		AO = ao * ao_strength + (1.-ao_strength);// * smoothstep(0.,1., micro_detail.z * 1.6);
		AO_LIGHT_AFFECT = 0.0;
	}

	// Subsurface Scattering: Enabled
	if (use_subsurface_scattering){
		if (native_humgen || smooth_depth > 0.){
			float sss_strength_value = subsurface_scattering_strength * 2.0;
			if (translucency){
				sss_strength_value += (sss_tex * 2.);
			}
			if (use_noise){
				vec2 ts = vec2(textureSize(texture_sss_noise, 0)) * 0.85;
				vec2 dither_uv = mod(FRAGCOORD.xy, ts) / ts + (TIME * 2.5);
				vec2 tiling_screen_uv = FRAGCOORD.xy / (0.64 * -1.);
				float noise = 3.* texture(texture_sss_noise, dither_uv).r;// + vec2(1000. * TIME)).r;
				//noise = 2.0 * ScreenSpaceDither12(FRAGCOORD.xy, TIME * .50);
				sss_strength_value *= noise;
				//ALBEDO = vec3(noise);
			}
			SSS_STRENGTH = sss_strength_value * texture(ambient_occlusion_texture, base_uv).r * (native_humgen ? 1.0 : smooth_depth) * (1.0 - plain);
		}
		//ALBEDO = vec3(smooth_depth);
		//float sss_depth = 1. - pow(clamp(world_camera.z,0.,1.), 0.5);
	}

}
float DistributionGGX(float cos_theta_m, float alpha){
	float alpha2 = alpha * alpha;
	float d = 1.0 + (alpha2 - 1.0) * cos_theta_m * cos_theta_m;
	return alpha2 / (PI * d * d);
}

float GeometryGGX(float NdotL, float NdotV, float alpha){
	return 0.5 / mix(2.0 * NdotL * NdotV, NdotL + NdotV, alpha);
}

vec3 SchlickBaseReflectivity(){
	float dielectric = native_humgen ? 0.08 * native_specular : 0.04 * specular * specular;
	return mix(vec3(dielectric), final_albedo, vec3(final_metallic));
}

float SchlickFresnel(float u){
	float m = 1.0 - u;
	float m2 = m * m;
	return m2 * m2 * m;
}

void light()
{
	// Calculate some vectors.
	vec3 lightColor = LIGHT_COLOR / PI;

	vec3 half = normalize(VIEW + LIGHT);

	float NdotL = max(dot(NORMAL, LIGHT), 0.0);
	float NdotV = max(dot(NORMAL, VIEW), 0.0);
	float NdotH = max(dot(NORMAL, half), 0.0);
	float LdotH = max(dot(LIGHT, half), 0.0);

	// Diffuse light (Lambert):
	//DIFFUSE_LIGHT += clamp(dot(mix(DIFFUSE_NORMAL, NORMAL, 0.3), LIGHT),0.,1.) * ATTENUATION * lightColor;

	//Custom wrapped diffuse:
	float wrap = 0.5;
	vec3 diff_light;
	float normal_depth = max((1.5-linear_depth), 1.);
	//normal_depth = 1.;

	if (!old_lightwarp_fallof){ //Better SSS aproximation:

		//Calculate custom normal:
		float s = skin_smoothness;
		vec3 radius = native_humgen ? sqrt(native_sss_radius) * (native_sss_scale / .01) : vec3(1., .8, .7);
		vec3 dn_r = custom_normal(textureLod(texture_normal, UV, (radius.r*s) * normal_depth).rgb);
		vec3 dn_g = custom_normal(textureLod(texture_normal, UV, (radius.g*s) * normal_depth).rgb);
		vec3 dn_b = custom_normal(textureLod(texture_normal, UV, (radius.b*s) * normal_depth).rgb);

		//Include micro normals in the mix:
		vec3 m_normal = texture(texture_micro_detail, UV * micro_detail_scale).rgb;
		m_normal = mix(vec3(.5,.5,1.), vec3(m_normal.xy, 1.), micro_normal_strength);

		dn_g = normal_blending(dn_g, m_normal);
		dn_b = normal_blending(dn_b, m_normal);
		////

		float sf = skin_fallof_smoothness;

		float wrapped_r = smoothstep(0.4 /sf, 1.2 *sf, dot(dn_r, LIGHT) * wrap + (1.0-wrap));
		float wrapped_g = smoothstep(0.425 /sf, 1.2 *sf, dot(dn_g, LIGHT) * wrap + (1.0-wrap));
		float wrapped_b = smoothstep(0.44 /sf, 1.2 *sf, dot(dn_b, LIGHT) * wrap + (1.0-wrap));
		vec3 wrapped_rgb = vec3(wrapped_r, wrapped_g, wrapped_b);
		if (use_ambient_occlusion){
			wrapped_rgb *= (ao * ao_block_light + (1.-ao_block_light));
		}

		diff_light = wrapped_rgb;
	}
	else{ //Lightwarp technique:
		vec3 diff_normal_map = mix(
			textureLod(texture_normal, UV, 6. * normal_depth).rgb,
			textureLod(texture_normal, UV, 3. * normal_depth).rgb,
			.2);
		vec3 diff_normal = custom_normal(diff_normal_map);

		float wrapped = pow(dot(diff_normal, LIGHT) * wrap + (1.0-wrap), 2.0);
		vec3 lw = texture(lightwarp, vec2(wrapped, 0.0)).rgb;
		if (use_ambient_occlusion){
			lw = texture(lightwarp, vec2(wrapped * (ao * ao_block_light + (1.-ao_block_light)), 0.0)).rgb;
		}
		diff_light = lw;
	}

	// Stone takes plain Lambert on its own relief: the wrapped skin response would blur the plates away.
	// Chalk-white Siann scatter neutral light; the human red-biased wrap otherwise paints brown eye sockets.
	if (use_opal) {
		diff_light = vec3(dot(diff_light, vec3(.333333)));
	}
	diff_light = mix(diff_light, vec3(NdotL), plain);

	//Tint shadow penumbra:
	vec3 att = overlay(min(vec3(ATTENUATION), 1.), vec3(1.2,.8,.6)) * max(ATTENUATION, 0.5);
	if (tinted_shadow_penumbra){att = att;} else{att = vec3(ATTENUATION);}

	DIFFUSE_LIGHT += diff_light * att * lightColor;

	if (translucency){
		float translucency_dot = dot(VIEW,-(LIGHT + NORMAL * .4));
		vec2 t_col_coord = vec2(sss_tex * translucency_dot * translucency_strength, 0.0);
		vec3 t_color = texture(texture_translucency_color, t_col_coord).rgb;
		vec3 translucent = max(translucency_dot * t_color, 0.0) * lightColor;
		//translucency *= max(translucency_strength, 1.);

		DIFFUSE_LIGHT += translucent;
	}

	vec3 f0 = SchlickBaseReflectivity();
	float LdotH5 = SchlickFresnel(LdotH);
	float f90 = clamp(50.0 * f0.g, 0.0, 1.0);
	vec3 F = f0 + (f90 - f0) * LdotH5;

	// Specular light (Schlick-GGX).
	float ggxAlpha = ROUGHNESS * ROUGHNESS;
	float D = DistributionGGX(NdotH, ggxAlpha);
	float G = GeometryGGX(NdotL, NdotV, ggxAlpha);

	vec3 specularBRDF = max(NdotL * D * G * F, 0.0);
	if (double_specularity){
		float D2 = DistributionGGX(NdotH, ggxAlpha + .3);
		float G2 = GeometryGGX(NdotL, NdotV, ggxAlpha + .3);

		vec3 specularBRDF2 = max(NdotL * D2 * G2 * F, 0.0);
		specularBRDF = mix(specularBRDF2, specularBRDF, .6) * 2.;
	}
	vec3 pearl_spec = vec3(1.);
	if (use_opal) {
		float angle = clamp(1. - NdotV, 0., 1.);
		vec3 tint = angle < .5 ? mix(vec3(1., .88, .86), vec3(.87, 1., .91), angle * 2.)
		    : mix(vec3(.87, 1., .91), vec3(.87, .95, 1.), (angle - .5) * 2.);
		pearl_spec = mix(vec3(1.), tint, opal_sheen * pow(angle, 1.5));
	}
	SPECULAR_LIGHT += specularBRDF * pearl_spec * LIGHT_COLOR * ATTENUATION;
	if (use_ambient_occlusion){
		SPECULAR_LIGHT *= ao * ao_block_light + (1.-ao_block_light);
	}
}
