extends SceneTree
## Production-scene regression/capture. Run Godot with --script <this file>, ordinary content/art arguments,
## --stand 600,700 and --visual tier=ultra,people=humgen,hud=production,weather=fair,hour=16.5.
## OTHERREACH_P1_FRAME_OUT names the receipt/frame directory. Acquire gpu_lock.ps1 first.
func _initialize():
    call_deferred("run")

func run():
    var output := OS.get_environment("OTHERREACH_P1_FRAME_OUT")
    if output.is_empty():
        push_error("OTHERREACH_P1_FRAME_OUT is required"); quit(2); return
    DirAccess.make_dir_recursive_absolute(output)
    var main = load("res://Main.tscn").instantiate()
    root.add_child(main)
    current_scene = main
    for frame in 30: await process_frame
    var hollow = main.get_node_or_null("Hollow")
    var scatter = main.get_node_or_null("Scatter")
    var stone = main.get_node_or_null("Hollow/rock_waystone")
    if hollow == null or scatter == null or stone == null:
        push_error("The production frontier scene did not build"); quit(2); return
    var folder: String = get_script().resource_path.get_base_dir()
    var water = load(folder + "/scatter_water_check.gd").check(scatter)
    var paint = load(folder + "/arena_paint_check.gd").check(hollow)
    var camera := Camera3D.new()
    camera.fov = 40; camera.near = 0.05; camera.far = 4000
    main.add_child(camera)
    var focus := Vector3(427, stone.global_position.y + 1.2, 758)
    camera.global_position = focus + Vector3(sin(deg_to_rad(300)) * 7, 1, cos(deg_to_rad(300)) * 7)
    camera.look_at(focus); camera.make_current()
    for terrain in main.find_children("*", "Terrain3D", true, false): terrain.call("set_camera", camera)
    for layer in main.find_children("*", "CanvasLayer", true, false): layer.visible = false
    main.set_process(false)
    for frame in 32: await process_frame
    await RenderingServer.frame_post_draw
    var receipt := {"water": water, "arena_paint": paint, "camera": str(camera.global_transform)}
    root.get_texture().get_image().save_png(output + "/waystone.png")
    FileAccess.open(output + "/checks.json", FileAccess.WRITE).store_string(JSON.stringify(receipt, "  "))
    print("FIRST MINUTES CHECKS ", JSON.stringify(receipt))
    quit(0 if water["passed"] and paint["passed"] else 1)
