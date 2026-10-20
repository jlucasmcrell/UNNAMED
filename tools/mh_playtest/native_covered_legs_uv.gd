extends SceneTree
const Native=preload("res://MHCharacters/NativeCharacter.gd")
func _initialize():call_deferred("run")
func run():
 var source: String=OS.get_environment("OTHERREACH_MH_CHARACTERS_ROOT").replace("\\","/").trim_suffix("/")+"/"
 var catalog: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(source+"catalog.json"))
 var rows:=[]
 for id in catalog.characters:
  var actor:=Native.new();root.add_child(actor)
  if not actor.configure(source,catalog.characters[id],{}):quit(2);return
  var waist: float=actor._body.get_bone_global_rest(actor._body.find_bone("pelvis")).origin.y+.12
  var triangles:=[]
  for mesh: MeshInstance3D in actor._body_meshes:
   for surface in mesh.mesh.get_surface_count():
    var a:=mesh.mesh.surface_get_arrays(surface)
    var vertices: PackedVector3Array=a[Mesh.ARRAY_VERTEX]
    var uv: PackedVector2Array=a[Mesh.ARRAY_TEX_UV]
    var indices: PackedInt32Array=a[Mesh.ARRAY_INDEX]
    for offset in range(0,indices.size(),3):
     var centre: Vector3=(vertices[indices[offset]]+vertices[indices[offset+1]]+vertices[indices[offset+2]])/3.0
     centre=actor._body.to_local(mesh.to_global(centre))
     if centre.y>waist:continue
     var triangle:=[]
     for corner in 3:triangle.append_array([uv[indices[offset+corner]].x,uv[indices[offset+corner]].y])
     triangles.append(triangle)
  assert(not triangles.is_empty())
  var record: Dictionary={"id":id,"waist_cutoff_m":waist,"triangles":triangles,"method":"Body Hidden Face Map for full-length trousers plus tall boots: all native body triangles below pelvis+12cm; hand/wrist UV preservation applied last; native buffers read-only"}
  var file:=FileAccess.open(source+catalog.characters[id].hidden_mask+".legs.json",FileAccess.WRITE)
  file.store_string(JSON.stringify(record));file.close()
  rows.append({"id":id,"covered_triangles":triangles.size(),"waist_cutoff_m":waist})
  actor.queue_free();await process_frame
 var file:=FileAccess.open(source+"evidence/covered_legs_uv.json",FileAccess.WRITE)
 file.store_string(JSON.stringify(rows,"  "));file.close();print("COVERED_LEGS_UV_DONE ",rows.size());quit()
