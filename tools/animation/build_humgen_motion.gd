extends SceneTree
## Package native Epic output on its exact original HumGen target. No rest conversion or retarget.

func _initialize() -> void:
 call_deferred("build")

func nodes(node: Node, type: String) -> Array:
 var result := []
 if node.is_class(type): result.append(node)
 for child in node.get_children(): result.append_array(nodes(child,type))
 return result

func glb(path: String) -> Node:
 var document := GLTFDocument.new()
 var state := GLTFState.new()
 assert(document.append_from_file(path,state)==OK,"Cannot import "+path)
 return document.generate_scene(state)

static func clean_contacts(events: Array, duration: float) -> Array:
 # A foot hovering at the 2.5cm classifier boundary can cross it twice during
 # ONE stance (run's L contacts at .016/.082s). Merge that sampling chatter,
 # including across the loop seam; never alter animation values or force L/R.
 var quiet := minf(.12,duration*.15)
 var result := []
 for side in ["L","R"]:
  var kept := []
  for event in events:
   if event.side==side and (kept.is_empty() or float(event.time)-float(kept[-1].time)>=quiet):kept.append(event)
  if kept.size()>1 and duration-float(kept[-1].time)+float(kept[0].time)<quiet:kept.pop_front()
  result.append_array(kept)
 result.sort_custom(func(a,b): return a.time < b.time)
 return result

func contacts(player: AnimationPlayer, rig: Skeleton3D, clip: StringName) -> Dictionary:
 var duration := player.get_animation(clip).length
 var count := maxi(24,ceili(duration*60))
 var dt := duration/count
 var samples := []
 var floor_y := INF
 var shoulder_mean := Vector3.ZERO
 var head_mean := Vector3.ZERO
 for frame in count:
  player.play(clip,0)
  player.seek(frame*dt,true)
  player.advance(0)
  shoulder_mean += rig.get_bone_global_pose(rig.find_bone("LeftShoulder")).origin-rig.get_bone_global_pose(rig.find_bone("RightShoulder")).origin
  head_mean += rig.get_bone_global_pose(rig.find_bone("Head")).origin-rig.get_bone_global_pose(rig.find_bone("Hips")).origin
  var feet := []
  for name_ in ["LeftFoot","RightFoot"]:
   var point: Vector3 = rig.global_transform*rig.get_bone_global_pose(rig.find_bone(name_)).origin
   feet.append(point)
   floor_y = minf(floor_y,point.y)
  samples.append(feet)
 var events := []
 var speeds := []
 var stance_velocity := Vector3.ZERO
 var stance_samples := 0
 for side in 2:
  var before: bool = samples[count-1][side].y <= floor_y+.025
  for frame in count:
   var planted: bool = samples[frame][side].y <= floor_y+.025
   if planted and not before: events.append({"side":"L" if side==0 else "R","time":frame*dt})
   if planted and frame>0 and frame<count-1:
    var velocity: Vector3 = (samples[frame+1][side]-samples[frame-1][side])/(2*dt)
    stance_velocity+=velocity
    stance_samples+=1
    var speed := Vector2(velocity.x,velocity.z).length()
    if speed>.05: speeds.append(speed)
   before = planted
  if not events.any(func(e): return e.side == ("L" if side==0 else "R")):
   var minimum := 0
   for frame in count:
    if samples[frame][side].y < samples[minimum][side].y: minimum=frame
   events.append({"side":"L" if side==0 else "R","time":minimum*dt})
 speeds.sort()
 events.sort_custom(func(a,b): return a.time < b.time)
 var raw_events := events.duplicate(true)
 events=clean_contacts(events,duration)
 var phase := 0.0
 for event in events:
  if event.side=="L": phase=event.time;break
 head_mean/=count
 stance_velocity/=maxi(1,stance_samples)
 return {"length":duration,"events":events,"raw_events":raw_events,"pace":speeds[speeds.size()/2] if not speeds.is_empty() else 0.0,"phase":phase,"floor_m":floor_y,"stance_velocity_xz":[stance_velocity.x,stance_velocity.z],"shoulder_heading_degrees":rad_to_deg(atan2(shoulder_mean.z,shoulder_mean.x)),"head_height_above_hips_m":head_mean.y}

func build() -> void:
 var manifest_path := OS.get_cmdline_user_args()[0]
 var manifest: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(manifest_path))
 var target := glb(manifest.body)
 root.add_child(target)
 var rig: Skeleton3D = nodes(target,"Skeleton3D")[0]
 assert(rig.get_bone_count()==59)
 var library := AnimationLibrary.new()
 var sources := {}
 var paces := {}
 var phases := {}
 var plants := {}
 var action_phases := {}
 var report := {"body":manifest.body,"bones":59,"clips":[]}
 var loops := ["idle","idle_look","idle_foldarms","walk","run","sprint","crouch_idle","crouch_walk","sword_ready","sword_block","spear_ready","bow_ready","talk","sit","sit_talk","fall","walk_strafe_left","walk_strafe_right","strafe_left","strafe_right","walk_back","run_back","captive_kneel"]
 var gaits := ["walk","run","sprint","crouch_walk","walk_strafe_left","walk_strafe_right","strafe_left","strafe_right","walk_back","run_back"]
 for entry in manifest.clips:
  assert(not "kaykit" in entry.source.to_lower())
  var transport := glb(entry.path)
  root.add_child(transport)
  var exported: Skeleton3D = nodes(transport,"Skeleton3D")[0]
  assert(exported.get_bone_count()==59)
  var error_m := 0.0
  var basis_error := 0.0
  for bone in rig.get_bone_count():
   var name_ := rig.get_bone_name(bone)
   var other := exported.find_bone(name_)
   assert(other>=0,"Missing native joint "+name_)
   var parent := rig.get_bone_parent(bone)
   var other_parent := exported.get_bone_parent(other)
   assert((parent<0)==(other_parent<0))
   assert(parent<0 or rig.get_bone_name(parent)==exported.get_bone_name(other_parent))
   var a := rig.get_bone_rest(bone)
   var b := exported.get_bone_rest(other)
   error_m=maxf(error_m,a.origin.distance_to(b.origin))
   for column in 3:
    for row in 3: basis_error=maxf(basis_error,absf(a.basis[column][row]-b.basis[column][row]))
  assert(error_m<.0001 and basis_error<.0001,"Native transport rest mismatch: "+entry.state+" "+str(error_m)+" / "+str(basis_error))
  var player: AnimationPlayer = nodes(transport,"AnimationPlayer")[0]
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  var names := player.get_animation_list()
  if names.has("RESET"): names.remove_at(names.find("RESET"))
  assert(names.size()==1)
  var animation: Animation = player.get_animation(names[0]).duplicate(true)
  var source_rate := float(entry.get("source_rate_scale",1.0))
  assert(absf(source_rate)>.001,"Invalid source playback rate: "+entry.state)
  # Epic's native glTF transport omits AnimSequence.RateScale. FAL backward
  # sequences use NEGATIVE rates, so raw forward-time keys cause moonwalking.
  # Preserve the source's playback with native Animation timeline APIs only:
  # every value is unchanged, across the whole clip, with no bone-frame math.
  if not is_equal_approx(source_rate,1.0):
   var old_length := animation.length
   animation.length=old_length/absf(source_rate)
   for track in animation.get_track_count():
    var keys := []
    for key in animation.track_get_key_count(track):
     var at := animation.track_get_key_time(track,key)
     keys.append([((old_length-at) if source_rate<0 else at)/absf(source_rate),animation.track_get_key_value(track,key),animation.track_get_key_transition(track,key)])
    for key in range(animation.track_get_key_count(track)-1,-1,-1): animation.track_remove_key(track,key)
    for key in keys: animation.track_insert_key(track,key[0],key[1],key[2])
  var finger_error := 0.0
  for track in range(animation.get_track_count()-1,-1,-1):
   var path := animation.track_get_path(track)
   assert(path.get_subname_count()==1)
   var bone_name := path.get_subname(0)
   # Body mocap jaw channels contain source-rest rotations, not facial motion.
   # Keep the native mouth rest; FaceDriver/HumGenJaw owns facial articulation.
   if bone_name in ["Jaw","UpperJaw"]:
    animation.remove_track(track)
    continue
   assert(rig.find_bone(bone_name)>=0)
   if animation.track_get_type(track)==Animation.TYPE_ROTATION_3D and ["Thumb","Index","Middle","Ring","Little"].any(func(part): return part in bone_name):
    var rest := rig.get_bone_rest(rig.find_bone(bone_name)).basis.get_rotation_quaternion()
    for key in animation.track_get_key_count(track):
     var actual: Quaternion = animation.track_get_key_value(track,key)
     finger_error=maxf(finger_error,rad_to_deg(rest.angle_to(actual)))
   # The game controller owns planar travel. Normalize only Root translation;
   # preserve every rotation (including FAL's native facing correction) and every
   # other track. This is in-place extraction, not a skeleton or frame rebase.
   if bone_name=="Root" and animation.track_get_type(track)==Animation.TYPE_POSITION_3D:
    for key in animation.track_get_key_count(track):
     var value: Vector3 = animation.track_get_key_value(track,key)
     animation.track_set_key_value(track,key,Vector3(0,value.y,0))
   animation.track_set_path(track,NodePath(".:"+bone_name))
  assert(finger_error<.1,"Excluded fingers changed: "+entry.state+" "+str(finger_error))
  animation.loop_mode=Animation.LOOP_LINEAR if entry.state in loops else Animation.LOOP_NONE
  library.add_animation(entry.state,animation)
  sources[entry.state]=entry.source
  if entry.has("action_phases"):
   var marks: Array = entry.action_phases
   assert(marks.size()==3 and marks[0]>0 and marks[0]<marks[1] and marks[1]<=marks[2] and marks[2]<=1)
   action_phases[entry.state]=marks
  # Measure the very animation being saved, on the original target body.
  var measurement := AnimationPlayer.new()
  rig.add_child(measurement)
  measurement.root_node=NodePath("..")
  measurement.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  var single := AnimationLibrary.new()
  single.add_animation(entry.state,animation)
  measurement.add_animation_library("",single)
  var measured := contacts(measurement,rig,entry.state)
  if entry.state in gaits:
   assert(measured.pace>.05,"No measurable native gait "+entry.state)
   assert(absf(measured.shoulder_heading_degrees)<38,"Gait faces sideways: "+entry.state+" "+str(measured.shoulder_heading_degrees))
   assert(measured.head_height_above_hips_m>(.2 if entry.state=="crouch_walk" else .45),"Gait torso collapsed/tipped: "+entry.state)
   var travel := Vector2(0,-1) if entry.state in ["walk_back","run_back"] else Vector2(1,0) if "left" in entry.state else Vector2(-1,0) if "right" in entry.state else Vector2(0,1)
   var planted := Vector2(measured.stance_velocity_xz[0],measured.stance_velocity_xz[1])
   # The low-foot-height classifier covers standing locomotion. In the deep
   # crouch shuffle both swing and stance stay in that height band; keep its
   # measured samples, but leave its contact direction to the rendered gate.
   if entry.state!="crouch_walk":
    assert(planted.dot(travel)<-.05,"Planted feet move with travel (moonwalk): "+entry.state+" "+str(planted))
   paces[entry.state]=measured.pace
   phases[entry.state]=measured.phase
   plants[entry.state]={"length":measured.length,"events":measured.events}
  report.clips.append({"state":entry.state,"tracks":animation.get_track_count(),"seconds":animation.length,"source_rate_scale":source_rate,"rest_position_error_m":error_m,"rest_basis_error":basis_error,"excluded_fingers_error_degrees":finger_error,"measurements":measured})
  measurement.free()
  rig.reset_bone_poses()
  transport.free()
  await process_frame
 for pair in [["crouch_move","crouch_walk"],["block","sword_block"],["hit_react","hit"],["talk_idle","talk"],["attack","sword_attack_1"],["jump_start","jump"],["get_up","lie_to_idle"]]:
  if library.has_animation(pair[1]):
   library.add_animation(pair[0],library.get_animation(pair[1]))
   sources[pair[0]]=sources[pair[1]]
 library.set_meta("target_body_sha256",FileAccess.get_sha256(manifest.body))
 library.set_meta("animation_sources",sources)
 library.set_meta("route","Epic IK Retargeter -> native GLTFExporter -> exact-target Godot library; Root planar extraction only")
 library.set_meta("motion_paces",paces)
 library.set_meta("motion_phases",phases)
 library.set_meta("action_phases",action_phases)
 library.set_meta("foot_plants_schema",1)
 library.set_meta("source_playback_schema",1)
 library.set_meta("foot_plants",plants)
 DirAccess.make_dir_recursive_absolute(manifest.output.get_base_dir())
 assert(ResourceSaver.save(library,manifest.output)==OK)
 var file := FileAccess.open(manifest.output+".audit.json",FileAccess.WRITE)
 file.store_string(JSON.stringify(report,"  "))
 file.close()
 target.free()
 print("HUMGEN_EXACT_NATIVE_LIBRARY_PASS ",library.get_animation_list().size()," ",manifest.output)
 quit()
