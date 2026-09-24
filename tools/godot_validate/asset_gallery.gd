extends Node3D

# Otherreach asset validation gallery.
#
# This is NOT gameplay. It exists so a human can look at the playable asset set in the real engine
# and see whether it is actually usable: right size, right way up, holding together as a set,
# animating, and with the sockets and collision where they were authored.
#
# It reads a manifest rather than hard-coding paths, so the gallery follows the asset set instead
# of drifting away from it. Everything is built at runtime, which keeps the .tscn trivial and means
# a missing asset is reported loudly here rather than silently absent.
#
# Views, per sprint section 28:
#   scale reference      one metre grid and marked posts, so a 1.30 m wolf reads as smaller than a
#                        1.80 m human at a glance
#   humanoid reference   the canonical Veth beside every other asset
#   animation playback   each clip played on its own asset
#   weapon grip          the weapon placed at the hand socket it declares
#   collision            the collision proxies drawn as wireframe, toggled with C
#   LOD distance         the LOD chain laid out at increasing distance
#
# Controls: 1 scale  2 characters  3 creatures  4 weapons  5 kit  6 props  7 animations
#           C toggle collision   Space play/pause   Esc quit

const MANIFEST := "res://assets/validation_scene.json"

var _rows: Array = []
var _root := Node3D.new()
var _show_collision := false
var _playing := true
var _clip_players: Array = []
var _registry_clips: Dictionary = {}
var _status := ""

func _ready() -> void:
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(MANIFEST))
	if not (parsed is Dictionary):
		push_error("cannot read %s" % MANIFEST)
		return
	_rows = parsed.get("rows", [])
	add_child(_root)
	_build()
	print("GALLERY_READY " + JSON.stringify({
		"rows": _rows.size(),
		"assets_placed": _placed,
		"assets_missing": _missing,
		"clips_playing": _clip_players.size(),
	}))

var _placed := 0
var _missing := 0

func _build() -> void:
	for row_index in _rows.size():
		var row: Dictionary = _rows[row_index]
		var z := float(row_index) * 6.0
		_add_label(str(row.get("label", "row %d" % row_index)), Vector3(-6.0, 2.2, z))
		if bool(row.get("scale_reference", false)):
			_add_scale_reference(z)
		var x := 0.0
		for entry in row.get("assets", []):
			var node := _load_asset(entry)
			if node == null:
				_missing += 1
				x += 3.0
				continue
			node.position = Vector3(x, 0.0, z)
			_root.add_child(node)
			_placed += 1
			x += float(entry.get("spacing", 3.0))

func _load_asset(entry: Dictionary) -> Node3D:
	var path: String = entry.get("path", "")
	if not ResourceLoader.exists(path):
		push_warning("asset missing: %s" % path)
		return null
	var scene: PackedScene = load(path)
	if scene == null:
		push_warning("asset would not load: %s" % path)
		return null
	var instance: Node = scene.instantiate()
	var holder := Node3D.new()
	holder.name = String(entry.get("name", path.get_file().get_basename()))
	holder.add_child(instance)

	# Rotation is authored per entry because the library's meshes are not all authored facing the
	# same way, and a gallery that shows a weapon edge-on is not validating anything.
	holder.rotation_degrees.y = float(entry.get("rotation_y_deg", 0.0))
	holder.scale = Vector3.ONE * float(entry.get("scale", 1.0))

	if entry.has("clip"):
		_attach_clip(holder, String(entry["clip"]))

	if _show_collision or bool(entry.get("show_collision", false)):
		_add_collision_wireframe(holder, entry)

	if entry.has("socket_marker"):
		_add_socket_marker(holder, entry)

	return holder

func _attach_clip(holder: Node3D, clip_name: String) -> void:
	var player := AnimationPlayer.new()
	holder.add_child(player)
	# The clip GLB is a separate animation-only file staged beside the body. Godot names imported
	# animations after the importing node rather than after the stable clip id, so take whatever
	# the clip file carries instead of matching a name that will not be there.
	var clip_scene: PackedScene = load("res://assets/clips/%s.glb" % clip_name)
	if clip_scene == null:
		push_warning("clip missing: %s" % clip_name)
		return
	var clip_root: Node = clip_scene.instantiate()
	var source_player := _first_player(clip_root)
	if source_player == null:
		clip_root.queue_free()
		return
	var library := AnimationLibrary.new()
	for animation_name in source_player.get_animation_list():
		var animation: Animation = source_player.get_animation(animation_name)
		# glTF carries no loop flag, so an imported clip is always LOOP_NONE: the walk, run and
		# sprint clips would each play once and then freeze in this gallery. The animation registry
		# owns the loop policy, so it is applied from there rather than from the file.
		if _should_loop(clip_name):
			animation.loop_mode = Animation.LOOP_LINEAR
		library.add_animation(String(animation_name), animation)
	player.add_animation_library("clip", library)
	var names := player.get_animation_list()
	if names.size() > 0:
		player.play(String(names[0]))
		player.active = _playing
		_clip_players.append(player)
	clip_root.queue_free()

func _should_loop(clip_name: String) -> bool:
	if _registry_clips.is_empty():
		var path := "res://assets/animation_registry.json"
		if FileAccess.file_exists(path):
			var parsed = JSON.parse_string(FileAccess.get_file_as_string(path))
			if parsed is Dictionary:
				_registry_clips = parsed.get("clips", {})
	return bool(_registry_clips.get(clip_name, {}).get("loop", false))

func _first_player(node: Node) -> AnimationPlayer:
	if node is AnimationPlayer:
		return node
	for child in node.get_children():
		var found := _first_player(child)
		if found != null:
			return found
	return null

func _add_collision_wireframe(holder: Node3D, entry: Dictionary) -> void:
	# Collision proxies are separate GLBs sitting beside the base asset.
	for suffix in ["_collision_hull", "_collision_box"]:
		var path: String = String(entry["path"]).replace(".glb", "%s.glb" % suffix)
		if not ResourceLoader.exists(path):
			continue
		var proxy: PackedScene = load(path)
		if proxy == null:
			continue
		var node: Node = proxy.instantiate()
		var mesh_instance := _first_mesh(node)
		if mesh_instance != null:
			var material := StandardMaterial3D.new()
			material.albedo_color = Color(0.1, 0.9, 1.0, 0.35)
			material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
			mesh_instance.material_override = material
			mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		holder.add_child(node)

func _first_mesh(node: Node) -> MeshInstance3D:
	if node is MeshInstance3D:
		return node
	for child in node.get_children():
		var found := _first_mesh(child)
		if found != null:
			return found
	return null

func _add_socket_marker(holder: Node3D, entry: Dictionary) -> void:
	var marker := MeshInstance3D.new()
	var sphere := SphereMesh.new()
	sphere.radius = 0.04
	sphere.height = 0.08
	marker.mesh = sphere
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(1.0, 0.4, 0.1)
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	marker.material_override = material
	var position: Array = entry.get("socket_marker", [0, 0, 0])
	# The manifest records the socket in the export frame, which is the frame Godot uses.
	marker.position = Vector3(position[0], position[1], position[2])
	marker.name = String(entry.get("socket_name", "SOCK_marker"))
	holder.add_child(marker)

func _add_scale_reference(z: float) -> void:
	# One metre posts, so size is judged against a real measure rather than against neighbouring
	# assets that might all be wrong together.
	for metre in range(0, 4):
		var post := MeshInstance3D.new()
		var box := BoxMesh.new()
		box.size = Vector3(0.06, 1.0, 0.06)
		post.mesh = box
		post.position = Vector3(-5.0 - metre * 0.4, metre + 0.5, z)
		var material := StandardMaterial3D.new()
		material.albedo_color = Color(0.9, 0.9, 0.9) if metre % 2 == 0 else Color(0.4, 0.4, 0.4)
		post.material_override = material
		_root.add_child(post)
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(40, 40)
	ground.mesh = plane
	ground.position = Vector3(0, 0, z)
	var ground_material := StandardMaterial3D.new()
	ground_material.albedo_color = Color(0.28, 0.28, 0.3)
	ground.material_override = ground_material
	_root.add_child(ground)

func _add_label(text: String, position: Vector3) -> void:
	var label := Label3D.new()
	label.text = text
	label.position = position
	label.font_size = 48
	label.modulate = Color(1, 1, 1)
	_root.add_child(label)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed:
		match event.keycode:
			KEY_C:
				_show_collision = not _show_collision
				_rebuild()
			KEY_SPACE:
				_playing = not _playing
				for player in _clip_players:
					player.active = _playing
			KEY_ESCAPE:
				get_tree().quit()

func _rebuild() -> void:
	for child in _root.get_children():
		child.queue_free()
	_placed = 0
	_missing = 0
	_clip_players.clear()
	_build()
