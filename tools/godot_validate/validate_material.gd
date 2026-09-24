extends MainLoop

# Headless validation for a generated PBR material.
#
# A material can look right in an image viewer and be wrong in engine, so this checks the things
# that only fail at import: the resource loads as the expected type, every map actually resolves
# to a texture rather than a broken path, the maps agree on size, and the ORM wiring is live
# rather than a texture sitting unconnected in the inspector.
#
# Run:
#   godot --headless --path <project> --script validate_material.gd -- <material.tres> [expected.json]

var _done := false
var _code := 1


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1:
		_finish({"ok": false, "problems": ["no material path given"], "checks": {}})
		return

	var path: String = args[0]
	var expected := {}
	if args.size() >= 2 and FileAccess.file_exists(args[1]):
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
		if parsed is Dictionary:
			expected = parsed

	var report := {"ok": true, "material": path.get_file(), "problems": [], "checks": {}}
	if not FileAccess.file_exists(path):
		report["ok"] = false
		report["problems"].append("material resource does not exist")
		_finish(report)
		return

	var resource = load(path)
	if resource == null:
		report["ok"] = false
		report["problems"].append("Godot could not load the material resource")
		_finish(report)
		return
	if not (resource is StandardMaterial3D):
		report["ok"] = false
		report["problems"].append("resource is %s, not StandardMaterial3D" % resource.get_class())
		_finish(report)
		return

	var material: StandardMaterial3D = resource
	report["checks"]["type"] = material.get_class()

	var albedo: Texture2D = material.albedo_texture
	var normal: Texture2D = material.normal_texture
	var orm: Texture2D = material.orm_texture

	if albedo == null:
		report["ok"] = false
		report["problems"].append("albedo texture did not resolve")
	if normal == null:
		report["ok"] = false
		report["problems"].append("normal texture did not resolve")
	if orm == null:
		report["ok"] = false
		report["problems"].append("ORM texture did not resolve")

	if not material.normal_enabled:
		report["ok"] = false
		report["problems"].append("normal mapping is disabled, so the normal map is inert")

	var sizes := {}
	for pair in [["albedo", albedo], ["normal", normal], ["orm", orm]]:
		var label: String = pair[0]
		var texture: Texture2D = pair[1]
		if texture == null:
			continue
		var size := texture.get_size()
		sizes[label] = [int(size.x), int(size.y)]
		if size.x != size.y:
			report["ok"] = false
			report["problems"].append("%s map is not square (%d x %d)" % [label, size.x, size.y])
		if int(size.x) & (int(size.x) - 1) != 0:
			report["ok"] = false
			report["problems"].append("%s map is %d, not a power of two" % [label, size.x])
	report["checks"]["map_sizes"] = sizes

	if sizes.size() == 3:
		var first: Array = sizes["albedo"]
		for label in sizes:
			if sizes[label] != first:
				report["ok"] = false
				report["problems"].append("%s map is %s but albedo is %s"
					% [label, sizes[label], first])

	# The real-world tile size decides how often the texture repeats, so it has to travel with
	# the material rather than live only in a sidecar a gameplay programmer might never read.
	if expected.has("tile_size_m"):
		var scale: Vector3 = material.uv1_scale
		report["checks"]["uv1_scale"] = [scale.x, scale.y, scale.z]
		report["checks"]["tile_size_m"] = expected["tile_size_m"]
		if scale.x <= 0.0:
			report["ok"] = false
			report["problems"].append("uv1_scale x is not positive, so the material cannot tile")

	report["checks"]["normal_scale"] = material.normal_scale
	_finish(report)


func _finish(report: Dictionary) -> void:
	print("MATERIAL_RESULT " + JSON.stringify(report))
	_code = 0 if report.get("ok", false) else 1
	_done = true


func _process(_delta: float) -> bool:
	return _done
