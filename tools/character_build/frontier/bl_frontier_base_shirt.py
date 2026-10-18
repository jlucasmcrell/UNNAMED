"""One new full-sleeve linen source on feminine Average, native mesh tools only."""
import bpy,bmesh,sys,hashlib,json
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).parent))
from native_clothing import approved_surface,weld
root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
fit='veth_f';source=root/'approved_clothing_sources/veth_f.glb'
before=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
body=approved_surface(fit)
mesh=body.data.copy();mesh.transform(body.matrix_world)
shirt=bpy.data.objects.new('frontier_linen_shirt',mesh);bpy.context.collection.objects.link(shirt)
shirt.shape_key_clear()
edit=bmesh.new();edit.from_mesh(mesh)
pelvis=arm.matrix_world@arm.data.bones['pelvis'].head_local
neck=arm.matrix_world@arm.data.bones['neck_01'].head_local
cuts=[((0,0,pelvis.z+.025),(0,0,1)),((0,0,neck.z-.025),(0,0,-1))]
for side in ('l','r'):
    elbow=arm.matrix_world@arm.data.bones['lowerarm_'+side].head_local
    wrist=arm.matrix_world@arm.data.bones['hand_'+side].head_local
    direction=(wrist-elbow).normalized()
    cuts.append((wrist+direction*.012,-direction))
for point,normal in cuts:
    bmesh.ops.bisect_plane(edit,geom=list(edit.verts)+list(edit.edges)+list(edit.faces),
        dist=1e-6,plane_co=point,plane_no=normal,clear_inner=True)
bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
edit.to_mesh(mesh);edit.free()
bpy.ops.object.select_all(action='DESELECT');shirt.select_set(True);bpy.context.view_layer.objects.active=shirt
weld(shirt)
smooth=shirt.modifiers.new('Linen surface ease','SMOOTH');smooth.factor=.5;smooth.iterations=3
bpy.ops.object.modifier_apply(modifier=smooth.name)
ease=shirt.modifiers.new('Sewn linen clearance','DISPLACE');ease.strength=.016;ease.mid_level=0
bpy.ops.object.modifier_apply(modifier=ease.name)
for group in body.vertex_groups:shirt.vertex_groups.new(name=group.name)
transfer=shirt.modifiers.new('Standard approved body weights','DATA_TRANSFER')
transfer.object=body;transfer.use_vert_data=True;transfer.data_types_verts={'VGROUP_WEIGHTS'}
transfer.vert_mapping='POLYINTERP_NEAREST';transfer.layers_vgroup_select_src='ALL';transfer.layers_vgroup_select_dst='NAME'
bpy.ops.object.modifier_apply(modifier=transfer.name)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.012);bpy.ops.object.mode_set(mode='OBJECT')
shirt.data.materials.clear();mat=bpy.data.materials.new('frontier_linen_cloth');mat.use_nodes=True
shader=mat.node_tree.nodes.get('Principled BSDF');shader.inputs['Base Color'].default_value=(.31,.28,.20,1);shader.inputs['Roughness'].default_value=.95
shirt.data.materials.append(mat)
for face in shirt.data.polygons:face.material_index=0;face.use_smooth=True
skin=shirt.modifiers.new('Approved skeleton','ARMATURE');skin.object=arm
matrix=shirt.matrix_world.copy();shirt.parent=arm;shirt.matrix_world=matrix
lods=[]
for level,ratio in ((1,.65),(2,.4),(3,.22)):
    lod=shirt.copy();lod.data=shirt.data.copy();lod.name=shirt.name+f'_LOD{level}';bpy.context.collection.objects.link(lod)
    bpy.context.view_layer.objects.active=lod
    dec=lod.modifiers.new('Native LOD','DECIMATE');dec.ratio=ratio;bpy.ops.object.modifier_apply(modifier=dec.name);lods.append(lod)
bpy.ops.object.select_all(action='DESELECT')
for obj in [shirt,*lods,arm]:obj.select_set(True)
out=root/'sources/fits/veth_f/base_shirt';out.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out/'frontier_linen_shirt_native.glb'),export_format='GLB',use_selection=True,export_skins=True,export_def_bones=False,export_influence_nb=8,export_animations=False)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'frontier_linen_shirt.blend'))
assert hashlib.sha256(source.read_bytes()).hexdigest()==before
(out/'source.json').write_text(json.dumps({'source':str(source),'sha256':before,'authored_on':'veth_f','construction':'New full sleeve surface, spatial opening cuts and standard skin transfer','body_edits':False,'hide_regions':['torso','upper_arms','forearms'],'acceptance':'PENDING visual review'},indent=2))
print('BASE_SHIRT_AUTHORED_ONCE',fit)
