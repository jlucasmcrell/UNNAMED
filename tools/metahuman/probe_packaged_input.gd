extends SceneTree
## Documented Godot input replay inside an unchanged exported game; no OS input injection.
func _initialize():
 call_deferred("run")
func run():
 var event=InputEventKey.new()
 event.keycode=KEY_W;event.physical_keycode=KEY_W;event.pressed=true
 Input.parse_input_event(event)
 Input.flush_buffered_events()
 await process_frame
 var result={"physical_w":Input.is_physical_key_pressed(KEY_W),"logical_w":Input.is_key_pressed(KEY_W),"main_scene":str(ProjectSettings.get_setting("application/run/main_scene")),"engine":Engine.get_version_info()}
 var released=event.duplicate();released.pressed=false;Input.parse_input_event(released)
 Input.flush_buffered_events()
 await process_frame
 result.released=not Input.is_physical_key_pressed(KEY_W)
 if OS.get_environment("OTHERREACH_INPUT_PROBE_SCENE")=="1":
  var packed=load(ProjectSettings.get_setting("application/run/main_scene")) as PackedScene
  var scene=packed.instantiate();var script=scene.get_script() as Script
  result.scene={"node_class":scene.get_class(),"script_path":script.resource_path if script else "","script_can_instantiate":script.can_instantiate() if script else false}
  scene.free()
  var rig_script=load("res://Player/CameraRig.cs") as Script
  if rig_script and rig_script.can_instantiate():
   var rig=rig_script.new()
   result.camera_methods=[];result.camera_properties=[]
   for method in rig.get_method_list():
    if str(method.name) in ["Look","Use","get_Yaw","set_Yaw"]:result.camera_methods.append(str(method.name))
   for prop in rig.get_property_list():
    if str(prop.name) in ["Yaw","Pitch","TargetDistance"]:result.camera_properties.append(str(prop.name))
   rig.free()
  var extension=ConfigFile.new()
  if extension.load("res://addons/terrain_3d/terrain.gdextension")==OK:
   result.terrain_libraries={}
   for key in extension.get_section_keys("libraries"):result.terrain_libraries[key]=extension.get_value("libraries",key)
 var path=OS.get_environment("OTHERREACH_INPUT_PROBE_OUT")
 assert(path.is_absolute_path())
 FileAccess.open(path,FileAccess.WRITE).store_string(JSON.stringify(result,"  "))
 quit(0 if result.physical_w and result.logical_w and result.released else 1)
