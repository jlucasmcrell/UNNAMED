extends SceneTree
## Pose samples from the exact compiled library, without a renderer or retarget.
func find_rig(node: Node) -> Skeleton3D:
 if node is Skeleton3D:return node
 for child in node.get_children():
  var found=find_rig(child)
  if found!=null:return found
 return null

func matrix(t: Transform3D) -> Array:
 return [[t.basis.x.x,t.basis.y.x,t.basis.z.x,t.origin.x],
 [t.basis.x.y,t.basis.y.y,t.basis.z.y,t.origin.y],
 [t.basis.x.z,t.basis.y.z,t.basis.z.z,t.origin.z],[0,0,0,1]]

func _initialize():call_deferred("sample")
func sample():
 var args=OS.get_cmdline_user_args()
 var manifest: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(args[0]))
 var document=GLTFDocument.new()
 var state=GLTFState.new()
 assert(document.append_from_file(manifest.body,state)==OK)
 var body=document.generate_scene(state)
 root.add_child(body)
 var rig=find_rig(body)
 assert(rig!=null and rig.get_bone_count()==59)
 rig.name="Skeleton3D"
 var player=AnimationPlayer.new()
 rig.add_child(player)
 player.root_node=NodePath("..")
 player.deterministic=true
 player.callback_mode_process=AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
 var library: AnimationLibrary=load(manifest.library)
 assert(library.get_meta("target_body_sha256")==FileAccess.get_sha256(manifest.body))
 player.add_animation_library("",library)
 var result={"body":manifest.body,"body_sha256":FileAccess.get_sha256(manifest.body),
 "library_sha256":FileAccess.get_sha256(manifest.library),"rest":{},"samples":[]}
 for bone in rig.get_bone_count():result.rest[rig.get_bone_name(bone)]=matrix(rig.global_transform*rig.get_bone_global_rest(bone))
 for clip in manifest.states:
  assert(library.has_animation(clip))
  for fraction in [.1,.35,.6,.9]:
   rig.reset_bone_poses()
   player.play(clip,0)
   player.seek(library.get_animation(clip).length*fraction,true)
   player.advance(0)
   rig.force_update_all_bone_transforms()
   var poses={}
   for bone in rig.get_bone_count():poses[rig.get_bone_name(bone)]=matrix(rig.global_transform*rig.get_bone_global_pose(bone))
   result.samples.append({"clip":clip,"fraction":fraction,"bones":poses})
 var file=FileAccess.open(manifest.output,FileAccess.WRITE)
 file.store_string(JSON.stringify(result));file.close()
 body.free()
 print("COMPILED_CONTROL_POSES_PASS ",result.samples.size())
 quit()
