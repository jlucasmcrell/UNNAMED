extends SceneTree
## Run with OTHERREACH_MH_CHARACTERS_ROOT pointing to the staged runtime bundle.
const NativeCharacter = preload("../NativeCharacter.gd")
var failures: Array[String] = []

func _initialize() -> void:
	call_deferred("_verify")

func _check(condition: bool, message: String) -> void:
	if not condition:
		failures.append(message)
		push_error(message)

func _verify() -> void:
	var source := OS.get_environment("OTHERREACH_MH_CHARACTERS_ROOT").replace("\\", "/").trim_suffix("/") + "/"
	var catalog: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(source + "catalog.json"))
	for id in ["ada", "mateo"]:
		var actor := NativeCharacter.new()
		root.add_child(actor)
		_check(actor.configure(source, catalog.characters[id]), "Native character load: " + id)
		await process_frame
		_check(actor._body.get_bone_count() == 342, id + " native body rig changed")
		_check(actor._face.get_bone_count() == 875, id + " native face rig changed")
		for body in actor._body_meshes:
			var original: Mesh = body.get_meta("native_source_mesh")
			_check(body.mesh == original, id + " native body mesh resource was replaced")
			for surface in original.get_surface_count():
				var before := original.surface_get_arrays(surface)
				var after := body.mesh.surface_get_arrays(surface)
				for field in [Mesh.ARRAY_VERTEX, Mesh.ARRAY_NORMAL, Mesh.ARRAY_TANGENT, Mesh.ARRAY_BONES, Mesh.ARRAY_WEIGHTS]:
					_check(before[field] == after[field], id + " native body data changed by clothing mask: " + str(field))
		var garments := 0
		for mesh: MeshInstance3D in actor._all(actor._body, "MeshInstance3D"):
			if mesh in actor._body_meshes: continue
			garments += 1
			_check(mesh.get_node(mesh.skeleton) == actor._body, id + " garment has a different skeleton")
			for bind in mesh.skin.get_bind_count():
				_check(actor._body.find_bone(mesh.skin.get_bind_name(bind)) >= 0, id + " unmatched garment bind")
		_check(garments >= 3, id + " accepted clothing missing")
		_check(actor._sync._pairs.size() == 32, id + " shared head/body sync changed")
		actor.queue_free()
		await process_frame
	print("MH_NATIVE_CHARACTER_CHECK failures=%d %s" % [failures.size(), failures])
	quit(0 if failures.is_empty() else 1)
