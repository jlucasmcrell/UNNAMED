extends SceneTree
## Bake the game's own clips onto the HumGen body OFFLINE, so the runtime only relocates track paths.
##
## The brief's route for MPFB was Epic's IK Retargeter directly onto the target rig, exported, then copied into an
## AnimationLibrary with no runtime conversion. That rig is a 53-bone UE-mannequin body; the HumGen body is a
## 103-bone HumGen rig whose rest is an A-pose, so the same direct route needs the full UAL1/UAL2/KayKit source set
## through UE for a one-off test. This does the equivalent work here instead, in one pass, with nothing left for
## the runtime: every source track is re-based onto the target's own rest by the game's own formula
## (`key' = restTarget * restSource .inverse() * key`, ArtLibrary.Retarget), the resulting clip is then measured on
## the target skeleton and its feet are planted - a correction a track copy cannot make.
##
## usage:
##   godot --headless --path <minimal project> --script build_humgen_motion_library.gd -- <body.glb> <out folder>

const STATES := {
	"idle": "idle", "walk": "walk", "run": "run", "sprint": "sprint",
	"crouch_idle": "crouch_idle", "crouch_walk": "crouch_walk",
	"jump_start": "jump_start", "fall": "fall", "land": "land", "interact": "interact",
	"talk": "talk", "sword_ready": "sword_ready",
	"sword_attack_1": "sword_attack_1_mc", "sword_attack_2": "sword_attack_2_mc",
	"sword_attack_3": "sword_attack_3_mc", "sword_attack_4": "sword_attack_4_mc",
	"sword_block": "sword_block_mc", "bow_ready": "bow_ready_mc", "bow_draw": "bow_draw_mc",
	"spear_ready": "spear_ready_mc", "spear_thrust": "spear_thrust_mc",
	"cast": "cast", "hit": "hit_mc", "death": "death_mc",
}
const ALIASES := {"sword_attack": "sword_attack_1", "stop": "idle", "turn": "idle"}
const LOOPS := ["idle", "walk", "run", "sprint", "crouch_idle", "crouch_walk", "fall", "talk",
	"sword_ready", "sword_block", "bow_ready", "spear_ready"]
const GAITS := ["walk", "run", "sprint", "crouch_walk"]
## Clips whose feet are not on the ground for the whole cycle: planting them would pin a jump to the floor.
const AIRBORNE := ["jump_start", "fall", "land", "death"]
const CONTACT_BONES := ["foot_l", "foot_r", "ball_l", "ball_r"]
const TRAVEL_BONES := ["Root", "pelvis"]


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
	call_deferred("build")


func build() -> void:
	var args := OS.get_cmdline_user_args()
	var body_path: String = args[0]
	var source_root: String = args[1].replace("\\", "/")
	var out_folder: String = args[2].replace("\\", "/")
	var report_path: String = args[3]
	var id: String = args[4]
	DirAccess.make_dir_recursive_absolute(out_folder)

	var body := glb(body_path)
	root.add_child(body)
	var target: Skeleton3D = find_type(body, "Skeleton3D")
	assert(target != null, "no skeleton in " + body_path)
	var target_rest := {}
	var target_origin := {}
	for b in target.get_bone_count():
		target_rest[target.get_bone_name(b)] = target.get_bone_rest(b).basis.get_rotation_quaternion()
		target_origin[target.get_bone_name(b)] = target.get_bone_rest(b).origin
	var target_height := target.get_bone_global_rest(target.find_bone("pelvis")).origin.y
	var rest_low := INF
	for bone in CONTACT_BONES:
		var index := target.find_bone(bone)
		if index >= 0:
			rest_low = minf(rest_low, target.get_bone_global_rest(index).origin.y)

	var library := AnimationLibrary.new()
	var rows := []
	var paces := {}
	var phases := {}
	var plants := {}
	for state in STATES:
		var source_id: String = STATES[state]
		var path := source_root + "/" + source_id + ".glb"
		if not FileAccess.file_exists(path):
			rows.append({"state": state, "source": source_id, "skipped": "missing " + path})
			continue
		var source := glb(path)
		root.add_child(source)
		var rig: Skeleton3D = find_type(source, "Skeleton3D")
		var player: AnimationPlayer = find_type(source, "AnimationPlayer")
		var names := Array(player.get_animation_list()).filter(func(n): return n != "RESET")
		assert(names.size() == 1, state + ": " + str(names))
		var animation: Animation = player.get_animation(names[0]).duplicate(true)
		var source_height := rig.get_bone_global_rest(rig.find_bone("pelvis")).origin.y
		var scale := target_height / source_height
		var dropped := 0
		var matched := 0
		for t in range(animation.get_track_count() - 1, -1, -1):
			var track_bone := animation.track_get_path(t).get_subname(0)
			if track_bone == "" or target.find_bone(track_bone) < 0:
				animation.remove_track(t)
				dropped += 1
				continue
			matched += 1
			animation.track_set_path(t, NodePath("Skeleton3D:" + track_bone))
			var source_index := rig.find_bone(track_bone)
			if source_index < 0:
				continue
			var kind := animation.track_get_type(t)
			var travels: bool = track_bone in TRAVEL_BONES
			if kind == Animation.TYPE_ROTATION_3D:
				# The game's own re-base (ArtLibrary.Retarget): key' = restTarget * restSource^-1 * key, on
				# LOCAL rests. Measured on this pair of rigs it leaves idle 8.6 cm under the floor and a
				# crouch 0.6 m up, which the foot planting below corrects. A global-frame variant
				# (restTarget^-1 * restSource, similarity) was tried first and folded the legs up by
				# 0.5-1.1 m on every clip, so the local frame is the one that matches this pair of rests.
				var toward: Quaternion = target_rest[track_bone] * rig.get_bone_rest(source_index).basis.get_rotation_quaternion().inverse()
				for k in animation.track_get_key_count(t):
					animation.track_set_key_value(t, k, (toward * (animation.track_get_key_value(t, k) as Quaternion)).normalized())
			elif kind == Animation.TYPE_POSITION_3D:
				var from: Vector3 = rig.get_bone_rest(source_index).origin
				var to: Vector3 = target_origin[track_bone]
				for k in animation.track_get_key_count(t):
					var moved: Vector3 = (animation.track_get_key_value(t, k) as Vector3) - from
					animation.track_set_key_value(t, k, to + (moved * scale if travels else moved))
			elif kind == Animation.TYPE_SCALE_3D:
				var rest_scale: Vector3 = target.get_bone_rest(target.find_bone(track_bone)).basis.get_scale()
				for k in animation.track_get_key_count(t):
					animation.track_set_key_value(t, k, rest_scale)
		# Only the root and the pelvis may translate, and the root's horizontal travel belongs to the controller.
		var stripped := 0.0
		for t in animation.get_track_count():
			var track_bone := animation.track_get_path(t).get_subname(0)
			if animation.track_get_type(t) != Animation.TYPE_POSITION_3D or track_bone in TRAVEL_BONES:
				continue
			var rest_position: Vector3 = target_origin[track_bone]
			for k in animation.track_get_key_count(t):
				stripped = maxf(stripped, rest_position.distance_to(animation.track_get_key_value(t, k)))
				animation.track_set_key_value(t, k, rest_position)
		for t in animation.get_track_count():
			if animation.track_get_path(t).get_subname(0) != "Root" or animation.track_get_type(t) != Animation.TYPE_POSITION_3D:
				continue
			for k in animation.track_get_key_count(t):
				var value: Vector3 = animation.track_get_key_value(t, k)
				value.x = 0.0
				value.z = 0.0
				animation.track_set_key_value(t, k, value)
		# Godot's glTF import drops a track that holds a bone at its rest, so an unheld bone carries the previous
		# clip's pose over. Every bone the clip does not move is pinned to its rest instead.
		var held := {}
		for t in animation.get_track_count():
			held[str(animation.track_get_path(t).get_subname(0)) + ":" + str(animation.track_get_type(t))] = true
		var held_added := 0
		for b in target.get_bone_count():
			var bone := target.get_bone_name(b)
			var rest := target.get_bone_rest(b)
			for kind in [Animation.TYPE_POSITION_3D, Animation.TYPE_ROTATION_3D]:
				if held.has(str(bone) + ":" + str(kind)):
					continue
				var t := animation.add_track(kind)
				animation.track_set_path(t, NodePath("Skeleton3D:" + bone))
				if kind == Animation.TYPE_POSITION_3D:
					animation.position_track_insert_key(t, 0, rest.origin)
				else:
					animation.rotation_track_insert_key(t, 0, rest.basis.get_rotation_quaternion())
				held_added += 1
		animation.loop_mode = Animation.LOOP_LINEAR if state in LOOPS else Animation.LOOP_NONE
		# Feet planted: the source rig's feet do not reach the ground on a body of these proportions. Measured on
		# the target skeleton, per frame, and corrected on the Root so no bone length changes.
		var low_before := [INF, -INF]
		var low_after := [INF, -INF]
		var corrected := 0
		var plant_enabled: bool = not (args.size() > 5 and args[5] == "noplant")
		if plant_enabled and not state in AIRBORNE:
			var probe := AnimationPlayer.new()
			target.add_child(probe)
			probe.root_node = NodePath("..")
			probe.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
			var one := AnimationLibrary.new()
			var copy: Animation = animation.duplicate(true)
			for t in copy.get_track_count():
				copy.track_set_path(t, NodePath(".:" + copy.track_get_path(t).get_subname(0)))
			one.add_animation("probe", copy)
			probe.add_animation_library("", one)
			var frames := maxi(8, roundi(animation.length * 60.0))
			var deltas := []
			for frame in frames + 1:
				probe.play("probe", 0)
				probe.seek(animation.length * float(frame) / float(frames), true)
				probe.advance(0)
				var low := INF
				for bone in CONTACT_BONES:
					var index := target.find_bone(bone)
					if index >= 0:
						low = minf(low, target.get_bone_global_pose(index).origin.y)
				low_before[0] = minf(low_before[0], low)
				low_before[1] = maxf(low_before[1], low)
				deltas.append(rest_low - low)
			target.remove_child(probe)
			probe.free()
			if deltas.size() > 0:
				var root_track := animation.find_track(NodePath("Skeleton3D:Root"), Animation.TYPE_POSITION_3D)
				if root_track < 0:
					root_track = animation.add_track(Animation.TYPE_POSITION_3D)
					animation.track_set_path(root_track, NodePath("Skeleton3D:Root"))
				var baseline: Vector3 = animation.track_get_key_value(root_track, 0) if animation.track_get_key_count(root_track) > 0 else target_origin["Root"]
				for frame in deltas.size():
					var value: Vector3 = baseline
					value.y = float(baseline.y) + float(deltas[frame])
					animation.position_track_insert_key(root_track, animation.length * float(frame) / float(deltas.size() - 1), value)
					low_after[0] = minf(low_after[0], float(deltas[frame]) + rest_low)
					low_after[1] = maxf(low_after[1], float(deltas[frame]) + rest_low)
				corrected = deltas.size()
		target.reset_bone_poses()
		if state in GAITS:
			var measurement := measure(animation, target)
			paces[state] = measurement.pace
			phases[state] = measurement.phase
			plants[state] = measurement.plants
		library.add_animation(state, animation)
		rows.append({"state": state, "source": source_id, "bones": rig.get_bone_count(),
			"tracks": animation.get_track_count(), "matched": matched, "dropped": dropped,
			"rest_holds": held_added, "seconds": animation.length,
			"stripped_non_root_translation_m": stripped,
			"plant_frames": corrected,
			"foot_low_before_m": low_before if corrected > 0 else null,
			"foot_low_after_m": low_after if corrected > 0 else null})
		source.free()
	for alias in ALIASES:
		library.add_animation(alias, library.get_animation(ALIASES[alias]))
	library.set_meta("target_body_sha256", FileAccess.get_sha256(body_path))
	library.set_meta("motion_paces", paces)
	library.set_meta("motion_phases", phases)
	library.set_meta("foot_plants", plants)
	library.set_meta("route", "offline re-base onto the target rest (ArtLibrary.Retarget's formula) + measured foot planting; no runtime conversion")
	var saved := out_folder + "/" + id + "_motion.res"
	assert(ResourceSaver.save(library, saved) == OK, saved)
	var file := FileAccess.open(report_path, FileAccess.WRITE)
	file.store_string(JSON.stringify({"id": id, "body": body_path, "saved": saved,
		"target_height_m": target_height, "rest_low_m": rest_low,
		"states": rows, "paces": paces, "phases": phases}, "  "))
	file.close()
	print("HUMGEN_MOTION_LIBRARY ", id, " states=", library.get_animation_list().size(),
		" saved=", saved, " paces=", JSON.stringify(paces))
	quit()


func measure(animation: Animation, rig: Skeleton3D) -> Dictionary:
	var player := AnimationPlayer.new()
	rig.add_child(player)
	player.root_node = NodePath("..")
	player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
	var library := AnimationLibrary.new()
	var copy: Animation = animation.duplicate(true)
	for t in copy.get_track_count():
		copy.track_set_path(t, NodePath(".:" + copy.track_get_path(t).get_subname(0)))
	library.add_animation("probe", copy)
	player.add_animation_library("", library)
	var count := maxi(20, ceili(animation.length * 120.0))
	var dt := animation.length / float(count)
	var samples := []
	var floors := [INF, INF]
	for f in count:
		player.play("probe", 0)
		player.seek(f * dt, true)
		player.advance(0)
		var feet := []
		for side in 2:
			var at := rig.get_bone_global_pose(rig.find_bone("ball_l" if side == 0 else "ball_r")).origin
			feet.append(at)
			floors[side] = minf(floors[side], at.y)
		samples.append(feet)
	var speeds := []
	var found := []
	for side in 2:
		var minima := []
		for f in count:
			var prev := (f + count - 1) % count
			var next := (f + 1) % count
			if samples[f][side].y <= floors[side] + 0.015:
				var velocity: Vector3 = (samples[next][side] - samples[prev][side]) / (2.0 * dt)
				if Vector2(velocity.x, velocity.z).length() > 0.05:
					speeds.append(Vector2(velocity.x, velocity.z).length())
			if samples[f][side].y <= samples[prev][side].y and samples[f][side].y < samples[next][side].y and samples[f][side].y <= floors[side] + 0.015:
				minima.append(f)
		for f in minima:
			if not found.any(func(p): return p.side == side and absf(p.seconds - f * dt) < animation.length * 0.2):
				found.append({"side": side, "seconds": f * dt, "phase": float(f) / float(count)})
	speeds.sort()
	found.sort_custom(func(a, b): return a.seconds < b.seconds)
	var pace: float = speeds[speeds.size() / 2] if not speeds.is_empty() else 0.0
	var phase := 0.0
	for plant in found:
		if plant.side == 0:
			phase = plant.seconds
			break
	rig.remove_child(player)
	player.free()
	rig.reset_bone_poses()
	return {"pace": pace, "phase": phase, "plants": found}




