"""Re-export accepted garment objects with every authored influence. Native body is excluded."""
import bpy,sys,json,hashlib
from pathlib import Path
who=sys.argv[sys.argv.index('--')+1]
root=Path('D:/Otherreach_Scratch/mh_playtest_20261019')
source=Path('C:/Otherreach_External_Assets/BodyShell_Footwear_20261019')
bpy.ops.wm.open_mainfile(filepath=str(source/'blend'/(who+'_shells.blend')))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
rest=[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
out=root/'runtime/wardrobe/shell';out.mkdir(parents=True,exist_ok=True)
records=[]
for part in ['boots','gloves']:
 obj=bpy.data.objects[who+'_'+part]
 bpy.context.view_layer.objects.active=obj
 tri=obj.modifiers.new('Export_Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
 bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);obj.select_set(True)
 bpy.context.view_layer.objects.active=rig
 path=out/(who+'_'+part+'.glb')
 bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,export_skins=True,export_all_influences=True,export_def_bones=False,export_animations=False,export_apply=False,export_morph=False,export_normals=True,export_tangents=True,export_image_format='JPEG')
 assert rest==[(b.name,b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones]
 records.append(dict(part=part,path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),source_blend=str(source/'blend'/(who+'_shells.blend')),native_rig_unchanged=True,all_authored_influences=True))
(root/'evidence'/('shells_'+who+'.json')).write_text(json.dumps(records,indent=2))
