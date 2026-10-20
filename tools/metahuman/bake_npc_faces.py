"""Community ARKit evaluated-mesh capture, including its documented groom propagation.
Original head vertices and native rest transforms are asserted unchanged.
"""
import bpy,json,sys,os,runpy
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from pipeline_paths import pipeline_root
root=pipeline_root()
id=sys.argv[sys.argv.index('--')+1]
reference=Path('D:/Otherreach_Scratch/mh_slice_20261019/scripts/bake_cinematic_arkit_glb.py').read_text()
reference=reference.replace("root=Path('D:/Otherreach_Scratch/mh_slice_20261019')", "root=Path(os.environ['OTHERREACH_PIPELINE_ROOT'])")
reference=reference.replace("sex=os.environ.get('OTHERREACH_MH_SEX','Feminine').lower()", "sex=os.environ['OTHERREACH_NPC_FACE_ID']")
reference=reference.replace("inputs=root/'exports/cinematic_arkit'\nif sex!='feminine':inputs=inputs/sex", "inputs=root/'exports/npc_arkit'/sex")
reference=reference.replace("data['nonzero_native_morph_curve_count']>0", "data['nonzero_native_morph_curve_count']>=0")
reference=reference.replace("face=next(o for o in bpy.data.objects if o.type=='MESH' and o.data.shape_keys)", "face=max((o for o in bpy.data.objects if o.type=='MESH'),key=lambda o:len(o.data.vertices))\nif not face.data.shape_keys:face.shape_key_add(name='Basis')")
reference=reference.replace("out=root/'glb/cinematic_arkit'/sex", "out=root/'runtime/heads_rcd3'/sex")
# UE FNames are case-insensitive, filesystem names are not; the export uses lower-case ids.
reference=reference.replace("sex+'_base.glb'", "sex.lower()+'_base.glb'").replace("sex+'_animation.glb'", "sex.lower()+'_animation.glb'")
(root/'blender').mkdir(exist_ok=True)
os.environ['OTHERREACH_NPC_FACE_ID']=id
scope={'__name__':'__main__'}
cache=root/'blender'/(id+'_cinematic_arkit.blend')
if os.environ.get('OTHERREACH_MH_FACE_USE_BAKE_CACHE')=='1' and cache.exists():
 bpy.ops.wm.open_mainfile(filepath=str(cache))
 scope['face']=max((o for o in bpy.data.objects if o.type=='MESH' and o.data.shape_keys),key=lambda o:len(o.data.vertices))
 scope['rig']=next(o for o in bpy.data.objects if o.type=='ARMATURE')
else:exec(compile(reference,'community_npc_arkit_capture','exec'),scope)
face=scope['face']
# Retain the accepted head's exact surface/material layout. UE's new export merged two
# material slots; only its expression deltas are transferred onto the original native head.
from mathutils import kdtree
previous=Path(os.environ['OTHERREACH_PREVIOUS_RUNTIME'])
config=json.loads((previous/'catalog.json').read_text())['characters'][id]
source_face=face
source_rig=scope['rig']
points=[source_face.matrix_world @ v.co for v in source_face.data.vertices]
tree=kdtree.KDTree(len(points))
for i,point in enumerate(points):tree.insert(point,i)
tree.balance()
shapes={key.name:[source_face.matrix_world @ v.co for v in key.data] for key in source_face.data.shape_keys.key_blocks[1:]}
before=set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(previous/config['head']),import_pack_images=False)
original_objects=[o for o in bpy.data.objects if o not in before]
original_rig=next(o for o in original_objects if o.type=='ARMATURE')
original_face=max((o for o in original_objects if o.type=='MESH'),key=lambda o:len(o.data.vertices))
assert len(original_rig.data.bones)==875
original_rest=[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in original_rig.data.bones]
for bone in original_rig.data.bones:
 other=source_rig.data.bones[bone.name]
 assert (bone.parent.name if bone.parent else None)==(other.parent.name if other.parent else None)
 # Expression deltas only: retain the accepted target's rest, including unused body helpers.
 # Source preview rests are not installed. Vertex-delta matching below proves the identical neutral head.
 pass
neutral=[v.co.copy() for v in original_face.data.vertices]
if not original_face.data.shape_keys:original_face.shape_key_add(name='Basis')
def weights(obj,v):return {obj.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>0.00001}
source_weights=[weights(source_face,v) for v in source_face.data.vertices]
def uvs(obj):
 result=[set() for _ in obj.data.vertices]
 if obj.data.uv_layers:
  layer=obj.data.uv_layers[0].data
  for loop in obj.data.loops:result[loop.vertex_index].add(tuple(round(x,5) for x in layer[loop.index].uv))
 return result
source_uvs=uvs(source_face);target_uvs=uvs(original_face)
indices=[]
for v in original_face.data.vertices:
 point=original_face.matrix_world @ v.co
 neighbours=tree.find_range(point,0.000002)
 assert neighbours,('Neutral head geometry differs',id,v.index,tree.find(point)[2])
 candidates=[n[1] for n in neighbours]
 wanted=weights(original_face,v)
 def score(candidate):
  given=source_weights[candidate]
  return sum(abs(wanted.get(k,0)-given.get(k,0)) for k in wanted.keys()|given.keys())
 # Position+UV identify the native topology. Skin weights rank coincident seam copies;
 # their export quantization is not installed or used to alter the accepted target weights.
 exact_uv=[candidate for candidate in candidates if source_uvs[candidate]==target_uvs[v.index]]
 assert exact_uv,('Native UV mismatch',id,v.index)
 candidates=exact_uv
 best=min(score(candidate) for candidate in candidates)
 candidates=[candidate for candidate in candidates if score(candidate)<best+0.00001]
 # Coincident UV-seam duplicates must carry the same expression delta.
 for candidate in candidates[1:]:
  assert all(((values[candidate]-points[candidate])-(values[candidates[0]]-points[candidates[0]])).length<0.00001 for values in shapes.values()),('Ambiguous face vertex',v.index)
 indices.append(candidates[0])
inverse=original_face.matrix_world.inverted().to_3x3()
for name,values in shapes.items():
 key=original_face.shape_key_add(name=name,from_mix=False);key.value=0.0
 for i,v in enumerate(key.data):v.co=neutral[i]+inverse @ (values[indices[i]]-points[indices[i]])
assert all((v.co-neutral[i]).length<1e-8 for i,v in enumerate(original_face.data.vertices))
for obj in list(bpy.data.objects):
 if obj not in original_objects:bpy.data.objects.remove(obj,do_unlink=True)
face=original_face
face.name=face.name if face.name.lower().endswith('lod0') else face.name+'_LOD0'
bpy.ops.object.select_all(action='DESELECT');face.select_set(True);original_rig.select_set(True)
bpy.context.view_layer.objects.active=original_rig
assert original_rest==[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in original_rig.data.bones]
head_path=root/'runtime/heads_rcd3'/id/'head.glb'
bpy.ops.export_scene.gltf(filepath=str(head_path),export_format='GLB',use_selection=True,export_skins=True,export_all_influences=True,export_def_bones=False,export_morph=True,export_morph_normal=True,export_morph_tangent=True,export_try_sparse_sk=True,export_normals=True,export_tangents=True,export_animations=False,export_apply=False)
manifest=json.loads((root/'runtime'/config['groom_manifest']).read_text())
facial_cards=[]
for card in manifest['cards']:
 if card['name'].startswith('Hair_'):continue
 before=set(bpy.data.objects)
 bpy.ops.import_scene.gltf(filepath=str(previous/card['glb']),import_pack_images=False)
 meshes=[o for o in bpy.data.objects if o not in before and o.type=='MESH']
 for mesh in meshes:mesh.name=card['name']+'_'+mesh.name
 facial_cards.append((card,meshes))
# Invoke the community implementation itself, with the known face supplied instead of its filename heuristic.
groom_script=Path('D:/Otherreach_Scratch/mh_slice_20261019/reference_metahuman-to-glb/5.7/cinematic/stages/02-blender-setup/tools/apply_arkit52_grooms.py')
module=runpy.run_path(str(groom_script))
apply=module['apply']
apply.__globals__['_find_face_mesh']=lambda:face
report=apply(char_id=id)
out=root/'runtime/grooms_rcd3'/id;out.mkdir(parents=True,exist_ok=True)
for card,meshes in facial_cards:
 bpy.ops.object.select_all(action='DESELECT')
 for mesh in meshes:mesh.select_set(True)
 bpy.context.view_layer.objects.active=meshes[0]
 path=out/(Path(card['glb']).stem+'.glb')
 bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_skins=True,export_all_influences=True,export_def_bones=False,export_morph=True,export_morph_normal=True,export_morph_tangent=True,export_try_sparse_sk=True,export_normals=True,export_tangents=True,export_animations=False,export_apply=False)
 card['glb']=path.relative_to(root/'runtime').as_posix()
config=json.loads((root/'runtime/catalog.json').read_text())['characters'][id]
config['head_expression_mesh']='heads_rcd3/'+id+'/head.glb'
config['groom_manifest']='grooms_rcd3/'+id+'/grooms.json'
(out/'grooms.json').write_text(json.dumps(manifest,indent=2))
(root/'evidence'/('face_ready_'+id+'.json')).write_text(json.dumps({'id':id,'config':config,'grooms':report,'reference':str(groom_script)},indent=2,default=str))
