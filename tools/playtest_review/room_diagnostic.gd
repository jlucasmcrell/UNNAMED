extends SceneTree

var output := OS.get_environment("B034_OUT")
var scene: Node
var camera: Camera3D

func _initialize() -> void:
	_run.call_deferred()

func shot(name: String) -> void:
	await create_timer(3).timeout
	if DisplayServer.get_name() != "headless":
		await RenderingServer.frame_post_draw
		root.get_texture().get_image().save_png(output.path_join(name + ".png"))
	print("B034 DIAGNOSTIC ", name)

func _run() -> void:
	DirAccess.make_dir_recursive_absolute(output)
	scene = load("res://Main.tscn").instantiate()
	root.add_child(scene)
	while scene.get_node_or_null("Creator") == null:
		await create_timer(1).timeout
	await create_timer(4).timeout
	var rig: Node3D
	for node in scene.find_children("*", "Node3D", true, false):
		if node.get_script() != null and node.get_script().resource_path == "res://Player/CameraRig.cs":
			rig = node
	assert(rig != null)
	# The native player can replace the initial avatar node. Read the production rig's eye instead of a transient node name.
	var feet := rig.global_position - Vector3(0, 1.62, 0)
	if feet.distance_to(Vector3(458, 7.2, 741)) > .05:
		push_error("B034 FAILED: camera rig is not following the requested stand: " + str(feet))
		quit(1)
		return
	# Absolute yaw through the production mouse-look method; read the native transform.
	rig.call("Look", Vector2(-wrapf(deg_to_rad(-140.0) - rig.rotation.y, -PI, PI) / .0025, 0))
	var game_camera := root.get_camera_3d()
	await shot("01_game_camera_shared")
	camera = Camera3D.new()
	camera.name = "B034_ReportCamera"
	camera.near = .05
	camera.far = 4000
	scene.add_child(camera)
	camera.fov = 58
	var centre := Vector3(497, -1, 844)
	camera.global_position = centre + Vector3(sin(deg_to_rad(134.0)) * 67.3, 24, cos(deg_to_rad(134.0)) * 67.3)
	camera.look_at(centre)
	camera.make_current()
	await shot("02_report_gorge_preroll")
	centre = feet + Vector3(0, 1, 0)
	camera.fov = 40
	camera.global_position = centre + Vector3(sin(deg_to_rad(220.0)) * 3.6, .5, cos(deg_to_rad(220.0)) * 3.6)
	camera.look_at(centre)
	await shot("03_report_room_pose_shared")
	var terrains := scene.find_children("*", "Terrain3D", true, false)
	for terrain in terrains:
		terrain.call("set_camera", camera)
	await shot("04_diagnostic_terrain_camera_bound")
	var occlusion := root.use_occlusion_culling
	root.use_occlusion_culling = false
	await shot("05_diagnostic_occlusion_off")
	root.use_occlusion_culling = occlusion
	game_camera.make_current()
	for terrain in terrains:
		terrain.call("set_camera", game_camera)
	await shot("06_game_camera_after")
	var arm: SpringArm3D = game_camera.get_parent()
	var camera_record := {"position":str(game_camera.global_position), "basis":str(game_camera.global_basis), "fov":game_camera.fov, "spring_length":arm.spring_length, "hit_length":arm.get_hit_length(), "occlusion":root.use_occlusion_culling}
	rig.call("Look", Vector2(-deg_to_rad(30.0) / .0025, 0))
	await shot("07_game_camera_after_other_angle")
	var file := FileAccess.open(output.path_join("diagnostic.json"), FileAccess.WRITE)
	file.store_string(JSON.stringify({"feet":str(feet),"camera":str(camera.global_position),"fov":camera.fov,"terrain_count":terrains.size(),"original_occlusion":occlusion,"game_camera_after":camera_record,"debug_collisions":debug_collisions_hint,"debug_navigation":debug_navigation_hint,"debug_draw":root.debug_draw}, "  "))
	print("B034 DIAGNOSTIC COMPLETE; diagnostic toggles are not fixes")
	quit(0)
