extends SceneTree
## Fast one-factor material controls. Diagnostic studio only, followed by normal-game proof.
const Native = preload("../NativeCharacter.gd")
func _initialize() -> void:call_deferred("run")
func run() -> void:
 assert(DisplayServer.get_name()!="headless")
 root.size=Vector2i(1280,960)
 var source := OS.get_environment("OTHERREACH_MH_CHARACTERS_ROOT").replace("\\","/").trim_suffix("/")+"/"
 var out := OS.get_environment("OTHERREACH_MH_REVIEW_OUT")
 DirAccess.make_dir_recursive_absolute(out)
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(source+"catalog.json"))
 var environment := WorldEnvironment.new();environment.environment=Environment.new()
 environment.environment.background_mode=Environment.BG_COLOR;environment.environment.background_color=Color(.2,.24,.3)
 environment.environment.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR
 environment.environment.ambient_light_color=Color(.7,.8,1);environment.environment.ambient_light_energy=.5
 root.add_child(environment)
 var sun := DirectionalLight3D.new();sun.rotation_degrees=Vector3(-51,-3,0);sun.light_energy=1.0;sun.shadow_enabled=true;root.add_child(sun)
 var camera := Camera3D.new();camera.fov=75;root.add_child(camera);camera.current=true
 var rows := []
 for id in ["ada","mateo"]:
  var actor := Native.new();root.add_child(actor);assert(actor.configure(source,catalog.characters[id],{}))
  actor.sample_clip("crouch_idle",.5);actor._animator.pause()
  var aim: Vector3=actor.bone_position("pelvis")-Vector3.UP*.10
  camera.position=aim+Vector3(0,.02,-.85);camera.look_at(aim)
  for arm in ["baseline","vest_normal_off","vest_specular_off","vest_roughness_1","vest_flat_albedo","vest_cull_front"]:
   var originals := []
   for mesh: MeshInstance3D in actor._all(actor,"MeshInstance3D"):
    if mesh.get_meta("mh_garment_piece","")!="chest.glb":continue
    for surface in mesh.mesh.get_surface_count():
     if not "vest" in mesh.mesh.surface_get_material(surface).resource_name.to_lower():continue
     var material: StandardMaterial3D=mesh.get_active_material(surface).duplicate()
     originals.append([mesh,surface,mesh.get_surface_override_material(surface)])
     match arm:
      "vest_normal_off":material.normal_enabled=false
      "vest_specular_off":material.metallic_specular=0
      "vest_roughness_1":material.roughness=1
      "vest_flat_albedo":material.albedo_texture=null;material.albedo_color=Color(.2,.1,.04)
      "vest_cull_front":material.cull_mode=BaseMaterial3D.CULL_FRONT
     mesh.set_surface_override_material(surface,material)
   for frame in 8:await RenderingServer.frame_post_draw
   root.get_texture().get_image().save_png(out+"/"+id+"_"+arm+".png")
   for original in originals:original[0].set_surface_override_material(original[1],original[2])
   rows.append({"id":id,"arm":arm})
  actor.queue_free();await process_frame
 var file := FileAccess.open(out+"/complete.json",FileAccess.WRITE);file.store_string(JSON.stringify({"status":"MATERIAL_DIAGNOSIS_ONLY","rows":rows},"  "));file.close();quit()
