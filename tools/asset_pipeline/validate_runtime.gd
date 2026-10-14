extends MainLoop

# Loads converted assets the way the game does - GLTFDocument.append_from_file on the file where it lies, exactly as
# ArtLibrary.LoadScene - and checks what ArtLibrary relies on, for the base model and every _lodN:
#   * the glTF loads and generates a scene with at least one MeshInstance3D
#   * every surface carries a BaseMaterial3D with an albedo texture (ArtLibrary.Textured: an untextured level ends
#     the LOD chain)
#   * occlusion, roughness and metallic resolve to ONE texture (the shipped ORM convention)
#   * the base sits on Y = 0 and is centred on X/Z, and the mesh resource name carries the asset id
#   * each LOD has fewer triangles than the level before it
#
# Run by validate_godot.py; directly:
#   godot --headless --path <probe project> --script res://validate_runtime.gd -- <ready/<id> folder> ...

var _done := false


func _initialize() -> void:
	var all_ok := true
	for folder in OS.get_cmdline_user_args():
		var report := _check_asset(folder)
		all_ok = all_ok and report["ok"]
		print("RUNTIME_RESULT " + JSON.stringify(report))
	print("RUNTIME_DONE " + ("ok" if all_ok else "failed"))
	_done = true


func _check_asset(folder: String) -> Dictionary:
	var id := folder.trim_suffix("/").trim_suffix("\\").get_file()
	var report := {"asset_id": id, "ok": true, "problems": [], "levels": []}
	var previous := -1
	for n in range(0, 4):
		var path := folder.path_join(id + (".glb" if n == 0 else "_lod%d.glb" % n))
		if not FileAccess.file_exists(path):
			if n == 0:
				report["ok"] = false
				report["problems"].append("no " + path.get_file())
			break
		var level := _check_file(path, id, n == 0)
		report["levels"].append(level)
		for p in level["problems"]:
			report["problems"].append("%s: %s" % [path.get_file(), p])
		if previous >= 0 and level["triangles"] >= previous:
			report["problems"].append("%s has %d triangles, not fewer than the level before (%d)" % [path.get_file(), level["triangles"], previous])
		previous = level["triangles"]
	report["ok"] = report["problems"].is_empty()
	return report


func _check_file(path: String, id: String, is_base: bool) -> Dictionary:
	var out := {"file": path.get_file(), "problems": [], "triangles": 0}
	var started := Time.get_ticks_msec()
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	var err := doc.append_from_file(path, state)
	var root: Node = doc.generate_scene(state) if err == OK else null
	if root == null:
		out["problems"].append("the glTF would not load (%s)" % error_string(err))
		return out
	out["load_ms"] = Time.get_ticks_msec() - started
	var meshes: Array = root.find_children("*", "MeshInstance3D", true, false)
	if meshes.is_empty():
		out["problems"].append("no MeshInstance3D")
		root.free()
		return out
	var aabb := AABB()
	var first := true
	var textures := {}
	var orm_shared := true
	var normal_mapped := true
	for mi in meshes:
		var mesh: Mesh = mi.mesh
		var box: AABB = _global(mi) * mi.get_aabb()
		aabb = box if first else aabb.merge(box)
		first = false
		if is_base and mesh.resource_name.find(id) == -1:
			out["problems"].append("mesh resource '%s' does not carry the asset id" % mesh.resource_name)
		for s in range(mesh.get_surface_count()):
			var arrays := mesh.surface_get_arrays(s)
			var idx: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
			out["triangles"] += (idx.size() if idx.size() > 0 else (arrays[Mesh.ARRAY_VERTEX] as PackedVector3Array).size()) / 3
			var mat := mi.get_active_material(s) as BaseMaterial3D
			if mat == null or mat.albedo_texture == null:
				out["problems"].append("surface %d has no albedo texture (ArtLibrary would stop the LOD chain here)" % s)
				continue
			textures["albedo"] = [mat.albedo_texture.get_width(), mat.albedo_texture.get_height()]
			if mat.normal_texture != null:
				textures["normal"] = [mat.normal_texture.get_width(), mat.normal_texture.get_height()]
			else:
				normal_mapped = false
			if mat.ao_texture == null or mat.ao_texture != mat.roughness_texture or mat.roughness_texture != mat.metallic_texture:
				orm_shared = false
			else:
				textures["orm"] = [mat.ao_texture.get_width(), mat.ao_texture.get_height()]
	out["textures"] = textures
	out["orm_shared"] = orm_shared
	out["normal_mapped"] = normal_mapped
	out["aabb_size_m"] = [snappedf(aabb.size.x, 0.0001), snappedf(aabb.size.y, 0.0001), snappedf(aabb.size.z, 0.0001)]
	out["aabb_min_m"] = [snappedf(aabb.position.x, 0.0001), snappedf(aabb.position.y, 0.0001), snappedf(aabb.position.z, 0.0001)]
	if not orm_shared:
		out["problems"].append("occlusion/roughness/metallic are not one ORM texture")
	# LOD0 is origined exactly; a decimated level keeps LOD0's frame (re-origining it would pop), so it may drift a little.
	var tol := 0.002 if is_base else 0.01 * maxf(aabb.size.x, maxf(aabb.size.y, aabb.size.z))
	if absf(aabb.position.y) > tol:
		out["problems"].append("base is at Y %.4f, not on the ground plane" % aabb.position.y)
	var centre := aabb.get_center()
	if absf(centre.x) > tol or absf(centre.z) > tol:
		out["problems"].append("footprint centre is at X %.4f Z %.4f, not the origin" % [centre.x, centre.z])
	root.free()
	return out


func _global(node: Node) -> Transform3D:
	var t := Transform3D.IDENTITY
	var current := node
	while current is Node3D:
		t = (current as Node3D).transform * t
		current = current.get_parent()
	return t


func _process(_delta: float) -> bool:
	return _done
