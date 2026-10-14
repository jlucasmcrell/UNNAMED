extends SceneTree

const OBSERVER_PATH := "res://capture_tools/capture_observer.gd"
const TARGET_SIZE := Vector2i(2560, 1440)

func _initialize() -> void:
    call_deferred("_start_private_scene")

func _start_private_scene() -> void:
    var ablation := OS.get_environment("UNNAMED_SUBVIEWPORT_ABLATION")
    if ablation not in ["baseline", "plant_height_zero"]:
        push_error("SubViewport runner requires baseline or plant_height_zero ablation mode")
        quit(2)
        return
    var source_viewport := root.get_viewport()
    var game_viewport := SubViewport.new()
    game_viewport.name = "GameNativeTarget"
    game_viewport.size = TARGET_SIZE
    game_viewport.own_world_3d = true
    game_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
    game_viewport.transparent_bg = false
    game_viewport.msaa_3d = source_viewport.msaa_3d
    game_viewport.screen_space_aa = source_viewport.screen_space_aa
    game_viewport.use_taa = source_viewport.use_taa
    game_viewport.scaling_3d_mode = source_viewport.scaling_3d_mode
    game_viewport.scaling_3d_scale = 1.0
    root.add_child(game_viewport)

    var packed_main := load("res://Main.tscn") as PackedScene
    if packed_main == null:
        push_error("SubViewport runner could not load res://Main.tscn")
        quit(2)
        return
    var main := packed_main.instantiate()
    game_viewport.add_child(main)
    await process_frame
    await process_frame

    var evidence := OS.get_environment("UNNAMED_CONNECTED_EVIDENCE")
    var mode := OS.get_environment("UNNAMED_SUBVIEWPORT_MODE")
    var result_path := OS.get_environment("UNNAMED_SUBVIEWPORT_RESULT")
    var route := _user_argument_value("--connected-check")
    var evidence_ready := mode == "headless" and !result_path.is_empty() or mode == "render" and !evidence.is_empty() and DirAccess.dir_exists_absolute(evidence)
    if !evidence_ready or route.is_empty():
        push_error("A fresh route is required; headless mode needs UNNAMED_SUBVIEWPORT_RESULT, render mode needs ConnectedCheck's fresh evidence folder")
        quit(2)
        return
    var observer_script := load(OBSERVER_PATH) as Script
    if observer_script == null:
        push_error("SubViewport observer script could not be loaded")
        quit(2)
        return
    var observer: Node = observer_script.new()
    observer.name = "SubViewportCaptureObserver"
    observer.process_priority = 100 # after Main/SkyView clock tick
    root.add_child(observer)
    observer.call("configure", main, game_viewport, route, evidence)

func _user_argument_value(option: String) -> String:
    var arguments := OS.get_cmdline_user_args()
    for index in range(arguments.size() - 1):
        if arguments[index] == option:
            return arguments[index + 1]
    return ""
