"""Native HumGen custom-clothing proof for the original gorget, CPU Blender only.

Uses the released equipment exporter0630dadb and its real body/face fitting field.
The saved source carrier is read only. All new authoring/export lives in our work.
"""
import bpy,sys,json,hashlib
from pathlib import Path
from mathutils import Matrix
from HumGen3D.human.human import Human

repo=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(repo/'tools/creator'))
from p2_gear_native_export import export_candidate
work=Path(sys.argv[sys.argv.index('--')+1])
carrier=Path(r'D:\Otherreach_Scratch\p2_gear_20261031\armour_native_headcontrols_v2\sources\gear_old_ward_greathelm__humgen_creator_female\native_outfit.blend')
source=work/'rigged_native_base01/gap_gorget__humgen_creator_female/gap_gorget__humgen_creator_female_rigged.glb'
bpy.ops.wm.open_mainfile(filepath=str(carrier))
bpy.context.scene.render.threads_mode='FIXED';bpy.context.scene.render.threads=4
human=Human.from_existing(bpy.data.objects['humgen_creator_female'])
human.clothing.outfit.remove();human.clothing.footwear.remove()
rig=human.objects.rig
rests={b.name:tuple(float(v) for row in b.matrix_local for v in row) for b in rig.data.bones}
bpy.ops.import_scene.gltf(filepath=str(source))
meshes=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('gap_gorget__')]
assert len(meshes)==1,[(o.name,o.type) for o in bpy.data.objects if o.type=='MESH']
obj=meshes[0];obj.data.transform(obj.matrix_world);obj.parent=None;obj.matrix_world=Matrix.Identity(4)
for modifier in list(obj.modifiers):
 if modifier.type=='ARMATURE':obj.modifiers.remove(modifier)
assert {group.name for group in obj.vertex_groups if any(group.index==g.group and g.weight>0 for v in obj.data.vertices for g in v.groups)}=={'UpperChest'}
obj.vertex_groups['UpperChest'].name='spine.003'
obj.name='gap_gorget'
solver=human.clothing.outfit.add_obj(obj,'full',recalculate_weights=False)
assert rests=={b.name:tuple(float(v) for row in b.matrix_local for v in row) for b in rig.data.bones}
row=dict(art_id='gap_gorget',item_id='item.armor.gorget',review_id='CMB04-01',slot='head',native_candidate_root='native_gorget_probe01')
export_candidate(human,row,work,work/'native_reference','female',source,solver,0,dict(method='Original gorget authored directly on unchanged native creator59 bind; same UpperChest ownership mapped to original HumGen spine.003 group, then normal custom-clothing field/export. Native carrier body and rig unmodified.'))
print('CMB04_NATIVE_GORGET_EXPORT_COMPLETE',flush=True)
