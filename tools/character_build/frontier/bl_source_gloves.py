"""Source-only cuff correction after the native sheet showed sleeve overlap."""
import bpy,bmesh,json,sys,hashlib,os
from pathlib import Path
root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
source=root/'standard8_source_frozen/gloves/frontier_smith_gloves_native.glb'
base='frontier_smith_gloves';bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
obj=bpy.data.objects[base];arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
for old in list(bpy.data.objects):
 if old.type=='MESH' and old.name.startswith(base+'_LOD'):bpy.data.objects.remove(old,do_unlink=True)
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
forearm={g.index for g in obj.vertex_groups if g.name.startswith('lowerarm')}
# Short glove cuff terminates beneath the sleeve. Remove only forearm-dominated
# cuff faces; retain the hand/finger source and its standard skin weights.
weights={v.index:sum(g.weight for g in v.groups if g.group in forearm) for v in obj.data.vertices}
mesh=bmesh.new();mesh.from_mesh(obj.data);mesh.verts.ensure_lookup_table()
bmesh.ops.delete(mesh,geom=[f for f in mesh.faces if sum(weights[v.index] for v in f.verts)/len(f.verts)>.35],context='FACES')
bmesh.ops.delete(mesh,geom=[v for v in mesh.verts if not v.link_faces],context='VERTS')
mesh.to_mesh(obj.data);mesh.free()
lods=[]
for level,ratio in [(1,.65),(2,.4),(3,.25)]:
 lod=obj.copy();lod.data=obj.data.copy();lod.name=base+f'_LOD{level}';bpy.context.collection.objects.link(lod)
 bpy.context.view_layer.objects.active=lod;dec=lod.modifiers.new('Native LOD','DECIMATE');dec.ratio=ratio
 bpy.ops.object.modifier_apply(modifier=dec.name);lods.append(lod)
bpy.ops.object.select_all(action='DESELECT')
for item in [obj,*lods,arm]:item.select_set(True)
out=root/os.environ.get('OTHERREACH_SOURCE_SET','sources/fits')/'veth_f/gloves';out.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out/(base+'_native.glb')),export_format='GLB',use_selection=True,
 export_skins=True,export_def_bones=False,export_influence_nb=8,export_animations=False)
bpy.ops.wm.save_as_mainfile(filepath=str(out/(base+'.blend')))
(out/'source.json').write_text(json.dumps({'source':str(source),'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
 'construction':'Existing first-party glove source; short cuff beneath sleeve; forearm-dominated faces removed after visible overlap; fingers unchanged',
 'authoring_fit':'veth_f','body_edits':False,'acceptance':'PENDING native motion review'},indent=2))
print('SOURCE_CUFF_RECESSED',flush=True)
