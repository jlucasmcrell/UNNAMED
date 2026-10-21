extends SceneTree
## Input replay over an unchanged PCK and unchanged published C# assemblies.
## No authoritative-state writes, teleports, engine-time override or source recompile.
var output: String
var held: Array[int]=[]
var movement_actions=["move_forward","move_back","move_left","move_right"]
func vector(value: Vector3):
 return [value.x,value.y,value.z]
func gameplay_player(main: Node) -> Node3D:
 var candidates: Array[Node3D]=[]
 for child in main.get_children():
  if not child is Node3D or child.is_queued_for_deletion():continue
  var script=child.get_script() as Script
  if script and script.resource_path.get_file() in ["MetaHumanFigure.cs","SkinnedFigure.cs","Avatar.cs"]:candidates.append(child)
 return candidates[0] if candidates.size()==1 else null
func save_json(name: String,value):
 FileAccess.open(output.path_join(name),FileAccess.WRITE).store_string(JSON.stringify(value,"  "))
func key(code: int,pressed: bool):
 var event=InputEventKey.new()
 event.keycode=code;event.physical_keycode=code;event.pressed=pressed
 Input.parse_input_event(event);Input.flush_buffered_events()
 if pressed and not held.has(code):held.append(code)
 if not pressed:held.erase(code)
func _initialize():
 output=OS.get_environment("OTHERREACH_PACKAGED_REPLAY_OUT")
 assert(output.is_absolute_path());DirAccess.make_dir_recursive_absolute(output)
 call_deferred("run")
func fail(reason: String):
 for code in held.duplicate():key(code,false)
 for action in movement_actions:Input.action_release(action)
 save_json("failed.json",{"reason":reason,"ticks_usec":Time.get_ticks_usec()})
 quit(1)
func run():
 DisplayServer.window_set_title("Otherreach - "+output.get_file())
 var packed=load(ProjectSettings.get_setting("application/run/main_scene")) as PackedScene
 var main=packed.instantiate()
 if not main.get_script():fail("Packaged C# Main did not instantiate");return
 root.add_child(main);current_scene=main
 var until=Time.get_ticks_msec()+330000
 var player: Node3D
 var gameplay_ready=false
 while Time.get_ticks_msec()<until:
  player=gameplay_player(main)
  var log_path=output.path_join("game.log")
  var in_control=FileAccess.file_exists(log_path) and FileAccess.get_file_as_string(log_path).contains("UNNAMED opening: in control")
  if in_control and not FileAccess.file_exists(output.path_join("scene_nodes.json")):
   var nodes=[]
   for child in main.get_children():
    var child_script=child.get_script() as Script
    nodes.append({"name":str(child.name),"script":child_script.resource_path if child_script else "","queued":child.is_queued_for_deletion()})
   save_json("scene_nodes.json",nodes)
  if is_instance_valid(player) and in_control and not root.disable_3d:
   gameplay_ready=true;break
  await process_frame
 if not gameplay_ready:fail("Normal autostart did not reach gameplay");return
 var rig=main.get_node_or_null("CameraRig") as Node3D
 if not rig or not rig.has_method("Look"):fail("Original CameraRig.Look unavailable");return
 await create_timer(2).timeout
 player=gameplay_player(main)
 rig=main.get_node_or_null("CameraRig") as Node3D
 if not is_instance_valid(player) or not is_instance_valid(rig):fail("Gameplay player/camera unavailable after creator transition");return
 save_json("record_ready.json",{"kind":"Packaged PCK/published DLLs; matching console engine; external Godot input replay", "engine":Engine.get_version_info(),"max_fps":Engine.max_fps,"start":vector(player.global_position),"camera":vector(root.get_camera_3d().global_position),"plant_lod":OS.get_environment("UNNAMED_SCATTER_PLANT_LOD"),"seed":OS.get_environment("OTHERREACH_LIGHTING_SEED"),"driver_sha256":FileAccess.get_sha256(get_script().resource_path),"reference_sha256":FileAccess.get_sha256(OS.get_environment("OTHERREACH_PACKAGED_REPLAY_ROUTE"))})
 until=Time.get_ticks_msec()+180000
 while not FileAccess.file_exists(output.path_join("recording.started")):
  if Time.get_ticks_msec()>until:fail("Recorder did not start");return
  await process_frame
 var sync_layer=CanvasLayer.new();sync_layer.layer=100;root.add_child(sync_layer)
 var flash=ColorRect.new();flash.color=Color.MAGENTA;flash.position=Vector2(4,4);flash.size=Vector2(48,48);flash.mouse_filter=Control.MOUSE_FILTER_IGNORE;sync_layer.add_child(flash)
 save_json("sync.json",{"ticks_usec":Time.get_ticks_usec(),"cue":"magenta corner square; use actual video PTS"})
 await create_timer(.15).timeout
 sync_layer.queue_free()
 var points: Array[Vector2]=[];var yaws: Array[float]=[]
 var route_path=OS.get_environment("OTHERREACH_PACKAGED_REPLAY_ROUTE")
 if route_path.get_extension()=="json":
  var route=JSON.parse_string(FileAccess.get_file_as_string(route_path))
  for point in route.points:
   points.append(Vector2(point.x,point.z));yaws.append(float(point.yaw))
 else:
  var trace=FileAccess.open(route_path,FileAccess.READ)
  if not trace:fail("Missing route CSV");return
  trace.get_line()
  while not trace.eof_reached():
   var row=trace.get_csv_line()
   if row.size()<4:continue
   var at=Vector2(float(row[2]),float(row[3]))
   if points.is_empty() or points[-1].distance_to(at)>=1.2:
    points.append(at);yaws.append(float(row[7]))
 if points.size()<10:fail("Route has insufficient samples");return
 var point_limit=int(OS.get_environment("OTHERREACH_PACKAGED_REPLAY_POINT_LIMIT"))
 if point_limit>0:points.resize(mini(points.size(),point_limit))
 var csv=FileAccess.open(output.path_join("route.csv"),FileAccess.WRITE)
 csv.store_line("ticks_usec,point,player_x,player_y,player_z,camera_x,camera_y,camera_z,yaw,w_pressed")
 var began=Time.get_ticks_msec();var index=0;var previous=player.global_position;var progress_at=began
 var sensitivity=float(OS.get_environment("OTHERREACH_REPLAY_SENSITIVITY"))
 if sensitivity<=0:sensitivity=.0025
 var physical_probe=OS.get_environment("OTHERREACH_PACKAGED_REPLAY_PHYSICAL")=="1"
 if physical_probe:key(KEY_W,true)
 while index<points.size() and Time.get_ticks_msec()-began<240000:
  var position=player.global_position;var flat=Vector2(position.x,position.z)
  while index<points.size() and flat.distance_to(points[index])<.3:index+=1
  if index>=points.size():break
  var direction=points[index]-flat
  var wanted=atan2(-direction.x,-direction.y) if physical_probe else yaws[index]
  var delta_yaw=wrapf(wanted-rig.rotation.y,-PI,PI)
  # Same public camera handler as normal mouse input; movement is a real physical W event.
  rig.call("Look",Vector2(-delta_yaw/sensitivity,0))
  if not physical_probe:
   var forward=Vector2(-sin(wanted),-cos(wanted));var right=Vector2(cos(wanted),-sin(wanted))
   var normal=direction.normalized();var stick=Vector2(normal.dot(right),normal.dot(forward))
   Input.action_press("move_forward",maxf(0,stick.y));Input.action_press("move_back",maxf(0,-stick.y))
   Input.action_press("move_right",maxf(0,stick.x));Input.action_press("move_left",maxf(0,-stick.x))
  await process_frame
  var camera=root.get_camera_3d().global_position
  csv.store_line("%d,%d,%.5f,%.5f,%.5f,%.5f,%.5f,%.5f,%.6f,%s"%[Time.get_ticks_usec(),index,position.x,position.y,position.z,camera.x,camera.y,camera.z,rig.rotation.y,str(Input.is_physical_key_pressed(KEY_W))])
  if previous.distance_to(position)>.5:previous=position;progress_at=Time.get_ticks_msec()
  if Time.get_ticks_msec()-progress_at>6000:fail("Normal input route blocked at point "+str(index));return
 key(KEY_W,false);csv.flush()
 for action in movement_actions:Input.action_release(action)
 if index<points.size():fail("Route time limit");return
 save_json("complete.json",{"status":"VIDEO_REQUIRES_REVIEW","points":points.size(),"seconds":(Time.get_ticks_msec()-began)/1000.0,"end":vector(player.global_position)})
 FileAccess.open(output.path_join("recording.finished"),FileAccess.WRITE).store_string(Time.get_datetime_string_from_system())
 await create_timer(2).timeout
 quit(0)
