extends RefCounted

const SKIN = preload("skin_shader.gdshader")
const LIGHTWARP = preload("lightwarp_skin1.tres")

static func solid_texture(color: Color) -> ImageTexture:
	var image := Image.create(4, 4, false, Image.FORMAT_RGBA8)
	image.fill(color)
	return ImageTexture.create_from_image(image)

static func apply(model: Node) -> int:
	var count := 0
	var white := solid_texture(Color.WHITE)
	var flat_normal := solid_texture(Color(0.5, 0.5, 1.0))
	for mesh in model.find_children("*", "MeshInstance3D", true, false):
		for surface in mesh.mesh.get_surface_count():
			var source = mesh.get_active_material(surface)
			if not source is BaseMaterial3D or not source.resource_name.begins_with("HG_Body_"):
				continue
			# Upstream shader reads green. Godot glTF roughness maps must match.
			assert(source.roughness_texture == null or source.roughness_texture_channel == BaseMaterial3D.TEXTURE_CHANNEL_GREEN)
			var material := ShaderMaterial.new()
			material.resource_name = source.resource_name + "_HumanShaders"
			material.shader = SKIN
			material.set_shader_parameter("albedo", source.albedo_color)
			material.set_shader_parameter("texture_albedo", source.albedo_texture if source.albedo_texture else white)
			material.set_shader_parameter("roughness", source.roughness)
			material.set_shader_parameter("texture_roughness", source.roughness_texture if source.roughness_texture else white)
			material.set_shader_parameter("texture_normal", source.normal_texture if source.normal_enabled and source.normal_texture else flat_normal)
			material.set_shader_parameter("normal_strength", source.normal_scale if source.normal_enabled else 0.0)
			material.set_shader_parameter("specular", source.metallic_specular)
			material.set_shader_parameter("metallic", 0.0)
			material.set_shader_parameter("texture_metallic", white)
			material.set_shader_parameter("texture_micro_detail", flat_normal)
			material.set_shader_parameter("micro_normal_strength", 0.0)
			material.set_shader_parameter("use_micro_detail", false)
			material.set_shader_parameter("ambient_occlusion_texture", white)
			material.set_shader_parameter("use_ambient_occlusion", false)
			material.set_shader_parameter("use_noise", false)
			material.set_shader_parameter("lightwarp", LIGHTWARP)
			material.set_shader_parameter("texture_translucency", white)
			material.set_shader_parameter("translucency", false)
			material.set_shader_parameter("use_subsurface_scattering", true)
			mesh.set_surface_override_material(surface, material)
			count += 1
			print("HUMAN_SKIN_APPLIED ", mesh.name, " / ", source.resource_name, " roughness=", source.roughness, " specular=", source.metallic_specular)
	assert(count == 1, "Expected exactly one HumGen body skin surface; no clothing, eyes, or hair changed")
	return count
