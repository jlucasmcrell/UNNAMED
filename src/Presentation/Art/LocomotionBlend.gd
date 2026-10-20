extends Node
## Godot AnimationTree only: plays already-retargeted tracks; never edits bones/rests/binds.
var tree: AnimationTree
var player: AnimationPlayer
var paces: Dictionary
var phases: Dictionary
var active := false
var direction := Vector2(0,1)
var jog := 0.0
var rate := 1.0

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
 var space := AnimationNodeBlendSpace2D.new()
 space.sync_mode=1
 space.add_blend_point(forward, Vector2(0,1), -1, "forward")
 space.add_blend_point(clip("walk_back"), Vector2(0,-1), -1, "back")
 space.add_blend_point(clip("strafe_right"), Vector2(-1,0), -1, "right") # Native bodies face +Z, their right is -X.
 space.add_blend_point(clip("strafe_left"), Vector2(1,0), -1, "left")
 var blend := AnimationNodeBlendTree.new()
 blend.add_node("direction",space)
 blend.add_node("pace",AnimationNodeTimeScale.new())
 blend.connect_node("pace",0,"direction")
 blend.connect_node("output",0,"pace")
 tree=AnimationTree.new()
 tree.active=false
 player.get_parent().add_child(tree)
 tree.anim_player=tree.get_path_to(player)
 tree.root_node=tree.get_path_to(player.get_node(player.root_node))
 tree.tree_root=blend
 tree.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL if manual else AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_IDLE
 return true

func stride(state: String, fallback: float) -> float:
 return maxf(.1,float(paces.get(state,fallback))*player.get_animation(state).length)

func move(local_velocity: Vector2, delta: float) -> bool:
 if tree==null or local_velocity.length()<.1: return false
 var speed:=local_velocity.length()
 var smoothing:=1.0-exp(-12.0*delta)
 jog=lerpf(jog,smoothstep(1.8,3.2,speed),smoothing)
 var forward_stride:=lerpf(stride("walk",1.4),stride("run",3.2),jog)
 var side_state:="strafe_left" if local_velocity.x>0 else "strafe_right"
 var side_stride:=stride(side_state,2.0)
 var fore_stride:=forward_stride if local_velocity.y>=0 else stride("walk_back",1.2)
 # Blend displacement vectors, not scalar distances: diagonal stride is their length.
 # Unequal forward/back/side strides need inverse-stride weights to follow the actual travel heading.
 var wanted:=Vector2(local_velocity.x/side_stride,local_velocity.y/fore_stride)
 wanted/=maxf(.001,absf(wanted.x)+absf(wanted.y))
 direction=direction.lerp(wanted,smoothing) if active else wanted
 direction/=maxf(.001,absf(direction.x)+absf(direction.y))
 var distance:=Vector2(direction.x*side_stride,direction.y*fore_stride).length()
 rate=speed/maxf(.1,distance)
 tree.set("parameters/direction/blend_position",direction)
 tree.set("parameters/direction/forward/blend_position",jog)
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
