extends SceneTree
## CPU development check on the actual native body and animation library. Packaged video is still required.
func _initialize():
 call_deferred("check")

func find_rig(node: Node) -> Skeleton3D:
 if node is Skeleton3D:return node
 for child in node.get_children():
  var rig:=find_rig(child)
  if rig!=null:return rig
 return null

func check():
 var m: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(OS.get_cmdline_user_args()[0]))
 var document:=GLTFDocument.new()
 var state:=GLTFState.new()
 assert(document.append_from_file(m.body,state)==OK)
 var body:=document.generate_scene(state)
 root.add_child(body)
 var rig:=find_rig(body)
 assert(rig!=null and rig.get_bone_count()==59)
 var library: AnimationLibrary=load(m.motion)
 assert(library.get_meta("target_body_sha256")==FileAccess.get_sha256(m.body))
 var player:=AnimationPlayer.new()
 rig.add_child(player)
 player.root_node=NodePath("..")
 player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
 player.add_animation_library("",library)
 var blend=load(m.blend).new()
 root.add_child(blend)
 assert(blend.configure(player,library.get_meta("motion_paces"),true,library.get_meta("motion_phases")))
 var previous:=[]
 var frames:=0
 var max_step:=0.0
 var transitions:=[]
 var water_motion_samples:=0
 var head_height:={}
 for section in [{"name":"walk","wet":false,"speed":1.6,"frames":60},{"name":"enter_water","wet":true,"speed":.8,"frames":90},{"name":"swim","wet":true,"speed":.8,"frames":180},{"name":"tread","wet":true,"speed":0.0,"frames":240},{"name":"exit_water","wet":false,"speed":1.6,"frames":90},{"name":"walk","wet":false,"speed":1.6,"frames":60}]:
  for frame in section.frames:
   var velocity:=Vector2(0,section.speed)
   var before: float=blend.water_weight
   if not blend.water(section.wet,velocity,1.0/60):blend.move(velocity,1.0/60)
   if section.wet:assert(blend.water_weight>=before)
   else:assert(blend.water_weight<=before)
   if blend.water_state in ["enter_water","exit_water"] and not transitions.has(blend.water_state):transitions.append(blend.water_state)
   blend.advance(1.0/60)
   rig.force_update_all_bone_transforms()
   if section.name in ["swim","tread"]:
    var y:=rig.get_bone_global_pose(rig.find_bone("Head")).origin.y
    var range_: Array=head_height.get(section.name,[INF,-INF])
    range_[0]=minf(range_[0],y);range_[1]=maxf(range_[1],y)
    head_height[section.name]=range_
   var sample:=[]
   var changed:=false
   for bone in rig.get_bone_count():
    var rotation:=rig.get_bone_pose_rotation(bone)
    if not previous.is_empty():
     var angle:=rad_to_deg(previous[bone].angle_to(rotation))
     max_step=maxf(max_step,angle)
     if angle>.05:changed=true
    sample.append(rotation)
   if section.name in ["swim","tread"] and changed:water_motion_samples+=1
   previous=sample
   frames+=1
  if section.name=="tread":assert(blend.water_state=="tread")
 assert(transitions.has("enter_water") and transitions.has("exit_water"))
 assert(water_motion_samples>350,"Water clips froze instead of playing at source speed")
 assert(max_step<35.0,"Native transition snapped a joint in one 60 Hz frame: "+str(max_step))
 assert(blend.water_weight==0.0)
 assert(blend.stop())
 assert(blend.water_weight==0.0)
 assert(blend.water(true,Vector2.ZERO,1.0/60) and blend.water_state=="enter_water")
 var report={"status":"PASS","body_sha256":FileAccess.get_sha256(m.body),"motion_sha256":FileAccess.get_sha256(m.motion),"blend_sha256":FileAccess.get_sha256(m.blend),"frames":frames,"water_motion_samples":water_motion_samples,"head_height_m":head_height,"max_joint_step_degrees":max_step,"transitions":transitions,"interruption_reset":true,"rendered":false}
 var out:=FileAccess.open(m.output,FileAccess.WRITE)
 out.store_string(JSON.stringify(report,"  "));out.close()
 print("NATIVE_WATER_BLEND_PASS ",report)
 quit()
