## Headless runtime-load check for the animated-armour v2 GLB, through the same GLTFDocument path the game's ArtLibrary uses.
## godot --headless --path <any project> --script godot_check_armour.gd -- <model.glb> <clips_dir>
## Reports: skeleton bones, meshes/surfaces/materials/textures/tangents, and for each of the six clips whether every
## track resolves to a bone of this skeleton and how far the clip skeleton's rest differs from this one.
extends SceneTree


func load_glb(path: String) -> Node:
	var doc := GLTFDocument.new()
	var st := GLTFState.new()
	var err := doc.append_from_file(path, st)
	if err != OK:
		print("LOAD_FAIL ", path, " err ", err)
		return null
	return doc.generate_scene(st)


func find_class(node: Node, cls: String) -> Node:
	if node.is_class(cls):
		return node
	for c in node.get_children():
		var r := find_class(c, cls)
		if r:
			return r
	return null


func collect(node: Node, cls: String, out: Array) -> void:
	if node.is_class(cls):
		out.append(node)
	for c in node.get_children():
		collect(c, cls, out)


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var model_path: String = args[0]
	var clips_dir: String = args[1]
	var ok := true
	var root := load_glb(model_path)
	if root == null:
		quit(1)
		return
	var sk: Skeleton3D = find_class(root, "Skeleton3D")
	print("skeleton ", sk.name, " bones ", sk.get_bone_count())
	var meshes := []
	collect(root, "MeshInstance3D", meshes)
	for m in meshes:
		var mi: MeshInstance3D = m
		var mesh: Mesh = mi.mesh
		var aabb := mi.get_aabb()
		print("mesh ", mi.name, " surfaces ", mesh.get_surface_count(), " skin ", mi.skin != null, " aabb ", aabb)
		for s in mesh.get_surface_count():
			var mat: Material = mesh.surface_get_material(s)
			var fmt: int = mesh.surface_get_format(s)
			var line := "  surface %d %s '%s' verts %d tangents %s" % [s, mat.get_class(), mat.resource_name, mesh.surface_get_array_len(s), str((fmt & Mesh.ARRAY_FORMAT_TANGENT) != 0)]
			if mat is BaseMaterial3D:
				var bm: BaseMaterial3D = mat
				line += " albedo_tex %s normal %s rough_tex %s metal_tex %s ao %s" % [str(bm.albedo_texture != null), str(bm.normal_enabled and bm.normal_texture != null), str(bm.roughness_texture != null), str(bm.metallic_texture != null), str(bm.ao_enabled)]
				if bm is ORMMaterial3D:
					line += " orm_tex %s" % str((bm as ORMMaterial3D).orm_texture != null)
			print(line)
	for clip in ["idle", "walk", "run", "attack", "hit", "death"]:
		var croot := load_glb(clips_dir.path_join("anim.creature.animated_armour.%s.glb" % clip))
		if croot == null:
			ok = false
			continue
		var ap: AnimationPlayer = find_class(croot, "AnimationPlayer")
		var csk: Skeleton3D = find_class(croot, "Skeleton3D")
		var anim: Animation = ap.get_animation(ap.get_animation_list()[0])
		var worst_o := 0.0
		var worst_q := 0.0
		var missing := 0
		for i in csk.get_bone_count():
			var j := sk.find_bone(csk.get_bone_name(i))
			if j < 0:
				missing += 1
				continue
			var a := csk.get_bone_rest(i)
			var b := sk.get_bone_rest(j)
			worst_o = max(worst_o, (a.origin - b.origin).length())
			# elementwise basis difference (quaternion angle_to is float32 acos noise near 1)
			for c in 3:
				worst_q = max(worst_q, (a.basis[c] - b.basis[c]).length())
		var unresolved := 0
		for t in anim.get_track_count():
			var p := anim.track_get_path(t)
			var bone := String(p.get_concatenated_subnames())
			if bone == "" or sk.find_bone(bone) < 0:
				unresolved += 1
		print("clip %s length %.3f tracks %d unresolved %d missing_bones %d rest_max_offset %.7f rest_max_angle %.7f" % [clip, anim.length, anim.get_track_count(), unresolved, missing, worst_o, worst_q])
		if unresolved > 0 or missing > 0 or worst_o > 1e-4 or worst_q > 1e-4:
			ok = false
		croot.free()
	print("GODOT_CHECK ", "PASS" if ok else "FAIL")
	root.free()
	quit(0 if ok else 2)
