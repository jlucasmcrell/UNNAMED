extends SceneTree
## Original offline release reaction, using native idle and owned recorded upper-body recoil.
func _initialize():call_deferred("build")
func sample(a: Animation,t: int,at: float):
 match a.track_get_type(t):
  Animation.TYPE_POSITION_3D:return a.position_track_interpolate(t,at)
  Animation.TYPE_ROTATION_3D:return a.rotation_track_interpolate(t,at)
  Animation.TYPE_SCALE_3D:return a.scale_track_interpolate(t,at)
 assert(false)
 return null
func build():
 var args=OS.get_cmdline_user_args()
 assert(args[0].simplify_path()!=args[1].simplify_path())
 var library: AnimationLibrary=load(args[0])
 assert(not library.has_animation("weapon_drop"))
 var idle=library.get_animation("idle")
 var recorded=library.get_animation("stumble")
 var tracks={}
 for t in recorded.get_track_count():
  if recorded.track_get_type(t)==Animation.TYPE_ROTATION_3D:tracks[str(recorded.track_get_path(t).get_subname(0))]=t
 var result: Animation=idle.duplicate(true)
 result.length=.4;result.loop_mode=Animation.LOOP_NONE
 var selected=["UpperChest","Neck","Head","RightShoulder","RightUpperArm","RightLowerArm","RightHand"]
 var maximum=0.0
 for t in result.get_track_count():
  var bone=str(result.track_get_path(t).get_subname(0))
  for key in range(result.track_get_key_count(t)-1,-1,-1):result.track_remove_key(t,key)
  for frame in 13:
   var at=frame/30.0
   var pose=sample(idle,t,at)
   if idle.track_get_type(t)==Animation.TYPE_ROTATION_3D and bone in selected and tracks.has(bone):
    var index=int(tracks[bone])
    var delta=(recorded.rotation_track_interpolate(index,0).inverse()*recorded.rotation_track_interpolate(index,at)).normalized()
    var angle=Quaternion.IDENTITY.angle_to(delta)
    var weight=sin(PI*at/.4)*minf(1,deg_to_rad(12)/maxf(angle,.000001))
    var recoil=Quaternion.IDENTITY.slerp(delta,weight)
    maximum=maxf(maximum,rad_to_deg(Quaternion.IDENTITY.angle_to(recoil)))
    pose=(pose*recoil).normalized()
   result.track_insert_key(t,at,pose)
 library.add_animation("weapon_drop",result)
 var sources: Dictionary=library.get_meta("animation_sources").duplicate(true)
 sources["weapon_drop"]="offline native idle + bounded right-arm recoil from "+str(sources["stumble"])+"; actual hand opens at the inventory event"
 library.set_meta("animation_sources",sources)
 library.set_meta("weapon_release_edit",{"seconds":.4,"max_delta_degrees":maximum,"bones":selected,"input_sha256":FileAccess.get_sha256(args[0]),"lower_body":"unchanged native idle"})
 var pending=args[1]+".pending.res"
 assert(ResourceSaver.save(library,pending)==OK)
 assert(DirAccess.rename_absolute(pending,args[1])==OK)
 print("WEAPON_RELEASE_CANDIDATE max_delta_degrees=",maximum)
 quit()
