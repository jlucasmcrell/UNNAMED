extends SceneTree
## Rebind already-retargeted native tracks only. No retargeting or rest conversion in Godot.
func _initialize() -> void: call_deferred("build")
func all(node: Node, type: String) -> Array:
 var found := []
 if node.is_class(type): found.append(node)
 for child in node.get_children(): found.append_array(all(child,type))
 return found
func load_glb(path: String) -> Node:
 var document := GLTFDocument.new();var state := GLTFState.new()
 if document.append_from_file(path,state,GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION)!=OK:return null
 return document.generate_scene(state)
func measure_pace(player: AnimationPlayer, rig: Skeleton3D, clip: StringName) -> Dictionary:
 var length_:=player.get_animation(clip).length
 var count:=maxi(12,ceili(length_*60.0));var dt:=length_/count
 var samples:=[];var floor_:=INF
 for frame in count:
  player.play(clip,0);player.seek(frame*dt,true);player.advance(0)
  var feet:=[]
  for name_ in ["foot_l","foot_r"]:
   var point:Vector3=rig.global_transform*rig.get_bone_global_pose(rig.find_bone(name_)).origin
   feet.append(point);floor_=minf(floor_,point.y)
  samples.append(feet)
 var speeds:=[]
 for frame in range(1,count-1):
  for foot in 2:
   if samples[frame][foot].y<=floor_+.02:
    var delta_:Vector3=(samples[frame+1][foot]-samples[frame-1][foot])/(2.0*dt)
    var speed:=Vector2(delta_.x,delta_.z).length()
    if speed>.05:speeds.append(speed)
 speeds.sort()
 return {"metres_per_second":speeds[speeds.size()/2] if not speeds.is_empty() else 0.0,"contact_samples":speeds.size(),"floor_m":floor_}
func build() -> void:
 var folder := "D:/Otherreach_Scratch/mh_playtest_20261019/"
 var runtime := folder+"runtime/"
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(runtime+"catalog.json"))
 var selected := OS.get_environment("OTHERREACH_MH_ANIM_CHARACTERS").split(",",false)
 var records := []
 for id in selected:
  var config: Dictionary=catalog.characters[id]
  var target := load_glb(runtime+config.body);root.add_child(target)
  var target_rig: Skeleton3D=all(target,"Skeleton3D")[0]
  var manifest: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(folder+"evidence/clean_animations_"+id+".json"))
  var library := AnimationLibrary.new()
  var reports := [];var paces:={}
  for clip in manifest.clips:
   var source := load_glb(clip.path);root.add_child(source)
   var rigs := all(source,"Skeleton3D")
   if rigs.size()!=1:push_error("Expected one native animation skeleton");quit(2);return
   var rig: Skeleton3D=rigs[0]
   if rig.get_bone_count()!=target_rig.get_bone_count():push_error("Native animation bone count mismatch");quit(3);return
   var worst := 0.0
   for bone in target_rig.get_bone_count():
    var name_ := target_rig.get_bone_name(bone);var index := rig.find_bone(name_)
    if index<0:push_error("Missing native animation bone "+name_);quit(4);return
    var parent := target_rig.get_bone_parent(bone);var source_parent := rig.get_bone_parent(index)
    if (parent<0)!=(source_parent<0) or (parent>=0 and target_rig.get_bone_name(parent)!=rig.get_bone_name(source_parent)):push_error("Native animation parent mismatch "+name_);quit(5);return
    var expected := target_rig.get_bone_rest(bone);var actual := rig.get_bone_rest(index)
    worst=maxf(worst,expected.origin.distance_to(actual.origin))
    if not expected.is_equal_approx(actual):push_error("Native animation rest mismatch "+id+" "+clip.logical+" "+name_+" expected="+str(expected)+" actual="+str(actual));quit(6);return
   var players := all(source,"AnimationPlayer")
   if players.size()!=1:push_error("Missing native animation player");quit(7);return
   var player: AnimationPlayer=players[0];var names := player.get_animation_list();names.remove_at(names.find("RESET")) if names.has("RESET") else 0
   if names.size()!=1:push_error("Expected exactly one native animation "+str(names));quit(8);return
   if clip.logical in ["walk","run","sprint","crouch_walk"]:
    player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
    var pace:=measure_pace(player,rig,names[0])
    if pace.metres_per_second<=.05:push_error("No measurable native gait pace "+id+" "+clip.logical);quit(11);return
    paces[clip.logical]=pace.metres_per_second
    reports.append({"pace_clip":clip.logical,"pace_measurement":pace})
   var animation: Animation=player.get_animation(names[0]).duplicate(true)
   for track in animation.get_track_count():
    var path := animation.track_get_path(track)
    if path.get_subname_count()!=1 or target_rig.find_bone(path.get_subname(0))<0:push_error("Unexpected native animation track "+str(path));quit(9);return
    animation.track_set_path(track,NodePath(".:"+path.get_subname(0)))
   animation.loop_mode=Animation.LOOP_LINEAR if clip.logical in ["idle","walk","run","sprint","crouch_idle","crouch_walk","fall","sword_ready","sword_block","talk","spear_ready","bow_ready"] else Animation.LOOP_NONE
   library.add_animation(clip.logical,animation)
   reports.append({"clip":clip.logical,"tracks":animation.get_track_count(),"seconds":animation.length,"max_rest_position_difference_m":worst})
   source.queue_free();await process_frame
  for pair in [["crouch_move","crouch_walk"],["block","sword_block"],["hit_react","hit"],["talk_idle","talk"],["attack","sword_attack_1"]]:
   if library.has_animation(pair[1]):library.add_animation(pair[0],library.get_animation(pair[1]))
  library.set_meta("native_body_sha256",FileAccess.get_sha256(runtime+config.body))
  library.set_meta("route","Epic IK Retargeter -> native GLTFExporter -> documented Blender bridge; no Godot retarget")
  DirAccess.make_dir_recursive_absolute(runtime+"animations")
  var path := "animations/"+id+".res"
  if ResourceSaver.save(library,runtime+path)!=OK:push_error("Native AnimationLibrary save failed");quit(10);return
  config.animation_library=path
  var existing_paces:Dictionary=config.get("animation_paces",{})
  existing_paces.merge(paces,true);config.animation_paces=existing_paces
  records.append({"id":id,"clips":reports,"native_bone_count":target_rig.get_bone_count(),"native_rest_unchanged":true})
  target.queue_free();await process_frame
 var file := FileAccess.open(runtime+"catalog.json",FileAccess.WRITE);file.store_string(JSON.stringify(catalog,"  "));file.close()
 var evidence_path:=folder+"evidence/native_animation_libraries.json"
 var previous:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(evidence_path)) if FileAccess.file_exists(evidence_path) else {"characters":[]}
 for old in previous.characters:
  if not old.id in selected:records.append(old)
 file=FileAccess.open(evidence_path,FileAccess.WRITE);file.store_string(JSON.stringify({"status":"DONE","characters":records},"  "));file.close()
 print("MH_NATIVE_ANIMATION_LIBRARIES_DONE characters=",records.size())
 quit()
