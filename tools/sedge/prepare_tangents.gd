extends SceneTree
func _initialize():
 var root=OS.get_cmdline_user_args()[0]
 var count=0
 for id in ["sedge_reed_clump","sedge_review_walk"]:
  var path=root+"/ready/"+id+"/"+id+".glb"
  var doc=GLTFDocument.new()
  var state=GLTFState.new()
  if doc.append_from_file(path,state)!=OK:
   push_error("Cannot import "+path);quit(1);return
  var scene=doc.generate_scene(state)
  for node in scene.find_children("*","MeshInstance3D",true,false):
   var old=node.mesh
   var mesh=ArrayMesh.new()
   for s in range(old.get_surface_count()):
    var tool=SurfaceTool.new()
    tool.create_from(old,s)
    var arrays=old.surface_get_arrays(s)
    if arrays[Mesh.ARRAY_TEX_UV]!=null and arrays[Mesh.ARRAY_NORMAL]!=null:
     tool.generate_tangents()
    tool.commit(mesh)
    mesh.surface_set_material(s,old.surface_get_material(s))
   node.mesh=mesh
  var output=GLTFState.new()
  if doc.append_from_scene(scene,output)!=OK or doc.write_to_filesystem(output,path+".prepared.glb")!=OK:
   push_error("Cannot export "+path);quit(1);return
  scene.free()
  if DirAccess.rename_absolute(path+".prepared.glb",path)!=OK:
   push_error("Cannot replace prepared asset "+path);quit(1);return
  count+=1
 print("SEDGE_NATIVE_TANGENTS ",count)
 quit(0)
