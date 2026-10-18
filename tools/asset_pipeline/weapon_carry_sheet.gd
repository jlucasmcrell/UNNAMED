# Render harness: the real weapons carried (sheathed at the hip, bow/spear/quiver/shield on the back), held in the
# right hand, and lying on the ground as loot, on the MetaHuman and production player bodies (static rest pose).
#
#   godot --path <project> --resolution 1100x1500 --script weapon_carry_sheet.gd -- <asset_root> <weapons_root> <carry_json> <out_dir> <body>
#   body: mh_m | mh_f | prod_m | prod_f
#
# Carry placements live in weapon_carry_sockets.json in BODY space (x = the body's left, y up, z forward, at its rest pose); this
# harness converts each to the bone's own space so the weapon follows the bone whatever axes the skeleton uses. Rendering only:
# nothing here is game state.
extends SceneTree

var asset_root := ""
var weapons_root := ""
var out_dir := ""
var body_key := ""
var carry := {}
var cam: Camera3D
var scene_root: Node3D


func _initialize() -> void:
	var a := OS.get_cmdline_user_args()
	asset_root = a[0]; weapons_root = a[1]
	var f := FileAccess.open(a[2], FileAccess.READ)
	carry = JSON.parse_string(f.get_as_text())
	out_dir = a[3]; body_key = a[4]
	DirAccess.make_dir_recursive_absolute(out_dir)
	_run()


func load_glb(path: String) -> Node3D:
	var doc := GLTFDocument.new()
	var st := GLTFState.new()
	var err := doc.append_from_file(path, st)
	if err != OK:
		push_error("cannot load " + path)
		return null
	var n := doc.generate_scene(st) as Node3D
	for c in n.find_children("*", "MeshInstance3D", true, false):
		var nm := String(c.name)
		if "_LOD1" in nm or "_LOD2" in nm or "_LOD3" in nm or "shadow" in nm or "skeleton" in nm and not "Skeleton3D" in nm:
			c.visible = false
	return n


func skeleton_of(n: Node) -> Skeleton3D:
	return n.find_children("*", "Skeleton3D", true, false)[0] as Skeleton3D


func weapon(id: String) -> Node3D:
	return load_glb("%s/ready/%s/%s.glb" % [weapons_root, id, id])


func socket(n: Node, name: String) -> Node3D:
	return n.find_child(name, true, false) as Node3D


func rel(n: Node3D, child: Node3D) -> Transform3D:
	# transform of child relative to n (both inside the same tree, n an ancestor-or-sibling root)
	return n.global_transform.affine_inverse() * child.global_transform


func basis_from(y_dir: Vector3, x_hint: Vector3) -> Basis:
	var y := y_dir.normalized()
	var z := x_hint.cross(y).normalized()
	var x := y.cross(z).normalized()
	return Basis(x, y, z)


func dress(root: Node3D, key: String) -> Dictionary:
	var info := {}
	if key.begins_with("prod"):
		var id := "pc_veth_m_body_c" if key == "prod_m" else "pc_veth_f"
		var b := load_glb("%s/rigged/%s/%s_rigged.glb" % [asset_root, id, id])
		root.add_child(b)
		info["skel"] = skeleton_of(b)
		for c in b.find_children("*", "MeshInstance3D", true, false):
			if String(c.name).findn("hide_cap") >= 0:
				c.visible = false
		info["hand"] = "SOCK_hand.R"
		info["pelvis"] = "hips"; info["spine"] = "spine_mid"; info["thigh_l"] = "thigh.L"; info["hand_l"] = "hand.L"
		info["forearm_l"] = "forearm.L"; info["thigh_r"] = "thigh.R"; info["chest"] = "chest"; info["hand_r"] = "hand.R"
	else:
		var sx := "m" if key == "mh_m" else "f"
		var id := "mh_veth_" + sx
		var b := load_glb("%s/rigged/%s/%s_rigged.glb" % [asset_root, id, id])
		root.add_child(b)
		info["skel"] = skeleton_of(b)
		for g in ["shirt", "trousers", "jerkin", "boots"]:
			var gid := "player_%s_%s__%s" % [g, sx, id]
			var p := "%s/rigged/%s/%s_rigged.glb" % [asset_root, gid, gid]
			if FileAccess.file_exists(p):
				var gn := load_glb(p)
				root.add_child(gn)
		info["hand"] = "SOCK_hand_r"
		info["pelvis"] = "pelvis"; info["spine"] = "spine_03"; info["thigh_l"] = "thigh_l"; info["hand_l"] = "hand_l"
		info["forearm_l"] = "lowerarm_l"; info["thigh_r"] = "thigh_r"; info["chest"] = "spine_04"; info["hand_r"] = "hand_r"
	return info


func world_of_bone(skel: Skeleton3D, bone: String) -> Transform3D:
	var i := skel.find_bone(bone)
	if i < 0:
		push_error("no bone " + bone)
		return Transform3D.IDENTITY
	return skel.global_transform * skel.get_bone_global_pose(i)


func attach(skel: Skeleton3D, bone: String, model: Node3D, model_socket: String, world_frame: Transform3D, socket_frame := false) -> void:
	# put model so its `model_socket` frame lies on `world_frame`, riding `bone`
	var ba := BoneAttachment3D.new()
	ba.bone_name = bone
	skel.add_child(ba)
	ba.add_child(model)
	await process_frame
	var sock := socket(model, model_socket)
	var s_local := Transform3D.IDENTITY
	if sock:
		s_local = model.global_transform.affine_inverse() * sock.global_transform
	var bone_world := world_of_bone(skel, bone)
	if socket_frame:
		# the weapon's socket frame (x = primary toward the head, y = secondary) lies on world_frame: the hand grip
		model.global_transform = world_frame * s_local.affine_inverse()
	else:
		# the weapon's own axes follow world_frame's (y = butt to head); the socket's point lies on world_frame's origin
		model.global_transform = Transform3D(world_frame.basis, world_frame.origin) * Transform3D(Basis.IDENTITY, -s_local.origin)
	# re-parent semantic: the attachment follows the bone from here on; its child keeps the world transform now.


func place(spec: Dictionary, body_info: Dictionary) -> Transform3D:
	# spec: anchor_bone (body-space reference), offset (body-space metres from the anchor), y_dir, x_hint
	var skel: Skeleton3D = body_info["skel"]
	var anchor := world_of_bone(skel, body_info[spec["anchor"]]).origin
	var off := Vector3(spec["offset"][0], spec["offset"][1], spec["offset"][2])
	var yd := Vector3(spec["y_dir"][0], spec["y_dir"][1], spec["y_dir"][2])
	var xh := Vector3(spec["x_hint"][0], spec["x_hint"][1], spec["x_hint"][2])
	var o := anchor + off
	if spec.has("abs_y"):
		o.y = float(spec["abs_y"]) + off.y
	return Transform3D(basis_from(yd, xh), o)


func hand_frame(body_info: Dictionary, prim: Vector3, sec: Vector3) -> Transform3D:
	var skel: Skeleton3D = body_info["skel"]
	var hs := skel.get_parent().find_child(body_info["hand"], true, false) as Node3D
	var t: Transform3D
	if hs:
		t = hs.global_transform
	else:
		t = world_of_bone(skel, body_info["hand_r"])
	# grip frame in the hand socket's space: x = primary (toward the weapon head), y = secondary
	var x := (t.basis * prim).normalized()
	var y := (t.basis * sec)
	y = (y - x * x.dot(y)).normalized()
	return Transform3D(Basis(x, y, x.cross(y)), t.origin)


func shoot(name: String, pos: Vector3, target: Vector3, fov := 38.0) -> void:
	cam.fov = fov
	cam.global_position = pos
	cam.look_at(target, Vector3.UP)
	await process_frame
	await process_frame
	await process_frame
	var img := root.get_texture().get_image()
	img.save_png("%s/%s.png" % [out_dir, name])


func _run() -> void:
	root.size = Vector2i(1100, 1500)
	scene_root = Node3D.new()
	root.add_child(scene_root)
	var env := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.56, 0.61, 0.66)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.72, 0.74, 0.78)
	e.ambient_light_energy = 0.9
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.environment = e
	scene_root.add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-45, 35, 0)
	sun.light_energy = 1.6
	sun.shadow_enabled = true
	scene_root.add_child(sun)
	var fill := DirectionalLight3D.new()
	fill.rotation_degrees = Vector3(-20, -140, 0)
	fill.light_energy = 0.6
	scene_root.add_child(fill)
	var floor_mesh := MeshInstance3D.new()
	var pm := PlaneMesh.new(); pm.size = Vector2(12, 12)
	floor_mesh.mesh = pm
	var fm := StandardMaterial3D.new(); fm.albedo_color = Color(0.30, 0.34, 0.26); fm.roughness = 0.95
	floor_mesh.material_override = fm
	scene_root.add_child(floor_mesh)
	cam = Camera3D.new()
	scene_root.add_child(cam)
	cam.current = true

	var body := Node3D.new()
	scene_root.add_child(body)
	var info := dress(body, body_key)
	await process_frame
	var skel: Skeleton3D = info["skel"]
	var ground_y := 0.0
	var spec: Dictionary = carry[("mh" if body_key.begins_with("mh") else "prod")]

	# ---- sheathed sword at the left hip, bow + quiver on the back
	var sw := weapon("weapon_real_arming_sword_sheathed")
	await attach(skel, info["pelvis"], sw, "SOCK_carry_hip", place(spec["sword_hip"], info))
	var bow := weapon("weapon_real_longbow")
	await attach(skel, info["spine"], bow, "SOCK_attach", place(spec["bow_back"], info))
	var qv := weapon("weapon_real_quiver")
	await attach(skel, info["spine"], qv, "SOCK_attach", place(spec["quiver_back"], info))
	var hip_height := world_of_bone(skel, info["pelvis"]).origin.y
	var tgt := Vector3(0, hip_height + 0.25, 0)
	await shoot("carry_sheathed_front34", Vector3(1.7, 1.45, 3.2), tgt)
	await shoot("carry_sheathed_side_left", Vector3(3.4, 1.2, 0.1), tgt)
	await shoot("carry_back34", Vector3(-1.5, 1.45, -3.2), tgt)
	await shoot("carry_hip_close", Vector3(1.55, hip_height + 0.12, 1.1), Vector3(0.1, hip_height - 0.15, 0.0), 30.0)
	sw.queue_free(); bow.queue_free(); qv.queue_free()
	await process_frame

	# ---- spear + shield on the back, axe at the right hip
	var sp := weapon("weapon_real_hunting_spear")
	await attach(skel, info["spine"], sp, "SOCK_attach", place(spec["spear_back"], info))
	var sh := weapon("weapon_real_kite_shield")
	await attach(skel, info["spine"], sh, "SOCK_attach", place(spec["shield_back"], info))
	var ax := weapon("weapon_real_axe")
	await attach(skel, info["pelvis"], ax, "SOCK_attach", place(spec["axe_hip"], info))
	await shoot("carry_b_front34", Vector3(1.7, 1.45, 3.4), tgt, 40.0)
	await shoot("carry_b_back34", Vector3(-1.7, 1.45, -3.4), tgt, 40.0)
	sp.queue_free(); sh.queue_free(); ax.queue_free()
	await process_frame

	# ---- held in the right hand: sword (grip), axe, estoc, knife, plus spear and bow
	var held := [
		["held_sword", "weapon_real_arming_sword", Vector3(-0.26, 0.87, -0.42), Vector3(0, 0, 1)],
		["held_axe", "weapon_real_axe", Vector3(-0.26, 0.87, -0.42), Vector3(0, 0, 1)],
		["held_estoc", "weapon_real_estoc", Vector3(-0.26, 0.87, -0.42), Vector3(0, 0, 1)],
		["held_knife", "weapon_real_knife", Vector3(-0.26, 0.87, -0.42), Vector3(0, 0, 1)],
		["held_spear", "weapon_real_hunting_spear", Vector3(0, 1, 0), Vector3(0, 0, 1)],
	]
	for h in held:
		var w := weapon(h[1])
		await attach(skel, info["hand_r"], w, "SOCK_grip_primary", hand_frame(info, h[2], h[3]), true)
		await shoot(h[0] + "_front34", Vector3(1.2, 1.1, 2.6), Vector3(-0.35, 1.0, 0), 38.0)
		await shoot(h[0] + "_side", Vector3(-2.8, 1.0, 0.2), Vector3(-0.35, 0.95, 0), 38.0)
		w.queue_free()
		await process_frame

	# ---- on the ground as loot (weapons lying flat on the floor beside a standing body for scale)
	body.visible = true
	var loot := [
		["weapon_real_knife", "x"], ["weapon_real_arming_sword", "x"], ["weapon_real_axe", "x"], ["weapon_real_estoc", "x"],
		["weapon_real_kite_shield", "z"], ["weapon_real_quiver", "x"], ["weapon_real_longbow", "z"], ["weapon_real_hunting_spear", "x"],
	]
	var xi := 0.0
	for l in loot:
		var w := weapon(l[0])
		scene_root.add_child(w)
		# lie flat, long axis along the floor (world Z), its flat face (x for blades, z for the bow and shield) up
		var lay: Basis
		if l[1] == "x":
			lay = Basis(Vector3(0, 1, 0), Vector3(0, 0, -1), Vector3(-1, 0, 0))
		else:
			lay = Basis(Vector3(1, 0, 0), Vector3(0, 0, -1), Vector3(0, 1, 0))
		w.global_transform = Transform3D(Basis(Vector3.UP, deg_to_rad(fmod(xi * 37.0, 14.0) - 7.0)) * lay, Vector3(0.9 + xi, 0, 0))
		var lowest := 1e9
		var zmin := 1e9
		var zmax := -1e9
		var xmin := 1e9
		var xmax := -1e9
		for mi in w.find_children("*", "MeshInstance3D", true, false):
			var m := mi as MeshInstance3D
			var xf := m.global_transform
			for v in m.mesh.get_faces():
				var p := xf * v
				lowest = min(lowest, p.y)
				zmin = min(zmin, p.z); zmax = max(zmax, p.z)
				xmin = min(xmin, p.x); xmax = max(xmax, p.x)
		w.global_position.y -= lowest
		w.global_position.z -= (zmin + zmax) * 0.5
		xi += maxf(0.30, (xmax - xmin) + 0.14)
	var cx := 0.9 + xi * 0.5
	await shoot("loot_ground_overview", Vector3(cx + 1.6, 2.6, 3.6), Vector3(cx, 0.0, 0.0), 42.0)
	await shoot("loot_ground_top", Vector3(cx, 5.2, 0.25), Vector3(cx, 0.0, 0.0), 40.0)
	await shoot("loot_ground_low", Vector3(cx - 0.4, 0.45, 2.6), Vector3(cx - 0.4, 0.0, 0.0), 34.0)
	quit()
