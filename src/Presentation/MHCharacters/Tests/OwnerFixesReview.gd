extends Node
## Diagnostic close-ups using actual MetaHumanFigure + HeldWeapon in the normal world.
func shot(camera: Camera3D, aim: Vector3, offset: Vector3, out: String, name_: String) -> void:
 camera.global_position=aim+offset;camera.look_at(aim)
 for frame in 5:await RenderingServer.frame_post_draw
 get_viewport().get_texture().get_image().save_png(out+"/"+name_+".png")
func run(figures: Array, camera: Camera3D, out: String) -> void:
 var rows := []
 for figure in figures:
  figure.visible=true
  var actor: Node3D=figure.get_node("Native")
  var rig: Skeleton3D=actor.body_skeleton()
  var poses: Array=["sword_ready","walk","run","crouch_idle","sword_attack_1"]
  if OS.get_environment("OTHERREACH_MH_FOCUS_BACK")=="1":poses=["sword_ready","crouch_idle"]
  for pose in poses:
   assert(actor.sample_clip(pose,.5));actor._animator.pause()
   actor.set_weapon_grip(true)
   for frame in 15:await RenderingServer.frame_post_draw
   var record := {"id":figure.CharacterId,"pose":pose,"sample_seconds":.5,"native_metrics":actor.native_metrics(),"grip_influence":actor._weapon_grip.influence,"garments":[]}
   for mesh: MeshInstance3D in actor._all(actor,"MeshInstance3D"):
    if mesh.has_meta("mh_garment_piece"):record.garments.append({"name":str(mesh.name),"piece":mesh.get_meta("mh_garment_piece"),"surfaces":mesh.mesh.get_surface_count()})
   var aim: Vector3=actor.bone_position("pelvis")-Vector3.UP*.10
   for angle in [0,90,180,270]:
    await shot(camera,aim,Vector3(sin(deg_to_rad(angle))*.85,.02,cos(deg_to_rad(angle))*.85),out,figure.CharacterId+"_"+pose+"_hip_"+str(angle))
   # One piece hidden at a time; source geometry stays read-only.
   for mode in ["body_off","trousers_off","chest_off","hood_off"]:
    var hidden := []
    for mesh: MeshInstance3D in actor._all(actor,"MeshInstance3D"):
     var piece: String=mesh.get_meta("mh_garment_piece","")
     if (mode=="body_off" and mesh in actor._body_meshes) or (mode=="trousers_off" and piece==actor._config.get("trousers_path","pants.glb")) or (mode=="chest_off" and piece=="chest.glb") or (mode=="hood_off" and piece=="hood.glb"):
      hidden.append(mesh);mesh.visible=false
    assert(not hidden.is_empty(),"Isolation did not hide any mesh: "+mode)
    for angle in [0,180]:
     await shot(camera,aim,Vector3(sin(deg_to_rad(angle))*.85,.02,cos(deg_to_rad(angle))*.85),out,figure.CharacterId+"_"+pose+"_"+mode+"_"+str(angle))
    for mesh in hidden:mesh.visible=true
   var originals := []
   for mesh: MeshInstance3D in actor._all(actor,"MeshInstance3D"):
    var piece: String=mesh.get_meta("mh_garment_piece","")
    if piece.is_empty() and not mesh in actor._body_meshes:continue
    for surface in mesh.mesh.get_surface_count():
     originals.append([mesh,surface,mesh.get_surface_override_material(surface)])
     var material := StandardMaterial3D.new();material.shading_mode=BaseMaterial3D.SHADING_MODE_UNSHADED
     material.albedo_color=Color.MAGENTA if mesh in actor._body_meshes else (Color.GREEN if piece=="chest.glb" and surface==0 else Color.BLUE if piece=="chest.glb" else Color.ORANGE if piece==actor._config.get("trousers_path","") else Color.CYAN if piece=="hood.glb" else Color.BROWN)
     mesh.set_surface_override_material(surface,material)
   await shot(camera,aim,Vector3(0,.02,-.85),out,figure.CharacterId+"_"+pose+"_colour_ids_back")
   for original in originals:original[0].set_surface_override_material(original[1],original[2])
   if pose=="sword_ready":
    for grip in [false,true]:
     actor.set_weapon_grip(grip)
     for frame in 15:await RenderingServer.frame_post_draw
     for side in ["l","r"]:
      var hand: Vector3=actor.bone_position("hand_"+side)
      for angle in [0,90,180,270]:
       await shot(camera,hand,Vector3(sin(deg_to_rad(angle))*.32,.10,cos(deg_to_rad(angle))*.32),out,figure.CharacterId+"_hand_"+side+("_grip" if grip else "_open")+"_"+str(angle))
    actor.set_weapon_grip(true)
   rows.append(record)
  figure.visible=false
 var file := FileAccess.open(out+"/owner_fixes.json",FileAccess.WRITE)
 file.store_string(JSON.stringify({"status":"DIAGNOSTIC_REQUIRES_REVIEW","rows":rows,"notes":"Actual game figures, actual sword socket, authored UE right grip. Normal world light/fill .24; staged figures +.35m and scatter hidden only for close-up. Piece-isolation controls, no native buffer changes."},"  "));file.close()
 get_tree().quit()
