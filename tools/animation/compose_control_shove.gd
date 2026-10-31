extends SceneTree
## Traditional offline pose edit on exact-target recorded clips; no rig or art changes.
func _initialize():call_deferred("build")

func value(a: Animation,track: int,at: float):
 match a.track_get_type(track):
  Animation.TYPE_POSITION_3D:return a.position_track_interpolate(track,at)
  Animation.TYPE_ROTATION_3D:return a.rotation_track_interpolate(track,at)
  Animation.TYPE_SCALE_3D:return a.scale_track_interpolate(track,at)
 assert(false,"Unsupported non-skeletal channel")
 return null

func skeleton(node: Node) -> Skeleton3D:
 if node is Skeleton3D:return node
 for child in node.get_children():
  var found=skeleton(child)
  if found:return found
 return null

func rest_value(rig: Skeleton3D, source: Animation, track: int):
 var bone=rig.find_bone(source.track_get_path(track).get_subname(0))
 assert(bone>=0,"Missing native target bone")
 var rest=rig.get_bone_rest(bone)
 match source.track_get_type(track):
  Animation.TYPE_POSITION_3D:return rest.origin
  Animation.TYPE_ROTATION_3D:return rest.basis.get_rotation_quaternion()
  Animation.TYPE_SCALE_3D:return rest.basis.get_scale()
 assert(false)
 return null

func build():
 var args=OS.get_cmdline_user_args()
 assert(args[0].simplify_path()!=args[1].simplify_path())
 var document=GLTFDocument.new()
 var state=GLTFState.new()
 assert(document.append_from_file(args[2],state)==OK)
 var target=document.generate_scene(state)
 var rig=skeleton(target)
 assert(rig and FileAccess.get_sha256(args[2])==load(args[0]).get_meta("target_body_sha256"))
 var library: AnimationLibrary=load(args[0])
 var idle=library.get_animation("idle")
 var source=library.get_animation("shove")
 var paths={}
 for t in idle.get_track_count():paths[str(idle.track_get_path(t).get_subname(0))+"|"+str(idle.track_get_type(t))]=t
 var result: Animation=source.duplicate(true)
 result.length=.6;result.loop_mode=Animation.LOOP_NONE
 for t in result.get_track_count():

  for k in range(result.track_get_key_count(t)-1,-1,-1):result.track_remove_key(t,k)
  for frame in 19:
   var at=frame/30.0
   var p=at/.6
   var weight=smoothstep(0,.45,p) if p<.45 else 1.0-smoothstep(.7,1,p)
   var key=str(source.track_get_path(t).get_subname(0))+"|"+str(source.track_get_type(t))
   var base=value(idle,paths[key],minf(at,idle.length)) if paths.has(key) else rest_value(rig,source,t)
   var push=value(source,t,lerpf(.15,.7,p)*source.length)
   var pose: Variant
   if source.track_get_type(t)==Animation.TYPE_ROTATION_3D:pose=base.slerp(push,weight).normalized()
   else:pose=base.lerp(push,weight)
   result.track_insert_key(t,at,pose)
 library.remove_animation("shove");library.add_animation("shove",result)
 var sources: Dictionary=library.get_meta("animation_sources").duplicate(true)
 var original=sources["shove"]
 sources["shove"]="offline native idle approach/recovery around recorded push: "+str(original)
 library.set_meta("animation_sources",sources)
 var phases: Dictionary=library.get_meta("action_phases",{}).duplicate(true)
 phases["shove"]=[.45,.7,1.0];library.set_meta("action_phases",phases)
 library.set_meta("cmb16_shove_edit",{"seconds":.6,"source":original,"native_idle":"idle","edit":"Smooth native-pose approach, recorded push, native-pose recovery. No rest/mesh/bind changes.","input_sha256":FileAccess.get_sha256(args[0])})
 var pending=args[1]+".pending.res"
 assert(ResourceSaver.save(library,pending)==OK)
 assert(DirAccess.rename_absolute(pending,args[1])==OK)
 target.free()
 print("CMB16_SHOVE_EDIT_CANDIDATE ",args[1])
 quit()
