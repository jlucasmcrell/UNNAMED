@tool
extends EditorScenePostImport

# Godot 3.2's glTF importer renamed bones ("thigh_R" -> "thigh_r", "spine3" -> "spine_3"), and the
# scenes, presets, saves and scripts all use those names. Godot 4 keeps the names from the file,
# so the rig and every part mesh are renamed back the same way: bones, skin binds and animation tracks.

func _post_import(scene: Node) -> Object:
	var names := {}  # bone name as imported -> Godot 3.2 name
	_rename_bones(scene, _node_names(scene, {}), names)
	_rename_uses(scene, names)
	return scene

static func godot3_name(bone: String) -> String:
	# hair0.glb comes from an armature with dotted names ("spine.001", "upper_arm.L"), so dots and
	# zero padding are normalised too: "spine.001" and "spine1" both become "spine_1".
	var renamed := bone.replace(".", "_")
	renamed = RegEx.create_from_string("([A-Za-z])(\\d)").sub(renamed, "$1_$2", true)
	renamed = RegEx.create_from_string("(^|[^0-9])0+(\\d)").sub(renamed, "$1$2", true)
	renamed = RegEx.create_from_string("_+").sub(renamed, "_", true)
	return renamed.to_lower()

func _node_names(node: Node, into: Dictionary) -> Dictionary:
	into[str(node.name)] = true
	for child in node.get_children():
		_node_names(child, into)
	return into

func _rename_bones(node: Node, node_names: Dictionary, names: Dictionary) -> void:
	if node is Skeleton3D:
		for i in node.get_bone_count():
			var bone: String = node.get_bone_name(i)
			# Godot 4 makes node and bone names unique together: head.glb's "head" bone shares its
			# mesh's name and arrives as "head_2".
			var base := bone.rstrip("0123456789").rstrip("_")
			if base != bone and node_names.has(base) and node.find_bone(base) < 0:
				names[bone] = godot3_name(base)
			else:
				names[bone] = godot3_name(bone)
		for i in node.get_bone_count():
			node.set_bone_name(i, names[node.get_bone_name(i)])
	for child in node.get_children():
		_rename_bones(child, node_names, names)

func _rename_uses(node: Node, names: Dictionary) -> void:
	if node is MeshInstance3D and node.skin:
		for i in node.skin.get_bind_count():
			var bind := str(node.skin.get_bind_name(i))
			node.skin.set_bind_name(i, names.get(bind, godot3_name(bind)))
	if node is AnimationPlayer:
		for library in node.get_animation_library_list():
			for animation_name in node.get_animation_library(library).get_animation_list():
				var animation: Animation = node.get_animation_library(library).get_animation(animation_name)
				for t in animation.get_track_count():
					var path := animation.track_get_path(t)
					if path.get_subname_count() == 1:
						var bone := str(path.get_concatenated_subnames())
						animation.track_set_path(t, NodePath(str(path.get_concatenated_names()) + ":" + names.get(bone, godot3_name(bone))))
	for child in node.get_children():
		_rename_uses(child, names)
