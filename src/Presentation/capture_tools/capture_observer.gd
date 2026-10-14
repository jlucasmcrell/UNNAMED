extends Node

const TARGET_SIZE := Vector2i(2560, 1440)
const MAX_FRAMES := 48
const PRE_EVENT_SAMPLES := 12
const POST_EVENT_FRAMES := 36
const PRE_EVENT_STRIDE := 2
const FALLBACK_FRAME := 900
const GRASS_CROP_RECT := Rect2(0.0, 800.0, 640.0, 360.0)
const GRASS_SAMPLE_LIMIT := 256
const AH_PROJECTED_FRAME_MIN := 18
const GRASS_PARAMETERS := ["plant_height", "flutter", "stiffness", "has_normal", "has_arm", "alpha_cut", "upright_normals", "roughness_value", "backlight_colour"]

var _main: Node3D
var _viewport: SubViewport
var _route_path := ""
var _evidence_path := ""
var _mode := "render"
var _ablation := ""
var _result_path := ""
var _frame_dir := ""
var _player: Node3D
var _camera_rig: Node3D
var _camera: Camera3D
var _scatter: Node3D
var _sky3d: WorldEnvironment
var _environment: Environment
var _sun: DirectionalLight3D
var _wind_field: Node
var _targets: Array[Dictionary] = []
var _route: Array = []
var _route_label := "11-point Ashen Hollow excerpt"
var _start_position := Vector3.ZERO
var _moving := false
var _frame := 0
var _event_frame := -1
var _post_remaining := 0
var _capture_fallback := false
var _busy := false
var _ring: Array[Dictionary] = []
var _records: Array[Dictionary] = []
var _last_distances: Dictionary = {}
var _crossings: Array[Dictionary] = []
var _cache_usec := 0
var _scanned_children := 0
var _failed := ""
var _shadow_override_reapplications := 0
var _grass_nodes: Array[MultiMeshInstance3D] = []
var _grass_node_kinds: Dictionary = {}
var _grass_material_refs: Array[Dictionary] = []
var _grass_material_manifest: Array[Dictionary] = []
var _grass_material_cache_complete := false
var _plant_height_reapplications := 0
var _plant_height_override_verified := false
var _wind_time_origin: Variant = null
var _wind_elapsed := 0.0
var _wind_time_estimate := 0.0
var _relative_wind_clock_ready := false
var _wind_time_terminal: Variant = null
var _last_delta := 0.0
var _frame_clock_usec := 0
var _readback_elapsed_usec := 0

func configure(main: Node3D, viewport: SubViewport, route_path: String, evidence_path: String) -> void:
    _main = main
    _viewport = viewport
    _route_path = route_path
    _evidence_path = evidence_path
    _mode = OS.get_environment("UNNAMED_SUBVIEWPORT_MODE")
    _ablation = OS.get_environment("UNNAMED_SUBVIEWPORT_ABLATION")
    _result_path = OS.get_environment("UNNAMED_SUBVIEWPORT_RESULT")
    if _mode == "render":
        _frame_dir = evidence_path.path_join("subviewport_frames")
    if _ablation not in ["baseline", "plant_height_zero"]:
        _failed = "ablation must be baseline or plant_height_zero"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    _read_route()
    if !_failed.is_empty():
        set_process(false)
        return
    _cache_live_nodes()
    if !_failed.is_empty():
        set_process(false)
        _write_receipt("BLOCKED")
        return
    _cache_grass_materials()
    if !_failed.is_empty() or !_grass_material_cache_complete:
        if _failed.is_empty():
            _failed = "foliage material cache did not complete"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    if _player == null or _camera == null or _camera_rig == null:
        _failed = "actual Main/Player/CameraRig/Pitch/Arm/Camera node not found"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    if _environment == null or _sun == null:
        if _failed.is_empty():
            _failed = "active Sky3D environment or its sun was not resolved"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    if !_camera.current or _camera.get_viewport() != _viewport:
        _failed = "actual Main camera is not current in the target SubViewport"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    if _mode == "render":
        if DirAccess.dir_exists_absolute(_frame_dir):
            _failed = "refuse existing frame sequence folder"
            set_process(false)
            _write_receipt("BLOCKED")
            return
        DirAccess.make_dir_recursive_absolute(_frame_dir)
    elif _mode != "headless" or _result_path.is_empty() or FileAccess.file_exists(_result_path):
        _failed = "invalid headless result path/mode or existing result"
        set_process(false)
        return
    _start_position = _player.global_position
    _moving = false

func _read_route() -> void:
    if !FileAccess.file_exists(_route_path):
        _failed = "connected route file unavailable"
        return
    var parsed = JSON.parse_string(FileAccess.get_file_as_string(_route_path))
    if typeof(parsed) != TYPE_DICTIONARY or !parsed.has("route"):
        _failed = "connected route JSON lacks a route array"
        return
    _route = parsed["route"]
    if _route.size() != 11:
        _route_label = "ConnectedCheck route has %d points; expected 11-point Ashen Hollow excerpt" % _route.size()

func _cache_live_nodes() -> void:
    _player = _main.get_node_or_null("Player") as Node3D
    _camera_rig = _main.get_node_or_null("CameraRig") as Node3D
    _camera = _main.get_node_or_null("CameraRig/Pitch/Arm/Camera") as Camera3D
    _scatter = _main.get_node_or_null("Scatter") as Node3D
    _sky3d = _main.get_node_or_null("Hollow/Lighting/Sky3D") as WorldEnvironment
    if _sky3d == null:
        _failed = "active Sky3D WorldEnvironment missing at Main/Hollow/Lighting/Sky3D"
        return
    _environment = _sky3d.environment
    if _environment == null:
        _failed = "active Sky3D WorldEnvironment has no Environment resource"
        return
    var active_sun: Variant = _sky3d.get("sun")
    if !(active_sun is DirectionalLight3D):
        _failed = "active Sky3D sun property is missing or is not a DirectionalLight3D"
        return
    _sun = active_sun as DirectionalLight3D
    _wind_field = _main.find_child("Wind", true, false)
    if _wind_field == null or !_wind_field.is_processing():
        _failed = "active WindField node named Wind is missing or not processing"
        return
    if _wind_field.process_priority >= process_priority:
        _failed = "WindField must process before the priority-100 capture observer"
        return
    var named_sun := _sky3d.get_node_or_null("SunLight") as DirectionalLight3D
    if named_sun == null or named_sun != _sun:
        _failed = "active Sky3D sun property does not resolve to its SunLight node"
        return
    if !_environment.ssao_enabled:
        _failed = "both arms require SSAO enabled on the active Sky3D Environment"
        return
    if !_sun.shadow_enabled:
        _failed = "both motion arms require active Sky3D sun shadows enabled and unchanged"
        return
    if _scatter == null or _route.size() < 2:
        return
    var started := Time.get_ticks_usec()
    _scanned_children = _scatter.get_child_count()
    var nearest := {"brambles": INF, "ferns": INF, "rock_clusters_lod2": INF,
        "moor_grass": INF, "blades_a": INF, "blades_b": INF, "blades_c": INF, "blades_d": INF}
    for child in _scatter.get_children():
        if !(child is MultiMeshInstance3D):
            continue
        var kind := _scatter_kind_from_name(str(child.name))
        if kind.is_empty() and str(child.name).begins_with("brambles_"):
            kind = "brambles"
        elif kind.is_empty() and str(child.name).begins_with("ferns_"):
            kind = "ferns"
        elif kind.is_empty() and str(child.name).begins_with("rock_clusters_") and _lod_from_name(str(child.name)) == 2:
            kind = "rock_clusters_lod2"
        if kind.is_empty():
            continue
        var bounds: AABB = child.get_aabb()
        var center: Vector3 = child.global_transform * bounds.get_center()
        var nearest_route := _distance_to_route_xz(center)
        if nearest_route < nearest[kind]:
            nearest[kind] = nearest_route
            var lod_marker := str(child.name).rfind("_lod")
            _targets = _replace_target(_targets, kind, {
                "node": child,
                "path": str(child.get_path()),
                "chunk_stem": str(child.name).substr(0, lod_marker),
                "lod": _lod_from_name(child.name),
                "center_m": _xyz(center),
                "bounds_position_m": _xyz(bounds.position),
                "bounds_size_m": _xyz(bounds.size),
                "route_nearest_distance_m": nearest_route,
                "visibility_begin_m": child.visibility_range_begin,
                "visibility_end_m": child.visibility_range_end,
                "cast_shadow": child.cast_shadow,
                "visibility_fade_mode": child.visibility_range_fade_mode,
            })
    _cache_usec = Time.get_ticks_usec() - started
    var expanded_targets: Array[Dictionary] = []
    for target in _targets:
        var chain := []
        var stem: String = target["chunk_stem"]
        for child in _scatter.get_children():
            if !(child is MultiMeshInstance3D) or !str(child.name).begins_with(stem + "_lod"):
                continue
            var bounds: AABB = child.get_aabb()
            var center: Vector3 = child.global_transform * bounds.get_center()
            chain.append({
                "path": str(child.get_path()),
                "lod": _lod_from_name(child.name),
                "center_m": _xyz(center),
                "bounds_position_m": _xyz(bounds.position),
                "bounds_size_m": _xyz(bounds.size),
                "route_nearest_distance_m": _distance_to_route_xz(center),
                "visibility_begin_m": child.visibility_range_begin,
                "visibility_end_m": child.visibility_range_end,
                "cast_shadow": child.cast_shadow,
                "visibility_fade_mode": child.visibility_range_fade_mode,
            })
        if _is_grass_kind(str(target["kind"])):
            for chain_node in chain:
                var chain_path: String = chain_node["path"]
                var matched: Node3D = _scatter.get_node_or_null(str(chain_path).get_file()) as Node3D
                if matched == null:
                    continue
                var chain_target := target.duplicate(true)
                chain_target["node"] = matched
                chain_target["path"] = chain_path
                chain_target["lod"] = chain_node["lod"]
                chain_target["center_m"] = chain_node["center_m"]
                chain_target["bounds_position_m"] = chain_node["bounds_position_m"]
                chain_target["bounds_size_m"] = chain_node["bounds_size_m"]
                chain_target["route_nearest_distance_m"] = chain_node["route_nearest_distance_m"]
                chain_target["visibility_begin_m"] = chain_node["visibility_begin_m"]
                chain_target["visibility_end_m"] = chain_node["visibility_end_m"]
                chain_target["cast_shadow"] = chain_node["cast_shadow"]
                chain_target["visibility_fade_mode"] = chain_node["visibility_fade_mode"]
                chain_target.erase("lod_chain")
                expanded_targets.append(chain_target)
        else:
            target["lod_chain"] = chain
            expanded_targets.append(target)
    _targets = expanded_targets
    for target in _targets:
        _last_distances[target["path"]] = null

func _replace_target(existing: Array[Dictionary], kind: String, target: Dictionary) -> Array[Dictionary]:
    var out: Array[Dictionary] = []
    for row in existing:
        if row["kind"] != kind:
            out.append(row)
    target["kind"] = kind
    out.append(target)
    return out

func _distance_to_route_xz(point: Vector3) -> float:
    var best := INF
    var p := Vector2(point.x, point.z)
    for i in range(_route.size() - 1):
        var a := Vector2(float(_route[i][0]), float(_route[i][1]))
        var b := Vector2(float(_route[i + 1][0]), float(_route[i + 1][1]))
        var segment := b - a
        var t: float = 0.0 if segment.length_squared() == 0.0 else clamp((p - a).dot(segment) / segment.length_squared(), 0.0, 1.0)
        best = min(best, p.distance_to(a + segment * t))
    return best

func _lod_from_name(value: String) -> int:
    var marker := value.rfind("_lod")
    if marker < 0:
        return -1
    var suffix := value.substr(marker + 4).split("@")[0]
    return int(suffix) if suffix.is_valid_int() else -1

func _scatter_kind_from_name(value: String) -> String:
    for kind in ["blades_a", "blades_b", "blades_c", "blades_d", "moor_grass"]:
        if value.begins_with(kind + "_"):
            return kind
    return ""

func _is_grass_kind(kind: String) -> bool:
    return kind in ["blades_a", "blades_b", "blades_c", "blades_d", "moor_grass"]

func _is_ah_grass_kind(kind: String) -> bool:
    return kind in ["blades_a", "blades_b", "blades_c", "blades_d"]

func _process(delta: float) -> void:
    if _main == null or !is_instance_valid(_camera) or !is_instance_valid(_player):
        return
    _last_delta = delta
    if !_relative_wind_clock_ready or !is_instance_valid(_wind_field) or !_wind_field.is_processing() or _wind_field.process_priority >= process_priority:
        _failed = "relative wind clock lost its active earlier-processing WindField"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    _wind_elapsed += delta
    _wind_time_estimate = _wind_elapsed
    _frame_clock_usec = Time.get_ticks_usec()
    if !_verify_grass_materials():
        set_process(false)
        _write_receipt("BLOCKED")
        return
    if _busy:
        return
    if !is_instance_valid(_sky3d) or !is_instance_valid(_environment) or !is_instance_valid(_sun):
        _failed = "active Sky3D environment/sun became invalid during route"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    var active_sun: Variant = _sky3d.get("sun")
    if !(active_sun is DirectionalLight3D) or (active_sun as DirectionalLight3D) != _sun or _sky3d.environment != _environment:
        _failed = "active Sky3D environment/sun reference changed during route"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    if _sun.shadow_enabled != true or !_environment.ssao_enabled:
        _failed = "baseline sun shadows and SSAO must remain enabled in both motion arms"
        set_process(false)
        _write_receipt("BLOCKED")
        return
    _frame += 1
    if _mode == "headless":
        var distances := _target_distances()
        var crossings := _find_crossings(distances)
        if !crossings.is_empty():
            _crossings.append_array(crossings)
        if _frame % 30 == 0:
            _records.append(_frame_record_headless(distances, crossings))
            if _records.size() == 1:
                # Persist while Main and its camera/light/viewport are still alive.
                _write_receipt("HEADLESS_EARLY_TECHNICAL_ONLY")
                set_process(false)
        return
    if !_moving:
        if _player.global_position.distance_to(_start_position) < 0.5:
            return
        _moving = true
    var distances := _target_distances()
    var crossings := _find_crossings(distances)
    if !crossings.is_empty():
        _crossings.append_array(crossings)
    var grass_crossings := []
    for crossing in crossings:
        if _is_ah_grass_kind(str(crossing["kind"])):
            grass_crossings.append(crossing)
    if _event_frame < 0 and !grass_crossings.is_empty():
        _event_frame = _frame
        _post_remaining = POST_EVENT_FRAMES
        _flush_ring()
        if !_failed.is_empty():
            set_process(false)
            _write_receipt("BLOCKED")
            return
        _capture_fallback = false
    elif _event_frame < 0 and _frame >= FALLBACK_FRAME:
        _event_frame = _frame
        _capture_fallback = true
        _post_remaining = POST_EVENT_FRAMES
        _flush_ring()
        if !_failed.is_empty():
            set_process(false)
            _write_receipt("BLOCKED")
            return
    if _event_frame < 0:
        if _frame % PRE_EVENT_STRIDE == 0:
            _busy = true
            _capture_into_ring(distances, crossings)
        return
    if _post_remaining > 0:
        _busy = true
        _capture_post_event(distances, crossings)

func _target_distances() -> Dictionary:
    var output := {}
    for target in _targets:
        var center := Vector3(target["center_m"][0], target["center_m"][1], target["center_m"][2])
        output[target["path"]] = _camera.global_position.distance_to(center)
    return output

func _find_crossings(distances: Dictionary) -> Array[Dictionary]:
    var found: Array[Dictionary] = []
    for target in _targets:
        var path: String = target["path"]
        var previous = _last_distances[path]
        var current = distances[path]
        if previous != null:
            for field in ["visibility_begin_m", "visibility_end_m"]:
                var threshold: float = target[field]
                if threshold > 0.0 and ((previous < threshold and current >= threshold) or (previous >= threshold and current < threshold)):
                    found.append({"kind": target["kind"], "node": path, "threshold_m": threshold, "boundary": field,
                        "distance_from_m": previous, "distance_to_m": current, "route_nearest_distance_m": target["route_nearest_distance_m"], "frame": _frame})
        _last_distances[path] = current
    return found

func _capture_into_ring(distances: Dictionary, crossings: Array[Dictionary]) -> void:
    var frame_state := _frame_state(distances, crossings)
    await RenderingServer.frame_post_draw
    var readback_started := Time.get_ticks_usec()
    var image: Image = _viewport.get_texture().get_image()
    _readback_elapsed_usec += Time.get_ticks_usec() - readback_started
    if image == null or image.get_width() != TARGET_SIZE.x or image.get_height() != TARGET_SIZE.y:
        _failed = "headless/renderer readback did not return exact native viewport dimensions"
        _busy = false
        set_process(false)
        _write_receipt("BLOCKED")
        return
    _ring.append(_frame_record(image, frame_state))
    while _ring.size() > PRE_EVENT_SAMPLES:
        _ring.pop_front()
    _busy = false

func _capture_post_event(distances: Dictionary, crossings: Array[Dictionary]) -> void:
    var frame_state := _frame_state(distances, crossings)
    await RenderingServer.frame_post_draw
    var readback_started := Time.get_ticks_usec()
    var image: Image = _viewport.get_texture().get_image()
    _readback_elapsed_usec += Time.get_ticks_usec() - readback_started
    if image == null or image.get_width() != TARGET_SIZE.x or image.get_height() != TARGET_SIZE.y:
        _failed = "headless/renderer readback did not return exact native viewport dimensions"
        _post_remaining = 0
        _busy = false
        set_process(false)
        _write_receipt("BLOCKED")
        return
    var record := _frame_record(image, frame_state)
    _save_record(record, image)
    if !_failed.is_empty():
        _post_remaining = 0
        _busy = false
        set_process(false)
        _write_receipt("BLOCKED")
        return
    record.erase("image")
    _records.append(record)
    _post_remaining -= 1
    _busy = false
    if _post_remaining == 0:
        _write_receipt("CAPTURED_TECHNICAL_ONLY")
        set_process(false)

func _flush_ring() -> void:
    for record in _ring:
        _save_record(record, record["image"])
        record.erase("image")
        _records.append(record)
    _ring.clear()

func _cache_grass_materials() -> void:
    if _scatter == null:
        _failed = "active Main Scatter node missing; cannot identify grass-kind materials"
        return
    var unique_materials := {}
    for child in _scatter.get_children():
        if !(child is MultiMeshInstance3D):
            continue
        var kind := _scatter_kind_from_name(str(child.name))
        if kind.is_empty():
            continue
        var node := child as MultiMeshInstance3D
        if node.multimesh == null or node.multimesh.mesh == null:
            _failed = "%s MultiMesh or resolved Mesh is missing: %s" % [kind, str(node.get_path())]
            return
        if node.multimesh.mesh.get_surface_count() < 1:
            _failed = "%s resolved mesh has no surfaces: %s" % [kind, str(node.get_path())]
            return
        _grass_nodes.append(node)
        _grass_node_kinds[node.get_instance_id()] = kind
        for surface_index in range(node.multimesh.mesh.get_surface_count()):
            var active_material: Material = node.material_override
            if active_material == null:
                active_material = node.multimesh.mesh.surface_get_material(surface_index)
            if !(active_material is ShaderMaterial):
                _failed = "%s active material is not ShaderMaterial: %s surface %d" % [kind, str(node.get_path()), surface_index]
                return
            var shader_material := active_material as ShaderMaterial
            if shader_material.shader == null:
                _failed = "%s ShaderMaterial has no shader: %s" % [kind, str(node.get_path())]
                return
            var shader_code: String = shader_material.shader.code
            if !shader_code.contains("global uniform float wind_time") or !shader_code.contains("uniform float plant_height") or !shader_code.contains("uniform float flutter"):
                _failed = "%s material shader does not match the pinned foliage wind shader: %s" % [kind, str(node.get_path())]
                return
            var material_id := shader_material.get_instance_id()
            var material_key := "%s_lod%d_surface%d" % [kind, _lod_from_name(str(node.name)), surface_index]
            if !unique_materials.has(material_id):
                var parameters := _read_grass_parameters(shader_material)
                if typeof(parameters["plant_height"]) != TYPE_FLOAT and typeof(parameters["plant_height"]) != TYPE_INT:
                    _failed = "%s plant_height is not numeric: %s" % [kind, str(node.get_path())]
                    return
                if float(parameters["plant_height"]) <= 0.0:
                    _failed = "%s base plant_height is not positive before ablation: %s" % [kind, str(node.get_path())]
                    return
                var row := {"key": material_key, "kind": kind, "lod": _lod_from_name(str(node.name)), "surface": surface_index, "material_id": material_id,
                    "resource_path": str(shader_material.resource_path), "shader_resource_path": str(shader_material.shader.resource_path),
                    "parameters_before": parameters.duplicate(true), "parameters_after": parameters.duplicate(true)}
                unique_materials[material_id] = row
                _grass_material_refs.append({"material": shader_material, "material_id": material_id, "key": material_key, "kind": kind, "before": parameters.duplicate(true)})
            else:
                var existing: Dictionary = unique_materials[material_id]
                if str(existing["kind"]) != kind:
                    _failed = "foliage ShaderMaterial is shared across scatter kinds %s and %s" % [str(existing["kind"]), kind]
                    return
                if existing["key"] != material_key:
                    existing["key"] = str(existing["key"]) + "," + material_key
            if _ablation == "plant_height_zero" and _is_ah_grass_kind(kind):
                shader_material.set_shader_parameter("plant_height", 0.0)
    for material_id in unique_materials:
        var item: Dictionary = unique_materials[material_id]
        var material := _find_grass_material(int(material_id))
        if material == null:
            _failed = "foliage material reference lost during scan"
            return
        item["parameters_after"] = _read_grass_parameters(material)
        if _ablation == "plant_height_zero":
            for parameter in GRASS_PARAMETERS:
                var is_target_height: bool = parameter == "plant_height" and _is_ah_grass_kind(str(item["kind"]))
                if is_target_height and float(item["parameters_after"][parameter]) != 0.0:
                    _failed = "AH blade plant_height_zero override did not persist during initialization"
                    return
                if !is_target_height and item["parameters_after"][parameter] != item["parameters_before"][parameter]:
                    _failed = "plant_height ablation changed another shader parameter: " + parameter
                    return
    for material_id in unique_materials:
        _grass_material_manifest.append(unique_materials[material_id])
    var ah_material_count := 0
    var cw_moor_material_count := 0
    for item in _grass_material_manifest:
        if _is_ah_grass_kind(str(item["kind"])):
            ah_material_count += 1
        elif str(item["kind"]) == "moor_grass":
            cw_moor_material_count += 1
    if _grass_nodes.is_empty() or _grass_material_manifest.is_empty() or ah_material_count == 0 or cw_moor_material_count == 0:
        _failed = "active AH blades_a..d and CW moor_grass nodes/materials are both required in Main/Scatter"
        return
    _wind_time_origin = null
    _wind_time_estimate = 0.0
    _relative_wind_clock_ready = is_instance_valid(_wind_field) and _wind_field.is_processing() and _wind_field.process_priority < process_priority
    if !_relative_wind_clock_ready:
        _failed = "source-ordered relative wind clock prerequisites are not proven"
        return
    _grass_material_cache_complete = true
    _plant_height_override_verified = _ablation == "plant_height_zero" and ah_material_count > 0

func _find_grass_material(material_id: int) -> ShaderMaterial:
    for row in _grass_material_refs:
        if int(row["material_id"]) == material_id:
            return row["material"] as ShaderMaterial
    return null

func _read_grass_parameters(material: ShaderMaterial) -> Dictionary:
    var output := {}
    var shader_defaults: Array[String] = []
    var defaults := {
        "plant_height": 1.0, "flutter": 0.35, "stiffness": 1.0, "has_normal": false, "has_arm": false,
        "alpha_cut": 0.18, "upright_normals": false, "roughness_value": 0.85, "backlight_colour": Vector3(0.88, 0.81, 0.63),
    }
    for parameter in GRASS_PARAMETERS:
        var value: Variant = material.get_shader_parameter(parameter)
        if value == null:
            value = defaults[parameter]
            shader_defaults.append(parameter)
        if value is Vector3:
            output[parameter] = _xyz(value)
        else:
            output[parameter] = value
    output["_shader_defaults_applied"] = shader_defaults
    return output

func _verify_grass_materials() -> bool:
    if !_grass_material_cache_complete:
        _failed = "foliage material cache is incomplete"
        return false
    for row in _grass_material_refs:
        var material := row["material"] as ShaderMaterial
        if !is_instance_valid(material):
            _failed = "resolved foliage material became invalid during route"
            return false
        var current := _read_grass_parameters(material)
        if current["_shader_defaults_applied"] != row["before"]["_shader_defaults_applied"]:
            _failed = "foliage shader parameter override/default state changed during route"
            return false
        for parameter in GRASS_PARAMETERS:
            if parameter == "plant_height":
                continue
            if current[parameter] != row["before"][parameter]:
                _failed = "foliage shader parameter changed during route: " + parameter
                return false
        if _ablation == "plant_height_zero" and _is_ah_grass_kind(str(row["kind"])):
            if float(current["plant_height"]) != 0.0:
                material.set_shader_parameter("plant_height", 0.0)
                _plant_height_reapplications += 1
            if float(material.get_shader_parameter("plant_height")) != 0.0:
                _failed = "plant_height_zero override was not maintained before draw"
                return false
        elif current["plant_height"] != row["before"]["plant_height"]:
            _failed = "baseline or non-target foliage plant_height changed during route"
            return false
    return true

func _grass_material_snapshot() -> Array[Dictionary]:
    var output: Array[Dictionary] = []
    for row in _grass_material_refs:
        var material := row["material"] as ShaderMaterial
        output.append({"key": row["key"], "kind": row["kind"], "parameters": _read_grass_parameters(material)})
    return output

func _grass_frame_snapshot() -> Dictionary:
    var samples: Array[Dictionary] = []
    var samples_by_kind: Dictionary = {}
    var truncated_by_kind: Dictionary = {}
    var counts_by_kind: Dictionary = {}
    var total_matches := 0
    var active_node_count := 0
    var projected_candidate_node_count := 0
    var active_instances_scanned := 0
    for node in _grass_nodes:
        if !is_instance_valid(node) or node.multimesh == null:
            continue
        var kind: String = str(_grass_node_kinds.get(node.get_instance_id(), "unknown"))
        if !counts_by_kind.has(kind):
            counts_by_kind[kind] = {"active_lod_node_count": 0, "projected_candidate_node_count": 0,
                "active_instance_transforms_scanned": 0, "projected_instance_match_count": 0}
            samples_by_kind[kind] = []
            truncated_by_kind[kind] = false
        var kind_counts: Dictionary = counts_by_kind[kind]
        var bounds: AABB = node.get_aabb()
        var center: Vector3 = node.global_transform * bounds.get_center()
        var center_distance := _camera.global_position.distance_to(center)
        var in_band := center_distance >= node.visibility_range_begin and (node.visibility_range_end <= 0.0 or center_distance < node.visibility_range_end)
        if !in_band:
            continue
        active_node_count += 1
        kind_counts["active_lod_node_count"] = int(kind_counts["active_lod_node_count"]) + 1
        if !_aabb_overlaps_grass_crop(node, bounds):
            continue
        projected_candidate_node_count += 1
        kind_counts["projected_candidate_node_count"] = int(kind_counts["projected_candidate_node_count"]) + 1
        var multi := node.multimesh
        for instance_index in range(multi.instance_count):
            active_instances_scanned += 1
            kind_counts["active_instance_transforms_scanned"] = int(kind_counts["active_instance_transforms_scanned"]) + 1
            var world_transform: Transform3D = node.global_transform * multi.get_instance_transform(instance_index)
            var world_position: Vector3 = world_transform.origin
            if _camera.is_position_behind(world_position):
                continue
            var screen_position: Vector2 = _camera.unproject_position(world_position)
            if !GRASS_CROP_RECT.has_point(screen_position):
                continue
            total_matches += 1
            kind_counts["projected_instance_match_count"] = int(kind_counts["projected_instance_match_count"]) + 1
            var kind_samples: Array = samples_by_kind[kind]
            if kind_samples.size() < GRASS_SAMPLE_LIMIT:
                kind_samples.append({"kind": kind, "node_path": str(node.get_path()), "lod": _lod_from_name(str(node.name)), "instance_index": instance_index,
                    "world_position_m": _xyz(world_position), "screen_position_px": _xyz2(screen_position),
                    "node_bounds_center_camera_distance_m": center_distance, "visibility_begin_m": node.visibility_range_begin,
                    "visibility_end_m": node.visibility_range_end, "cast_shadow": node.cast_shadow})
                samples_by_kind[kind] = kind_samples
                samples.append(kind_samples[kind_samples.size() - 1])
            else:
                truncated_by_kind[kind] = true
    var ah_projected_matches := 0
    var ah_projected_nodes := 0
    for kind in counts_by_kind:
        if _is_ah_grass_kind(str(kind)):
            ah_projected_matches += int(counts_by_kind[kind]["projected_instance_match_count"])
            ah_projected_nodes += int(counts_by_kind[kind]["projected_candidate_node_count"])
    return {"screen_crop_xywh": [int(GRASS_CROP_RECT.position.x), int(GRASS_CROP_RECT.position.y), int(GRASS_CROP_RECT.size.x), int(GRASS_CROP_RECT.size.y)],
        "visibility_semantics": "instance origin projects inside crop and its MultiMesh LOD node AABB center is within the configured distance band; no occlusion/depth visibility claim",
        "active_lod_node_count": active_node_count, "projected_candidate_node_count": projected_candidate_node_count,
        "active_instance_transforms_scanned": active_instances_scanned,
        "projected_instance_match_count": total_matches, "sample_limit": GRASS_SAMPLE_LIMIT,
        "samples_truncated_by_kind": truncated_by_kind, "samples": samples,
        "counts_by_kind": counts_by_kind, "ah_blades_projected_candidate_node_count": ah_projected_nodes,
        "ah_blades_projected_instance_match_count": ah_projected_matches}

func _aabb_overlaps_grass_crop(node: MultiMeshInstance3D, bounds: AABB) -> bool:
    var camera_local: Vector3 = node.global_transform.affine_inverse() * _camera.global_position
    if bounds.has_point(camera_local):
        return true
    var screen_min := Vector2(INF, INF)
    var screen_max := Vector2(-INF, -INF)
    var has_front_corner := false
    for mask in range(8):
        var local_corner := Vector3(
            bounds.position.x if (mask & 1) == 0 else bounds.end.x,
            bounds.position.y if (mask & 2) == 0 else bounds.end.y,
            bounds.position.z if (mask & 4) == 0 else bounds.end.z
        )
        var world_corner: Vector3 = node.global_transform * local_corner
        if _camera.is_position_behind(world_corner):
            continue
        var screen_corner: Vector2 = _camera.unproject_position(world_corner)
        screen_min.x = min(screen_min.x, screen_corner.x)
        screen_min.y = min(screen_min.y, screen_corner.y)
        screen_max.x = max(screen_max.x, screen_corner.x)
        screen_max.y = max(screen_max.y, screen_corner.y)
        has_front_corner = true
    if !has_front_corner:
        return false
    return screen_max.x >= GRASS_CROP_RECT.position.x and screen_min.x < GRASS_CROP_RECT.end.x         and screen_max.y >= GRASS_CROP_RECT.position.y and screen_min.y < GRASS_CROP_RECT.end.y

func _frame_state(distances: Dictionary, crossings: Array[Dictionary]) -> Dictionary:
    var eye := _camera.global_position
    var player := _player.global_position
    return {
        "frame": _frame,
        "engine_process_frame": Engine.get_process_frames(),
        "delta_s": _last_delta,
        "observer_clock_usec": _frame_clock_usec,
        "wind_time_estimate_s": _wind_time_estimate,
        "viewport_use_taa": _viewport.use_taa,
        "viewport_msaa_3d": int(_viewport.msaa_3d),
        "viewport_screen_space_aa": int(_viewport.screen_space_aa),
        "camera_rig_path": str(_camera_rig.get_path()),
        "camera_rig_position_m": _xyz(_camera_rig.global_position),
        "camera_path": str(_camera.get_path()),
        "camera_is_current": _camera.current,
        "camera_viewport_is_target": _camera.get_viewport() == _viewport,
        "camera_position_m": _xyz(eye),
        "player_position_m": _xyz(player),
        "lighting": _lighting_snapshot(),
        "camera_to_selected_band_aabb_center_m": distances,
        "crossings": crossings,
        "grass_instances_in_crop": _grass_frame_snapshot(),
        "grass_materials": _grass_material_snapshot(),
    }

func _frame_record(image: Image, frame_state: Dictionary) -> Dictionary:
    frame_state["image"] = image
    frame_state["image_size_px"] = [image.get_width(), image.get_height()]
    frame_state["viewport_requested_px"] = [int(_viewport.size.x), int(_viewport.size.y)]
    frame_state["viewport_texture_size_reported_px"] = _xyz2(_viewport.get_texture().get_size())
    return frame_state

func _frame_record_headless(distances: Dictionary, crossings: Array[Dictionary]) -> Dictionary:
    var frame_state := _frame_state(distances, crossings)
    frame_state["viewport_requested_px"] = [int(_viewport.size.x), int(_viewport.size.y)]
    frame_state["viewport_texture_size_reported_px"] = _xyz2(_viewport.get_texture().get_size())
    return frame_state

func _save_record(record: Dictionary, image: Image) -> void:
    var name := "frame_%04d.png" % int(record["frame"])
    var error := image.save_png(_frame_dir.path_join(name))
    record["png"] = name
    record["png_error"] = error
    if error != OK:
        _failed = "PNG write failed for %s: %s" % [name, error_string(error)]

func _write_receipt(status: String) -> void:
    var lighting := _lighting_snapshot()
    _wind_time_terminal = null
    if !_failed.is_empty() and status == "CAPTURED_TECHNICAL_ONLY":
        status = "BLOCKED"
    var target_summary := []
    for target in _targets:
        var clean := target.duplicate()
        clean.erase("node")
        target_summary.append(clean)
    var ah_lod_crossing_count := 0
    for crossing in _crossings:
        if _is_ah_grass_kind(str(crossing["kind"])):
            ah_lod_crossing_count += 1
    var ah_projected_frame_count := 0
    var ah_projected_instance_total := 0
    for record in _records:
        var crop: Dictionary = record.get("grass_instances_in_crop", {})
        var matches := int(crop.get("ah_blades_projected_instance_match_count", 0))
        ah_projected_instance_total += matches
        if matches > 0:
            ah_projected_frame_count += 1
    var ah_route_crossing_qualified := !_capture_fallback and ah_lod_crossing_count > 0
    var ah_projection_qualified := ah_projected_instance_total > 0 and ah_projected_frame_count >= AH_PROJECTED_FRAME_MIN
    var receipt := {
        "status": status,
        "failure": _failed,
        "visual_acceptance": "NOT OBSERVED; this is a technical capture wrapper only",
        "route_label": _route_label,
        "route_path": _route_path,
        "capture_frame_count": _records.size() if _mode == "render" else 0,
        "headless_observation_sample_count": _records.size() if _mode == "headless" else 0,
        "event_frame": _event_frame,
        "lod_boundary_crossings": _crossings,
        "fallback_excerpt_window": _capture_fallback,
        "viewport_requested_px": [TARGET_SIZE.x, TARGET_SIZE.y],
        "native_3d_scale": _viewport.scaling_3d_scale if is_instance_valid(_viewport) else null,
        "client_window_verification": "separate and not established by this offscreen SubViewport",
        "simulation_clock": "ConnectedCheck advances one authoritative tick per rendered frame; not a real-time FPS result",
        "readback_timing": "no frame readback in headless mode" if _mode == "headless" else "synchronous PNG readback stalls the capture; no FPS claim",
        "ablation": _ablation,
        "post_remaining": _post_remaining,
        "pre_event_frame_count": _count_window_frames(true),
        "post_event_frame_count": _count_window_frames(false),
        "png_error_count": _png_error_count(),
        "temporal_clock_policy": "consecutive captured frames gated by Engine.get_process_frames() at fixed simulation step; synchronous image readback/write may slow wall time and is not an FPS measurement",
        "temporal_capture_readback_elapsed_usec": _readback_elapsed_usec,
        "viewport_use_taa": _viewport.use_taa if is_instance_valid(_viewport) else null,
        "viewport_msaa_3d": int(_viewport.msaa_3d) if is_instance_valid(_viewport) else null,
        "viewport_screen_space_aa": int(_viewport.screen_space_aa) if is_instance_valid(_viewport) else null,
        "wind_time_origin_s": _wind_time_origin,
        "wind_time_terminal_observed_s": _wind_time_terminal,
        "wind_time_terminal_minus_estimate_s": null,
        "wind_time_origin_status": "ABSOLUTE_ORIGIN_UNKNOWN",
        "wind_time_clock_kind": "RELATIVE_DELTA_ESTIMATE_AFTER_WINDFIELD",
        "wind_field_path": str(_wind_field.get_path()) if is_instance_valid(_wind_field) else null,
        "wind_field_process_priority": _wind_field.process_priority if is_instance_valid(_wind_field) else null,
        "observer_process_priority": process_priority,
        "wind_time_estimator": "cumulative observer process delta after source-pinned WindField; relative elapsed only, absolute shader phase unknown",

        "plant_height_override_verified": _plant_height_override_verified,
        "plant_height_override_reapplications": _plant_height_reapplications,
        "grass_material_manifest": _grass_material_manifest,
        "grass_material_cache_complete": _grass_material_cache_complete,
        "scan_once_child_count": _scanned_children,
        "target_scan_elapsed_usec": _cache_usec,
        "selected_band_nodes": target_summary,
        "grass_kind_attribution": "MultiMesh name prefix; blades_a..d are Ashen Hollow rule kinds; moor_grass is tracked separately as Cairnwold",
        "grass_lod_chain_policy": "nearest route chunk selected independently per foliage kind; every node in that chunk's full LOD chain is checked for begin/end crossings",
        "ablation_target_kinds": ["blades_a", "blades_b", "blades_c", "blades_d"],
        "ah_grass_lod_crossing_count": ah_lod_crossing_count,
        "ah_grass_route_crossing_qualified": ah_route_crossing_qualified,
        "ah_grass_projected_frame_count": ah_projected_frame_count,
        "ah_grass_projected_instance_total": ah_projected_instance_total,
        "ah_grass_projection_min_frames": AH_PROJECTED_FRAME_MIN,
        "ah_grass_projection_qualified": ah_projection_qualified,
        "ah_grass_pair_candidate_ready": status == "CAPTURED_TECHNICAL_ONLY" and ah_route_crossing_qualified and ah_projection_qualified and _count_window_frames(true) == PRE_EVENT_SAMPLES and _count_window_frames(false) == POST_EVENT_FRAMES and _post_remaining == 0 and _png_error_count() == 0 and _failed.is_empty(),
        "lighting": lighting,
        "frames": _records,
    }
    var destination := _result_path if _mode == "headless" else _frame_dir.path_join("capture.json")
    var error := FileAccess.open(destination, FileAccess.WRITE)
    if error != null:
        error.store_string(JSON.stringify(receipt, "  "))
        error.close()

func _lighting_snapshot() -> Dictionary:
    if _sky3d == null or _environment == null or _sun == null:
        return {}
    return {
        "world_environment_path": str(_sky3d.get_path()),
        "environment_instance_id": _environment.get_instance_id(),
        "ssao_enabled": _environment.ssao_enabled,
        "ablation": _ablation,
        "sun_path": str(_sun.get_path()),
        "sun_shadow_enabled": _sun.shadow_enabled,
        "shadow_override_reapplications": _shadow_override_reapplications,
        "angular_distance_deg": _sun.light_angular_distance,
        "shadow_bias": _sun.shadow_bias,
        "shadow_normal_bias": _sun.shadow_normal_bias,
        "directional_shadow_max_distance_m": _sun.directional_shadow_max_distance,
        "blend_splits": _sun.directional_shadow_blend_splits,
        "shadow_mode": _sun.directional_shadow_mode,
    }

func _count_window_frames(pre_event: bool) -> int:
    if _event_frame < 0:
        return 0
    var count := 0
    for record in _records:
        if (int(record["frame"]) < _event_frame) == pre_event:
            count += 1
    return count

func _png_error_count() -> int:
    var count := 0
    for record in _records:
        if int(record.get("png_error", OK)) != OK:
            count += 1
    return count

func _xyz(value: Vector3) -> Array[float]:
    return [value.x, value.y, value.z]

func _xyz2(value: Vector2) -> Array[float]:
    return [value.x, value.y]

func _exit_tree() -> void:
    if _mode == "headless" and !_result_path.is_empty() and !FileAccess.file_exists(_result_path):
        _write_receipt("HEADLESS_TECHNICAL_ONLY")
        return
    if _frame_dir.is_empty() or !DirAccess.dir_exists_absolute(_frame_dir):
        return
    if !_failed.is_empty():
        _write_receipt("BLOCKED")
    elif _event_frame >= 0 and _post_remaining > 0:
        _failed = "main route ended before event-window frame sequence was complete"
        _write_receipt("INCOMPLETE")
