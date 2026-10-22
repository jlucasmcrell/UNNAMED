extends SceneTree
## Extract Epic-authored right digits from new clips; no body/skin/rest writes.
func _initialize():call_deferred("run")
func find_type(node:Node,type:String)->Node:
 if node.is_class(type):return node
 for child in node.get_children():
  var found=find_type(child,type)
  if found:return found
 return null
func glb(path:String)->Node:
 var doc=GLTFDocument.new();var state=GLTFState.new();assert(doc.append_from_file(path,state)==OK)
 return doc.generate_scene(state)
func run():
 var job=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/")
 var args=OS.get_cmdline_user_args();var id:String=args[0] if args.size()==1 else "ada"
 var runtime="D:/Otherreach_Scratch/rcd3_20261020/runtime/"
 var catalog:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(runtime+"catalog.json"))
 var manifest:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(job+"/evidence/clean_extremities_"+id+".json"))
 var target=glb(runtime+catalog.characters[id].body);root.add_child(target)
 var expected:Skeleton3D=find_type(target,"Skeleton3D");assert(expected.get_bone_count()==342)
 DirAccess.make_dir_recursive_absolute(job+"/hand_grips")
 var records=[]
 for clip in manifest.clips:
  var source=glb(clip.path);root.add_child(source)
  var rig:Skeleton3D=find_type(source,"Skeleton3D");assert(rig.get_bone_count()==342)
  for b in 342:
   var name_=expected.get_bone_name(b);var s=rig.find_bone(name_);assert(s>=0)
   assert(expected.get_bone_rest(b).is_equal_approx(rig.get_bone_rest(s)),"Rest mismatch "+name_)
   var p=expected.get_bone_parent(b);var sp=rig.get_bone_parent(s)
   assert((p<0 and sp<0) or (p>=0 and sp>=0 and expected.get_bone_name(p)==rig.get_bone_name(sp)))
  var player:AnimationPlayer=find_type(source,"AnimationPlayer")
  var names=Array(player.get_animation_list()).filter(func(n):return n!="RESET");assert(names.size()==1)
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  player.play(names[0],0);player.seek(0,true);player.advance(0)
  var rotations={};var left_error=0.0
  for digit in ["thumb","index","middle","ring","pinky"]:
   for joint in ["01","02","03"]:
    for side in ["l","r"]:
     var name_:String=digit+"_"+joint+"_"+side;var b=rig.find_bone(name_);var q=rig.get_bone_pose_rotation(b).normalized()
     if side=="r":rotations[name_]=[q.x,q.y,q.z,q.w]
     else:left_error=maxf(left_error,rad_to_deg(q.angle_to(rig.get_bone_rest(b).basis.get_rotation_quaternion())))
  var output=job+"/hand_grips/"+id+"_"+clip.logical+".json"
  var full_hand={};var helper_changed=[];var hand=rig.find_bone("hand_r")
  for b in rig.get_bone_count():
   var ancestor=rig.get_bone_parent(b)
   while ancestor>=0 and ancestor!=hand:ancestor=rig.get_bone_parent(ancestor)
   if ancestor!=hand:continue
   var q=rig.get_bone_pose_rotation(b).normalized();var p=rig.get_bone_pose_position(b)
   var rest=rig.get_bone_rest(b);var angle=rad_to_deg(q.angle_to(rest.basis.get_rotation_quaternion()))
   var displacement=p.distance_to(rest.origin)
   full_hand[rig.get_bone_name(b)]={"rotation":[q.x,q.y,q.z,q.w],"position":[p.x,p.y,p.z]}
   if not rotations.has(rig.get_bone_name(b)) and (angle>.1 or displacement>.00001):helper_changed.append({"bone":rig.get_bone_name(b),"degrees":angle,"position_mm":displacement*1000.0})
  var record={"id":id,"native_body_sha256":FileAccess.get_sha256(runtime+catalog.characters[id].body),"native_bones":342,"native_rest_unchanged":true,"rotations":rotations,"source":clip.path,"sample_seconds":0.0,"left_hand_max_rest_degrees":left_error,"runtime_scope":"only fifteen right digit rotations; left clips untouched","route":"Epic Sequencer Bake To Control Rig -> native GLTFExporter -> Blender bridge"}
  record.full_hand_pose=full_hand;record.additional_animated_hand_bones=helper_changed
  FileAccess.open(output,FileAccess.WRITE).store_string(JSON.stringify(record,"  "))
  records.append({"id":clip.logical,"path":output});source.free()
 var manifest_path=job+"/evidence/grip_candidates.json"
 var candidates:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(manifest_path)) if FileAccess.file_exists(manifest_path) else {}
 candidates[id]=records
 FileAccess.open(manifest_path,FileAccess.WRITE).store_string(JSON.stringify(candidates,"  "))
 print("CONTROL_RIG_GRIPS_EXTRACTED ",records.size());quit()
