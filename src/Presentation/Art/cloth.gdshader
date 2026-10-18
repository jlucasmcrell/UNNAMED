// UNNAMED - a MetaHuman garment's surface (Outfit.cs): glTF metal-roughness PBR plus the body/garment region mask, which a
// StandardMaterial3D cannot carry.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://Art/region_mask.gdshaderinc"

uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D texture_albedo : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float roughness : hint_range(0.0, 1.0) = 1.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
// glTF packing: R occlusion, G roughness, B metallic.
uniform sampler2D texture_orm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D texture_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float normal_strength = 1.0;
uniform float alpha_scissor = 0.0;

void vertex() {
	region_mask(VERTEX, UV2);
}

void fragment() {
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	vec4 color = albedo * texture(texture_albedo, UV);
	if (color.a < alpha_scissor) {
		discard;
	}
	vec3 orm = texture(texture_orm, UV).rgb;
	ALBEDO = color.rgb;
	AO = orm.r;
	ROUGHNESS = roughness * orm.g;
	METALLIC = metallic * orm.b;
	NORMAL_MAP = texture(texture_normal, UV).rgb;
	NORMAL_MAP_DEPTH = normal_strength;
}
