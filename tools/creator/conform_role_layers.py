"""Blender native Shrinkwrap clearance on garments only, for every exported body control and LOD.
https://docs.blender.org/manual/en/5.2/modeling/modifiers/deform/shrinkwrap.html
The native body, rig, skin weights, UVs and garment topology are never edited.
"""
import bpy,numpy as np
from mathutils.bvhtree import BVHTree
from mathutils import Vector

def conform(human,clearance=.01):
 meshes=list(human.clothing.outfit.objects)
 apron=next(o for o in meshes if 'elvs_mens_apron1' in o.name.lower())
 inner=[o for o in meshes if o is not apron]
 assert len(inner)==2 and apron.data.shape_keys
 keys=apron.data.shape_keys.key_blocks
 originals={k.name:np.array([v.co[:] for v in k.data],dtype=np.float64) for k in keys}
 body=human.objects.body
 body_before=np.array([v.co[:] for v in body.data.vertices])
 rig_before=[tuple(v for row in b.matrix_local for v in row) for b in human.objects.rig.data.bones]
 faces=[list(f.vertices) for f in apron.data.polygons];reports=[]
 for key in keys:
  points=[];polygons=[]
  for obj in inner:
   blocks=obj.data.shape_keys.key_blocks if obj.data.shape_keys else None
   source=blocks.get(key.name) if blocks else None
   if source is None:source=blocks[0] if blocks else None
   source=source.data if source else obj.data.vertices
   transform=apron.matrix_world.inverted()@obj.matrix_world
   start=len(points);points.extend(transform@v.co for v in source)
   polygons.extend([[start+i for i in f.vertices] for f in obj.data.polygons])
  tree=BVHTree.FromPolygons(points,polygons,all_triangles=False)
  original=originals[key.name];indices=[]
  for index,point in enumerate(original):
   nearest,normal,face,distance=tree.find_nearest(Vector(point))
   if nearest is not None and distance<.15 and (Vector(point)-nearest).dot(normal)<clearance:indices.append(index)
  target_data=bpy.data.meshes.new('layer_target');target_data.from_pydata(points,[],polygons);target_data.update()
  target=bpy.data.objects.new('layer_target',target_data);bpy.context.scene.collection.objects.link(target)
  data=bpy.data.meshes.new('layer_probe');data.from_pydata(original.tolist(),[],faces);data.update()
  probe=bpy.data.objects.new('layer_probe',data);bpy.context.scene.collection.objects.link(probe)
  group=probe.vertex_groups.new(name='intersecting_only')
  if indices:group.add(indices,1,'REPLACE')
  modifier=probe.modifiers.new('Native outside clearance','SHRINKWRAP');modifier.target=target
  modifier.wrap_method='NEAREST_SURFACEPOINT';modifier.wrap_mode='OUTSIDE';modifier.offset=clearance;modifier.vertex_group=group.name
  bpy.context.view_layer.update();evaluated=probe.evaluated_get(bpy.context.evaluated_depsgraph_get());result=evaluated.to_mesh()
  coords=np.array([v.co[:] for v in result.vertices]);evaluated.to_mesh_clear()
  movement=np.linalg.norm(coords-original,axis=1);assert np.isfinite(coords).all() and movement.max()<.151,(key.name,movement.max())
  key.data.foreach_set('co',coords.astype(np.float32).ravel())
  reports.append(dict(key=key.name,selected=len(indices),moved=int((movement>1e-6).sum()),max_m=float(movement.max())))
  for obj in [probe,target]:bpy.data.objects.remove(obj,do_unlink=True)
  bpy.data.meshes.remove(data);bpy.data.meshes.remove(target_data)
 assert np.array_equal(body_before,np.array([v.co[:] for v in body.data.vertices]))
 assert rig_before==[tuple(v for row in b.matrix_local for v in row) for b in human.objects.rig.data.bones]
 return dict(apron=apron.name,method='Blender SHRINKWRAP/OUTSIDE/NEAREST_SURFACEPOINT',offset_m=clearance,keys=reports,body_and_rig_unchanged=True)
