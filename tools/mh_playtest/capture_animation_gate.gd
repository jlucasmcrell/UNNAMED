extends SceneTree
const Native=preload("res://MHCharacters/NativeCharacter.gd")
var folder := "D:/Otherreach_Scratch/mh_playtest_20261019/"
var cameras: Array[Camera3D]=[]
var scenes: Array[Node3D]=[]
var labels: Array[Label]=[]
func _initialize() -> void: call_deferred("capture")
func all(node: Node, type: String) -> Array:
 var found := []
 if node.is_class(type):found.append(node)
 for child in node.get_children():found.append_array(all(child,type))
 return found
func viewport_panel(parent: Control) -> Node3D:
 var container := SubViewportContainer.new();container.size_flags_horizontal=Control.SIZE_EXPAND_FILL;container.size_flags_vertical=Control.SIZE_EXPAND_FILL;container.stretch=true;parent.add_child(container)
 var viewport := SubViewport.new();viewport.size=Vector2i(960,1080);viewport.own_world_3d=true;viewport.render_target_update_mode=SubViewport.UPDATE_ALWAYS;container.add_child(viewport)
 var scene := Node3D.new();viewport.add_child(scene);scenes.append(scene)
 var environment := WorldEnvironment.new();environment.environment=Environment.new()
 environment.environment.background_mode=Environment.BG_COLOR;environment.environment.background_color=Color(.07,.08,.09)
 environment.environment.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR;environment.environment.ambient_light_color=Color(.72,.79,.86);environment.environment.ambient_light_energy=.35;environment.environment.tonemap_mode=Environment.TONE_MAPPER_AGX;scene.add_child(environment)
 for spec in [[Vector3(-35,-35,0),2.0],[Vector3(-10,120,0),.8]]:
  var light:=DirectionalLight3D.new();light.rotation_degrees=spec[0];light.light_energy=spec[1];scene.add_child(light)
 var camera:=Camera3D.new();camera.near=.01;camera.fov=35;scene.add_child(camera);camera.current=true;cameras.append(camera)
 var layer:=CanvasLayer.new();viewport.add_child(layer)
 var label:=Label.new();label.position=Vector2(16,16);label.add_theme_font_size_override("font_size",20);layer.add_child(label);labels.append(label)
 return scene
func capture() -> void:
 if DisplayServer.get_name()=="headless":push_error("Animation visual gate requires a real renderer");quit(2);return
 root.size=Vector2i(1920,1080)
 var controls:=HBoxContainer.new();controls.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT);root.add_child(controls)
 var native_scene:=viewport_panel(controls);var source_scene:=viewport_panel(controls)
 var id:=OS.get_environment("OTHERREACH_MH_GATE_CHARACTER")
 if id.is_empty():id="ada"
 var catalog:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(folder+"runtime/catalog.json"))
 var native:=Native.new();native_scene.add_child(native)
 if not native.configure(folder+"runtime/",catalog.characters[id],{}):quit(3);return
 var unclothed:=OS.get_environment("OTHERREACH_MH_GATE_UNCLOTHED")=="1"
 var rest_only:=OS.get_environment("OTHERREACH_MH_GATE_REST")=="1"
 if unclothed:
  for mesh:MeshInstance3D in all(native._body,"MeshInstance3D"):
   if not mesh in native._body_meshes:mesh.visible=false
   else:
    for surface in mesh.mesh.get_surface_count():mesh.get_surface_override_material(surface).set_shader_parameter("use_hidden_face_map",false)
 var manifest:=OS.get_environment("OTHERREACH_MH_ANIM_MANIFEST")
 if manifest.is_empty():manifest="ue_native_animations_rootfix.json"
 var exports:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(folder+"evidence/"+manifest))
 var record:Dictionary
 for row in exports.characters:
  if row.id==id:record=row
 var sources:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(folder+"evidence/ue_animation_sources.json"))
 var only:=OS.get_environment("OTHERREACH_MH_GATE_CLIPS").split(",",false)
 var out:=folder+"evidence/animation_gate/"+id+OS.get_environment("OTHERREACH_MH_GATE_SUFFIX")+("_unclothed" if unclothed else "")+("_rest" if rest_only else "")+"/";DirAccess.make_dir_recursive_absolute(out)
 var rows:=[];var current_source:Node;var current_path:=""
 for clip in record.clips:
  if not only.is_empty() and not clip.logical in only:continue
  var original:Dictionary
  for source in sources.sources:
   for imported in source.assets:
    if imported.asset==clip.source:original=source
  if original.is_empty():push_error("Original source provenance missing");quit(4);return
  if current_path!=original.path:
   if current_source!=null:current_source.queue_free();await process_frame
   var document:=GLTFDocument.new();var state:=GLTFState.new()
   if document.append_from_file(original.path,state,GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION)!=OK:quit(5);return
   current_source=document.generate_scene(state);source_scene.add_child(current_source);current_path=original.path
  var player:AnimationPlayer=all(current_source,"AnimationPlayer")[0]
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  var source_name:String=String(clip.source).get_file().get_slice(".",0).trim_prefix(String(original.path).get_file().get_basename())
  var names:Array=[]
  for name_ in player.get_animation_list():
   if String(name_)==source_name or String(name_).ends_with("/"+source_name):names.append(name_)
  if names.size()!=1:push_error("Original source clip lookup failed "+source_name+" "+str(player.get_animation_list()));quit(6);return
  var fractions:=[.5]
  if clip.logical in ["walk","run","sprint","sword_attack_1","sword_attack_2","sword_attack_3"]:fractions=[.2,.5,.8]
  for fraction in fractions:
   var seconds:float=clip.seconds*fraction
   if not rest_only and not native.sample_clip(clip.logical,seconds):quit(7);return
   player.play(names[0],0);player.seek(seconds,true);player.advance(0)
   var source_rig:Skeleton3D=all(current_source,"Skeleton3D")[0]
   var bounds:=AABB();var started:=false
   for rig:Skeleton3D in [native._body,source_rig]:
    for bone in rig.get_bone_count():
     var point:Vector3=rig.to_global(rig.get_bone_global_pose(bone).origin)
     if not started:bounds=AABB(point,Vector3.ZERO);started=true
     else:bounds=bounds.expand(point)
   for view in ["full","shoulder","hips"]:
    for index in cameras.size():
     var target:=bounds.get_center();var distance:=maxf(3.7,maxf(bounds.size.y,bounds.size.x/.8889)*1.12/(2.0*tan(deg_to_rad(17.5))))+bounds.size.z*.5
     if view!="full":
      var rig:Skeleton3D=native._body if index==0 else source_rig
      var bone:=("upperarm_l" if rig.find_bone("upperarm_l")>=0 else "upperarm.l") if view=="shoulder" else ("pelvis" if rig.find_bone("pelvis")>=0 else "hips")
      if rig.find_bone(bone)<0:push_error("Source camera bone missing: "+bone);quit(9);return
      target=rig.to_global(rig.get_bone_global_pose(rig.find_bone(bone)).origin)+Vector3(0,-.08 if view=="shoulder" else .02,0)
      distance=1.6 if view=="shoulder" else 1.4
     cameras[index].position=target+Vector3(0,0,distance);cameras[index].look_at(target)
    labels[0].text="%s · native MH%s\n%s  %.3fs · %s"%[id," REST DIAGNOSTIC" if rest_only else " Epic-retargeted",clip.logical,seconds,view]
    labels[1].text="Original %s\n%s  %.3fs · %s"%[original.id,source_name,seconds,view]
    await process_frame;await process_frame;await RenderingServer.frame_post_draw
    var name_:String="%s_%02d_%s"%[clip.logical,roundi(fraction*100),view]
    if root.get_texture().get_image().save_png(out+name_+".png")!=OK:quit(8);return
    rows.append({"frame":name_,"seconds":seconds,"source":original.path,"source_clip":source_name,"native_clip":clip.logical,"metrics":native.native_metrics()})
    print("MH_ANIMATION_FRAME ",id," ",name_)
 var file:=FileAccess.open(out+"frames.json",FileAccess.WRITE);file.store_string(JSON.stringify({"status":"CAPTURED_REQUIRES_VISUAL_REVIEW","frames":rows},"  "));file.close()
 print("MH_ANIMATION_GATE_CAPTURED frames=",rows.size());quit()
