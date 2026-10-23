extends SceneTree
## Package already UE-retargeted target-rig tracks. No Godot retargeting or rest conversion.
func _initialize(): call_deferred("build")
func find_type(node: Node,type: String) -> Node:
 if node.is_class(type): return node
 for child in node.get_children():
  var found=find_type(child,type)
  if found: return found
 return null
func glb(path: String) -> Node:
 var document=GLTFDocument.new();var state=GLTFState.new()
 assert(document.append_from_file(path,state)==OK,path)
 return document.generate_scene(state)
func build():
 var folder=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/")
 assert(folder.is_absolute_path())
 var manifest_name=OS.get_environment("OTHERREACH_MPFB_ANIM_MANIFEST")
 if manifest_name.is_empty():manifest_name="ue_mpfb_motion.json"
 var output_folder=OS.get_environment("OTHERREACH_MPFB_LIBRARY_FOLDER")
 if output_folder.is_empty():output_folder="ue_libraries"
 var source_folder=OS.get_environment("OTHERREACH_MPFB_LIBRARY_SOURCE").replace("\\","/")
 var manifest=JSON.parse_string(FileAccess.get_file_as_string(folder+"/evidence/"+manifest_name))
 assert(manifest.status=="DONE")
 DirAccess.make_dir_recursive_absolute(folder+"/"+output_folder)
 var rows=[]
 for character in manifest.characters:
  var id=character.id
  if not character.get("complete",false):continue
  var paces={}
  var phases={}
  var contacts={}
  var target=glb("D:/Otherreach_Scratch/rcd3_20261020/shared_fallback/rigged/"+id+"/"+id+"_rigged.glb");root.add_child(target)
  var target_rig:Skeleton3D=find_type(target,"Skeleton3D")
  var library=load(source_folder+"/"+id+"_motion.res").duplicate(true) as AnimationLibrary if not source_folder.is_empty() else AnimationLibrary.new()
  if not source_folder.is_empty():
   paces=library.get_meta("motion_paces",{});phases=library.get_meta("motion_phases",{});contacts=library.get_meta("foot_plants",{})
  for clip in character.clips:
   var source=glb(clip.path);root.add_child(source)
   var rig:Skeleton3D=find_type(source,"Skeleton3D")
   assert(rig.get_bone_count()==target_rig.get_bone_count(),"Bone count mismatch")
   var worst=0.0
   for b in target_rig.get_bone_count():
    var name=target_rig.get_bone_name(b);var s=rig.find_bone(name)
    assert(s>=0,"Missing target bone: "+name)
    var expected=target_rig.get_bone_rest(b);var actual=rig.get_bone_rest(s)
    worst=maxf(worst,expected.origin.distance_to(actual.origin))
    if not expected.is_equal_approx(actual):
     push_error("MPFB UE rest mismatch "+id+" "+name+" expected="+str(expected)+" actual="+str(actual));quit(4);return
   var player:AnimationPlayer=find_type(source,"AnimationPlayer")
   var names=Array(player.get_animation_list()).filter(func(n):return n!="RESET")
   assert(names.size()==1)
   var animation=player.get_animation(names[0]).duplicate(true) as Animation
   # Engine poses include rest. Keep only root/pelvis translation, all other bone lengths fixed.
   var stripped=0.0
   for t in animation.get_track_count():
    var bone=animation.track_get_path(t).get_subname(0)
    if animation.track_get_type(t)==Animation.TYPE_POSITION_3D and not bone in ["Root","pelvis"]:
     var rest_position=target_rig.get_bone_rest(target_rig.find_bone(bone)).origin
     for k in animation.track_get_key_count(t):
      stripped=maxf(stripped,rest_position.distance_to(animation.track_get_key_value(t,k)))
      animation.track_set_key_value(t,k,rest_position)
    elif animation.track_get_type(t)==Animation.TYPE_SCALE_3D:
     for k in animation.track_get_key_count(t):animation.track_set_key_value(t,k,target_rig.get_bone_rest(target_rig.find_bone(bone)).basis.get_scale())
   var types={}
   for t in animation.get_track_count():
    var path=animation.track_get_path(t)
    assert(path.get_subname_count()==1)
    var bone=path.get_subname(0)
    assert(target_rig.find_bone(bone)>=0)
    animation.track_set_path(t,NodePath("Skeleton3D:"+bone))
    types[str(bone)+":"+str(animation.track_get_type(t))]=true
    if bone=="Root" and animation.track_get_type(t)==Animation.TYPE_POSITION_3D:
     var origin=target_rig.get_bone_rest(target_rig.find_bone(bone)).origin
     for k in animation.track_get_key_count(t):
      var position:Vector3=animation.track_get_key_value(t,k);position.x=origin.x;position.z=origin.z
      animation.track_set_key_value(t,k,position)
   # Explicit rest holds for omitted native tracks prevent the preceding clip bleeding into a crouch.
   for b in target_rig.get_bone_count():
    var bone=target_rig.get_bone_name(b);var rest=target_rig.get_bone_rest(b)
    for kind in [Animation.TYPE_POSITION_3D,Animation.TYPE_ROTATION_3D]:
     if types.has(str(bone)+":"+str(kind)):continue
     var t=animation.add_track(kind);animation.track_set_path(t,NodePath("Skeleton3D:"+bone))
     if kind==Animation.TYPE_POSITION_3D:animation.position_track_insert_key(t,0,rest.origin)
     else:animation.rotation_track_insert_key(t,0,rest.basis.get_rotation_quaternion())
   animation.loop_mode=Animation.LOOP_LINEAR if clip.logical in ["idle","walk","run","sprint","crouch_idle","crouch_walk","fall","sword_ready","sword_block","talk","spear_ready","bow_ready","strafe_left","strafe_right","walk_strafe_left","walk_strafe_right","run_back","walk_back","idle_foldarms","idle_look","sit","sit_talk"] else Animation.LOOP_NONE
   if clip.logical in ["walk","run","sprint","walk_back","strafe_left","strafe_right","walk_strafe_left","walk_strafe_right","run_back","crouch_walk"]:
    # Measure the actual cleaned target-space animation, not the source-body cadence.
    var measurement=measure(animation,target_rig)
    paces[clip.logical]=measurement.pace
    phases[clip.logical]=measurement.phase
    contacts[clip.logical]=measurement.plants

   if library.has_animation(clip.logical):library.remove_animation(clip.logical)
   library.add_animation(clip.logical,animation)
   rows.append({"id":id,"clip":clip.logical,"bones":rig.get_bone_count(),"rest_error_m":worst,"max_removed_nonroot_translation_m":stripped,"tracks":animation.get_track_count()})
   source.free()
  for pair in [["crouch_move","crouch_walk"],["block","sword_block"],["hit_react","hit"],["talk_idle","talk"],["attack","sword_attack_1"]]:
   if pair[0]=="attack" and OS.get_environment("OTHERREACH_CALM_ATTACK_CLIPS")=="1" and library.has_animation("attack"):library.remove_animation("attack")
   if not library.has_animation(pair[0]) and library.has_animation(pair[1]):library.add_animation(pair[0],library.get_animation(pair[1]))
  library.set_meta("target_body_sha256",FileAccess.get_sha256("D:/Otherreach_Scratch/rcd3_20261020/shared_fallback/rigged/"+id+"/"+id+"_rigged.glb"))
  library.set_meta("motion_paces",paces)
  library.set_meta("motion_phases",phases)
  library.set_meta("foot_plants",contacts)
  library.set_meta("route","Epic IK Retargeter -> native GLTFExporter -> direct target track copy; no Godot retarget")
  assert(ResourceSaver.save(library,folder+"/"+output_folder+"/"+id+"_motion.res")==OK)
  target.free()
 FileAccess.open(folder+"/evidence/"+manifest_name.trim_suffix(".json")+"_libraries.json",FileAccess.WRITE).store_string(JSON.stringify(rows,"  "))
 print("UE_MPFB_MOTION_LIBRARIES_DONE ",rows)
 quit()

func measure(animation: Animation, rig: Skeleton3D) -> Dictionary:
 var player=AnimationPlayer.new();rig.add_child(player);player.root_node=NodePath("..")
 player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
 var library=AnimationLibrary.new();var copy=animation.duplicate(true) as Animation
 for t in copy.get_track_count():copy.track_set_path(t,NodePath(".:"+copy.track_get_path(t).get_subname(0)))
 library.add_animation("probe",copy);player.add_animation_library("",library)
 var count=maxi(20,ceili(animation.length*120));var dt=animation.length/count
 var samples=[];var floors=[INF,INF]
 for f in count:
  player.play("probe",0);player.seek(f*dt,true);player.advance(0)
  var feet=[]
  for side in 2:
   var at=rig.get_bone_global_pose(rig.find_bone("ball_l" if side==0 else "ball_r")).origin
   feet.append(at);floors[side]=minf(floors[side],at.y)
  samples.append(feet)
 var speeds=[];var plants=[]
 for side in 2:
  var minima=[]
  for f in count:
   var prev=(f+count-1)%count;var next=(f+1)%count
   if samples[f][side].y<=floors[side]+.015:
    var velocity=(samples[next][side]-samples[prev][side])/(2*dt)
    if Vector2(velocity.x,velocity.z).length()>.05:speeds.append(Vector2(velocity.x,velocity.z).length())
   if samples[f][side].y<=samples[prev][side].y and samples[f][side].y<samples[next][side].y and samples[f][side].y<=floors[side]+.015:minima.append(f)
  for f in minima:
   if not plants.any(func(p):return p.side==side and absf(p.seconds-f*dt)<animation.length*.2):plants.append({"side":side,"seconds":f*dt,"phase":float(f)/count})
 speeds.sort();plants.sort_custom(func(a,b):return a.seconds<b.seconds)
 var pace=speeds[speeds.size()/2] if not speeds.is_empty() else 0.0
 assert(pace>.05,"Target gait has no measured stride")
 var phase=0.0
 for plant in plants:
  if plant.side==0:phase=plant.seconds;break
 rig.remove_child(player);player.free();rig.reset_bone_poses()
 return {"pace":pace,"phase":phase,"plants":plants}
