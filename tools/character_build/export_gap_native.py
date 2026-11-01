"""Original gap garment -> existing native HumGen custom-clothing/control pipeline.

Blender CPU -- <work> <female|male> <part>. The original carrier and target body
stay read only. Canonical skin coordinates are put into the original authoring
rest before normal HumGen processing, retaining the original weights by role.
Only explicitly declared garment spring bones may be added; the body rest is exact.
"""
import bpy,json,sys,re
import numpy as np
from pathlib import Path
from mathutils import Matrix,Vector
from HumGen3D.human.human import Human
from HumGen3D.human.process import game_rig
from HumGen3D.human.process.settings import ExportSettings
repo=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(repo/'tools/creator'));sys.path.insert(0,str(repo/'tools/animation'))
from p2_gear_native_export import export_candidate
from audit_humgen_transport import Glb
args=sys.argv[sys.argv.index('--')+1:];work=Path(args[0]);sex,part=args[1:3]
assert sex in ['female','male'] and part.replace('_','').isalnum()
base='humgen_creator_'+sex;identifier='gap_'+part+'__'+base
source=work/'rigged_native_base01'/identifier/(identifier+'_rigged.glb')
raw=Glb(source);poses=raw.globals();metadata=json.loads(source.with_name(identifier+'_garment.json').read_text())
carrier=Path(r'D:\Otherreach_Scratch\p2_gear_20261031\armour_native_headcontrols_v2\sources')/('gear_old_ward_greathelm__'+base)/'native_outfit.blend'
bpy.ops.wm.open_mainfile(filepath=str(carrier));bpy.context.scene.render.threads_mode='FIXED';bpy.context.scene.render.threads=4
human=Human.from_existing(bpy.data.objects[base]);human.clothing.outfit.remove();human.clothing.footwear.remove();rig=human.objects.rig
rests={b.name:(b.parent.name if b.parent else None,tuple(v for row in b.matrix_local for v in row)) for b in rig.data.bones}
rename=game_rig._new_bone_names(rig,game_rig.get_preset(ExportSettings.from_recipe('godot').skeleton.names));aliases={new:old for old,new in rename.items()}
for bone in rig.data.bones:
 aliases.setdefault(bone.name,bone.name)
native={canonical:rig.matrix_world@rig.data.bones[name].matrix_local for canonical,name in aliases.items() if canonical in poses}
helpers=metadata.get('secondary_joints',[])
parents={child:index for index,node in enumerate(raw.nodes) for child in node.get('children',[])}
for name in helpers:
 node=raw.names[name];parent=raw.nodes[parents[node]]['name']
 assert parent in native,parent
 native[name]=native[parent]@Matrix(poses[parent].tolist()).inverted()@Matrix(poses[name].tolist())
 aliases[name]=name
if helpers:
 bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
 for name in helpers:
  parent=raw.nodes[parents[raw.names[name]]]['name'];bone=rig.data.edit_bones.new(name);bone.parent=rig.data.edit_bones[aliases[parent]];bone.matrix=rig.matrix_world.inverted()@native[name];bone.length=.035;bone.use_deform=True
 bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.import_scene.gltf(filepath=str(source))
objects=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('gap_'+part+'__')];assert len(objects)==1,[o.name for o in objects]
obj=objects[0];obj.data.transform(obj.matrix_world);obj.parent=None;obj.matrix_world=Matrix.Identity(4)
for modifier in list(obj.modifiers):
 if modifier.type=='ARMATURE':obj.modifiers.remove(modifier)
# glTF Y-up to Blender Z-up. The original native authoring pose precedes the
# export recipe's T-pose; change garment coordinates, never the body's rig.
axis=Matrix(((1,0,0,0),(0,0,-1,0),(0,1,0,0),(0,0,0,1)))
changes={name:native[name]@Matrix(poses[name].tolist()).inverted() for name in native}
for vertex in obj.data.vertices:
 point=axis.inverted()@vertex.co.to_4d();result=Vector((0,0,0,0));total=0
 for binding in vertex.groups:
  if binding.weight<=0:continue
  name=obj.vertex_groups[binding.group].name;assert name in changes,name
  result+=(changes[name]@point)*binding.weight;total+=binding.weight
 assert abs(total-1)<.001
 vertex.co=result.to_3d()/total
for group in obj.vertex_groups:
 if group.name in aliases:group.name=aliases[group.name]
obj.name='gap_'+part;obj.data.update()
slot=re.search(r'^slot:\s*(\w+)',(repo/'content/items/armor'/(part+'.yaml')).read_text(),re.M).group(1)
solver=(human.clothing.footwear.add_obj(obj,recalculate_weights=False) if slot=='feet'
        else human.clothing.outfit.add_obj(obj,'full',recalculate_weights=False))
assert all(rests[name]==(rig.data.bones[name].parent.name if rig.data.bones[name].parent else None,tuple(v for row in rig.data.bones[name].matrix_local for v in row)) for name in rests)
row=dict(art_id='gap_'+part,item_id='item.armor.'+part,review_id='CMB04-'+part,slot=slot,native_candidate_root='native_gap01',garment_bone_prefixes=['cloth_gap_'])
export_candidate(human,row,work,work/'native_reference',sex,source,solver,0,dict(method='Exact canonical garment skin placed in original HumGen authoring rest, normal native custom-clothing field, complete native body/face controls',original_authoring_bones=len(rests),original_authoring_rest_exact=True,declared_garment_helpers=helpers))
folder=work/'native_gap01/primary/rigged'/identifier
p=folder/'creator_part.json';contract=json.loads(p.read_text());contract.update(covers_hair=False,preserve_underlayers=True);p.write_text(json.dumps(contract,indent=2))
p=folder/(identifier+'_garment.json');sidecar=json.loads(p.read_text());sidecar.update(layer=metadata['layer'],chains=metadata.get('chains',{}),secondary_joints=helpers);p.write_text(json.dumps(sidecar,indent=2))
print('NATIVE_GAP_EXPORT_COMPLETE',identifier,flush=True)
