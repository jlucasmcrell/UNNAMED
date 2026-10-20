extends SceneTree
## Run with OTHERREACH_MH_CHARACTERS_ROOT pointing to the staged runtime bundle.
const NativeCharacter = preload("../NativeCharacter.gd")
var failures: Array[String] = []
var measured: Array = []

func _initialize() -> void:
	call_deferred("_verify")

func _check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		push_error(message)

func _verify() -> void:
	var source := OS.get_environment("OTHERREACH_MH_CHARACTERS_ROOT").replace("\\", "/").trim_suffix("/") + "/"
	var catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(source + "catalog.json"))
	var shared := {}
	for id in catalog.characters.keys() + ["ada"]:
		var actor := NativeCharacter.new()
		root.add_child(actor)
		# Independent native import before configure: compare values, not two aliases of one mesh.
		var untouched := actor._load_model(source + catalog.characters[id].body)
		var expected := []
		for mesh: MeshInstance3D in actor._all(untouched, "MeshInstance3D"):
			var surfaces := []
			for surface in mesh.mesh.get_surface_count():
				surfaces.append({"arrays": mesh.mesh.surface_get_arrays(surface).duplicate(true), "raw": RenderingServer.mesh_get_surface(mesh.mesh.get_rid(), surface)})
			expected.append(surfaces)
		untouched.free()
		_check(actor.configure(source, catalog.characters[id], shared), "Native character load: " + id)
		await process_frame
		_check(actor._body.get_bone_count() == int(catalog.characters[id].get("body_bones", 342)), id + " native body rig changed")
		_check(actor._face.get_bone_count() == int(catalog.characters[id].get("face_bones", 875)), id + " native face rig changed")
		var mesh_index := 0
		for body in actor._body_meshes:
			var original: Mesh = body.get_meta("native_source_mesh")
			_check(body.mesh == original, id + " native body mesh resource was replaced")
			for surface in original.get_surface_count():
				var before: Array = expected[mesh_index][surface].arrays
				var after := body.mesh.surface_get_arrays(surface)
				for field in [Mesh.ARRAY_VERTEX, Mesh.ARRAY_NORMAL, Mesh.ARRAY_TANGENT, Mesh.ARRAY_BONES, Mesh.ARRAY_WEIGHTS]:
					_check(before[field] == after[field], id + " native body data changed by clothing mask: " + str(field))
				var raw_before: Dictionary = expected[mesh_index][surface].raw
				var raw_after := RenderingServer.mesh_get_surface(body.mesh.get_rid(), surface)
				for buffer in ["vertex_data", "attribute_data", "skin_data", "index_data", "blend_shape_data"]:
					_check(raw_before.get(buffer) == raw_after.get(buffer), id + " native raw buffer changed: " + buffer)
			mesh_index += 1
		var semantic := {"skin": false, "teeth": false, "eye_l": false, "eye_r": false}
		for mesh: MeshInstance3D in actor._all(actor._face, "MeshInstance3D"):
			for surface in mesh.mesh.get_surface_count():
				var material := mesh.mesh.surface_get_material(surface)
				var label := material.resource_name.to_lower() if material else ""
				semantic.skin = semantic.skin or "face_skin" in label or "graytexture_head" in label
				semantic.teeth = semantic.teeth or "teeth" in label
				semantic.eye_l = semantic.eye_l or "eyel_" in label
				semantic.eye_r = semantic.eye_r or "eyer_" in label
		for required in semantic:
			_check(semantic[required], id + " native face lacks required material surface: " + required)
		var garments := 0
		for mesh: MeshInstance3D in actor._all(actor._body, "MeshInstance3D"):
			if mesh in actor._body_meshes: continue
			garments += 1
			_check(mesh.get_node(mesh.skeleton) == actor._body, id + " garment has a different skeleton")
			for bind in mesh.skin.get_bind_count():
				_check(actor._body.find_bone(mesh.skin.get_bind_name(bind)) >= 0, id + " unmatched garment bind")
		_check(garments >= ((3 if catalog.characters[id].get("hood", true) else 2) + (2 if catalog.characters[id].get("shells", "") != "" else 0)), id + " accepted clothing missing")
		_check(actor._sync._pairs.size() == 32, id + " shared head/body sync changed")
		var points := PackedVector3Array()
		var native_meshes: Array = actor._body_meshes.duplicate()
		for mesh: MeshInstance3D in actor._all(actor._face, "MeshInstance3D"):
			if mesh.skin != null and not mesh.skeleton.is_empty() and mesh.get_node(mesh.skeleton) == actor._face: native_meshes.append(mesh)
		for mesh: MeshInstance3D in native_meshes:
			var local: Transform3D = actor._root.global_transform.affine_inverse() * mesh.global_transform
			for surface in mesh.mesh.get_surface_count():
				for vertex: Vector3 in mesh.mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]: points.append(local * vertex)
		var dimensions: Dictionary = actor.Measurements.measure(points)
		var file_name := "ue_source_measurements.json" if id in ["ada", "mateo"] else "ue_npc_measurements.json"
		var baseline := {}
		if FileAccess.file_exists(source + "evidence/" + file_name):
			var ue: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(source + "evidence/" + file_name))
			for row in ue.characters:
				if id in ["ada", "mateo"] and row.get("sex", "") == catalog.characters[id].sex: baseline = row.measurements_m
				elif row.get("id", "") == id: baseline = row.selected_lod_measurements_m
		var errors := {}
		for key in baseline:
			errors[key] = 100.0 * absf(dimensions[key] - baseline[key]) / baseline[key]
			_check(errors[key] <= .5, id + " native UE/Godot proportion drift: " + key + " " + str(errors[key]) + "%")
		measured.append({"id": id, "measurements_m": dimensions, "ue_measurements_m": baseline, "errors_percent": errors, "rendered_gate_pending": true})
		if actor.has_clip("idle"):
			_check(actor.sample_clip("idle", actor.clip_length("idle") * .5), id + " native idle sample failed")
			var pelvis := actor._body.find_bone("pelvis")
			var rest_height := actor._body.get_bone_global_rest(pelvis).origin.y
			var idle_height := actor._body.get_bone_global_pose(pelvis).origin.y
			_check(idle_height > rest_height * .5, id + " idle pelvis collapsed to root; check UE root-motion bone separately from retarget pelvis")
			measured[-1]["idle_pelvis_height_m"] = idle_height
			measured[-1]["rest_pelvis_height_m"] = rest_height
		actor.queue_free()
		await process_frame
	var report := FileAccess.open(source + "evidence/godot_rest_measurements.json", FileAccess.WRITE)
	report.store_string(JSON.stringify(measured, "  "))
	report.close()
	print("MH_NATIVE_CHARACTER_CHECK failures=%d %s" % [failures.size(), failures])
	quit(0 if failures.is_empty() else 1)
