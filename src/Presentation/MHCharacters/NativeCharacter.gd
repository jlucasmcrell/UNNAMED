extends Node3D
## Native separate head/body, native garment weights, no body IK or rest edits.
const LeaderPose = preload("../MHSlice/LeaderPose.gd")
const SKIN_SHADER = preload("../MHSlice/Shaders/skin_shader_local.gdshader")
const EYE_SHADER = preload("../MHSlice/Shaders/eye_shader_local.gdshader")
const HAIR_SHADER = preload("../MHSlice/Shaders/hair_card.gdshader")
const HAIR_BACKING_SHADER = preload("../MHSlice/Shaders/hair_backing.gdshader")
var _textures: Dictionary = {}
var _source := ""
var _sex := ""
var _config: Dictionary
var _root: Node3D
var _body: Skeleton3D
var _face: Skeleton3D
var _sync: Node
var _animator: AnimationPlayer
var _body_meshes: Array[MeshInstance3D] = []
var _load_failed := false

func _all(node: Node, cls: String) -> Array:
	var result := []
	if node.is_class(cls): result.append(node)
	for child in node.get_children(): result.append_array(_all(child,cls))
	return result

func _texture(path: String) -> Texture2D:
	if _textures.has(path): return _textures[path]
	var img := Image.load_from_file(_asset_path(path))
	if img == null or img.is_empty():
		_load_failed = true
		push_error("Required native texture missing: "+path)
		return null
	var limit: int = _config.get("max_texture_size", 4096)
	if max(img.get_width(), img.get_height()) > limit:
		var ratio: float = float(limit) / maxi(img.get_width(), img.get_height())
		img.resize(maxi(1, roundi(img.get_width() * ratio)), maxi(1, roundi(img.get_height() * ratio)), Image.INTERPOLATE_LANCZOS)
	img.generate_mipmaps()
	var texture := ImageTexture.create_from_image(img)
	_textures[path] = texture
	return texture

func _asset_path(path: String) -> String:
	var normalized := path.replace("\\","/")
	var original := "D:/Otherreach_Scratch/mh_slice_20261019/"
	if normalized.begins_with(_source):return normalized
	if normalized.begins_with(original):return _source+normalized.trim_prefix(original)
	assert(not normalized.is_absolute_path(),"Slice asset is outside its selected source bundle")
	return _source+normalized

func _skin(part: String) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = SKIN_SHADER
	var maps: String = _source + _config.maps
	mat.set_shader_parameter("texture_albedo",_texture(maps + part + "_Basecolor.png"))
	mat.set_shader_parameter("texture_normal",_texture(maps + part + "_Normal.png"))
	mat.set_shader_parameter("texture_roughness",_texture(maps + part + "_SRMF.png"))
	mat.set_shader_parameter("roughness",1.0)
	mat.set_shader_parameter("albedo",Color.WHITE)
	mat.set_shader_parameter("metallic",0.0)
	mat.set_shader_parameter("use_noise",false)
	mat.set_shader_parameter("use_scatter_map",false)
	mat.set_shader_parameter("use_ambient_occlusion",false)
	var white := Image.create(1,1,false,Image.FORMAT_RGBA8)
	white.fill(Color.WHITE)
	mat.set_shader_parameter("ambient_occlusion_texture",ImageTexture.create_from_image(white))
	return mat

func _face_material(name_: String) -> Material:
	var maps: String = _source + _config.maps
	if "teeth" in name_:
		var mat := _skin("Head")
		mat.set_shader_parameter("texture_albedo",_texture(maps+"Teeth_Color.png"))
		mat.set_shader_parameter("texture_normal",_texture(maps+"Teeth_Normal.png"))
		mat.set_shader_parameter("texture_roughness",mat.get_shader_parameter("ambient_occlusion_texture"))
		mat.set_shader_parameter("roughness",.4)
		return mat
	if "eyeshell" in name_ or "lacrimal" in name_:
		var film := StandardMaterial3D.new()
		film.transparency=BaseMaterial3D.TRANSPARENCY_ALPHA
		film.albedo_color=Color(1,1,1,.015)
		film.roughness=.08
		return film
	if "eyelashes" in name_:
		var lashes := ShaderMaterial.new()
		lashes.shader=HAIR_SHADER
		lashes.set_shader_parameter("coverage_atlas",_texture(maps+"Eyelashes_Color.png"))
		# DCC eyelash color is constant RGB; its native coverage is alpha.
		lashes.set_shader_parameter("use_red_mask",false)
		lashes.set_shader_parameter("hair_color",Color(.025,.016,.01))
		lashes.set_shader_parameter("alpha_threshold",.5)
		lashes.set_shader_parameter("root_darkening",0.0)
		lashes.set_shader_parameter("roughness_val",.5)
		lashes.set_shader_parameter("specular_val",.12)
		return lashes
	if "eyel_" in name_ or "eyer_" in name_:
		var eye := ShaderMaterial.new()
		eye.shader=EYE_SHADER
		var color := _texture(maps+"Eyes_Color.png")
		var normal := _texture(maps+"Eyes_Normal.png")
		eye.set_shader_parameter("sclera_albedo_texture",color)
		eye.set_shader_parameter("iris_albedo_texture",color)
		eye.set_shader_parameter("sclera_normal_texture",normal)
		eye.set_shader_parameter("iris_normal_texture",normal)
		var eye_manifest: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(_source+_config.eye_manifest))
		for character in eye_manifest.get("characters",[]):
			if character.sex!=_sex:continue
			for native_material in character.materials:
				if ("eyel_" in name_ and native_material.name!="MI_EyeL_Baked") or ("eyer_" in name_ and native_material.name!="MI_EyeR_Baked"):continue
				for pair in [["Sclera Basecolor Baked","sclera_albedo_texture"],["Iris Basecolor Baked","iris_albedo_texture"],["Sclera Normal Baked","sclera_normal_texture"],["Iris Normal Baked","iris_normal_texture"]]:
					if native_material.textures.has(pair[0]):eye.set_shader_parameter(pair[1],_texture(native_material.textures[pair[0]].path))
		eye.set_shader_parameter("uv1_scale",Vector3.ONE)
		eye.set_shader_parameter("uv1_offset",Vector3.ZERO)
		eye.set_shader_parameter("iris_scale",2.5)
		eye.set_shader_parameter("subsurface_scattering",false)
		eye.set_shader_parameter("normal_strength",.35)
		eye.set_shader_parameter("use_heightmap",false)
		var white := Image.create(1,1,false,Image.FORMAT_RGBA8)
		white.fill(Color.WHITE)
		eye.set_shader_parameter("texture_heightmap",_texture(_source+"materials/human_shaders/eye_iris_parallax.png"))
		return eye
	return null

func configure(source: String, config: Dictionary) -> bool:
	_source = source.replace("\\", "/").trim_suffix("/") + "/"
	_config = config
	_sex = config.sex
	_body = null
	_face = null
	_animator=null
	if is_instance_valid(_root):
		remove_child(_root)
		_root.queue_free()
	_root = Node3D.new()
	add_child(_root)
	for part in ["body","head"]:
		var path: String = _source + config[part]
		if not FileAccess.file_exists(path):
			push_error("Required MetaHuman mesh missing: " + path)
			return false
		var doc := GLTFDocument.new()
		var state := GLTFState.new()
		var err := doc.append_from_file(path,state,GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION)
		if err != OK:
			push_error("Native GLB failed to load: " + path)
			return false
		var model := doc.generate_scene(state) as Node3D
		_root.add_child(model)
		var skels := _all(model,"Skeleton3D")
		if skels.size() != 1:
			push_error("Expected one native skeleton: " + path)
			return false
		if part == "body": _body = skels[0]
		else: _face = skels[0]
		var skin := _skin("Body" if part == "body" else "Head")
		for n in _all(model,"MeshInstance3D"):
			var mi := n as MeshInstance3D
			if part == "body":
				_body_meshes.append(mi)
				mi.set_meta("native_source_mesh", mi.mesh)
			for i in mi.get_blend_shape_count():mi.set_blend_shape_value(i,0)
			for s in range(mi.mesh.get_surface_count()):
				var original := mi.mesh.surface_get_material(s)
				var name_lower := original.resource_name.to_lower() if original else ""
				if part == "body" or "graytexture_head" in name_lower or "face_skin" in name_lower:
					mi.set_surface_override_material(s,skin)
				elif "hide" in name_lower:
					var hidden := StandardMaterial3D.new()
					hidden.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
					hidden.albedo_color = Color(0,0,0,0)
					mi.set_surface_override_material(s,hidden)
				else:
					var semantic := _face_material(name_lower)
					if semantic:mi.set_surface_override_material(s,semantic)
	_sync = LeaderPose.new()
	_sync.body = _body
	_sync.face = _face
	_root.add_child(_sync)
	if not _load_grooms(): return false
	if not _load_garments(): return false
	if _load_failed: return false
	print("MH_CHARACTER id=%s body_bones=%d face_bones=%d native_bind=true ik=false" % [config.id, _body.get_bone_count(), _face.get_bone_count()])
	return true

func _load_grooms() -> bool:
	var manifest_path: String = _source+_config.groom_manifest
	if not FileAccess.file_exists(manifest_path):
		push_error("Native groom manifest missing: " + manifest_path)
		return false
	var manifest: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
	for card in manifest.get("cards",[]):
		if card.sex!=_sex:continue
		var doc:=GLTFDocument.new()
		var state:=GLTFState.new()
		var error := doc.append_from_file(_asset_path(card.glb),state,GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION)
		if error!=OK:
			push_error("Native groom load failed: "+card.glb)
			return false
		var model:=doc.generate_scene(state) as Node3D
		var attachment:=BoneAttachment3D.new()
		attachment.bone_name="head"
		_face.add_child(attachment)
		attachment.add_child(model)
		# Native card vertices are in character space. Preserve that rest placement.
		model.transform=_face.get_bone_global_rest(_face.find_bone("head")).affine_inverse()*model.transform
		var material:=ShaderMaterial.new()
		material.shader=HAIR_SHADER
		material.set_shader_parameter("coverage_atlas",_texture(card.coverage))
		material.set_shader_parameter("use_red_mask",true)
		material.set_shader_parameter("alpha_threshold",.08 if "Eyebrows" in card.name else .07)
		material.set_shader_parameter("root_darkening",.42)
		material.set_shader_parameter("roughness_val",.72)
		material.set_shader_parameter("specular_val",.12)
		var rgba: Array=card.hair_color
		material.set_shader_parameter("hair_color",Color(rgba[0],rgba[1],rgba[2],rgba[3]))
		for mi in _all(model,"MeshInstance3D"):
			mi.add_to_group("mh_facial_grooms")
			for i in mi.get_blend_shape_count():mi.set_blend_shape_value(i,0)
			for surface in mi.mesh.get_surface_count():mi.set_surface_override_material(surface,material)
			if String(card.name).begins_with("Hair_"):
				# Same native card mesh, inset backing pass from the working Godot reference.
				var backing_material := ShaderMaterial.new()
				backing_material.shader=HAIR_BACKING_SHADER
				backing_material.set_shader_parameter("coverage_atlas",_texture(card.coverage))
				backing_material.set_shader_parameter("use_red_mask",true)
				backing_material.set_shader_parameter("hair_color",Color(rgba[0],rgba[1],rgba[2],rgba[3]).darkened(.25))
				backing_material.set_shader_parameter("cutoff",.035)
				backing_material.set_shader_parameter("inset",.009)
				var backing := MeshInstance3D.new()
				backing.mesh=mi.mesh
				backing.transform=mi.transform
				for surface in mi.mesh.get_surface_count():backing.set_surface_override_material(surface,backing_material)
				mi.add_sibling(backing)

	return true

func body_skeleton() -> Skeleton3D:
	return _body

func set_shadow_only(enabled: bool) -> void:
	for mesh in _all(self, "MeshInstance3D"):
		mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY if enabled else GeometryInstance3D.SHADOW_CASTING_SETTING_ON

func bone_position(bone_name: String) -> Vector3:
	var index := _body.find_bone(bone_name)
	return _body.to_global(_body.get_bone_global_pose(index).origin) if index >= 0 else global_position

func _cloth_material(option: Array) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_texture = _texture("wardrobe/cloth/" + option[1])
	material.normal_texture = _texture("wardrobe/cloth/" + option[2])
	material.normal_enabled = true
	material.normal_scale = option[3]
	material.metallic = 0.0
	material.roughness = .68
	var tint: Array = _config.get("tint", [1.0, 1.0, 1.0])
	material.albedo_color = Color(tint[0], tint[1], tint[2])
	return material

func _load_garments() -> bool:
	var plans := {
		"chest.glb": [["shirt", "Shirt_BaseColor.png", "Shirt_Normal.png", .40], ["vest", "Vest_BaseColor.png", "Vest_Normal.png", .40]],
		"pants.glb": [["default", "Pants_Leather_baseColor.png", "Pants_Leather_normal.png", .28]],
		"hood.glb": [["default", "Hood_BaseColor.png", "Hood_Normal.png", .45]],
	}
	var garments: Array[MeshInstance3D] = []
	for file_name in plans:
		if file_name == "hood.glb" and not _config.get("hood", true): continue
		var document := GLTFDocument.new()
		var state := GLTFState.new()
		var path: String = _source + "wardrobe/" + file_name
		if document.append_from_file(path, state, GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION | GLTFDocument.IMPORT_FLAG_USE_NAMED_SKIN_BINDS) != OK:
			push_error("Required accepted garment missing: " + path)
			return false
		var model := document.generate_scene(state)
		_root.add_child(model)
		for mesh: MeshInstance3D in _all(model, "MeshInstance3D"):
			if mesh.skin == null:
				push_error("Garment has no native skin: " + file_name)
				return false
			var unmatched: Array[String] = []
			for bind in mesh.skin.get_bind_count():
				var bone_name := mesh.skin.get_bind_name(bind)
				if bone_name.is_empty() or _body.find_bone(bone_name) < 0:
					unmatched.append(str(bone_name))
			if not unmatched.is_empty():
				push_error("Native garment bind mismatch: %s %s" % [file_name, unmatched])
				return false
			for surface in mesh.mesh.get_surface_count():
				var original := mesh.mesh.surface_get_material(surface)
				var label := original.resource_name.to_lower() if original else ""
				var option: Array = plans[file_name][0]
				for candidate in plans[file_name]:
					if candidate[0] in label: option = candidate
				if "vest" in label and not _config.get("vest", true):
					var hidden := StandardMaterial3D.new()
					hidden.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
					hidden.albedo_color = Color(0,0,0,0)
					mesh.set_surface_override_material(surface, hidden)
					mesh.set_meta("hidden_surface_" + str(surface), true)
				else: mesh.set_surface_override_material(surface, _cloth_material(option))
			var world := mesh.global_transform
			mesh.reparent(_body, true)
			mesh.global_transform = world
			mesh.skeleton = mesh.get_path_to(_body)
			garments.append(mesh)
		model.queue_free()
	if OS.get_environment("OTHERREACH_MH_BAKE_MASKS") == "1":
		_bake_hidden_face_uv(garments)
	else:
		var mask_path: String = _config.get("hidden_mask", "")
		if mask_path.is_empty() or not FileAccess.file_exists(_source + mask_path):
			push_error("Required baked Body Hidden Face Map missing: " + str(_config.id))
			return false
		var mask := _texture(mask_path)
		for mesh in _body_meshes:
			for surface in mesh.mesh.get_surface_count():
				var material: ShaderMaterial = mesh.get_surface_override_material(surface)
				material.set_shader_parameter("hidden_face_map", mask)
				material.set_shader_parameter("use_hidden_face_map", true)
	return true

func _near_cloth(point: Vector3, grid: Dictionary, radius: float) -> bool:
	var cell := Vector3i((point / radius).floor())
	for x in range(-1, 2):
		for y in range(-1, 2):
			for z in range(-1, 2):
				for other: Vector3 in grid.get(cell + Vector3i(x,y,z), []):
					if point.distance_squared_to(other) <= radius * radius: return true
	return false

func _bake_hidden_face_uv(garments: Array[MeshInstance3D]) -> void:
	# Offline classification only. The native meshes and all their buffers are read-only.
	var radius := .03
	var grid := {}
	for mesh in garments:
		for surface in mesh.mesh.get_surface_count():
			if mesh.get_meta("hidden_surface_" + str(surface), false): continue
			var vertices: PackedVector3Array = mesh.mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]
			for index in range(0, vertices.size(), 2):
				var point := _body.to_local(mesh.to_global(vertices[index]))
				var cell := Vector3i((point / radius).floor())
				if not grid.has(cell): grid[cell] = []
				grid[cell].append(point)
	var hidden_uv := []
	for mesh in _body_meshes:
		for surface in mesh.mesh.get_surface_count():
			var arrays := mesh.mesh.surface_get_arrays(surface)
			var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
			var uv: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV]
			var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
			for index in range(0, indices.size(), 3):
				var centre := (vertices[indices[index]] + vertices[indices[index+1]] + vertices[indices[index+2]]) / 3.0
				if _near_cloth(_body.to_local(mesh.to_global(centre)), grid, radius):
					hidden_uv.append([uv[indices[index]].x, uv[indices[index]].y, uv[indices[index+1]].x, uv[indices[index+1]].y, uv[indices[index+2]].x, uv[indices[index+2]].y])
	DirAccess.make_dir_recursive_absolute(_source + "masks")
	var path: String = _source + _config.hidden_mask + ".triangles.json"
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(JSON.stringify({"character": _config.id, "triangles": hidden_uv, "radius_metres": radius}))
	file.close()
	print("MH_HIDDEN_FACE_MAP triangles=%d file=%s native_mesh_untouched=true" % [hidden_uv.size(), path])
