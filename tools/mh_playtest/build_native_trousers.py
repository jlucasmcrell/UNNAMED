"""Run Claude's generalized body-shell trouser handoff, then export only its garment with all weights."""
import bpy,sys,runpy,json,shutil,hashlib,re
from pathlib import Path
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019')
handoff=Path('C:/Otherreach_External_Assets/BodyShell_Footwear_20261019')
report=Path('C:/Otherreach_External_Assets/REPORT_FOR_CLAUDE_BODYSHELL_FOOTWEAR.md')
assert re.search(r'^#{1,3}[^\n]*trousers|_trousers\.glb',report.read_text(),re.I|re.M),'Wait for Claude trouser handoff section'
id=sys.argv[sys.argv.index('--')+1]
catalog=json.loads((root/'runtime/catalog.json').read_text())
source=root/'runtime'/catalog['characters'][id]['body']
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
body=max((o for o in bpy.data.objects if o.type=='MESH'),key=lambda o:len(o.data.vertices))
rest=[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
neutral=[tuple(v.co) for v in body.data.vertices]
out=root/'trousers'/id
for name in ['tex','blend','glb']:(out/name).mkdir(parents=True,exist_ok=True)
for name in ['trouser_wool_albedo_1k.jpg','trouser_wool_nor_folds_1k.png']:shutil.copy2(handoff/'tex'/name,out/'tex'/name)
script=handoff/'scripts/build_shells.py'
sys.argv=[str(script),'--',str(source),str(out),id,'--pieces','trousers']
namespace=runpy.run_path(str(script),run_name='__main__')
rig=namespace['arm'];body=namespace['body'];garment=namespace['trousers']
assert rest==[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
assert neutral==[tuple(v.co) for v in body.data.vertices]
bpy.context.view_layer.objects.active=garment
tri=garment.modifiers.new('Export_Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);garment.select_set(True);bpy.context.view_layer.objects.active=rig
dest=root/'runtime/wardrobe/shell'/(id+'_trousers.glb')
bpy.ops.export_scene.gltf(filepath=str(dest),export_format='GLB',use_selection=True,export_skins=True,export_all_influences=True,export_def_bones=False,export_normals=True,export_tangents=True,export_animations=False,export_apply=False,export_morph=False,export_image_format='JPEG')
assert rest==[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
(root/'evidence'/('trousers_'+id+'.json')).write_text(json.dumps(dict(status='DONE',id=id,path=str(dest),source_script=str(script),source_script_sha256=hashlib.sha256(script.read_bytes()).hexdigest(),source_body=str(source),source_body_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),native_rest_and_body_preserved=True,all_influences=True,derived_body_not_staged=True,garment_triangles=len(garment.data.polygons)),indent=2))
print('NATIVE_TROUSERS_EXPORTED',id)
