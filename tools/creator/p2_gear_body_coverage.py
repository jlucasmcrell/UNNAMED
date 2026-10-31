"""Write native-body UV coverage from actual outward garment occlusion rays.

CPU Blender: -- <candidate root> [equipment identifiers ...]. The saved native
authoring scene is read only. No body vertices, skin weights or rests change.
"""
import bpy,json,sys
from pathlib import Path
from mathutils.bvhtree import BVHTree
from HumGen3D.human.human import Human

args=sys.argv[sys.argv.index('--')+1:]
root=Path(args[0])
folders=[root/'primary/rigged'/name for name in args[1:]] if len(args)>1 else sorted((root/'primary/rigged').iterdir())
for folder in folders:
    contract=json.loads((folder/'creator_part.json').read_text())
    scene=root/'sources'/folder.name/'native_outfit.blend'
    bpy.ops.wm.open_mainfile(filepath=str(scene))
    human=Human.from_existing(bpy.data.objects[contract['target_base']])
    group=human.clothing.footwear if contract['slot']=='feet' else human.clothing.outfit
    # A boot authoring scene also contains its separate trouser underlayer;
    # each exported modular piece gets only its own coverage.
    meshes=list(group.objects)
    assert len(meshes)==1,[(o.name) for o in meshes]
    deps=bpy.context.evaluated_depsgraph_get();points=[];polygons=[]
    for obj in meshes:
        evaluated=obj.evaluated_get(deps);offset=len(points)
        points.extend(evaluated.matrix_world@v.co for v in evaluated.data.vertices)
        polygons.extend([offset+i for i in p.vertices] for p in evaluated.data.polygons)
    tree=BVHTree.FromPolygons(points,polygons)
    body=human.objects.body;mesh=body.evaluated_get(deps).data
    normals=body.matrix_world.to_3x3().inverted().transposed();covered=set()
    for vertex in mesh.vertices:
        origin=body.matrix_world@vertex.co;direction=(normals@vertex.normal).normalized()
        hit=tree.ray_cast(origin+direction*.00001,direction,.015)
        if hit[0] is not None:covered.add(vertex.index)
    uv=mesh.uv_layers.active.data;triangles=[]
    # Fully covered polygons only: exposed skin along openings must survive.
    for polygon in mesh.polygons:
        if not all(i in covered for i in polygon.vertices):continue
        corners=[uv[i].uv for i in polygon.loop_indices]
        for step in range(1,len(corners)-1):
            triangles.append([float(v) for p in (corners[0],corners[step],corners[step+1]) for v in p])
    receipt=dict(method='Outward native-body normal rays, 15mm; fully occluded source polygons only; never rig-weight masks',
        target_body_sha256=contract['target_body_sha256'],body_vertex_count=len(mesh.vertices),
        source_vertex_indices=sorted(covered),covered=len(covered),uv_triangles=triangles,ray_length_m=.015)
    (folder/'body_coverage.json').write_text(json.dumps(receipt))
    print('P2_NATIVE_BODY_COVERAGE',folder.name,len(covered),len(triangles),flush=True)
