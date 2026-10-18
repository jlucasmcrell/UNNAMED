extends SceneTree
# Renders each item model to a transparent 1024 px PNG: fixed three-quarter camera, neutral key light, soft rim.
# godot --path <any project dir> --script tools/ui_icons/render_item_icons.gd -- <out dir> <name>=<glb>[@spin degrees about the long axis] ...
const SIZE := 1024

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var out_dir: String = args[0]
	DirAccess.make_dir_recursive_absolute(out_dir)
	var vp := SubViewport.new()
	vp.size = Vector2i(SIZE, SIZE)
	vp.transparent_bg = true
	vp.msaa_3d = Viewport.MSAA_4X
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(vp)
	var env := Environment.new()
	env.background_mode = Environment.BG_CLEAR_COLOR
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.62, 0.64, 0.68)
	env.ambient_light_energy = 0.8
	var we := WorldEnvironment.new()
	we.environment = env
	vp.add_child(we)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-45, -35, 0)
	key.light_energy = 1.3
	vp.add_child(key)
	var rim := DirectionalLight3D.new()
	rim.rotation_degrees = Vector3(-20, 150, 0)
	rim.light_color = Color(0.75, 0.85, 1.0)
	rim.light_energy = 0.7
	vp.add_child(rim)
	await process_frame
	var cam := Camera3D.new()
	cam.projection = Camera3D.PROJECTION_ORTHOGONAL
	vp.add_child(cam)
	for i in range(1, args.size()):
		var parts: PackedStringArray = args[i].split("=", true, 1)
		var spin := 0.0
		if parts[1].contains("@"):
			var sp := parts[1].rsplit("@", true, 1)
			parts[1] = sp[0]
			spin = float(sp[1])
		var doc := GLTFDocument.new()
		var st := GLTFState.new()
		if doc.append_from_file(parts[1], st) != OK:
			printerr("load failed: ", parts[1])
			continue
		var model: Node3D = doc.generate_scene(st)
		vp.add_child(model)
		await process_frame
		# a three-quarter pose: turn the model, then frame its bounds
		model.rotation_degrees = Vector3(0, 0, 0)
		var aabb := _bounds(model)
		var longest := aabb.get_longest_axis_index()
		# lay the longest axis diagonally across the tile
		var axis := Vector3.ZERO
		axis[longest] = 1.0
		var tilt := Basis(Vector3(0, 0, 1), deg_to_rad(40.0))
		var to_x := Basis(Quaternion(axis, Vector3.RIGHT))
		model.basis = tilt * Basis(Vector3.RIGHT, deg_to_rad(spin)) * to_x
		# a flat thing (a folded hide, a page, a key) seen edge-on is a stick: turn its thinnest side to the camera instead
		var size := aabb.size
		var shortest := size.min_axis_index()
		if size[shortest] < 0.3 * size[longest]:
			var cols := [Vector3.ZERO, Vector3.ZERO, Vector3.ZERO]
			var mid := 3 - longest - shortest
			cols[longest] = Vector3.RIGHT
			cols[mid] = Vector3.UP
			cols[shortest] = Vector3.BACK
			var flat := Basis(cols[0], cols[1], cols[2])
			if flat.determinant() < 0.0:
				cols[mid] = Vector3.DOWN
				flat = Basis(cols[0], cols[1], cols[2])
			model.basis = Basis(Vector3(0, 0, 1), deg_to_rad(30.0)) * Basis(Vector3.RIGHT, deg_to_rad(-35.0)) * flat
		var b2 := _bounds(model)
		model.position = -(b2.get_center())
		var radius := b2.size.length() * 0.5
		cam.size = radius * 2.0 * 1.0
		cam.position = Vector3(radius * 0.6, radius * 0.5, radius * 3.0)
		cam.look_at(Vector3.ZERO)
		for f in 4:
			await process_frame
		var img := vp.get_texture().get_image()
		img.save_png(out_dir.path_join(parts[0] + ".png"))
		model.queue_free()
		await process_frame
	quit()

func _bounds(n: Node3D) -> AABB:
	var acc := AABB()
	var first := true
	for m in n.find_children("*", "MeshInstance3D", true, false):
		var mi := m as MeshInstance3D
		var a: AABB = mi.global_transform * mi.get_aabb()
		if first:
			acc = a
			first = false
		else:
			acc = acc.merge(a)
	return acc
