extends MainLoop

# Headless check on what the asset validation gallery draws.
#
# Running the gallery scene itself needs a rendering device, which headless Godot does not have, so
# this verifies the same thing the scene depends on: every asset in the manifest loads, instantiates
# into the expected kind of node, and measures what it is supposed to measure, and every clip
# carries an animation. A gallery that opens with half its rows silently empty validates nothing.
#
# Run:
#   godot --headless --path <project> --script validate_scene.gd

const MANIFEST := "res://assets/validation_scene.json"

var _done := false
var _code := 1


func _initialize() -> void:
	var report := {"ok": true, "problems": [], "rows": [], "assets": 0, "clips": 0}
	if not FileAccess.file_exists(MANIFEST):
		report["ok"] = false
		report["problems"].append("manifest missing; run _build_validation_scene.py --apply")
		_finish(report)
		return

	var parsed = JSON.parse_string(FileAccess.get_file_as_string(MANIFEST))
	if not (parsed is Dictionary):
		report["ok"] = false
		report["problems"].append("manifest is not valid JSON")
		_finish(report)
		return

	for row in parsed.get("rows", []):
		var row_report := {"label": String(row.get("label", "?")), "assets": [], "problems": []}
		for entry in row.get("assets", []):
			var path: String = entry.get("path", "")
			if not ResourceLoader.exists(path):
				row_report["problems"].append("%s: file missing" % path.get_file())
				continue
			var scene: PackedScene = load(path)
			if scene == null:
				row_report["problems"].append("%s: Godot could not load it" % path.get_file())
				continue
			var instance: Node = scene.instantiate()
			var meshes: Array = []
			_collect_meshes(instance, meshes)
			if meshes.is_empty():
				row_report["problems"].append("%s: no mesh after import" % path.get_file())
				instance.free()
				continue

			# The imported AABB is the real measurement, in metres, in the engine.
			var aabb: AABB = meshes[0].get_aabb()
			for index in range(1, meshes.size()):
				aabb = aabb.merge(meshes[index].get_aabb())
			var size := aabb.size
			report["assets"] += 1
			row_report["assets"].append({
				"name": String(entry.get("name", path.get_file())),
				"size_m": [snappedf(size.x, 0.001), snappedf(size.y, 0.001),
					snappedf(size.z, 0.001)],
				"meshes": meshes.size(),
			})
			instance.free()

			if entry.has("clip"):
				var clip_path := "res://assets/clips/%s.glb" % String(entry["clip"])
				if not ResourceLoader.exists(clip_path):
					row_report["problems"].append("%s: clip %s missing"
						% [path.get_file(), entry["clip"]])
					continue
				var clip_scene: PackedScene = load(clip_path)
				var clip_root: Node = clip_scene.instantiate()
				var player := _first_player(clip_root)
				if player == null:
					row_report["problems"].append("%s: clip has no AnimationPlayer"
						% entry["clip"])
				elif player.get_animation_list().size() == 0:
					row_report["problems"].append("%s: clip has no animation"
						% entry["clip"])
				else:
					report["clips"] += 1
				clip_root.free()

		if not row_report["problems"].is_empty():
			report["ok"] = false
			for problem in row_report["problems"]:
				report["problems"].append("%s: %s" % [row_report["label"], problem])
		report["rows"].append(row_report)

	_finish(report)


func _collect_meshes(node: Node, meshes: Array) -> void:
	if node is MeshInstance3D:
		meshes.append(node)
	for child in node.get_children():
		_collect_meshes(child, meshes)


func _first_player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node
	for child in node.get_children():
		var found := _first_player(child)
		if found != null:
			return found
	return null


func _finish(report: Dictionary) -> void:
	print("SCENE_RESULT " + JSON.stringify(report))
	_code = 0 if report.get("ok", false) else 1
	_done = true


func _process(_delta: float) -> bool:
	return _done
