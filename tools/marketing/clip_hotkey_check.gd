extends SceneTree
## Run against the private PACKAGED candidate after the release gate, without --fixed-fps.
## Ordinary populated world, normal player camera, Ultra, --stand 427,752, separate --profile.
## Only real input events and visible UI controls: no injected player/world/animation state.
var output: String
var main: Node
var events: Array[Dictionary] = []
var started: int

func _initialize():
    call_deferred("run")

func require(condition: bool, message: String) -> bool:
    if condition: return true
    push_error(message)
    if not output.is_empty():
        FileAccess.open(output.path_join("FAILED.json"), FileAccess.WRITE).store_string(JSON.stringify({"error": message}, "  "))
    quit(1)
    return false

func wait_seconds(seconds: float):
    var until := Time.get_ticks_msec() + int(seconds * 1000)
    while Time.get_ticks_msec() < until: await process_frame

func key(code: Key, pressed: bool):
    var event := InputEventKey.new()
    event.keycode = code; event.physical_keycode = code; event.pressed = pressed
    Input.parse_input_event(event)

func tap(code: Key):
    key(code, true)
    await process_frame
    key(code, false)
    await process_frame

func mark(phase: String):
    var player := main.get_node("Player") as Node3D
    events.append({"phase": phase, "seconds": (Time.get_ticks_msec() - started) / 1000.0,
        "player": [player.global_position.x, player.global_position.y, player.global_position.z], "render_frame": Engine.get_frames_drawn()})

func control(id: String) -> Control:
    for node in main.find_children("*", "Control", true, false):
        if node.get_meta("ui_id", "") == id and node.is_visible_in_tree(): return node
    return null

func status() -> Label:
    var layer := main.get_node_or_null("ClipRecording")
    if layer == null: return null
    var labels := layer.find_children("*", "Label", true, false)
    return labels[0] if not labels.is_empty() else null

func labels(node: Node) -> String:
    var text := ""
    for label in node.find_children("*", "Label", true, false): text += label.text + "\n"
    return text

func wait_saved() -> bool:
    var until := Time.get_ticks_msec() + 15000
    while status() != null and status().visible:
        if Time.get_ticks_msec() > until: return require(false, "Clip did not finish within 15 seconds")
        await process_frame
    return true

func shot(name: String):
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png(output.path_join(name + ".png"))

func run():
    output = OS.get_environment("OTHERREACH_CLIP_PROOF_OUT")
    if not require(not output.is_empty(), "OTHERREACH_CLIP_PROOF_OUT is required"): return
    DirAccess.make_dir_recursive_absolute(output)
    if not require(OS.has_feature("template"), "Clip acceptance must run from the packaged candidate"): return
    main = load("res://Main.tscn").instantiate()
    root.add_child(main); current_scene = main
    var deadline := Time.get_ticks_msec() + 450000
    while main.get_node_or_null("Player") == null or main.get_node_or_null("Settings") == null or main.get_node_or_null("Npcs") == null:
        if Time.get_ticks_msec() > deadline:
            require(false, "Packaged world did not become ready"); return
        await process_frame
    await wait_seconds(8)
    if not require(main.get_node("Npcs").get_child_count() > 0, "The packaged world must remain populated"): return
    var holders: Array[String] = []
    for action in InputMap.get_actions():
        for event in InputMap.action_get_events(action):
            if event is InputEventKey and (event.physical_keycode == KEY_F11 or event.keycode == KEY_F11) \
                and not (event.ctrl_pressed or event.alt_pressed or event.shift_pressed or event.meta_pressed):
                holders.append(str(action))
    if not require(holders == ["record_clip"], "F11 must belong only to record_clip: " + str(holders)): return
    started = Time.get_ticks_msec()
    mark("before_F11")
    await tap(KEY_F11); await wait_seconds(.3)
    if not require(status() != null and status().visible and status().text.contains("REC"), "F11 did not start native clip capture"): return
    var origin: Vector3 = main.get_node("Player").global_position
    key(KEY_W, true); await wait_seconds(1.6); key(KEY_W, false)
    mark("walk_forward")
    if not require(origin.distance_to(main.get_node("Player").global_position) > .5, "The ordinary movement key did not move the player"): return
    key(KEY_S, true); await wait_seconds(1.6); key(KEY_S, false); mark("walk_back")
    key(KEY_D, true); await wait_seconds(1.2); key(KEY_D, false); mark("walk_right")
    key(KEY_A, true); await wait_seconds(1.2); key(KEY_A, false); mark("walk_left")
    await wait_seconds(2)
    await tap(KEY_F11)
    if not await wait_saved(): return
    mark("F11_stopped_and_saved")

    await tap(KEY_F1); await wait_seconds(.3)
    var help := main.get_node("Help")
    if not require(help.visible, "Field Guide did not open"): return
    var search: LineEdit = help.find_children("*", "LineEdit", true, false)[0]
    search.text = "video clip"; search.text_changed.emit(search.text)
    await wait_seconds(.2)
    var guide_text := labels(help)
    if not require(guide_text.contains("video clip") and guide_text.contains("F11"), "The Field Guide must list the live clip key"): return
    await shot("field_guide_clip_key")
    await tap(KEY_F1); await wait_seconds(.2)
    await tap(KEY_ESCAPE); await wait_seconds(.2)
    var settings_button := control("Settings")
    if not require(settings_button != null, "System Menu Settings row is missing"): return
    settings_button.emit_signal("pressed"); await wait_seconds(.2)
    var controls_button := control("page.controls")
    if not require(controls_button != null, "Settings Controls page is missing"): return
    controls_button.emit_signal("pressed"); await wait_seconds(.2)
    var binding := control("key.record_clip.0") as Button
    if not require(binding != null and binding.text == "F11", "Settings must expose the default clip binding"): return
    binding.grab_focus()
    var ancestor := binding.get_parent()
    while ancestor != null:
        if ancestor is ScrollContainer: ancestor.ensure_control_visible(binding)
        ancestor = ancestor.get_parent()
    await wait_seconds(.2); await shot("settings_clip_key")

    # Rebind through the real Settings row; F11 must stop owning the action, F9 must start/stop it even in a modal screen.
    binding.emit_signal("pressed"); await process_frame; await tap(KEY_F9); await wait_seconds(.3)
    var rebound := control("key.record_clip.0") as Button
    if not require(rebound != null and rebound.text == "F9", "The clip binding did not change to F9"): return
    await tap(KEY_F11); await wait_seconds(.2)
    if not require(not status().visible, "The old F11 binding still starts recording after rebinding"): return
    await tap(KEY_F9); await wait_seconds(.3)
    if not require(status().visible and status().text.contains("REC"), "The new F9 binding did not start a clip"): return
    await wait_seconds(2); await tap(KEY_F9)
    if not await wait_saved(): return
    mark("rebound_F9_started_stopped_and_saved")
    var reset := control("controls.reset")
    if not require(reset != null, "Settings reset is missing"): return
    reset.emit_signal("pressed"); await wait_seconds(.3)
    if not require((control("key.record_clip.0") as Button).text == "F11", "Reset did not restore F11"): return
    await tap(KEY_ESCAPE); await wait_seconds(.2)
    await tap(KEY_F11); await wait_seconds(.3)
    if not require(status().visible and status().text.contains("REC"), "Recording could not restart before quit"): return
    await wait_seconds(2)
    mark("quit_while_recording")
    var receipt := {"status": "INPUT_AND_UI_CHECKS_PASSED_QUIT_FINALISATION_PENDING_DECODE",
        "packaged_executable": OS.get_executable_path(), "fps_override": false,
        "F11_holders": holders, "field_guide_lists_clip": true, "settings_rebind_reset": true,
        "screenshots_folder": OS.get_executable_path().get_base_dir().path_join("screenshots"), "events": events}
    FileAccess.open(output.path_join("input_checks.json"), FileAccess.WRITE).store_string(JSON.stringify(receipt, "  "))
    quit(0)
