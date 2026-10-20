extends SceneTree
## Read Epic-retargeted local finger rotations; never change the native body or rest.
func _initialize() -> void: call_deferred("extract")
func all(node: Node, type: String) -> Array:
 var found := []
 if node.is_class(type): found.append(node)
 for child in node.get_children(): found.append_array(all(child,type))
 return found
func load_glb(path: String) -> Node:
 var document := GLTFDocument.new();var state := GLTFState.new()
 if document.append_from_file(path,state,GLTFDocument.IMPORT_FLAG_FORCE_DISABLE_MESH_COMPRESSION)!=OK:return null
 return document.generate_scene(state)
func extract() -> void:
 var job := OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/").trim_suffix("/")
 assert(job.is_absolute_path() and not job.to_lower().begins_with("d:/otherreach_scratch/mh_playtest_20261019"))
 var runtime := job+"/runtime/"
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(runtime+"catalog.json"))
 DirAccess.make_dir_recursive_absolute(runtime+"hand_grips")
 var records := []
 for id in OS.get_environment("OTHERREACH_MH_ANIM_CHARACTERS").split(",",false):
  var config: Dictionary=catalog.characters[id]
  var target := load_glb(runtime+config.body);root.add_child(target)
  var target_rig: Skeleton3D=all(target,"Skeleton3D")[0]
  var manifest: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(job+"/evidence/clean_extremities_"+id+".json"))
  assert(manifest.clips.size()==1 and manifest.clips[0].logical=="weapon_grip")
  var source := load_glb(manifest.clips[0].path);root.add_child(source)
  var rig: Skeleton3D=all(source,"Skeleton3D")[0]
  assert(target_rig.get_bone_count()==342 and rig.get_bone_count()==342)
  for bone in 342:
   var name_ := target_rig.get_bone_name(bone);var index := rig.find_bone(name_)
   assert(index>=0,"Missing native grip bone "+name_)
   var parent := target_rig.get_bone_parent(bone);var source_parent := rig.get_bone_parent(index)
   assert((parent<0)==(source_parent<0))
   assert(parent<0 or target_rig.get_bone_name(parent)==rig.get_bone_name(source_parent))
   assert(target_rig.get_bone_rest(bone).is_equal_approx(rig.get_bone_rest(index)),"Native grip rest mismatch "+name_)
  var player: AnimationPlayer=all(source,"AnimationPlayer")[0]
  var names := player.get_animation_list()
  if names.has("RESET"):names.remove_at(names.find("RESET"))
  assert(names.size()==1)
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  player.play(names[0],0);player.seek(.5,true);player.advance(0)
  var rotations := {};var largest := 0.0;var left_worst := 0.0
  for finger in ["thumb","index","middle","ring","pinky"]:
   for joint in ["01","02","03"]:
    for side in ["l","r"]:
     var name_: String=finger+"_"+joint+"_"+side;var index := rig.find_bone(name_)
     var q := rig.get_bone_pose_rotation(index).normalized()
     var angle := rad_to_deg(q.angle_to(rig.get_bone_rest(index).basis.get_rotation_quaternion()))
     if side=="l":left_worst=maxf(left_worst,angle)
     else:
      rotations[name_]=[q.x,q.y,q.z,q.w];largest=maxf(largest,angle)
  assert(rotations.size()==15 and largest>10.0,"Grip did not close right fingers")
  assert(left_worst<.1,"Empty hand must stay native relaxed rest")
  var record := {"id":id,"native_body_sha256":FileAccess.get_sha256(runtime+config.body),"rotations":rotations,"native_bones":342,"native_rest_unchanged":true,"left_hand_max_rest_degrees":left_worst,"right_hand_max_grip_degrees":largest,"source":manifest.clips[0].path,"sample_seconds":.5,"route":"Epic IK Retargeter right digits only -> native GLTFExporter -> Blender bridge"}
  var path := "hand_grips/"+id+".json"
  var file := FileAccess.open(runtime+path,FileAccess.WRITE);file.store_string(JSON.stringify(record,"  "));file.close()
  config.right_hand_grip=path
  records.append(record)
  source.queue_free();target.queue_free();await process_frame
 var file := FileAccess.open(runtime+"catalog.json",FileAccess.WRITE);file.store_string(JSON.stringify(catalog,"  "));file.close()
 file=FileAccess.open(job+"/evidence/native_weapon_grips.json",FileAccess.WRITE);file.store_string(JSON.stringify(records,"  "));file.close()
 print("MH_NATIVE_WEAPON_GRIPS_DONE characters=",records.size());quit()
