extends SceneTree
## Real AnimationPlayer/AnimationTree callbacks against target skeletons and baked minima.
func _initialize():call_deferred("run")
func rig_in(node: Node) -> Skeleton3D:
 if node is Skeleton3D:return node
 for child in node.get_children():
  var found=rig_in(child)
  if found:return found
 return null
func run():
 var args=OS.get_cmdline_user_args();assert(args.size()==1)
 var manifest:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(args[0]))
 var receipt:Dictionary=JSON.parse_string("{}")
 var curves:Array=JSON.parse_string(FileAccess.get_file_as_string(manifest.receipt))
 var results=[]
 for entry in manifest.entries:
  if not entry.id in ["ada","mateo","mpfb_fem_lt","mpfb_male_lt"]:continue
  var original_hash=FileAccess.get_sha256(entry.output)
  var document=GLTFDocument.new();var state=GLTFState.new()
  assert(document.append_from_file(entry.body,state)==OK)
  var body=document.generate_scene(state);root.add_child(body)
  var rig=rig_in(body);assert(rig!=null)
  var rests=[]
  for b in rig.get_bone_count():rests.append(rig.get_bone_rest(b))
  var library=load(entry.output).duplicate() as AnimationLibrary
  var original=load(entry.output) as AnimationLibrary
  for clip in library.get_animation_list():
   var copy=library.get_animation(clip).duplicate(true) as Animation
   for t in copy.get_track_count():copy.track_set_path(t,NodePath(".:"+copy.track_get_path(t).get_subname(0)))
   library.remove_animation(clip);library.add_animation(clip,copy)
  var player=AnimationPlayer.new();rig.add_child(player);player.root_node=NodePath("..")
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  player.add_animation_library("",library)
  var selected={"clip":""};var events=[]
  var plants=load("res://Art/FootPlants.gd").new()
  assert(plants.configure(player,func():return selected.clip))
  plants.planted.connect(func(clip,side,key_time):events.append({"clip":clip,"side":side,"key":key_time,"actual":player.current_animation_position}))
  var record={"id":entry.id,"clips":[],"blends":[]}
  var sampled:Dictionary=curves.filter(func(c):return c.id==entry.id)[0]
  for clip in ["walk","run","sprint","walk_back","strafe_left","strafe_right","crouch_walk"]:
   events.clear();selected.clip=clip;rig.reset_bone_poses();player.play(clip,0);player.seek(0,true)
   var length=player.get_animation(clip).length;var count=ceili(length*120);var dt=length/count
   var worst_phase=0.0;var worst_floor=0.0
   for frame in count*2:
    var start=events.size();player.advance(dt)
    for index in range(start,events.size()):
     var event:Dictionary=events[index]
     var difference=fposmod(float(event.actual)-float(event.key),length)
     worst_phase=maxf(worst_phase,minf(difference,length-difference))
     var samples:Array=sampled.curves[clip].left_y if event.side=="L" else sampled.curves[clip].right_y
     var height=rig.get_bone_global_pose(rig.find_bone("foot_l" if event.side=="L" else "foot_r")).origin.y
     worst_floor=maxf(worst_floor,height-float(samples.min()))
   assert(events.size()>=3 and events.size()<=5,"Unexpected step count "+entry.id+" "+clip+" "+str(events.size()))
   assert(worst_phase<=dt+.0001,"Method event late "+str(worst_phase))
   assert(worst_floor<.003,"Event above foot minimum "+str(worst_floor))
   record.clips.append({"clip":clip,"events":events.size(),"max_key_error_ms":worst_phase*1000,"max_height_above_min_mm":worst_floor*1000})
  var loco=load("res://Art/LocomotionBlend.gd").new();rig.add_child(loco)
  var paces:Dictionary=original.get_meta("motion_paces",{})
  assert(loco.configure(player,paces,true,{}))
  plants.dominant=func():return loco.dominant_clip()
  events.clear()
  for case in [[Vector2(0,1.3),"walk"],[Vector2(0,3.2),"run"],[Vector2(0,-1.4),"walk_back"],[Vector2(1.5,0),"strafe_left"],[Vector2(-1.5,0),"strafe_right"],[Vector2(1.5,1.5),""]]:
   var velocity:Vector2=case[0];var expected:String=case[1]
   events.clear()
   var settled=0
   for frame in 720:
    if frame==360:settled=events.size()
    loco.move(velocity,1.0/120);loco.advance(1.0/120)
   assert(not events.is_empty(),"Blended gait silent "+entry.id+" "+str(velocity))
   assert(events.size()>settled)
   var emitted={}
   for index in range(settled,events.size()):
    emitted[events[index].clip]=true
    if not expected.is_empty():assert(events[index].clip==expected,"Wrong gait emitted a step "+entry.id+" "+str(velocity)+" "+events[index].clip)
   assert(emitted.size()==1,"Diagonal blend emitted competing gait steps")
   record.blends.append({"velocity":str(velocity),"events":events.size(),"dominant":loco.dominant_clip()})
  for b in rig.get_bone_count():assert(rig.get_bone_rest(b)==rests[b])
  assert(FileAccess.get_sha256(entry.output)==original_hash)
  for clip in original.get_animation_list():
   var animation=original.get_animation(clip)
   for t in animation.get_track_count():assert(animation.track_get_type(t)!=Animation.TYPE_METHOD)
  results.append(record);body.free()
 receipt={"status":"PASS","bodies":results,"original_libraries_and_rests_unchanged":true}
 FileAccess.open(args[0].get_base_dir()+"/engine_events.json",FileAccess.WRITE).store_string(JSON.stringify(receipt,"  "))
 print("FOOT_PLANT_ENGINE_PASS ",results.size());quit()
