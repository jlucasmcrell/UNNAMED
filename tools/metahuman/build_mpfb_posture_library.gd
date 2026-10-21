extends SceneTree
## Package already UE-retargeted target-rig tracks. No Godot retargeting or rest conversion.
func _initialize(): call_deferred("build")
func find_type(node: Node,type: String) -> Node:
 if node.is_class(type): return node
 for child in node.get_children():
  var found=find_type(child,type)
  if found: return found
 return null
func glb(path: String) -> Node:
 var document=GLTFDocument.new();var state=GLTFState.new()
 assert(document.append_from_file(path,state)==OK,path)
 return document.generate_scene(state)
func build():
 var folder="D:/Otherreach_Scratch/rcd5_20261021"
 var manifest=JSON.parse_string(FileAccess.get_file_as_string(folder+"/evidence/ue_mpfb_posture.json"))
 assert(manifest.status=="DONE")
 DirAccess.make_dir_recursive_absolute(folder+"/ue_libraries")
 var rows=[]
 for character in manifest.characters:
  var id=character.id
  var target=glb("D:/Otherreach_Scratch/rcd3_20261020/shared_fallback/rigged/"+id+"/"+id+"_rigged.glb");root.add_child(target)
  var target_rig:Skeleton3D=find_type(target,"Skeleton3D")
  var library=AnimationLibrary.new()
  for clip in character.clips:
   var source=glb(clip.path);root.add_child(source)
   var rig:Skeleton3D=find_type(source,"Skeleton3D")
   assert(rig.get_bone_count()==target_rig.get_bone_count(),"Bone count mismatch")
   var worst=0.0
   for b in target_rig.get_bone_count():
    var name=target_rig.get_bone_name(b);var s=rig.find_bone(name)
    assert(s>=0,"Missing target bone: "+name)
    var expected=target_rig.get_bone_rest(b);var actual=rig.get_bone_rest(s)
    worst=maxf(worst,expected.origin.distance_to(actual.origin))
    if not expected.is_equal_approx(actual):
     push_error("MPFB UE rest mismatch "+id+" "+name+" expected="+str(expected)+" actual="+str(actual));quit(4);return
   var player:AnimationPlayer=find_type(source,"AnimationPlayer")
   var names=Array(player.get_animation_list()).filter(func(n):return n!="RESET")
   assert(names.size()==1)
   var animation=player.get_animation(names[0]).duplicate(true) as Animation
   var types={}
   for t in animation.get_track_count():
    var path=animation.track_get_path(t)
    assert(path.get_subname_count()==1)
    var bone=path.get_subname(0)
    assert(target_rig.find_bone(bone)>=0)
    animation.track_set_path(t,NodePath("Skeleton3D:"+bone))
    types[str(bone)+":"+str(animation.track_get_type(t))]=true
    if bone=="Root" and animation.track_get_type(t)==Animation.TYPE_POSITION_3D:
     var origin=target_rig.get_bone_rest(target_rig.find_bone(bone)).origin
     for k in animation.track_get_key_count(t):
      var position:Vector3=animation.track_get_key_value(t,k);position.x=origin.x;position.z=origin.z
      animation.track_set_key_value(t,k,position)
   # Explicit rest holds for omitted native tracks prevent the preceding clip bleeding into a crouch.
   for b in target_rig.get_bone_count():
    var bone=target_rig.get_bone_name(b);var rest=target_rig.get_bone_rest(b)
    for kind in [Animation.TYPE_POSITION_3D,Animation.TYPE_ROTATION_3D]:
     if types.has(str(bone)+":"+str(kind)):continue
     var t=animation.add_track(kind);animation.track_set_path(t,NodePath("Skeleton3D:"+bone))
     if kind==Animation.TYPE_POSITION_3D:animation.position_track_insert_key(t,0,rest.origin)
     else:animation.rotation_track_insert_key(t,0,rest.basis.get_rotation_quaternion())
   animation.loop_mode=Animation.LOOP_LINEAR
   library.add_animation(clip.clip,animation)
   rows.append({"id":id,"clip":clip.clip,"bones":rig.get_bone_count(),"rest_error_m":worst,"tracks":animation.get_track_count()})
   source.free()
  library.set_meta("route","Epic IK Retargeter -> native GLTFExporter -> direct target track copy; no Godot retarget")
  assert(ResourceSaver.save(library,folder+"/ue_libraries/"+id+"_posture.res")==OK)
  target.free()
 FileAccess.open(folder+"/evidence/ue_mpfb_libraries.json",FileAccess.WRITE).store_string(JSON.stringify(rows,"  "))
 print("UE_MPFB_POSTURE_LIBRARIES_DONE ",rows)
 quit()
