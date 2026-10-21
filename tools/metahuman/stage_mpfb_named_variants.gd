extends SceneTree
func _initialize():call_deferred("stage")
func find_type(node:Node,type:String)->Node:
 if node.is_class(type):return node
 for child in node.get_children():
  var found=find_type(child,type)
  if found:return found
 return null
func glb(path:String)->Node:
 var doc=GLTFDocument.new();var state=GLTFState.new();assert(doc.append_from_file(path,state)==OK,path);return doc.generate_scene(state)
func stage():
 var root_path=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/")
 var manifest=JSON.parse_string(FileAccess.get_file_as_string(root_path+"/mpfb_named/build.json"));assert(manifest.status=="DONE")
 var report=[]
 for row in manifest.characters:
  var base_path="D:/Otherreach_Scratch/rcd3_20261020/shared_fallback/rigged/"+row.base+"/"+row.base+"_rigged.glb"
  var base=glb(base_path);root.add_child(base);var expected:Skeleton3D=find_type(base,"Skeleton3D")
  var actor=glb(row.path);root.add_child(actor);var actual:Skeleton3D=find_type(actor,"Skeleton3D")
  assert(expected.get_bone_count()==53 and actual.get_bone_count()==53)
  var worst=0.0
  for b in expected.get_bone_count():
   var name_=expected.get_bone_name(b);var other=actual.find_bone(name_);assert(other>=0)
   var parent=expected.get_bone_parent(b);var other_parent=actual.get_bone_parent(other)
   assert((parent<0 and other_parent<0) or (parent>=0 and other_parent>=0 and expected.get_bone_name(parent)==actual.get_bone_name(other_parent)))
   var wanted=expected.get_bone_rest(b);var got=actual.get_bone_rest(other)
   worst=maxf(worst,wanted.origin.distance_to(got.origin));assert(wanted.is_equal_approx(got),row.id+" changed rest "+name_)
  var source="C:/Otherreach_Build/playtest_mh_rcd1_owner_wt/src/Presentation/Art/MPFBMotion/"+row.base+"_motion.res"
  var library=load(source).duplicate(true) as AnimationLibrary
  assert(library.get_meta("target_body_sha256")==FileAccess.get_sha256(base_path))
  assert(library.get_animation_list().size()==37)
  library.set_meta("base_target_body_sha256",library.get_meta("target_body_sha256"))
  library.set_meta("target_body_sha256",FileAccess.get_sha256(row.path))
  library.set_meta("variant_base_rig",row.base)
  library.set_meta("variant_rest_verified",true)
  var repo="C:/Otherreach_Build/playtest_mh_rcd1_owner_wt/src/Presentation/Art/MPFBMotion/"+row.id+"_motion.res"
  assert(ResourceSaver.save(library,repo)==OK)
  assert(ResourceSaver.save(library,root_path+"/ue_libraries/"+row.id+"_motion.res")==OK)
  report.append({"id":row.id,"base":row.base,"bones":53,"rest_error_m":worst,"states":37,"body_sha256":FileAccess.get_sha256(row.path),"route":"Original exact Epic target tracks on verified identical MPFB rig; geometry-only MPFB face variants"})
  actor.free();base.free()
 assert(report.size()==10)
 FileAccess.open(root_path+"/evidence/mpfb_named_staging.json",FileAccess.WRITE).store_string(JSON.stringify(report,"  "))
 print("MPFB_NAMED_STAGE_DONE ",report.size());quit()
