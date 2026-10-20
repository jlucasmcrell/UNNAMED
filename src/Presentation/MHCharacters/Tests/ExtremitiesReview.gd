extends Node3D
const Native = preload("res://MHCharacters/NativeCharacter.gd")
func run(asset_root: String, out: String, camera: Camera3D, feet: Vector3):
 var source: String=asset_root.replace("\\","/").trim_suffix("/")+"/"
 DirAccess.make_dir_recursive_absolute(out)
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(source+"catalog.json"))
 feet+=Vector3.UP*.35 # Staged inspection clears terrain; normal player placement is unchanged.
 var rows:=[]
 for id in ["ada","mateo"]:
  var actor:=Native.new();add_child(actor)
  if not actor.configure(source,catalog.characters[id],{}):get_tree().quit(2);return
  for mesh: MeshInstance3D in actor._all(actor,"MeshInstance3D"):mesh.layers|=1<<19 # Same character fill layer as MetaHumanFigure.
  var model=actor._load_model(source+"review_source/UAL1_Standard.glb",true)
  add_child(model);model.visible=false
  for mesh: MeshInstance3D in actor._all(model,"MeshInstance3D"):mesh.layers|=1<<19
  actor.position=feet;model.position=feet
  var rig: Skeleton3D=actor._all(model,"Skeleton3D")[0]
  var player: AnimationPlayer=actor._all(model,"AnimationPlayer")[0]
  for pose in ["idle","walk"]:
   actor.sample_clip(pose,.5);actor._animator.pause();actor._sync.apply_leader_pose()
   for frame in 2:await RenderingServer.frame_post_draw
   var clip: String=""
   for candidate in player.get_animation_list():
    if candidate==("Idle_Loop" if pose=="idle" else "Walk_Loop"):clip=candidate;break
   assert(not clip.is_empty());player.play(clip);player.seek(.5,true);player.pause()
   var record: Dictionary={"character":id,"clip":pose,"sample_seconds":.5,"source_clip":clip,"native_metrics":actor.native_metrics(),"bones":[]}
   for bone in ["foot_l","ball_l","foot_r","ball_r"]:
    var i:=actor._body.find_bone(bone)
    record.bones.append({"name":bone,"position":str(actor._body.to_global(actor._body.get_bone_global_pose(i).origin)),"basis":str(actor._body.get_bone_global_pose(i).basis)})
   for mode in ["bare","booted","source"]:
    actor.visible=mode!="source";model.visible=mode=="source"
    for mesh: MeshInstance3D in actor._all(actor._body,"MeshInstance3D"):
     if mesh not in actor._body_meshes:mesh.visible=mode=="booted"
    for mesh: MeshInstance3D in actor._body_meshes:
     for surface in mesh.mesh.get_surface_count():
      var material=mesh.get_surface_override_material(surface)
      if material is ShaderMaterial:material.set_shader_parameter("use_hidden_face_map",mode!="bare")
    var aim: Vector3=actor.bone_position("foot_l").lerp(actor.bone_position("foot_r"),.5)-Vector3.UP*.04
    if mode=="source":aim=rig.to_global(rig.get_bone_global_pose(rig.find_bone("foot_l")).origin.lerp(rig.get_bone_global_pose(rig.find_bone("foot_r")).origin,.5))-Vector3.UP*.04
    for angle in [0,90,45]:
     camera.global_position=aim+Vector3(sin(deg_to_rad(angle))*.85,.16,cos(deg_to_rad(angle))*.85);camera.look_at(aim)
     for frame in 5:await RenderingServer.frame_post_draw
     get_viewport().get_texture().get_image().save_png(out+"/"+id+"_"+pose+"_"+mode+"_"+str(angle)+".png")
   actor.visible=true;model.visible=false
   for mesh: MeshInstance3D in actor._all(actor._body,"MeshInstance3D"):
    if mesh not in actor._body_meshes:mesh.visible=true
   for hand in ["hand_l","hand_r"]:
    var aim: Vector3=actor.bone_position(hand)+Vector3.UP*.045
    camera.global_position=aim+Vector3(.45 if hand=="hand_l" else -.45,.025,.08);camera.look_at(aim)
    for frame in 5:await RenderingServer.frame_post_draw
    get_viewport().get_texture().get_image().save_png(out+"/"+id+"_"+pose+"_"+hand+"_side.png")
   rows.append(record)
  model.queue_free();actor.queue_free();await get_tree().process_frame
 var file:=FileAccess.open(out+"/complete.json",FileAccess.WRITE)
 file.store_string(JSON.stringify({"status":"DIAGNOSTIC_NOT_ACCEPTED","executable":OS.get_executable_path(),"native_body_buffers_modified":false,"world":"Normal packaged game world and lighting; standard character fill0.24; diagnostic cameras; figures raised35cm to clear terrain; foliage hidden; no performance measurement","rows":rows},"  "));file.close()
 get_tree().quit()
