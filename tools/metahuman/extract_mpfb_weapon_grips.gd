extends SceneTree
## Read Epic's target-space finger pose only; preserve original 53 joints/rests/skins.
func _initialize(): call_deferred("run")
func find_type(node: Node,type: String) -> Node:
 if node.is_class(type):return node
 for child in node.get_children():
  var found=find_type(child,type)
  if found:return found
 return null
func glb(path: String) -> Node:
 var document=GLTFDocument.new();var state=GLTFState.new()
 assert(document.append_from_file(path,state)==OK)
 return document.generate_scene(state)
func run():
 var folder=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/")
 assert(folder.is_absolute_path())
 var control:=not OS.get_environment("OTHERREACH_MPFB_CONTROL_GRIP").is_empty()
 var job_name:String="mpfb_control_grips" if control else "mpfb_weapon_grips"
 var suffix:=OS.get_environment("OTHERREACH_MPFB_CONTROL_JOB")
 if control and not suffix.is_empty():job_name+="_"+suffix
 var manifest:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(folder+"/evidence/ue_"+job_name+".json"))
 assert(manifest.status=="DONE")
 var output=folder+"/"+job_name+"/json"
 DirAccess.make_dir_recursive_absolute(output)
 var records=[]
 for character in manifest.characters:
  assert(character.complete and character.clips.size()==1 and character.clips[0].logical=="weapon_grip")
  var id:String=character.id
  var target_path="D:/Otherreach_Scratch/rcd3_20261020/shared_fallback/rigged/"+id+"/"+id+"_rigged.glb"
  var target=glb(target_path);root.add_child(target)
  var source=glb(character.clips[0].path);root.add_child(source)
  var rig:Skeleton3D=find_type(source,"Skeleton3D")
  var expected:Skeleton3D=find_type(target,"Skeleton3D")
  assert(rig.get_bone_count()==53 and expected.get_bone_count()==53)
  for b in 53:
   var name_=expected.get_bone_name(b);var s=rig.find_bone(name_)
   assert(s>=0 and expected.get_bone_rest(b).is_equal_approx(rig.get_bone_rest(s)),"Rest mismatch "+id+" "+name_)
   var parent=expected.get_bone_parent(b);var actual_parent=rig.get_bone_parent(s)
   assert((parent<0 and actual_parent<0) or (parent>=0 and actual_parent>=0 and expected.get_bone_name(parent)==rig.get_bone_name(actual_parent)))
  var player:AnimationPlayer=find_type(source,"AnimationPlayer")
  var names=Array(player.get_animation_list()).filter(func(n):return n!="RESET")
  assert(names.size()==1)
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  player.play(names[0],0);player.seek(.5,true);player.advance(0)
  var rotations={};var left_error=0.0;var right_angle=0.0
  for digit in ["thumb","index","middle","ring","pinky"]:
   for joint in ["01","02","03"]:
    for side in ["l","r"]:
     var name_:String=digit+"_"+joint+"_"+side;var b=rig.find_bone(name_)
     var q=rig.get_bone_pose_rotation(b).normalized()
     var angle=rad_to_deg(q.angle_to(rig.get_bone_rest(b).basis.get_rotation_quaternion()))
     if side=="l":left_error=maxf(left_error,angle)
     else:
      rotations[name_]=[q.x,q.y,q.z,q.w];right_angle=maxf(right_angle,angle)
  assert(rotations.size()==15 and left_error<.1 and right_angle>10)
  var record={"id":id,"target_body_sha256":FileAccess.get_sha256(target_path),"rotations":rotations,"native_bones":53,"native_rest_unchanged":true,"left_hand_max_rest_degrees":left_error,"right_hand_max_grip_degrees":right_angle,"source":character.clips[0].source,"sample_seconds":.5,"route":"Epic IK Retargeter right digits only -> native GLTFExporter; no runtime retarget"}
  FileAccess.open(output+"/"+id+".json",FileAccess.WRITE).store_string(JSON.stringify(record,"  "))
  records.append(record);source.free();target.free()
 FileAccess.open(folder+"/evidence/"+job_name+".json",FileAccess.WRITE).store_string(JSON.stringify(records,"  "))
 print("MPFB_AUTHORED_GRIPS_DONE ",records.size());quit()
