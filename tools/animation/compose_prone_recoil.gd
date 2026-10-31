extends SceneTree
## Offline animation edit: recorded ground-recoil deltas on the native prone base.
## No body rest, bind, mesh, material, or standing-hit clip is changed.
func _initialize():call_deferred("build")

func build():
 var args=OS.get_cmdline_user_args()
 var library: AnimationLibrary=load(args[0])
 var base=library.get_animation("prone_idle")
 var ground=library.get_animation("prone_hit")
 var result: Animation=base.duplicate(true)
 result.length=.5
 result.loop_mode=Animation.LOOP_NONE
 var selected=["Spine","Chest","UpperChest","Neck","Head","LeftShoulder","RightShoulder","LeftUpperArm","RightUpperArm","LeftLowerArm","RightLowerArm","LeftHand","RightHand"]
 var source_tracks={}
 for track in ground.get_track_count():source_tracks[str(ground.track_get_path(track))]=track
 var maximum=0.0
 for track in result.get_track_count():
  var kind=base.track_get_type(track)
  var bone=base.track_get_path(track).get_subname(0)
  for key in range(result.track_get_key_count(track)-1,-1,-1):result.track_remove_key(track,key)
  for frame in 16:
   var at=frame/30.0
   var value: Variant
   match kind:
    Animation.TYPE_POSITION_3D:value=base.position_track_interpolate(track,at)
    Animation.TYPE_SCALE_3D:value=base.scale_track_interpolate(track,at)
    Animation.TYPE_ROTATION_3D:
     value=base.rotation_track_interpolate(track,at)
     var path=str(base.track_get_path(track))
     if bone in selected and source_tracks.has(path):
      var source_track=int(source_tracks[path])
      var first=ground.rotation_track_interpolate(source_track,0)
      var actual=ground.rotation_track_interpolate(source_track,at)
      var delta=(first.inverse()*actual).normalized()
      # Native recorded differences, bounded for an additive flinch. Smooth zero
      # at both ends keeps the prone base continuous through the reaction.
      var angle=Quaternion.IDENTITY.angle_to(delta)
      var weight=sin(PI*at/.5)*minf(1.0,deg_to_rad(20)/maxf(angle,.000001))
      var recoil=Quaternion.IDENTITY.slerp(delta,weight)
      maximum=maxf(maximum,rad_to_deg(Quaternion.IDENTITY.angle_to(recoil)))
      value=(value*recoil).normalized()
    _:assert(false,"Unsupported skeletal track")
   result.track_insert_key(track,at,value)
 library.remove_animation("prone_hit")
 library.add_animation("prone_hit",result)
 var sources: Dictionary=library.get_meta("animation_sources")
 var recorded=sources["prone_hit"]
 sources["prone_hit"]="offline additive edit: "+str(sources["prone_idle"])+" + ground recoil delta from "+str(recorded)
 library.set_meta("animation_sources",sources)
 library.set_meta("prone_recoil_edit",{"base":"prone_idle","recorded_ground":"prone_hit crop 4.25-4.75s","seconds":.5,"max_delta_degrees":maximum,"bones":selected,"root_and_lower_body":"unchanged prone base","input_sha256":FileAccess.get_sha256(args[0])})
 assert(ResourceSaver.save(library,args[1])==OK)
 print("PRONE_RECOIL_EDIT_CANDIDATE max_delta_degrees=",maximum)
 quit()
