extends Node

var viewport: SubViewport
var main: Node3D
var output := ""
var frame := 0
var sweep_step := 0
var pending := false
var camera: Camera3D
var center := Vector3.ZERO
var start_eye := Vector3.ZERO
var records: Array = []
var previous_usec := 0
var native_terrain_postdraw_verified := false

func vec(v: Vector3) -> Array:
    return [v.x,v.y,v.z]

func write_json(path: String, data: Variant) -> void:
    var file := FileAccess.open(path,FileAccess.WRITE)
    file.store_string(JSON.stringify(data,"  "))
    file.close()

func _ready() -> void:
    var args := OS.get_cmdline_user_args()
    output = args[args.find("--coverage-out")+1]
    viewport = SubViewport.new()
    viewport.name = "ActualConnectedNative4K"
    viewport.size = Vector2i(3840,2160)
    viewport.own_world_3d = true
    viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
    add_child(viewport)
    var scene := load("res://Main.tscn") as PackedScene
    main = scene.instantiate() as Node3D
    if main.get_script().resource_path != "res://Main.cs" or not main.has_method("_Process"):
        push_error("Actual compiled Main missing")
        get_tree().quit(3)
        return
    if DisplayServer.get_name() == "headless":
        DirAccess.make_dir_recursive_absolute(output)
        write_json(output.path_join("bootstrap.json"),{"status":"ACTUAL_MAIN_ATTACHED_NOT_BOOTED","viewport":[3840,2160],"gpu_used":false,"script":main.get_script().resource_path})
        main.free()
        get_tree().quit(0)
        return
    process_priority = 100
    viewport.add_child(main)
    if not require_native_terrain("SCENE_BUILT"):
        return
    RenderingServer.viewport_set_measure_render_time(viewport.get_viewport_rid(),true)
    RenderingServer.frame_post_draw.connect(after_draw)
    print("FULL_WORLD_NATIVE_VIEWPORT ",viewport.size," actualMain=",main.get_script().resource_path)

func _process(_delta: float) -> void:
    frame += 1
    if not is_instance_valid(main) or not FileAccess.file_exists(output.path_join("raw/quarry_station.json")):
        return
    camera = main.get_node_or_null("AuditCamera") as Camera3D
    if camera == null:
        return
    if sweep_step == 0:
        var rock := main.find_child("rock_rim_02",true,false) as Node3D
        if rock == null:
            push_error("Actual world quarry node absent")
            get_tree().quit(4)
            return
        var meshes := rock.find_children("*","MeshInstance3D",true,false)
        if meshes.is_empty():
            push_error("Actual world quarry meshes absent")
            get_tree().quit(4)
            return
        var mesh := meshes[0] as MeshInstance3D
        center = mesh.global_transform * mesh.get_aabb().get_center()
        start_eye = camera.global_position
        previous_usec = Time.get_ticks_usec()
    sweep_step += 1
    var fraction := minf(float(sweep_step-1)/119.0,1.0)
    var direction := (start_eye-center).normalized().rotated(Vector3.UP,deg_to_rad(24.0*fraction))
    var eye := center + direction * lerpf(start_eye.distance_to(center),58.0,fraction)
    # Preserve actual terrain heights along the camera path rather than passing through the slope.
    for node in main.find_children("*","Terrain3D",true,false):
        var data = node.get("data")
        if data != null and data.has_method("get_height"):
            var ground: float = data.call("get_height",Vector3(eye.x,0,eye.z))
            if is_finite(ground):
                eye.y = maxf(eye.y,ground+2.65)
    camera.look_at_from_position(eye,center,Vector3.UP)
    pending = sweep_step <= 120 and sweep_step % 4 == 0

func after_draw() -> void:
    if not native_terrain_postdraw_verified:
        if not require_native_terrain("FRAME_POST_DRAW"):
            return
        native_terrain_postdraw_verified = true
    if not pending or camera == null:
        return
    pending = false
    var image := viewport.get_texture().get_image()
    if image.get_width() != 3840 or image.get_height() != 2160:
        push_error("Actual native image dimensions failed")
        get_tree().quit(5)
        return
    var path := output.path_join("motion_%03d.png" % records.size())
    if image.save_png(path) != OK:
        push_error("Actual native frame save failed")
        get_tree().quit(5)
        return
    var now := Time.get_ticks_usec()
    records.append({"file":path.get_file(),"step":sweep_step,"world_frame":frame,"camera_eye":vec(camera.global_position),"camera_target":vec(center),"observed_center_distance_m":camera.global_position.distance_to(center),"fov":camera.fov,"near":camera.near,"far":camera.far,"wall_usec":now,"sample_interval_ms":float(now-previous_usec)/1000.0,"gpu_ms":RenderingServer.viewport_get_measured_render_time_gpu(viewport.get_viewport_rid()),"cpu_ms":RenderingServer.viewport_get_measured_render_time_cpu(viewport.get_viewport_rid()),"vram_mb":Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)/1048576.0,"primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME),"draw_calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),"image_size":[image.get_width(),image.get_height()],"viewport_size":[viewport.size.x,viewport.size.y]})
    previous_usec = now
    write_json(output.path_join("motion_frames.json"),{"status":"COMPLETE" if records.size()==30 else "IN_PROGRESS","records":records,"adapter":RenderingServer.get_video_adapter_name(),"rendering_method":RenderingServer.get_current_rendering_method(),"root_window_size":[get_viewport().get_visible_rect().size.x,get_viewport().get_visible_rect().size.y],"actual_main":main.get_script().resource_path,"capture":"Actual connected world; camera-only sweep, not gameplay travel. Motion VisualAudit nominal eye metadata is superseded by these post-draw poses."})
    print("FULL_WORLD_MOTION_CAPTURE ",records.size()," distance=",camera.global_position.distance_to(center))

func require_native_terrain(phase: String) -> bool:
    var terrain := main.find_child("Terrain3D",true,false) as Node3D
    var old := main.find_child("Terrain",true,false) as MeshInstance3D
    var data = terrain.get("data") if terrain != null else null
    var count: int = int(data.call("get_region_count")) if data != null and data.has_method("get_region_count") else 0
    var height: float = float(data.call("get_height",Vector3(431,0,688))) if data != null and data.has_method("get_height") else NAN
    var valid: bool = terrain != null and terrain.get_class() == "Terrain3D" and terrain.is_visible_in_tree() and old != null and not old.is_visible_in_tree() and count > 0 and is_finite(height)
    write_json(output.path_join("native_terrain_"+phase.to_lower()+".json"),{"status":"PASS" if valid else "FAIL","phase":phase,"native_class":terrain.get_class() if terrain != null else null,"native_path":str(terrain.get_path()) if terrain != null else null,"native_visible":terrain.is_visible_in_tree() if terrain != null else false,"native_regions":count,"height_at_quarry":height if is_finite(height) else null,"old_terrain_path":str(old.get_path()) if old != null else null,"old_terrain_visible":old.is_visible_in_tree() if old != null else null,"adapter":RenderingServer.get_video_adapter_name(),"viewport_size":[viewport.size.x,viewport.size.y],"scope":"Native Terrain3D instance visible with populated data and finite actual height; old textured Terrain hidden. Postdraw receipt plus captured actual world ground are rendering evidence; not per-pass profile."})
    if not valid:
        push_error("Required actual Terrain3D missing/unpopulated or old Terrain fallback still visible")
        get_tree().quit(6)
    return valid
