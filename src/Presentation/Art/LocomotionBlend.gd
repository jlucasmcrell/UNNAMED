extends Node
## Godot AnimationTree only: plays already-retargeted tracks; never edits bones/rests/binds.
var tree: AnimationTree
var player: AnimationPlayer
var paces: Dictionary
var phases: Dictionary
var active := false
var direction := Vector2(0,1)
var smoothed_direction := Vector2(0,1)
var jog := 0.0
var rate := 1.0
var calm_back := false
var walk_strafes := false

func clip(name_: String) -> AnimationNodeAnimation:
 var node := AnimationNodeAnimation.new()
 node.animation = name_
 node.use_custom_timeline = true
 node.timeline_length = 1.0
 node.start_offset = float(phases.get(name_,0.0))
 node.stretch_time_scale = true
 node.loop_mode = Animation.LOOP_LINEAR
 return node

func configure(source: AnimationPlayer, measured: Dictionary, manual: bool = true, contacts: Dictionary = {}) -> bool:
 for state in ["walk","run","strafe_left","strafe_right","walk_back"]:
  if not source.has_animation(state): return false
 player = source
 paces = measured
 phases = contacts
 var forward := AnimationNodeBlendSpace1D.new()
 forward.sync_mode=1
 forward.min_space=0.0
 forward.max_space=1.0
 forward.add_blend_point(clip("walk"),0.0)
 # The measured jog has a longer stride; scale it for sprint instead of racing the short sprint cycle.
 forward.add_blend_point(clip("run"),1.0)
 walk_strafes=source.has_animation("walk_strafe_left") and source.has_animation("walk_strafe_right")
 var space := AnimationNodeBlendSpace2D.new()
 space.sync_mode=1
 space.add_blend_point(forward, Vector2(0,1), -1, "forward")
 space.add_blend_point(clip("walk_back"), Vector2(0,-1), -1, "back")
 # Native bodies face +Z, their right is -X. Existing libraries retain their original graph.
 for side in ["right","left"]:
  var point=Vector2(-1,0) if side=="right" else Vector2(1,0)
  if walk_strafes:
   var lateral=AnimationNodeBlendSpace1D.new()
   lateral.sync_mode=1;lateral.min_space=0.0;lateral.max_space=1.0
   lateral.add_blend_point(clip("walk_strafe_"+side),0.0)
   lateral.add_blend_point(clip("strafe_"+side),1.0)
   space.add_blend_point(lateral,point,-1,side)
  else:space.add_blend_point(clip("strafe_"+side),point,-1,side)
 var blend := AnimationNodeBlendTree.new()
 blend.add_node("direction",space)
 blend.add_node("pace",AnimationNodeTimeScale.new())
 # Standard filtered AnimationNodeBlend2; environment 0 is the review baseline.
 # Backward KayKit arms/head are stylised; retain the original legs and hip motion.
 calm_back=OS.get_environment("OTHERREACH_CALM_BACK")!="0" and source.has_animation("idle")
 if calm_back:
  var upper:=AnimationNodeBlend2.new()
  upper.filter_enabled=true
  var idle:=source.get_animation("idle")
  var animation_root:=source.get_node(source.root_node)
  var filtered:=0
  for track in idle.get_track_count():
   if idle.track_get_type(track)!=Animation.TYPE_ROTATION_3D:continue
   var path:=idle.track_get_path(track)
   if path.get_subname_count()!=1:continue
   var rig:=animation_root.get_node_or_null(NodePath(path.get_concatenated_names())) as Skeleton3D
   if rig==null:continue
   var bone:=rig.find_bone(path.get_subname(0))
   while bone>=0:
    if rig.get_bone_name(bone) in ["spine_01","spine"]:
     upper.set_filter_path(path,true);filtered+=1;break
    bone=rig.get_bone_parent(bone)
  assert(filtered>=5,"Calm backward candidate found no upper-body tracks")
  blend.add_node("neutral_upper",clip("idle"))
  blend.add_node("calm_upper",upper)
  blend.connect_node("calm_upper",0,"direction")
  blend.connect_node("calm_upper",1,"neutral_upper")
  blend.connect_node("pace",0,"calm_upper")
 else:blend.connect_node("pace",0,"direction")
 blend.connect_node("output",0,"pace")
 tree=AnimationTree.new()
 tree.active=false
 player.get_parent().add_child(tree)
 tree.anim_player=tree.get_path_to(player)
 tree.root_node=tree.get_path_to(player.get_node(player.root_node))
 tree.tree_root=blend
 tree.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL if manual else AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_IDLE
 tree.callback_mode_method=AnimationMixer.ANIMATION_CALLBACK_MODE_METHOD_IMMEDIATE
 return true

func dominant_clip() -> String:
 var forward:=maxf(direction.y,0)
 var weights={"walk":forward*(1-jog),"run":forward*jog,"walk_back":maxf(-direction.y,0),"strafe_left":maxf(direction.x,0),"strafe_right":maxf(-direction.x,0)}
 if walk_strafes:
  for side in ["left","right"]:
   var weight=float(weights["strafe_"+side])
   weights["strafe_"+side]=weight*jog
   weights["walk_strafe_"+side]=weight*(1-jog)
 var winner:="";var largest:=-1.0
 for clip in weights:
  if float(weights[clip])>largest:winner=clip;largest=float(weights[clip])
 return winner

func stride(state: String, fallback: float) -> float:
 return maxf(.1,float(paces.get(state,fallback))*player.get_animation(state).length)

func move(local_velocity: Vector2, delta: float) -> bool:
 if tree==null or local_velocity.length()<.1: return false
 var speed:=local_velocity.length()
 var smoothing:=1.0-exp(-12.0*delta)
 jog=lerpf(jog,smoothstep(1.8,3.2,speed),smoothing)
 var forward_stride:=lerpf(stride("walk",1.4),stride("run",3.2),jog)
 var side_state:="strafe_left" if local_velocity.x>0 else "strafe_right"
 var side_stride:=lerpf(stride("walk_"+side_state,1.4),stride(side_state,2.0),jog) if walk_strafes else stride(side_state,2.0)
 var fore_stride:=forward_stride if local_velocity.y>=0 else stride("walk_back",1.2)
 # Blend displacement vectors, not scalar distances: diagonal stride is their length.
 # Unequal forward/back/side strides need inverse-stride weights to follow the actual travel heading.
 var wanted:=Vector2(local_velocity.x/side_stride,local_velocity.y/fore_stride)
 wanted/=maxf(.001,absf(wanted.x)+absf(wanted.y))
 # Preserve the unnormalised smoothing state. Projecting it back onto the
 # diamond each frame prevents an opposite direction ever crossing zero.
 smoothed_direction=smoothed_direction.lerp(wanted,smoothing) if active else wanted
 direction=smoothed_direction
 direction/=maxf(.001,absf(direction.x)+absf(direction.y))
 var distance:=Vector2(direction.x*side_stride,direction.y*fore_stride).length()
 rate=speed/maxf(.1,distance)
 tree.set("parameters/direction/blend_position",direction)
 tree.set("parameters/direction/forward/blend_position",jog)
 if walk_strafes:
  tree.set("parameters/direction/left/blend_position",jog)
  tree.set("parameters/direction/right/blend_position",jog)
 if calm_back:
  var neutral_amount:=.85*maxf(-direction.y,0)+.30*maxf(direction.y,0)*jog+.20*absf(direction.x)
  tree.set("parameters/calm_upper/blend_amount",neutral_amount)
 tree.set("parameters/pace/scale",rate)
 player.active=false
 tree.active=true
 active=true
 return true

func stop() -> bool:
 if not active:return false
 tree.active=false
 player.active=true
 active=false
 return true

func advance(delta: float) -> void:
 if active:tree.advance(delta)
