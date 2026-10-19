extends SceneTree
## Diagnostic import audit. File checks alone never pass G1-G4.
var _records := []
var _fails := 0

func _init() -> void:
	call_deferred("_run")

func _all(root_node: Node, cls: String) -> Array:
	var nodes := [root_node]
	var result := []
	while not nodes.is_empty():
		var n: Node = nodes.pop_back()
		if n.is_class(cls): result.append(n)
		nodes.append_array(n.get_children())
	return result

func _run() -> void:
	var args := OS.get_cmdline_user_args()
	var sex: String = args[0] if not args.is_empty() else "feminine"
	var route: String = args[1] if args.size() > 1 else "blender_gltf"
	var base := "D:/Otherreach_Scratch/mh_slice_20261019/"
	var expected: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(base + "evidence/ue_native_bones.json"))
	for part in ["body", "head"]:
		var path: String = base + "glb/" + route + "/" + sex + "/" + part + ".glb"
		if route=="slice":path=base+"glb/"+("rom" if part=="body" else "cinematic_arkit")+"/"+sex+"/"+part+".glb"
		var doc := GLTFDocument.new()
		var state := GLTFState.new()
		var err := doc.append_from_file(path, state)
		assert(err == OK, "GLB read failed: " + path)
		var scene := doc.generate_scene(state)
		root.add_child(scene)
		var skels := _all(scene, "Skeleton3D")
		assert(skels.size() == 1)
		var sk: Skeleton3D = skels[0]
		var source: Dictionary = {}
		for item in expected.sources:
			if item.sex.to_lower() == sex and item.part.to_lower() == part: source = item
		assert(not source.is_empty())
		var missing := []
		var wrong_parent := []
		for bone in source.bones:
			var index: int = sk.find_bone(bone.name)
			if index < 0:
				missing.append(bone.name)
				continue
			var parent: int = sk.get_bone_parent(index)
			var actual_parent: String = sk.get_bone_name(parent) if parent >= 0 else "None"
			if actual_parent != bone.parent: wrong_parent.append([bone.name, bone.parent, actual_parent])
		var meshes := []
		for node in _all(scene, "MeshInstance3D"):
			var mi: MeshInstance3D = node
			if not mi.mesh: continue
			var surface_data := []
			for s in range(mi.mesh.get_surface_count()):
				var a: Array = mi.mesh.surface_get_arrays(s)
				var vertices: PackedVector3Array = a[Mesh.ARRAY_VERTEX]
				var weights: PackedFloat32Array = a[Mesh.ARRAY_WEIGHTS]
				var weight_width: int = weights.size() / vertices.size() if vertices.size() else 0
				surface_data.append({"vertices":vertices.size(), "weight_width":weight_width})
			meshes.append({"name":mi.name,"shape_keys":mi.get_blend_shape_count(),"surfaces":surface_data})
		var passed: bool = missing.is_empty() and wrong_parent.is_empty() and sk.get_bone_count() == int(source.bone_count)
		if route=="slice" and part=="head":
			var named_face: MeshInstance3D
			for node in _all(scene,"MeshInstance3D"):
				if node.get_blend_shape_count()>=40:named_face=node
			assert(named_face!=null)
			for sentinel in ["jawOpen","eyeBlinkLeft","mouthSmileLeft","noseSneerLeft","tongueOut"]:
				assert(named_face.find_blend_shape_by_name(sentinel)>=0,"Missing native ARKit sentinel "+sentinel)
			assert(named_face.mesh.get_surface_count()==8,"UE5.8 semantic surfaces changed; repeated M_Hide currently merges to one surface")
		if not passed: _fails += 1
		var row := {"sex":sex,"part":part,"ue_bone_count":source.bone_count,"godot_bone_count":sk.get_bone_count(),"missing_bones":missing,"wrong_parents":wrong_parent,"meshes":meshes,"native_skeleton_preserved":passed}
		_records.append(row)
		print("NATIVE_IMPORT ", JSON.stringify(row))
		scene.queue_free()
	var out := FileAccess.open(base + "evidence/godot_" + route + "_import_" + sex + ".json", FileAccess.WRITE)
	out.store_string(JSON.stringify({"imports":_records,"gates_passed":[],"structural_failures":_fails},"  "))
	quit(1 if _fails else 0)


