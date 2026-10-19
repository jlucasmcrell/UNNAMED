"""Official Blender glTF import/export bridge. Only native selected LODs; no mesh or rig edits."""
import bpy,json,sys,hashlib,struct
from pathlib import Path
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019');id=sys.argv[sys.argv.index('--')+1]
source=next(c for c in json.loads((root/'evidence/npc_glb_export.json').read_text())['characters'] if c['id']==id)
records=[]
for record in source['exports']:
 if record['part'] not in ['body','head']:continue
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.gltf(filepath=record['path'],import_pack_images=False)
 rigs=[o for o in bpy.data.objects if o.type=='ARMATURE']
 if len(rigs)!=1:raise RuntimeError('Expected one untouched native rig')
 rig=rigs[0];actual=[dict(name=b.name,parent=b.parent.name if b.parent else 'None') for b in rig.data.bones]
 if len(actual)!=record['bone_count'] or any(b not in actual for b in record['bones']):raise RuntimeError('Native bone hierarchy changed on import')
 rest=[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
 meshes=[o for o in bpy.data.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
 if not meshes:raise RuntimeError('No native skinned meshes')
 if record['part']=='head':
  # ibrews/MetaHumanGodot release.gd PROFILES.her fixed native LOD1 surface table.
  # Apply the table in Blender before export: repeated native M_Hide slots otherwise
  # coalesce mouth and scalp into one Godot surface. Only material assignments change.
  raw=Path(record['path']).read_bytes();g=json.loads(raw[20:20+struct.unpack_from('<I',raw,12)[0]])
  primitives=[p for m in g['meshes'] for p in m['primitives'] if g['accessors'][p['indices']]['count']]
  roles=['skin','teeth','hide','eyeR','eyeL','hide','hide','skin','lashes']
  assert len(meshes)==1 and len(primitives)==len(roles),('Native reference surface table mismatch',len(meshes),len(primitives))
  face=meshes[0];polygons=list(face.data.polygons)
  assert len(polygons)==sum(g['accessors'][p['indices']]['count']//3 for p in primitives)
  original=[m.name for m in face.data.materials];offset=0
  for primitive in primitives:
   count=g['accessors'][primitive['indices']]['count']//3
   label=g['materials'][primitive['material']]['name']
   assert all(original[p.material_index]==label and len(p.vertices)==3 for p in polygons[offset:offset+count]),('Native primitive order mismatch',label,offset)
   offset+=count
  face.data.materials.clear()
  names={'skin':'MI_Face_Skin','teeth':'MI_Teeth','hide':'M_Hide','eyeR':'MI_EyeR','eyeL':'MI_EyeL','lashes':'MI_Face_Eyelashes'}
  for index,role in enumerate(roles):face.data.materials.append(bpy.data.materials.new(names[role]+'_Reference_'+str(index)))
  offset=0
  for index,primitive in enumerate(primitives):
   count=g['accessors'][primitive['indices']]['count']//3
   for polygon in polygons[offset:offset+count]:polygon.material_index=index
   offset+=count
 neutral={o.name:[tuple(v.co) for v in o.data.vertices] for o in meshes}
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
 for obj in meshes:obj.select_set(True)
 bpy.context.view_layer.objects.active=rig
 out=root/'runtime/npc'/id;out.mkdir(parents=True,exist_ok=True);path=out/(record['part']+'.glb')
 bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_skins=True,
  export_all_influences=True,export_def_bones=False,export_morph=False,export_normals=True,
  export_tangents=True,export_animations=False,export_apply=False)
 if rest!=[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]:raise RuntimeError('Native rest/hierarchy changed during export')
 if any(neutral[o.name]!=[tuple(v.co) for v in o.data.vertices] for o in meshes):raise RuntimeError('Native neutral vertices changed')
 raw=path.read_bytes();length=struct.unpack_from('<I',raw,12)[0];g=json.loads(raw[20:20+length])
 if any(a.get('count',0)==0 for a in g['accessors']):raise RuntimeError('Invalid empty GLTF accessor')
 if [len(s['joints']) for s in g['skins']]!=[record['bone_count']]:raise RuntimeError('Native joints lost in clean bridge')
 records.append(dict(part=record['part'],native_lod=record['native_lod'],glb=str(path),sha256=hashlib.sha256(raw).hexdigest(),native_bones_parents_rest_preserved=True,native_neutral_vertices_preserved=True,face_surface_roles=roles if record['part']=='head' else None))
(root/'evidence'/('clean_'+id+'.json')).write_text(json.dumps(dict(status='DONE',exports=records),indent=2))
