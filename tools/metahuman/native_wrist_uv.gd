extends SceneTree
const Native = preload("res://MHCharacters/NativeCharacter.gd")
func _initialize(): call_deferred("collect")
func collect():
 var source: String=OS.get_environment("OTHERREACH_MH_CHARACTERS_ROOT").replace("\\","/").trim_suffix("/")+"/"
 var job: String=OS.get_environment("OTHERREACH_PIPELINE_ROOT").replace("\\","/").trim_suffix("/")
 if not job.is_absolute_path():push_error("Set an absolute OTHERREACH_PIPELINE_ROOT");quit(2);return
 DirAccess.make_dir_recursive_absolute(job+"/evidence")
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(source+"catalog.json"))
 var rows := [];var shared := {}
 for id in catalog.characters:
  var actor:=Native.new();root.add_child(actor)
  if not actor.configure(source,catalog.characters[id],shared):quit(2);return
  var revealed := []
  var wrists := [actor._body.get_bone_global_rest(actor._body.find_bone("hand_l")).origin,actor._body.get_bone_global_rest(actor._body.find_bone("hand_r")).origin]
  for mesh: MeshInstance3D in actor._body_meshes:
   var hand_binds := {}
   for bind in mesh.skin.get_bind_count():
    var name: String=mesh.skin.get_bind_name(bind)
    if name.is_empty():name=actor._body.get_bone_name(mesh.skin.get_bind_bone(bind))
    for prefix in ["hand_","thumb_","index_","middle_","ring_","pinky_"]:
     if name.begins_with(prefix):hand_binds[bind]=true
   for surface in mesh.mesh.get_surface_count():
    var a:=mesh.mesh.surface_get_arrays(surface)
    var vertices: PackedVector3Array=a[Mesh.ARRAY_VERTEX]
    var bones: PackedInt32Array=a[Mesh.ARRAY_BONES]
    var weights: PackedFloat32Array=a[Mesh.ARRAY_WEIGHTS]
    var uv: PackedVector2Array=a[Mesh.ARRAY_TEX_UV]
    var indices: PackedInt32Array=a[Mesh.ARRAY_INDEX]
    var influences: int=bones.size()/vertices.size()
    for offset in range(0,indices.size(),3):
     var hand_weight:=0.0
     for corner in 3:
      for influence in influences:
       var entry:int=indices[offset+corner]*influences+influence
       if hand_binds.has(bones[entry]):hand_weight+=weights[entry]/3.0
     var centre: Vector3=(vertices[indices[offset]]+vertices[indices[offset+1]]+vertices[indices[offset+2]])/3.0
     centre=actor._body.to_local(mesh.to_global(centre))
     var wrist_visible: bool=centre.distance_to(wrists[0])<.08 or centre.distance_to(wrists[1])<.08
     if hand_weight < .5 and not wrist_visible:continue
     var triangle:=[]
     for corner in 3:triangle.append_array([uv[indices[offset+corner]].x,uv[indices[offset+corner]].y])
     revealed.append(triangle)
  if revealed.is_empty():push_error("No native hand UV triangles: "+id);quit(3);return
  var file:=FileAccess.open(source+catalog.characters[id].hidden_mask+".hands.json",FileAccess.WRITE)
  file.store_string(JSON.stringify({"character":id,"triangles":revealed,"method":"Native hand/finger skin-bind majority plus 8cm wrist coverage; buffers read-only"}));file.close()
  rows.append({"id":id,"native_hand_triangles":revealed.size()})
  actor.queue_free();await process_frame
 var file:=FileAccess.open(job+"/evidence/native_hands_wrists.json",FileAccess.WRITE)
 file.store_string(JSON.stringify(rows,"  "));file.close()
 print("NATIVE_HAND_UV_DONE ",rows)
 quit()
