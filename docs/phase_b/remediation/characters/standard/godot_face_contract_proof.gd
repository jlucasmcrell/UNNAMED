extends Node3D
# The face channel contract in Godot: the same channel names found on two characters' meshes, one animation (blink, a smile, eye
# look via eye bones, a head turn via the neck/head bones) built by NAME only and played on both, over a looping body sway.
var t := 0.0
var figures := []

func find_meshes(n: Node, out: Array) -> void:
	if n is MeshInstance3D and n.mesh and n.mesh.get_blend_shape_count() > 0:
		out.append(n)
	for c in n.get_children():
		find_meshes(c, out)

func find_skeleton(n: Node) -> Skeleton3D:
	if n is Skeleton3D:
		return n
	for c in n.get_children():
		var s := find_skeleton(c)
		if s:
			return s
	return null

func _ready() -> void:
	var x := -0.28
	for path in ["res://player.glb", "res://renn.glb"]:
		var doc := GLTFDocument.new()
		var state := GLTFState.new()
		var err := doc.append_from_file(ProjectSettings.globalize_path(path), state)
		var scene := doc.generate_scene(state)
		add_child(scene)
		scene.position = Vector3(x, 0, 0)
		x += 0.56
		var meshes := []
		find_meshes(scene, meshes)
		var names := {}
		for m in meshes:
			for i in m.mesh.get_blend_shape_count():
				names[m.mesh.get_blend_shape_name(i)] = true
		var skel := find_skeleton(scene)
		print("FIGURE ", path, " err=", err, " meshes_with_shapes=", meshes.size(), " channels=", names.size(),
			" has eyeBlinkLeft=", names.has("eyeBlinkLeft"), " viseme_aa=", names.has("viseme_aa"), " bones=", skel.get_bone_count() if skel else 0,
			" eye.L=", skel.find_bone("eye.L") if skel else -1)
		figures.append({"meshes": meshes, "skel": skel})
	var cam := Camera3D.new()
	add_child(cam)
	cam.position = Vector3(0, 1.66, 1.25)
	cam.look_at(Vector3(0, 1.62, 0))
	cam.fov = 32
	var sun := DirectionalLight3D.new()
	add_child(sun)
	sun.rotation_degrees = Vector3(-35, 25, 0)
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.55, 0.55, 0.57)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.6, 0.6, 0.62)
	add_child(env)

func channel(name: String, v: float) -> void:
	for f in figures:
		for m in f.meshes:
			var i: int = m.find_blend_shape_by_name(name)
			if i >= 0:
				m.set_blend_shape_value(i, v)

func _process(delta: float) -> void:
	t += delta
	var blink: float = clamp(1.0 - abs(fmod(t, 2.4) - 1.2) / 0.08, 0.0, 1.0)
	channel("eyeBlinkLeft", blink)
	channel("eyeBlinkRight", blink)
	var smile: float = clamp(sin(t * 1.3) * 1.4 - 0.4, 0.0, 1.0)
	channel("mouthSmileLeft", smile)
	channel("mouthSmileRight", smile)
	channel("viseme_aa", clamp(sin(t * 9.0), 0.0, 1.0) * 0.6 if fmod(t, 4.0) > 2.8 else 0.0)
	for f in figures:
		var s: Skeleton3D = f.skel
		if s == null:
			continue
		var head := s.find_bone("head")
		var neck := s.find_bone("neck")
		var turn := Quaternion(Vector3.UP, sin(t * 0.7) * 0.35)
		if head >= 0:
			s.set_bone_pose_rotation(head, s.get_bone_rest(head).basis.get_rotation_quaternion() * turn)
		if neck >= 0:
			s.set_bone_pose_rotation(neck, s.get_bone_rest(neck).basis.get_rotation_quaternion() * Quaternion(Vector3.RIGHT, sin(t * 0.5) * 0.12))
		for e in ["eye.L", "eye.R"]:
			var b := s.find_bone(e)
			if b >= 0:
				s.set_bone_pose_rotation(b, s.get_bone_rest(b).basis.get_rotation_quaternion() * Quaternion(Vector3.FORWARD, sin(t * 1.9) * 0.3))
