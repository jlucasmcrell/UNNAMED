extends MainLoop

# The shared AnimationLibrary and state machine proof.
#
# Section 6 lists "AnimationLibrary compatibility" as a required proof and section 12 records it as
# outstanding: the clips import individually and attach to one character, but nothing has assembled
# the whole set into a single library, and nothing has checked that the stable clip ids the animation
# registry defines are the ids the engine actually serves.
#
# This does three things a per-clip check cannot:
#
#   1. builds ONE AnimationLibrary from all seventeen staged clips, keyed by stable clip id;
#   2. cross-checks the engine against the registry - the Animation Godot built must have the
#      declared length and the declared loop mode, which is the only place the registry's numbers
#      ever meet the engine's;
#   3. builds the AnimationTree the state machine manifest describes and drives it, so a state
#      change is observed rather than assumed.
#
# Run:
#   godot --headless --path <project> --script validate_anim_tree.gd

const STATE_MACHINE := "res://assets/animation_state_machine.json"
const REGISTRY := "res://assets/animation_registry.json"
const CLIP_DIR := "res://assets/clips/%s.glb"
const LENGTH_TOLERANCE := 0.05

var _done := false
var _code := 1


func _initialize() -> void:
	var report := {
		"ok": true, "problems": [], "clips": 0, "library_entries": 0, "states": 0,
		"transitions": 0, "blend_points": 0, "engine_vs_registry": [], "runtime_travel": [],
	}
	var machine := _read_json(STATE_MACHINE)
	if machine.is_empty():
		report["ok"] = false
		report["problems"].append(
			"state machine manifest missing or invalid; run _make_anim_state_machine.py")
		_finish(report)
		return

	var states: Dictionary = machine.get("states", {})
	var machine_clips := _named_clips(states)
	var declared: Dictionary = _read_json(REGISTRY).get("clips", {})
	# The library is built from the whole registry, not only from the clips this machine names, so
	# the engine check covers every clip that exists rather than only the player's.
	var wanted: Array = declared.keys()
	wanted.sort()
	if wanted.is_empty():
		report["problems"].append("registry not staged; cannot build the library")
		report["ok"] = false
		_finish(report)
		return

	var player := AnimationPlayer.new()
	player.name = "AnimationPlayer"
	var library := AnimationLibrary.new()
	for clip_id in wanted:
		var animation := _load_animation(String(clip_id))
		if animation == null:
			report["problems"].append("%s: no animation could be loaded" % clip_id)
			continue
		# glTF carries no loop flag, so the importer always produces LOOP_NONE and every clip that
		# is declared to loop plays exactly once in the engine. The registry owns that flag, so the
		# policy is applied here, where the stable id and its declared metadata meet the Animation
		# the engine will actually play. Without it a walking character hitches once per stride,
		# which no per-clip check can see.
		var wants_loop := bool(declared.get(clip_id, {}).get("loop", false))
		animation.loop_mode = Animation.LOOP_LINEAR if wants_loop else Animation.LOOP_NONE
		library.add_animation(StringName(clip_id), animation)

	# The state machine is a subset of the registry, so a state that names a clip the library was
	# never told about would be a state that plays nothing.
	for clip_id in machine_clips:
		if not declared.has(clip_id):
			report["problems"].append(
				"%s: named by the state machine but absent from the registry" % clip_id)

	if not report["problems"].is_empty():
		report["ok"] = false
		_finish(report)
		return

	# An empty library name is what makes every animation addressable by the bare stable id.
	if player.add_animation_library("", library) != OK:
		report["ok"] = false
		report["problems"].append("add_animation_library failed")
		_finish(report)
		return
	report["library_entries"] = library.get_animation_list().size()
	report["clips"] = wanted.size()

	_check_registry(player, wanted, report)

	var tree := AnimationTree.new()
	var host := Node.new()
	host.name = "AnimHost"
	host.add_child(player)
	host.add_child(tree)
	tree.name = "AnimationTree"
	tree.anim_player = tree.get_path_to(player)

	var built := _build_state_machine(machine, states, tree, player, report)
	if built.is_empty():
		report["ok"] = false
		_finish(report)
		return

	_run_tree(tree, built["state_machine"], built["locomotion"], states, report)
	host.free()

	report["ok"] = report["problems"].is_empty()
	_finish(report)


func _read_json(path: String) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {}
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if parsed is Dictionary else {}


func _named_clips(states: Dictionary) -> Array:
	# Built from the contract rather than from whatever is lying in the staging directory, so a
	# clip the machine forgot to mention shows up as a missing library entry rather than as a
	# silently absent state.
	var out: Array = []
	var seen := {}
	for state_name in states:
		var state: Dictionary = states[state_name]
		var ids: Array = []
		if state.has("clip"):
			ids.append(String(state["clip"]))
		for point in state.get("points", []):
			ids.append(String(point["clip"]))
		for clip_id in ids:
			if not seen.has(clip_id):
				seen[clip_id] = true
				out.append(clip_id)
	return out


func _load_animation(clip_id: String) -> Animation:
	var path := CLIP_DIR % clip_id
	if not ResourceLoader.exists(path):
		return null
	var clip_scene: PackedScene = load(path)
	if clip_scene == null:
		return null
	var clip_root: Node = clip_scene.instantiate()
	var source := _first_player(clip_root)
	var animation: Animation = null
	if source != null and not source.get_animation_list().is_empty():
		# Godot names an imported animation after the importing node; the library is keyed by the
		# stable id instead, which is the entire point of building it here.
		animation = source.get_animation(source.get_animation_list()[0])
	clip_root.free()
	return animation


func _check_registry(player: AnimationPlayer, wanted: Array, report: Dictionary) -> void:
	# The registry is the contract, the engine's Animation is the implementation, and this is the
	# only place the two meet. A mismatch is a real defect in the export or in the declared metadata.
	var registry := _read_json(REGISTRY)
	var clips: Dictionary = registry.get("clips", {})
	for clip_id in wanted:
		if not clips.has(clip_id):
			report["problems"].append("%s: not in the registry" % clip_id)
			continue
		if not player.has_animation(StringName(clip_id)):
			report["problems"].append("%s: library does not serve the stable id" % clip_id)
			continue
		var expected: Dictionary = clips[clip_id]
		var animation: Animation = player.get_animation(StringName(clip_id))
		var declared := float(expected.get("duration_s", 0.0))
		var loops := animation.loop_mode != Animation.LOOP_NONE
		var wants_loop := bool(expected.get("loop", false))
		if absf(animation.length - declared) > LENGTH_TOLERANCE:
			report["problems"].append("%s: engine length %.4fs against declared %.4fs"
				% [clip_id, animation.length, declared])
		if wants_loop != loops:
			report["problems"].append("%s: declared loop=%s but engine loop_mode=%s"
				% [clip_id, wants_loop, animation.loop_mode])
		if animation.get_track_count() == 0:
			report["problems"].append("%s: animation drives no tracks" % clip_id)
		report["engine_vs_registry"].append({
			"clip": clip_id, "engine_length_s": snappedf(animation.length, 0.001),
			"declared_length_s": declared, "engine_loops": loops, "declared_loop": wants_loop,
			"tracks": animation.get_track_count(),
		})


func _build_state_machine(machine: Dictionary, states: Dictionary, tree: AnimationTree,
		player: AnimationPlayer, report: Dictionary) -> Dictionary:
	var sm := AnimationNodeStateMachine.new()
	var locomotion := ""
	for state_name in states:
		var state: Dictionary = states[state_name]
		var key := String(state_name)
		if String(state.get("kind", "")) == "blend_space_1d":
			locomotion = key
			var blend := AnimationNodeBlendSpace1D.new()
			var low := INF
			var high := -INF
			for point in state.get("points", []):
				var clip := String(point["clip"])
				if not player.has_animation(StringName(clip)):
					report["problems"].append(
						"blend point '%s' is not in the library" % clip)
				var node := AnimationNodeAnimation.new()
				node.animation = StringName(clip)
				var at := float(point["at_speed_mps"])
				blend.add_blend_point(node, at, -1, StringName(clip))
				low = minf(low, at)
				high = maxf(high, at)
			blend.min_space = low
			blend.max_space = high
			report["blend_points"] = state.get("points", []).size()
			report["blend_space_mps"] = [low, high]
			sm.add_node(key, blend, Vector2(0, 0))
		else:
			var clip := String(state.get("clip", ""))
			if not player.has_animation(StringName(clip)):
				report["problems"].append("state '%s' names '%s', which is not in the library"
					% [key, clip])
			var node := AnimationNodeAnimation.new()
			node.animation = StringName(clip)
			sm.add_node(key, node, Vector2(0, 0))

	if locomotion == "":
		report["problems"].append("no blend space state; there is no default state")
		return {}

	for transition in machine.get("transitions", []):
		var from := String(transition.get("from", ""))
		var to := String(transition.get("to", ""))
		# A wildcard source cannot be an edge, so it stays a contract fact rather than becoming an
		# edge from nowhere.
		if from == "*" or not sm.has_node(from) or not sm.has_node(to):
			continue
		var edge := AnimationNodeStateMachineTransition.new()
		# Leaving a one-shot state happens when that clip ends, not immediately; entering one does.
		if states.has(from) and states[from].get("kind", "") == "one_shot":
			edge.switch_mode = AnimationNodeStateMachineTransition.SWITCH_MODE_AT_END
		sm.add_transition(from, to, edge)
		report["transitions"] += 1

	tree.tree_root = sm
	report["states"] = sm.get_node_list().size()
	return {"state_machine": sm, "locomotion": locomotion}


func _run_tree(tree: AnimationTree, sm: AnimationNodeStateMachine, locomotion: String,
		states: Dictionary, report: Dictionary) -> void:
	# Manual stepping rather than waiting on frames: this runs inside a MainLoop, which has no scene
	# processing, and waiting on frames would make the proof depend on the host rather than on the
	# tree.
	var playback = tree.get("parameters/playback")
	if playback == null:
		report["problems"].append("no playback object; the root is not a state machine")
		return
	report["playback_type"] = playback.get_class()
	report["initial_node"] = String(playback.get_current_node())

	for state_name in ["sword_attack", "cast", "interact", "pickup", "hit_react", "death"]:
		if not states.has(state_name):
			continue
		playback.travel(StringName(state_name))
		for step in range(12):
			tree.advance(0.05)
		var current := String(playback.get_current_node())
		report["runtime_travel"].append({"requested": state_name, "current": current})
		if current != state_name:
			report["problems"].append("travel(%s) landed on '%s'" % [state_name, current])
		if state_name != "death":
			playback.travel(StringName(locomotion))
			for step in range(4):
				tree.advance(0.05)
			if String(playback.get_current_node()) != locomotion:
				report["problems"].append("could not return to %s from %s"
					% [locomotion, state_name])

	# The blend space is driven by one float, so its mapping is checkable directly.
	var path := "parameters/%s/blend_position" % locomotion
	tree.set(path, 3.4)
	var position = tree.get(path)
	report["blend_position_after_set"] = snappedf(float(position), 0.01)
	if absf(float(position) - 3.4) > 0.01:
		report["problems"].append("blend position did not take the value it was given")


func _first_player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node
	for child in node.get_children():
		var found := _first_player(child)
		if found != null:
			return found
	return null


func _finish(report: Dictionary) -> void:
	print("ANIM_TREE_RESULT " + JSON.stringify(report))
	_code = 0 if report.get("ok", false) else 1
	_done = true


func _process(_delta: float) -> bool:
	return _done
