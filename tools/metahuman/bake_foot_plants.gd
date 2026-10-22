extends SceneTree
## Read cleaned target clips, bake contact metadata into NEW libraries only.
## Manifest entries: id, body, library, output. No retargeting or bone edits.
func _initialize(): call_deferred("run")

func find_rig(node: Node) -> Skeleton3D:
 if node is Skeleton3D: return node
 for child in node.get_children():
  var result=find_rig(child)
  if result:return result
 return null

func starts(heights: Array, dt: float) -> Array:
 var floor_:float=heights.min()
 var ceiling:float=heights.max()
 if ceiling-floor_<.005:return [] # Standing foot is not a repeated step.
 var result=[]
 var armed=true
 var peak=heights.find(ceiling)
 for offset in heights.size():
  var i=(peak+offset)%heights.size()
  if heights[i]>floor_+minf(.015,(ceiling-floor_)*.5):armed=true
  # First sample of the minimum plateau, rather than its final/toe-off sample.
  # Rearm only after the foot lifts, so millimetre-level planted-foot rocking
  # cannot produce two sounds during one stance.
  if armed and heights[i]<=floor_+.001:
   result.append(i*dt)
   armed=false
 result.sort()
 return result

func run():
 assert(starts([.02,.01,0.0,0.0,.01,.02],.01)==[.02])
 assert(starts([0.0,.01,.02,.02,.01,0.0],.01)==[.05])
 assert(starts([0.0,0.0,0.0,0.0],.01).is_empty())
 assert(starts([.02,0.0,.0015,0.0,.02,.04],.01)==[.01])
 var args=OS.get_cmdline_user_args()
 assert(args.size()==1,"Expected absolute JSON manifest")
 var manifest:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(args[0]))
 var receipts=[]
 for entry in manifest.entries:
  assert(entry.output!=entry.library and not FileAccess.file_exists(entry.output))
  var original_hash=FileAccess.get_sha256(entry.library)
  var document=GLTFDocument.new();var state=GLTFState.new()
  assert(document.append_from_file(entry.body,state)==OK)
  var model=document.generate_scene(state);root.add_child(model)
  var rig=find_rig(model);assert(rig!=null)
  var player=AnimationPlayer.new();rig.add_child(player);player.root_node=NodePath("..")
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  var source=load(entry.library) as AnimationLibrary
  assert(source!=null)
  var library=source.duplicate(true) as AnimationLibrary
  var probe=AnimationLibrary.new()
  for clip in ["walk","run","sprint","walk_back","strafe_left","strafe_right","crouch_walk"]:
   if not library.has_animation(clip):continue
   var animation=library.get_animation(clip).duplicate(true) as Animation
   for track in animation.get_track_count():
    assert(animation.track_get_path(track).get_subname_count()==1)
    animation.track_set_path(track,NodePath(".:"+animation.track_get_path(track).get_subname(0)))
   probe.add_animation(clip,animation)
  player.add_animation_library("",probe)
  var contacts={};var curves={}
  for clip in probe.get_animation_list():
   var animation=probe.get_animation(clip)
   var count=ceili(animation.length*120);var dt=animation.length/count
   var left=[];var right=[]
   for frame in count:
    player.play(clip,0);player.seek(frame*dt,true);player.advance(0)
    left.append(rig.get_bone_global_pose(rig.find_bone("foot_l")).origin.y)
    right.append(rig.get_bone_global_pose(rig.find_bone("foot_r")).origin.y)
   var events=[]
   for side in ["L","R"]:
    for time in starts(left if side=="L" else right,dt):events.append({"side":side,"time":time})
   events.sort_custom(func(a,b):return a.time<b.time)
   assert(events.size()>=2,"Missing left/right contact: "+str(entry.id)+" "+clip)
   contacts[clip]={"length":animation.length,"events":events,"sample_hz":1.0/dt}
   curves[clip]={"dt":dt,"left_y":left,"right_y":right}
  library.set_meta("foot_plants",contacts)
  library.set_meta("foot_plants_schema",1)
  assert(ResourceSaver.save(library,entry.output)==OK)
  assert(FileAccess.get_sha256(entry.library)==original_hash)
  receipts.append({"id":entry.id,"source_sha256":original_hash,"output":entry.output,"contacts":contacts,"curves":curves})
  model.free()
 FileAccess.open(manifest.receipt,FileAccess.WRITE).store_string(JSON.stringify(receipts,"  "))
 print("FOOT_PLANTS_BAKED ",receipts.size())
 quit()
