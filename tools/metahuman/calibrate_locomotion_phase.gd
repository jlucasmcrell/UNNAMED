extends SceneTree
func _initialize(): call_deferred("run")
func find_rig(node: Node) -> Skeleton3D:
 if node is Skeleton3D:return node
 for child in node.get_children():
  var found= find_rig(child)
  if found:return found
 return null
func run():
 var folder=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/")+"/"
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(folder+"runtime/catalog.json"))
 var rows=[]
 for id in catalog.characters:
  var config: Dictionary=catalog.characters[id]
  var document=GLTFDocument.new();var state=GLTFState.new()
  assert(document.append_from_file(folder+"runtime/"+config.body,state)==OK)
  var model=document.generate_scene(state);root.add_child(model)
  var rig=find_rig(model)
  var player=AnimationPlayer.new();rig.add_child(player);player.root_node=NodePath("..")
  player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
  player.add_animation_library("",load(folder+"runtime/"+config.animation_library))
  var phases={}
  for clip in ["walk","run","strafe_left","strafe_right","walk_back"]:
   var direction=Vector3(1,0,0) if clip=="strafe_left" else Vector3(-1,0,0) if clip=="strafe_right" else Vector3(0,0,-1) if clip=="walk_back" else Vector3(0,0,1)
   var best=-INF;var phase=0.0;var samples=[];var floor_=INF
   for frame in 120:
    player.play(clip,0);player.seek(frame/120.0*player.get_animation(clip).length,true);player.advance(0)
    var point=rig.get_bone_global_pose(rig.find_bone("foot_l")).origin
    samples.append(point);floor_=minf(floor_,point.y)
   for frame in 120:
    var point:Vector3=samples[frame]
    if point.y>floor_+.06:continue
    var reach=point.dot(direction)
    if reach>best:best=reach;phase=frame/120.0
   phases[clip]=phase
  config.animation_phases=phases
  rows.append({"id":id,"phases":phases,"method":"left-foot forward contact; native animation sampling, no key or skeleton edits"})
  model.free()
 var file=FileAccess.open(folder+"runtime/catalog.json",FileAccess.WRITE);file.store_string(JSON.stringify(catalog,"  "));file.close()
 file=FileAccess.open(folder+"evidence/locomotion_phase.json",FileAccess.WRITE);file.store_string(JSON.stringify(rows,"  "));file.close()
 quit()
