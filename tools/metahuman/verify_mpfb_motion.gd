extends SceneTree
func _initialize():call_deferred("verify")
func find_type(node:Node,type:String)->Node:
 if node.is_class(type):return node
 for child in node.get_children():
  var found=find_type(child,type)
  if found:return found
 return null
func verify():
 var folder=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/")
 var report=[]
 var cases=[]
 for id in ["mpfb_fem_armour","mpfb_fem_lt","mpfb_male_armour","mpfb_male_lt","mpfb_var_armour"]:
  cases.append({"id":id,"path":"D:/Otherreach_Scratch/rcd3_20261020/shared_fallback/rigged/"+id+"/"+id+"_rigged.glb"})
 if FileAccess.file_exists(folder+"/mpfb_named/build.json"):
  var variants=JSON.parse_string(FileAccess.get_file_as_string(folder+"/mpfb_named/build.json"))
  assert(variants.status=="DONE")
  for row in variants.characters:cases.append({"id":row.id,"path":row.path})
 for item in cases:
  var id=item.id
  var path=item.path
  var doc=GLTFDocument.new();var state=GLTFState.new();assert(doc.append_from_file(path,state)==OK)
  var actor=doc.generate_scene(state);root.add_child(actor)
  var rig:Skeleton3D=find_type(actor,"Skeleton3D")
  var library=load(folder+"/ue_libraries/"+id+"_motion.res") as AnimationLibrary
  assert(library.get_meta("target_body_sha256")==FileAccess.get_sha256(path))
  assert(library.get_animation_list().size()==37)
  var player=AnimationPlayer.new();rig.add_child(player);player.root_node=NodePath("..")
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  var local=AnimationLibrary.new()
  for name_ in library.get_animation_list():
   var animation=library.get_animation(name_).duplicate(true) as Animation
   var worst=0.0
   for t in animation.get_track_count():
    var bone=animation.track_get_path(t).get_subname(0)
    animation.track_set_path(t,NodePath(".:"+bone))
    assert(rig.find_bone(bone)>=0)
    if animation.track_get_type(t)==Animation.TYPE_POSITION_3D and not bone in ["Root","pelvis"]:
     for k in animation.track_get_key_count(t):worst=maxf(worst,rig.get_bone_rest(rig.find_bone(bone)).origin.distance_to(animation.track_get_key_value(t,k)))
   assert(worst<.000001,"Non-root bone lengths changed")
   local.add_animation(name_,animation)
  player.add_animation_library("",local)
  for clip in ["idle","walk","run","sprint","walk_back","strafe_left","strafe_right","sword_attack_1","sword_attack_2","sword_attack_3","crouch_idle","crouch_walk"]:
   var count=120;var min_contact=INF;var max_contact=-INF;var pelvis_min=INF;var planted_min=INF
   for frame in count:
    rig.reset_bone_poses();player.play(clip,0);player.seek(local.get_animation(clip).length*frame/count,true);player.advance(0)
    var low=INF
    for side in ["l","r"]:
     var b=rig.find_bone("ball_"+side)
     low=minf(low,rig.get_bone_global_pose(b).origin.y-rig.get_bone_global_rest(b).origin.y)
    planted_min=minf(planted_min,minf(rig.get_bone_global_pose(rig.find_bone("ball_l")).origin.y,rig.get_bone_global_pose(rig.find_bone("ball_r")).origin.y))
    min_contact=minf(min_contact,low);max_contact=maxf(max_contact,low)
    pelvis_min=minf(pelvis_min,rig.get_bone_global_pose(rig.find_bone("pelvis")).origin.y)
   report.append({"id":id,"clip":clip,"lowest_toe_delta_from_rest_m":min_contact,"highest_lower_toe_delta_from_rest_m":max_contact,"pelvis_min_m":pelvis_min,"minimum_lower_toe_height_m":planted_min,"body_sha256":FileAccess.get_sha256(path)})
  actor.free()
 FileAccess.open(folder+"/evidence/mpfb_motion_cpu_probe.json",FileAccess.WRITE).store_string(JSON.stringify(report,"  "))
 print("MPFB_MOTION_CPU_PROBE_DONE ",report.size());quit()
