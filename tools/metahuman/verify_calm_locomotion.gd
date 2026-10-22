extends SceneTree
func _initialize():call_deferred("run")
func find_rig(node: Node) -> Skeleton3D:
 if node is Skeleton3D:return node
 for child in node.get_children():
  var found:=find_rig(child)
  if found:return found
 return null
func actor(entry: Dictionary,calm: bool) -> Dictionary:
 var document:=GLTFDocument.new();var state:=GLTFState.new()
 assert(document.append_from_file(entry.body,state)==OK)
 var body:=document.generate_scene(state);root.add_child(body)
 var rig:=find_rig(body);var library=load(entry.output).duplicate(true) as AnimationLibrary
 for clip in library.get_animation_list():
  var animation:=library.get_animation(clip)
  for t in animation.get_track_count():animation.track_set_path(t,NodePath(".:"+animation.track_get_path(t).get_subname(0)))
 var player:=AnimationPlayer.new();rig.add_child(player);player.root_node=NodePath("..")
 player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL;player.add_animation_library("",library)
 OS.set_environment("OTHERREACH_CALM_BACK","1" if calm else "0")
 var loco=load("res://Art/LocomotionBlend.gd").new();rig.add_child(loco)
 assert(loco.configure(player,library.get_meta("motion_paces",{}),true,{}))
 return {"body":body,"rig":rig,"loco":loco}
func run():
 var path:=OS.get_cmdline_user_args()[0]
 var manifest:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(path));var rows=[]
 for entry in manifest.entries:
  if not entry.id in ["ada","mateo","mpfb_fem_lt","mpfb_male_lt"]:continue
  for velocity in [Vector2(0,-1.4),Vector2(0,3.2),Vector2(1.5,0)]:
   var before:=actor(entry,false);var after:=actor(entry,true)
   var a:Skeleton3D=before.rig;var b:Skeleton3D=after.rig
   var worst_leg:=0.0;var changed_head:=0.0;var neck_length_error:=0.0
   for frame in 240:
    for instance in [before,after]:instance.loco.move(velocity,1.0/60);instance.loco.advance(1.0/60)
    for name_ in ["pelvis","thigh_l","calf_l","foot_l","ball_l","thigh_r","calf_r","foot_r","ball_r"]:
     var bone:=a.find_bone(name_);assert(bone>=0)
     var old:=a.get_bone_global_pose(bone);var now:=b.get_bone_global_pose(bone)
     worst_leg=maxf(worst_leg,old.origin.distance_to(now.origin));assert(old.basis.is_equal_approx(now.basis))
    var head:=a.find_bone("head");changed_head=maxf(changed_head,a.get_bone_global_pose(head).basis.get_rotation_quaternion().angle_to(b.get_bone_global_pose(head).basis.get_rotation_quaternion()))
    var parent:=b.get_bone_parent(head)
    var length:=b.get_bone_global_pose(head).origin.distance_to(b.get_bone_global_pose(parent).origin)
    neck_length_error=maxf(neck_length_error,absf(length-a.get_bone_global_pose(head).origin.distance_to(a.get_bone_global_pose(parent).origin)))
   assert(worst_leg<.00001,"Calm filter affected legs")
   assert(changed_head>.01,"Calm filter did not affect head")
   print("CALM_LENGTH_DIAGNOSTIC ",entry.id," ",neck_length_error*1000)
   assert(neck_length_error<.00001,"Head-to-parent length differs from baseline")
   rows.append({"id":entry.id,"velocity":str(velocity),"leg_position_error_mm":worst_leg*1000,"head_rotation_change_degrees":rad_to_deg(changed_head),"head_parent_length_error_mm":neck_length_error*1000})
   before.body.free();after.body.free()
 FileAccess.open(path.get_base_dir()+"/upper_filter_verification.json",FileAccess.WRITE).store_string(JSON.stringify(rows,"  "))
 print("CALM_BACK_LEGS_AND_LENGTH_PASS ",rows.size());quit()
