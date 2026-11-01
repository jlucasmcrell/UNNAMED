"""Inspect selected owned source meshes and anatomical joints; no rendering."""
import bpy,json,sys
from pathlib import Path
args=sys.argv[sys.argv.index('--')+1:]
work=Path(args[0]);only=set(args[1:]);report={}
rows=json.loads((work/'selected_manifest_armour_native_v1.json').read_text())
for row in rows:
    if only and row['review_id'] not in only:continue
    bpy.ops.wm.read_homefile(use_empty=True)
    path=Path(row['source'])
    if path.suffix.lower()=='.fbx':bpy.ops.import_scene.fbx(filepath=str(path))
    elif path.suffix.lower()=='.blend':bpy.ops.wm.open_mainfile(filepath=str(path))
    else:bpy.ops.import_scene.gltf(filepath=str(path))
    meshes={}
    for obj in bpy.data.objects:
        if obj.type!='MESH':continue
        points=[obj.matrix_world@v.co for v in obj.data.vertices]
        meshes[obj.name]=dict(vertices=len(points),bounds=[[float(fn(p[i] for p in points)) for i in range(3)] for fn in (min,max)],groups=[g.name for g in obj.vertex_groups])
    rigs={obj.name:{b.name:[list(obj.matrix_world@b.head_local),list(obj.matrix_world@b.tail_local)] for b in obj.data.bones} for obj in bpy.data.objects if obj.type=='ARMATURE'}
    report[row['review_id']]=dict(source=str(path),source_object=row.get('source_object'),objects=meshes,rigs=rigs)
    print('P2_SOURCE_INSPECTED',row['review_id'],flush=True)
(work/'armour_native/source_inspection_remaining.json').write_text(json.dumps(report,indent=2))
