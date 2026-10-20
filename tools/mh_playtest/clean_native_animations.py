"""Same documented Blender glTF bridge as native bodies; preserve every native joint and rest."""
import bpy,json,sys,struct,hashlib,os
from pathlib import Path
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019')
id=sys.argv[sys.argv.index('--')+1]
manifest=root/'evidence'/os.environ.get('OTHERREACH_MH_ANIM_MANIFEST','ue_native_animations_rootfix.json')
record=next(c for c in json.loads(manifest.read_text())['characters'] if c['id']==id)
folder=root/'animations/clean'/id;folder.mkdir(parents=True,exist_ok=True)
rows=[]
for clip in record['clips']:
 path=folder/(clip['logical']+'.glb')
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.gltf(filepath=clip['path'],import_pack_images=False)
 rigs=[o for o in bpy.data.objects if o.type=='ARMATURE'];assert len(rigs)==1
 rig=rigs[0];assert len(rig.data.bones)==342
 rest=[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
 meshes=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
 assert meshes
 neutral={o.name:[tuple(v.co) for v in o.data.vertices] for o in meshes}
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
 for obj in meshes:obj.select_set(True)
 bpy.context.view_layer.objects.active=rig
 bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_skins=True,export_all_influences=True,export_def_bones=False,export_morph=False,export_normals=True,export_tangents=True,export_animations=True,export_animation_mode='ACTIONS',export_apply=False)
 assert rest==[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
 assert all(neutral[o.name]==[tuple(v.co) for v in o.data.vertices] for o in meshes)
 raw=path.read_bytes();g=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
 assert [len(s['joints']) for s in g['skins']]==[342] and len(g.get('animations',[]))==1
 rows.append(dict(logical=clip['logical'],path=str(path),sha256=hashlib.sha256(raw).hexdigest(),native_rest_and_vertices_unchanged=True))
(root/'evidence'/('clean_animations_'+id+'.json')).write_text(json.dumps(dict(status='DONE',clips=rows),indent=2))
