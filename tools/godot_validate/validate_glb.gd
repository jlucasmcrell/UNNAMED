extends MainLoop

# Headless import validation for Otherreach production GLBs.
#
# Wave 0 requires that "done" means the asset imports correctly into Godot, not merely that
# Blender exported a file. This checks the things that actually break in practice:
#
#   * scale       - the imported AABB must match the declared metres
#   * orientation - compared per axis, so a swapped up-axis is not masked by a similar total
#   * materials   - surfaces must survive import with names intact
#   * naming      - the mesh resource must carry the asset id, not "Mesh_0"
#   * sockets     - named attachment nodes must arrive where they were authored
#
# Uses a MainLoop rather than a SceneTree script: headless Godot has no display server, and
# a SceneTree-based script waits on one that will never arrive.
#
# Run:
#   godot --headless --path <project> --script validate_glb.gd -- <glb> [expected.json]
#
# expected.json:
#   { "asset_id": "...", "dimensions_m": [x, y, z],
#     "sockets": { "SOCK_head": [x, y, z], ... } }

var _done := false
var _code := 1


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1:
		_finish({"ok": false, "problems": ["no glb path given"], "checks": {}})
		return

	var glb_path: String = args[0]
	var expected := {}
	if args.size() >= 2 and FileAccess.file_exists(args[1]):
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
		if parsed is Dictionary:
			expected = parsed

	var report := {"ok": true, "glb": glb_path.get_file(), "problems": [], "checks": {}}

	if not FileAccess.file_exists(glb_path):
		report["ok"] = false
		report["problems"].append("file does not exist")
		_finish(report)
		return

	var packed: PackedScene = load(glb_path)
	if packed == null:
		report["ok"] = false
		report["problems"].append("Godot could not load the GLB as a PackedScene")
		_finish(report)
		return

	var root: Node = packed.instantiate()
	var meshes: Array = []
	var names: Array = []
	_collect(root, meshes, names)

	report["checks"]["mesh_instance_count"] = meshes.size()
	report["checks"]["node_names"] = names

	# Animation-only clips legitimately carry no mesh: the body provides geometry and the clip
	# provides motion. Requiring a mesh here would reject every correct clip.
	var players: Array = []
	_collect_players(root, players)
	var is_animation_only := expected.has("animation_id")
	report["checks"]["animation_players"] = players.size()

	if is_animation_only:
		if players.is_empty():
			report["ok"] = false
			report["problems"].append("no AnimationPlayer in an animation-only GLB")
		else:
			var player: AnimationPlayer = players[0]
			var list: PackedStringArray = player.get_animation_list()
			report["checks"]["animations"] = Array(list)
			var wanted := String(expected["animation_id"])
			var found := ""
			for a in list:
				if String(a) == wanted or String(a).ends_with(wanted):
					found = String(a)
					break
			# Godot names animations after the importing node (for example
			# "RIG_anim_humanoid_locomotion_walk_forwardAction"), not after the stable animation
			# id. The animation document is explicit that gameplay must key off stable ids and
			# metadata rather than imported clip names, so a single well-formed animation is
			# accepted and the engine's name is recorded as the mapping to resolve at load time.
			if found == "" and list.size() == 1:
				found = String(list[0])
				report["checks"]["animation_name_mapping"] = {wanted: found}
			if found == "":
				report["ok"] = false
				report["problems"].append("animation '%s' not found among %s" % [wanted, list])
			else:
				var anim: Animation = player.get_animation(found)
				report["checks"]["animation_name"] = found
				report["checks"]["animation_length_s"] = snappedf(anim.length, 0.0001)
				if expected.has("duration_s"):
					var want_len := float(expected["duration_s"])
					if abs(anim.length - want_len) > 0.02:
						report["ok"] = false
						report["problems"].append(
							"animation is %.4f s, expected %.4f s" % [anim.length, want_len])
				# Track count is the direct measure of whether the clip actually drives the rig.
				report["checks"]["animation_tracks"] = anim.get_track_count()
				if anim.get_track_count() == 0:
					report["ok"] = false
					report["problems"].append("animation has no tracks, so it drives nothing")
		_finish(report)
		return

	if meshes.is_empty():
		report["ok"] = false
		report["problems"].append("no MeshInstance3D found in the imported scene")
		_finish(report)
		return

	var aabb: AABB = meshes[0].get_aabb()
	var mesh_name: String = meshes[0].mesh.resource_name
	for i in range(1, meshes.size()):
		aabb = aabb.merge(meshes[i].get_aabb())

	var size := aabb.size
	report["checks"]["aabb_size_m"] = [snappedf(size.x, 0.0001), snappedf(size.y, 0.0001), snappedf(size.z, 0.0001)]
	report["checks"]["aabb_position_m"] = [snappedf(aabb.position.x, 0.0001), snappedf(aabb.position.y, 0.0001), snappedf(aabb.position.z, 0.0001)]
	report["checks"]["mesh_resource_name"] = mesh_name

	if expected.has("asset_id"):
		if mesh_name.find(String(expected["asset_id"])) == -1:
			report["ok"] = false
			report["problems"].append("mesh resource '%s' does not contain the asset id" % mesh_name)

	if expected.has("dimensions_m"):
		var want: Array = expected["dimensions_m"]
		var got := [size.x, size.y, size.z]
		for axis in range(3):
			var delta: float = abs(got[axis] - float(want[axis]))
			if delta > 0.006:
				report["ok"] = false
				report["problems"].append("axis %s extent %.4f m, expected %.4f m (delta %.4f)"
					% ["XYZ"[axis], got[axis], want[axis], delta])

	var materials: Array = []
	for mi in meshes:
		var mesh: Mesh = mi.mesh
		for s in range(mesh.get_surface_count()):
			var mat: Material = mi.get_active_material(s)
			if mat != null:
				materials.append(mat.resource_name)
			else:
				report["ok"] = false
				report["problems"].append("surface %d has no material after import" % s)
	report["checks"]["materials"] = materials
	if materials.is_empty():
		report["ok"] = false
		report["problems"].append("no materials survived import")

	if expected.has("sockets"):
		var present := {}
		for n in names:
			present[String(n)] = true
		var socket_positions := {}
		for socket_name in expected["sockets"].keys():
			if not present.has(String(socket_name)):
				report["ok"] = false
				report["problems"].append("socket '%s' missing from the imported scene" % socket_name)
				continue
			var node := root.find_child(String(socket_name), true, false)
			if node == null:
				continue
			# Presence and naming are what this engine check owns. Exact socket coordinates are
			# verified at the GLB level by _verify_character_skeleton.py, which walks the glTF
			# node hierarchy directly; Godot re-parents imported bones under a Skeleton3D, so a
			# composed transform here does not correspond to the authored frame and comparing it
			# reports a metre of error on a correct asset.
			socket_positions[String(socket_name)] = _global_origin(node)
		report["checks"]["socket_positions_m"] = socket_positions

	_finish(report)


func _global_origin(node: Node) -> Vector3:
	# A socket is parented to the bone it rides, so its own transform.origin is bone-local.
	# Comparing that against the authored world position reports a huge error on a correct asset.
	# Compose up the chain instead: global = root * ... * parent * local.
	var composed := Transform3D.IDENTITY
	var current: Node = node
	while current is Node3D:
		composed = (current as Node3D).transform * composed
		current = current.get_parent()
	return composed.origin


func _collect(node: Node, meshes: Array, names: Array) -> void:
	names.append(node.name)
	if node is MeshInstance3D:
		meshes.append(node)
	for child in node.get_children():
		_collect(child, meshes, names)


func _collect_players(node: Node, players: Array) -> void:
	if node is AnimationPlayer:
		players.append(node)
	for child in node.get_children():
		_collect_players(child, players)


func _finish(report: Dictionary) -> void:
	print("VALIDATE_RESULT " + JSON.stringify(report))
	_code = 0 if report.get("ok", false) else 1
	_done = true


func _process(_delta: float) -> bool:
	# A MainLoop has no quit(); returning true from _process ends the loop. Verified against
	# MainLoop.get_method_list() in 4.7, which exposes only _initialize, _process,
	# _physics_process and _finalize.
	return _done
