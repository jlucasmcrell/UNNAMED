extends Node3D
## Undressed MetaHuman review inside the game project. Native rigs; runtime IK off.
const LeaderPose = preload("LeaderPose.gd")
const SKIN_SHADER = preload("Shaders/skin_shader_local.gdshader")
const EYE_SHADER = preload("Shaders/eye_shader_local.gdshader")
const HAIR_SHADER = preload("Shaders/hair_card.gdshader")
const SeamProbe = preload("Tests/SeamProbe.gd")
const Measurements = preload("Tests/Measurements.gd")
var _root: Node3D
var _camera: Camera3D
var _status: Label
var _sex := "feminine"
var _source := "D:/Otherreach_Scratch/mh_slice_20261019/"
var _distance := 2.6
var _side := false
var _view_angle := 0.0
var _area := "full_body"
var _target := Vector3(0,1.0,0)
const AREAS = ["full_body","face","hairline","neck","armpit","wrist","ankle"]
var _body: Skeleton3D
var _face: Skeleton3D
var _sync: Node
var _rom := false
var _time := 0.0
var _head_route := ""
var _body_route := ""
var _animator: AnimationPlayer
var _rom_animation := ""

func _ready() -> void:
	get_viewport().msaa_3d=Viewport.MSAA_4X
	var source_override := OS.get_environment("OTHERREACH_MH_SLICE_ROOT")
	if not source_override.is_empty(): _source = source_override.replace("\\","/").trim_suffix("/") + "/"
	_head_route=OS.get_environment("OTHERREACH_MH_HEAD_ROUTE")
	_body_route=OS.get_environment("OTHERREACH_MH_BODY_ROUTE")
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.07, 0.08, 0.09)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.72, 0.79, 0.86)
	env.environment.ambient_light_energy = 0.35
	env.environment.tonemap_mode = Environment.TONE_MAPPER_AGX
	add_child(env)
	for spec in [[Vector3(-35,-35,0),2.0],[Vector3(-10,120,0),0.8]]:
		var light := DirectionalLight3D.new()
		light.rotation_degrees = spec[0]
		light.light_energy = spec[1]
		light.shadow_enabled = true
		add_child(light)
	_camera = Camera3D.new()
	_camera.near = 0.01
	_camera.fov = 35
	add_child(_camera)
	_camera.current = true
	var canvas := CanvasLayer.new()
	_status = Label.new()
	_status.position = Vector2(16,16)
	canvas.add_child(_status)
	add_child(canvas)
	_load_character("feminine")
	if "--mh-source-evidence" in OS.get_cmdline_user_args():
		call_deferred("_source_evidence")
	elif "--mh-owner-evidence" in OS.get_cmdline_user_args():
		call_deferred("_owner_evidence")

func _all(node: Node, cls: String) -> Array:
	var result := []
	if node.is_class(cls): result.append(node)
	for child in node.get_children(): result.append_array(_all(child,cls))
	return result

func _texture(path: String) -> Texture2D:
	var img := Image.load_from_file(_asset_path(path))
	assert(img != null and not img.is_empty(),"Required native texture missing: "+path)
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)

func _asset_path(path: String) -> String:
	var normalized := path.replace("\\","/")
	var original := "D:/Otherreach_Scratch/mh_slice_20261019/"
	if normalized.begins_with(original):return _source+normalized.trim_prefix(original)
	if normalized.begins_with(_source):return normalized
	assert(not normalized.is_absolute_path(),"Slice asset is outside its selected source bundle")
	return _source+normalized

func _skin(part: String) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = SKIN_SHADER
	var maps := _source + "exports/final/" + _sex + "/dcc/MHC_Slice_" + _sex.capitalize() + "/Maps/"
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
	var maps := _source+"exports/final/"+_sex+"/dcc/MHC_Slice_"+_sex.capitalize()+"/Maps/"
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
		var eye_manifest: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(_source+"evidence/native_eye_materials.json"))
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

func _load_character(sex: String) -> void:
	_sex = sex
	_rom = false
	_body = null
	_face = null
	_animator=null
	_rom_animation=""
	if is_instance_valid(_root):
		remove_child(_root)
		_root.queue_free()
	_root = Node3D.new()
	add_child(_root)
	for part in ["body","head"]:
		var path: String = _source + "glb/blender_gltf/" + sex + "/" + part + ".glb"
		if part=="head" and not _head_route.is_empty():
			var alternate: String = _source+"glb/"+_head_route+"/"+sex+"/head.glb"
			if FileAccess.file_exists(alternate):path=alternate
		if part=="body" and not _body_route.is_empty():
			var alternate: String = _source+"glb/"+_body_route+"/"+sex+"/body.glb"
			if FileAccess.file_exists(alternate):path=alternate
		if not FileAccess.file_exists(path):
			_status.text = "OTHERREACH MetaHuman slice: source not ready\n" + path
			return
		var doc := GLTFDocument.new()
		var state := GLTFState.new()
		var err := doc.append_from_file(path,state,GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION)
		assert(err == OK,"Native GLB failed to load")
		var model := doc.generate_scene(state) as Node3D
		_root.add_child(model)
		var skels := _all(model,"Skeleton3D")
		assert(skels.size() == 1)
		if part == "body": _body = skels[0]
		else: _face = skels[0]
		if part=="body":
			var players := _all(model,"AnimationPlayer")
			if not players.is_empty():
				_animator=players[0]
				for animation_name in _animator.get_animation_list():
					if animation_name!="RESET":_rom_animation=animation_name;break
		var skin := _skin("Body" if part == "body" else "Head")
		for n in _all(model,"MeshInstance3D"):
			var mi := n as MeshInstance3D
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
	_load_grooms()
	_status.text = "OTHERREACH | MetaHuman undressed review | " + sex.capitalize() + "\n1 Ada / feminine   2 Mateo / masculine   F front   S side   Q three-quarter\nC next close-up   wheel zoom   R native range of motion   T teeth\nBase underwear | owner review pending"
	_set_area(_area)

func _load_grooms() -> void:
	var manifest_path := _source+"evidence/groom_shape_exports.json"
	if not FileAccess.file_exists(manifest_path):return
	var manifest: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
	for card in manifest.get("cards",[]):
		if card.sex!=_sex:continue
		var doc:=GLTFDocument.new()
		var state:=GLTFState.new()
		assert(doc.append_from_file(_asset_path(card.glb),state,GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION)==OK)
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
		material.set_shader_parameter("alpha_threshold",.08 if "Eyebrows" in card.name else .333)
		var rgba: Array=card.hair_color
		material.set_shader_parameter("hair_color",Color(rgba[0],rgba[1],rgba[2],rgba[3]))
		for mi in _all(model,"MeshInstance3D"):
			mi.add_to_group("mh_facial_grooms")
			for i in mi.get_blend_shape_count():mi.set_blend_shape_value(i,0)
			for surface in mi.mesh.get_surface_count():mi.set_surface_override_material(surface,material)

func _camera_pose() -> void:
	_camera.projection=Camera3D.PROJECTION_PERSPECTIVE
	_camera.position = _target + Vector3(sin(_view_angle),0,cos(_view_angle)) * _distance
	_camera.look_at(_target)

func _set_area(area: String) -> void:
	_area=area
	var head := _body.global_transform*_body.get_bone_global_rest(_body.find_bone("head")).origin
	match area:
		"full_body":_target=Vector3(0,.9,0);_distance=3.3
		"face":_target=head+Vector3(0,.08,0);_distance=.55
		"hairline":_target=head+Vector3(0,.11,0);_distance=.42
		"neck":_target=head+Vector3(0,-.08,0);_distance=.53
		"armpit":_target=_body.global_transform*_body.get_bone_global_rest(_body.find_bone("upperarm_l")).origin+Vector3(0,-.08,0);_distance=.55
		"wrist":_target=_body.global_transform*_body.get_bone_global_rest(_body.find_bone("hand_l")).origin;_distance=.35
		"ankle":_target=_body.global_transform*_body.get_bone_global_rest(_body.find_bone("foot_l")).origin;_distance=.4
	_camera_pose()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		match event.keycode:
			KEY_1: _load_character("feminine")
			KEY_2: _load_character("masculine")
			KEY_F: _view_angle=0; _camera_pose()
			KEY_S: _view_angle=PI/2; _camera_pose()
			KEY_Q: _view_angle=PI/4; _camera_pose()
			KEY_C: _set_area(AREAS[(AREAS.find(_area)+1)%AREAS.size()])
			KEY_T:
				for mi in _all(_root,"MeshInstance3D"):
					var shape: int=mi.find_blend_shape_by_name("jawOpen")
					if shape>=0:mi.set_blend_shape_value(shape,1.0-mi.get_blend_shape_value(shape))
			KEY_R:
				_rom=not _rom
				if _animator and not _rom_animation.is_empty():
					if _rom:_animator.play(_rom_animation)
					else:_animator.stop();_body.reset_bone_poses()
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_WHEEL_UP: _distance=max(0.25,_distance*0.85)
		if event.button_index == MOUSE_BUTTON_WHEEL_DOWN: _distance=min(5.0,_distance/0.85)
		_camera_pose()

func _process(delta: float) -> void:
	if not _rom or _body == null: return
	if _animator and not _rom_animation.is_empty():return
	_time += delta
	for spec in [["neck_01",Vector3.UP,55.0],["head",Vector3.RIGHT,35.0],
		["hand_l",Vector3.UP,70.0],["hand_r",Vector3.UP,-70.0],
		["upperarm_l",Vector3.FORWARD,45.0],["upperarm_r",Vector3.FORWARD,-45.0]]:
		var bi: int = _body.find_bone(spec[0])
		if bi < 0: continue
		var rest := _body.get_bone_rest(bi)
		var basis := rest.basis * Basis(spec[1],deg_to_rad(spec[2])*sin(_time*0.8))
		_body.set_bone_pose_rotation(bi,basis.get_rotation_quaternion())

func _source_evidence() -> void:
	var records := []
	for sex in ["feminine","masculine"]:
		_load_character(sex)
		await get_tree().process_frame
		await RenderingServer.frame_post_draw
		var bmi: MeshInstance3D
		var hmi: MeshInstance3D
		var hs := -1
		for n in _all(_root,"MeshInstance3D"):
			var mi := n as MeshInstance3D
			if mi.skin==null or mi.skeleton.is_empty():continue
			if mi.get_node(mi.skeleton) == _body: bmi=mi
			if mi.get_node(mi.skeleton) == _face:
				hmi=mi
				for s in mi.mesh.get_surface_count():
					var material_name := mi.mesh.surface_get_material(s).resource_name.to_lower()
					if "graytexture_head" in material_name or "face_skin" in material_name: hs=s
		assert(bmi != null and hmi != null and hs >= 0)
		var rest_points := PackedVector3Array()
		for mi in [bmi,hmi]:
			for s in mi.mesh.get_surface_count():
				var vertices: PackedVector3Array = mi.mesh.surface_get_arrays(s)[Mesh.ARRAY_VERTEX]
				for p in vertices:rest_points.append(mi.global_transform*p)
		var proportions := Measurements.measure(rest_points)
		var bl := SeamProbe.boundary_loops(bmi,0)
		var hl := SeamProbe.boundary_loops(hmi,hs)
		var bc := []
		var hc := []
		for loop in bl: bc.append(SeamProbe.center(SeamProbe.points(bmi,0,loop,false)))
		for loop in hl: hc.append(SeamProbe.center(SeamProbe.points(hmi,hs,loop,false)))
		assert(not bl.is_empty() and not hl.is_empty())
		# The neck is the highest open boundary component of the intact body.
		var bi := 0
		for i in bc.size():
			if bc[i].y > bc[bi].y: bi=i
		var hi := 0
		for i in hc.size():
			if hc[i].distance_to(bc[bi]) < hc[hi].distance_to(bc[bi]): hi=i
		var bp := SeamProbe.points(bmi,0,bl[bi],false)
		var hp := SeamProbe.points(hmi,hs,hl[hi],false)
		var pairs := SeamProbe.pair_nearest(bp,hp)
		var reverse_pairs := SeamProbe.pair_nearest(hp,bp)
		var row := {"sex":sex,"body_loops":bl.map(func(x):return x.size()),"head_loops":hl.map(func(x):return x.size()),
			"measurements_m":proportions,
			"skin_weights":[_audit_weights(bmi),_audit_weights(hmi)],
			"body_centers":str(bc),"head_centers":str(hc),"body_boundary_vertices":bp.size(),"head_boundary_vertices":hp.size(),
			"neutral_max_mm":1000.0*maxf(SeamProbe.max_distance(bp,hp,pairs),SeamProbe.max_distance(hp,bp,reverse_pairs)),"poses":[]}
		var ue_path := _source+"evidence/ue_source_measurements.json"
		if FileAccess.file_exists(ue_path):
			var ue: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(ue_path))
			if ue.status=="DONE":
				for character in ue.characters:
					if character.sex!=sex:continue
					row["ue_measurements_m"]=character.measurements_m
					var errors := {}
					for key in proportions:errors[key]=100*absf(proportions[key]-character.measurements_m[key])/character.measurements_m[key]
					row["proportion_error_percent"]=errors
		_camera.projection=Camera3D.PROJECTION_ORTHOGONAL
		_camera.size=1.9
		for side_view in [false,true]:
			_camera.position=Vector3(3,0.85,0) if side_view else Vector3(0,0.85,3)
			_camera.look_at(Vector3(0,0.85,0))
			await RenderingServer.frame_post_draw
			get_viewport().get_texture().get_image().save_png(_source+"evidence/source_"+sex+("_ortho_side.png" if side_view else "_ortho_front.png"))
		_camera.projection=Camera3D.PROJECTION_PERSPECTIVE
		for angle in [0.0,-1.0,1.0]:
			if _animator and not _rom_animation.is_empty():
				var sample_time := 0.0 if angle==0 else (1.25 if angle>0 else 3.75)
				_animator.play(_rom_animation)
				_animator.pause()
				_animator.seek(sample_time,true)
				_animator.advance(0)
				row["native_rom_animation"]=_rom_animation
			else:
				for spec in [["neck_01",Vector3.UP,55.0],["head",Vector3.RIGHT,35.0],
					["hand_l",Vector3.UP,70.0],["hand_r",Vector3.UP,-70.0],
					["upperarm_l",Vector3.FORWARD,45.0],["upperarm_r",Vector3.FORWARD,-45.0]]:
					var bone: int = _body.find_bone(spec[0])
					var basis: Basis = _body.get_bone_rest(bone).basis*Basis(spec[1],deg_to_rad(spec[2])*angle)
					_body.set_bone_pose_rotation(bone,basis.get_rotation_quaternion())
			_sync.apply_leader_pose()
			await get_tree().process_frame
			await RenderingServer.frame_post_draw
			bp=SeamProbe.points(bmi,0,bl[bi],true)
			hp=SeamProbe.points(hmi,hs,hl[hi],true)
			var max_mm := 1000.0*maxf(SeamProbe.max_distance(bp,hp,pairs),SeamProbe.max_distance(hp,bp,reverse_pairs))
			row.poses.append({"angle_factor":angle,"max_boundary_distance_mm":max_mm})
			_camera.position=Vector3(0,1.5,0.65)
			_camera.look_at(Vector3(0,1.40,0))
			await RenderingServer.frame_post_draw
			get_viewport().get_texture().get_image().save_png(_source+"evidence/source_"+sex+"_pose_"+str(angle)+".png")
		records.append(row)
		if _animator:_animator.stop()
		_body.reset_bone_poses()
		_sync.apply_leader_pose()
		await get_tree().process_frame
		await RenderingServer.frame_post_draw
		row["expression_sentinels"]=await _expression_evidence(hmi)
		print("SOURCE_SEAM ",JSON.stringify(row))
	var out := FileAccess.open(_source+"evidence/game_source_seams.json",FileAccess.WRITE)
	out.store_string(JSON.stringify({"source_draft":true,"gates_passed":[],"measurements":records},"  "))
	get_tree().quit()

func _expression_evidence(mi: MeshInstance3D) -> Array:
	var records := []
	var head_index := _body.find_bone("head")
	var target := _body.get_bone_global_pose(head_index).origin+Vector3(0,.08,0)
	_camera.position=target+Vector3(0,0,.55)
	_camera.look_at(target)
	for name_ in ["jawOpen","eyeBlinkLeft","mouthSmileLeft"]:
		var shape := mi.find_blend_shape_by_name(name_)
		if shape<0:
			records.append({"shape":name_,"status":"MISSING"})
			continue
		mi.set_blend_shape_value(shape,1)
		_groom_expression(name_,1)
		await RenderingServer.frame_post_draw
		var baked := mi.bake_mesh_from_current_blend_shape_mix()
		var worst := 0.0
		for s in mi.mesh.get_surface_count():
			var base: PackedVector3Array = mi.mesh.surface_get_arrays(s)[Mesh.ARRAY_VERTEX]
			var posed: PackedVector3Array = baked.surface_get_arrays(s)[Mesh.ARRAY_VERTEX]
			assert(base.size()==posed.size())
			for i in base.size():worst=maxf(worst,base[i].distance_to(posed[i]))
		records.append({"shape":name_,"max_displacement_mm":worst*1000})
		get_viewport().get_texture().get_image().save_png(_source+"evidence/source_"+_sex+"_expression_"+name_+".png")
		mi.set_blend_shape_value(shape,0)
		_groom_expression(name_,0)
		await RenderingServer.frame_post_draw
	return records

func _groom_expression(name_: String,value: float) -> void:
	for groom in _all(_root,"MeshInstance3D"):
		if not groom.is_in_group("mh_facial_grooms"):continue
		var index: int=groom.find_blend_shape_by_name(name_)
		if index>=0:groom.set_blend_shape_value(index,value)

func _owner_evidence() -> void:
	var directory := OS.get_environment("OTHERREACH_MH_EVIDENCE_DIR").replace("\\","/")
	if directory.is_empty():directory=OS.get_executable_path().get_base_dir()+"/evidence/owner_closeups"
	DirAccess.make_dir_recursive_absolute(directory)
	var captures := []
	for sex in ["feminine","masculine"]:
		_load_character(sex)
		await get_tree().process_frame
		await RenderingServer.frame_post_draw
		for area in AREAS:
			_set_area(area)
			for view in [["front",0.0],["side",PI/2],["three_quarter",PI/4]]:
				_view_angle=view[1]
				_camera_pose()
				await RenderingServer.frame_post_draw
				await RenderingServer.frame_post_draw
				var filename: String=sex+"_"+area+"_"+view[0]+".png"
				assert(get_viewport().get_texture().get_image().save_png(directory+"/"+filename)==OK)
				captures.append(filename)
		_set_area("face")
		_view_angle=0
		_camera_pose()
		for mi in _all(_root,"MeshInstance3D"):
			var shape: int=mi.find_blend_shape_by_name("jawOpen")
			if shape>=0:mi.set_blend_shape_value(shape,1)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		var filename: String=sex+"_teeth_jawOpen.png"
		assert(get_viewport().get_texture().get_image().save_png(directory+"/"+filename)==OK)
		captures.append(filename)
	var report := {"executable":OS.get_executable_path(),"packaged":not OS.has_feature("editor"),"asset_root":_source,"captures":captures,"owner_approval":"pending","clothing":"paused; native base underwear"}
	var file := FileAccess.open(directory+"/capture_manifest.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"  "))
	print("OWNER_CAPTURE_COMPLETE ",JSON.stringify(report))
	get_tree().quit()

func _audit_weights(mi: MeshInstance3D) -> Dictionary:
	var eight_count := 0
	var max_error := 0.0
	var count := 0
	for surface in mi.mesh.get_surface_count():
		var arrays: Array=mi.mesh.surface_get_arrays(surface)
		var weights: PackedFloat32Array=arrays[Mesh.ARRAY_WEIGHTS]
		var vertices: PackedVector3Array=arrays[Mesh.ARRAY_VERTEX]
		assert(not vertices.is_empty())
		var stride: int=weights.size()/vertices.size()
		assert(stride==4 or stride==8)
		for vertex in vertices.size():
			var total := 0.0
			var nonzero := 0
			for influence in stride:
				var weight: float=weights[vertex*stride+influence]
				total+=weight
				if weight>0:nonzero+=1
			max_error=maxf(max_error,absf(total-1.0))
			if nonzero==8:eight_count+=1
		count+=vertices.size()
	return {"mesh":mi.name,"vertices":count,"max_weight_sum_error":max_error,"eight_influence_vertices":eight_count}
