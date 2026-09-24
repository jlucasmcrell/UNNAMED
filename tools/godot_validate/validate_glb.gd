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
		for socket_name in expected["sockets"].keys():
			if not present.has(String(socket_name)):
				report["ok"] = false
				report["problems"].append("socket '%s' missing from the imported scene" % socket_name)
				continue
			var node := root.find_child(String(socket_name), true, false)
			if node == null:
				continue
			var want_pos: Array = expected["sockets"][socket_name]
			var got_pos: Vector3 = node.transform.origin
			var drift: float = Vector3(want_pos[0], want_pos[1], want_pos[2]).distance_to(got_pos)
			if drift > 0.004:
				report["ok"] = false
				report["problems"].append("socket '%s' is %.1f mm from its authored position"
					% [socket_name, drift * 1000.0])

	_finish(report)


func _collect(node: Node, meshes: Array, names: Array) -> void:
	names.append(node.name)
	if node is MeshInstance3D:
		meshes.append(node)
	for child in node.get_children():
		_collect(child, meshes, names)


func _finish(report: Dictionary) -> void:
	print("VALIDATE_RESULT " + JSON.stringify(report))
	_code = 0 if report.get("ok", false) else 1
	_done = true


func _process(_delta: float) -> bool:
	# A MainLoop has no quit(); returning true from _process ends the loop. Verified against
	# MainLoop.get_method_list() in 4.7, which exposes only _initialize, _process,
	# _physics_process and _finalize.
	return _done
