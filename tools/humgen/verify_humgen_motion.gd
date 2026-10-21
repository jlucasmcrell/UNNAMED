extends SceneTree
## Gate for the baked HumGen motion: what the library actually does to the body it was baked for.
##
## usage: godot --headless --path <minimal project> --script verify_humgen_motion.gd -- <body.glb> <lib.res> <out.json>

func find_type(node: Node, type: String) -> Node:
	if node.is_class(type):
		return node
	for child in node.get_children():
		var found := find_type(child, type)
		if found:
			return found
	return null


func glb(path: String) -> Node:
	var document := GLTFDocument.new()
	var state := GLTFState.new()
	assert(document.append_from_file(path, state) == OK, path)
	return document.generate_scene(state)


func _initialize() -> void:
	call_deferred("run")


func run() -> void:
	var args := OS.get_cmdline_user_args()
	var body_path: String = args[0]
	var library_path: String = args[1]
	var out_path: String = args[2]
	var body := glb(body_path)
	root.add_child(body)
	var rig: Skeleton3D = find_type(body, "Skeleton3D")
	var library: AnimationLibrary = ResourceLoader.load(library_path)
	assert(library != null, library_path)
	var report := {"body": body_path, "library": library_path,
		"sha_matches": library.get_meta("target_body_sha256", "") == FileAccess.get_sha256(body_path),
		"states": [], "paces": library.get_meta("motion_paces", {})}
	var contact := ["foot_l", "foot_r", "ball_l", "ball_r"]
	var rest_low := INF
	for bone in contact:
		var index := rig.find_bone(bone)
		if index >= 0:
			rest_low = minf(rest_low, rig.get_bone_global_rest(index).origin.y)
	report["rest_low_m"] = rest_low
	var player := AnimationPlayer.new()
	rig.add_child(player)
	player.root_node = player.get_path_to(body)
	player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
	var prefix := str(body.get_path_to(rig))
	for state in library.get_animation_list():
		var animation: Animation = library.get_animation(state).duplicate(true)
		for t in animation.get_track_count():
			animation.track_set_path(t, NodePath(prefix + ":" + animation.track_get_path(t).get_subname(0)))
		var missing := 0
		var bones := 0
		for t in animation.get_track_count():
			var bone := animation.track_get_path(t).get_subname(0)
			if bone == "" or rig.find_bone(bone) < 0:
				missing += 1
			else:
				bones += 1
		var probe := AnimationLibrary.new()
		probe.add_animation("probe", animation)
		player.add_animation_library(state, probe)
		var frames := maxi(8, roundi(animation.length * 60.0))
		var low_min := INF
		var low_max := -INF
		var pelvis_min := INF
		var pelvis_max := -INF
		for f in frames + 1:
			player.play(state + "/probe", 0)
			player.seek(animation.length * float(f) / float(frames), true)
			player.advance(0)
			var low := INF
			for bone in contact:
				var index := rig.find_bone(bone)
				if index >= 0:
					low = minf(low, rig.get_bone_global_pose(index).origin.y)
			low_min = minf(low_min, low)
			low_max = maxf(low_max, low)
			var pelvis := rig.get_bone_global_pose(rig.find_bone("pelvis")).origin.y
			pelvis_min = minf(pelvis_min, pelvis)
			pelvis_max = maxf(pelvis_max, pelvis)
		report["states"].append({"state": state, "seconds": animation.length,
			"tracks": animation.get_track_count(), "bone_tracks": bones, "missing_tracks": missing,
			"loop": animation.loop_mode, "foot_low_min": low_min, "foot_low_max": low_max,
			"pelvis_min": pelvis_min, "pelvis_max": pelvis_max,
			"penetration_m": maxf(0.0, rest_low - low_min)})
	rig.remove_child(player)
	player.free()
	var file_out := FileAccess.open(out_path, FileAccess.WRITE)
	file_out.store_string(JSON.stringify(report, "  "))
	file_out.close()
	print("VERIFY_HUMGEN_STATES ", library.get_animation_list().size(), " sha_matches=", report["sha_matches"],
		" rest_low=", snappedf(rest_low, 0.0001))
	for row in report["states"]:
		print("STATE ", row.state.rpad(16), " sec=", snappedf(row.seconds, 0.01),
			" tracks=", row.tracks, " bone=", row.bone_tracks, " missing=", row.missing_tracks,
			" foot_low=", snappedf(row.foot_low_min, 0.001), "..", snappedf(row.foot_low_max, 0.001),
			" pelvis=", snappedf(row.pelvis_min, 0.001), "..", snappedf(row.pelvis_max, 0.001),
			" penetration=", snappedf(row.penetration_m, 0.001))
	quit()




