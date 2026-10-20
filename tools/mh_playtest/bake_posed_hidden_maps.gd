extends SceneTree
## Offline Hidden Face Map bake using documented Godot skin-pose readback and TriangleMesh queries.
## Temporary clothing readbacks are never assigned to the original meshes. Body buffers remain read-only.
const Native = preload("res://MHCharacters/NativeCharacter.gd")
func _initialize() -> void: call_deferred("bake")
func bake() -> void:
 if DisplayServer.get_name()=="headless":push_error("Posed clothing masks require a real renderer");quit(2);return
 var source := OS.get_environment("OTHERREACH_MH_CHARACTERS_ROOT").replace("\\","/").trim_suffix("/")+"/"
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(source+"catalog.json"))
 var shared := {};var records := []
 for id in catalog.characters:
  var actor := Native.new();root.add_child(actor)
  if not actor.configure(source,catalog.characters[id],shared):quit(3);return
  await process_frame
  await process_frame
  await RenderingServer.frame_post_draw
  var snapshots := []
  for body: MeshInstance3D in actor._body_meshes:
   var surfaces := []
   for surface in body.mesh.get_surface_count():surfaces.append(RenderingServer.mesh_get_surface(body.mesh.get_rid(),surface))
   snapshots.append({"mesh":body.mesh,"surfaces":surfaces})
  var faces := PackedVector3Array();var cloth_rows := []
  for mesh: MeshInstance3D in actor._all(actor._body,"MeshInstance3D"):
   if mesh in actor._body_meshes:continue
   var posed := mesh.bake_mesh_from_current_skeleton_pose()
   if posed==null or posed.get_surface_count()!=mesh.mesh.get_surface_count():push_error("Native clothing pose readback failed: "+id+" "+str(mesh.name));quit(4);return
   var raw_high := -INF;var posed_high := -INF
   for surface in posed.get_surface_count():
    if mesh.get_meta("hidden_surface_"+str(surface),false):continue
    var arrays := posed.surface_get_arrays(surface)
    var vertices: PackedVector3Array=arrays[Mesh.ARRAY_VERTEX]
    var indices: PackedInt32Array=arrays[Mesh.ARRAY_INDEX]
    for vertex: Vector3 in mesh.mesh.surface_get_arrays(surface)[Mesh.ARRAY_VERTEX]:raw_high=maxf(raw_high,vertex.y)
    for vertex in vertices:posed_high=maxf(posed_high,vertex.y)
    for index in indices:faces.append(actor._body.to_local(mesh.to_global(vertices[index])))
   cloth_rows.append({"mesh":mesh.name,"raw_max_y":raw_high,"native_posed_max_y":posed_high})
  var cloth := TriangleMesh.new()
  if not cloth.create_from_faces(faces):push_error("Native clothing BVH failed: "+id);quit(5);return
  var hidden_uv := [];var total := 0
  for mesh: MeshInstance3D in actor._body_meshes:
   var transform_: Transform3D=actor._body.global_transform.affine_inverse()*mesh.global_transform
   for surface in mesh.mesh.get_surface_count():
    var arrays := mesh.mesh.surface_get_arrays(surface)
    var vertices: PackedVector3Array=arrays[Mesh.ARRAY_VERTEX]
    var normals: PackedVector3Array=arrays[Mesh.ARRAY_NORMAL]
    var uv: PackedVector2Array=arrays[Mesh.ARRAY_TEX_UV]
    var indices: PackedInt32Array=arrays[Mesh.ARRAY_INDEX]
    for offset in range(0,indices.size(),3):
     var a:=indices[offset];var b:=indices[offset+1];var c:=indices[offset+2]
     var point: Vector3=transform_*((vertices[a]+vertices[b]+vertices[c])/3.0)
     var normal: Vector3=(transform_.basis*(normals[a]+normals[b]+normals[c])).normalized()
     total+=1
     if not cloth.intersect_segment(point-normal*.03,point+normal*.12).is_empty():hidden_uv.append([uv[a].x,uv[a].y,uv[b].x,uv[b].y,uv[c].x,uv[c].y])
  for index in actor._body_meshes.size():
   var body: MeshInstance3D=actor._body_meshes[index]
   if body.mesh!=snapshots[index].mesh:push_error("Native body resource replaced during mask bake");quit(6);return
   for surface in body.mesh.get_surface_count():
    var after := RenderingServer.mesh_get_surface(body.mesh.get_rid(),surface)
    for buffer in ["vertex_data","attribute_data","skin_data","index_data","blend_shape_data"]:
     if snapshots[index].surfaces[surface].get(buffer)!=after.get(buffer):push_error("Native body buffer changed during mask bake: "+buffer);quit(7);return
  if hidden_uv.is_empty() or hidden_uv.size()==total:push_error("Empty/full native outfit mask: "+id);quit(8);return
  var path: String=source+catalog.characters[id].hidden_mask+".triangles.json"
  var file:=FileAccess.open(path,FileAccess.WRITE)
  file.store_string(JSON.stringify({"character":id,"triangles":hidden_uv,"method":"native posed clothing readback + bounded normal segments","inner_metres":.03,"outer_metres":.12,"body_untouched":true}));file.close()
  records.append({"id":id,"hidden":hidden_uv.size(),"total":total,"clothing_triangles":faces.size()/3,"clothing":cloth_rows,"body_buffers_byte_identical":true})
  print("MH_POSED_MASK id=%s hidden=%d total=%d native_body_unchanged=true" % [id,hidden_uv.size(),total])
  actor.queue_free()
  await process_frame
 var file:=FileAccess.open(source+"evidence/posed_masks.json",FileAccess.WRITE)
 file.store_string(JSON.stringify({"status":"DONE","characters":records,"renderer":RenderingServer.get_current_rendering_method()},"  "));file.close()
 print("MH_POSED_MASKS_DONE characters=",records.size())
 quit()
