extends SceneTree

func _initialize() -> void:
 var args:=OS.get_cmdline_user_args()
 var builder=load(args[0])
 var raw=[{"side":"L","time":.016},{"side":"L","time":.082},{"side":"R","time":.475}]
 var fixed:Array=builder.clean_contacts(raw,.933)
 assert(fixed.size()==2 and fixed[0].side=="L" and fixed[1].side=="R")
 assert(raw.size()==3,"Source measurements must remain available in the audit")
 var seam:Array=builder.clean_contacts([{"side":"L","time":.02},{"side":"R","time":.5},{"side":"L","time":.95}],1.0)
 assert(seam.size()==2 and seam[0].side=="R" and is_equal_approx(seam[1].time,.95))
 var separate:Array=builder.clean_contacts([{"side":"L","time":.1},{"side":"L","time":.4},{"side":"R","time":.7}],1.0)
 assert(separate.size()==3,"Do not force alternation or hide separate measured stances")
 var actor:=Node3D.new()
 root.add_child(actor)
 var player:=AnimationPlayer.new()
 actor.add_child(player)
 var library:=AnimationLibrary.new()
 var paces={"walk":1.6,"run":3.2,"strafe_left":2.0,"strafe_right":2.0,"walk_strafe_left":1.2,"walk_strafe_right":1.2,"walk_back":1.0,"run_back":3.0}
 for state in paces:
  var animation:=Animation.new()
  animation.length=1.0
  library.add_animation(state,animation)
 player.add_animation_library("",library)
 var blend=load(args[1]).new()
 actor.add_child(blend)
 assert(blend.configure(player,paces))
 assert(blend.move(Vector2(0,1.6),10.0))
 var forward:float=blend.rate
 assert(blend.move(Vector2(1,1).normalized()*1.6,10.0))
 assert(is_equal_approx(blend.rate,forward),"Diagonal walk must retain forward walk cadence")
 assert(blend.move(Vector2(1.6,0),10.0))
 assert(is_equal_approx(blend.rate,1.6/1.2),"Measured pure strafe cadence must remain unchanged")
 actor.free()
 print("HUMGEN_CONTACT_AND_DIAGONAL_CADENCE_PASS")
 quit()
