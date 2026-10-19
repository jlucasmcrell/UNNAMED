"""Fit the licensed leather pack once per Average body family; no body edits."""
import bpy,bmesh,hashlib,json,sys,os
from pathlib import Path
from mathutils import Matrix,Vector
sys.path.insert(0,str(Path(__file__).parent))
from native_clothing import approved_surface,arm_weights
root=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
source=next((root/'licensed_sources/leather_armor_eb56ba5c').glob('*.blend'))
part=sys.argv[sys.argv.index('--')+1];assert part in ('vest','trousers')
base='frontier_sealed_vest' if part=='vest' else 'frontier_wool_trousers'
fit=os.environ.get('OTHERREACH_SOURCE_BODY','veth_f')
assert fit in ('veth_f','veth_m'), 'Author one Average source per body family'
family_scale=1.126 if fit=='veth_m' else 1.0
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(root/'approved_clothing_sources'/(fit+'.glb')))
arm=next(o for o in bpy.data.objects if o.type=='ARMATURE');body=approved_surface(fit)
names=['Vest','Vest Detail','Colllar Metal','Belt','Belt Metal buckle']+[f'Botton {n:02}' for n in range(1,7)] if part=='vest' else ['Pants','Pants Lace']
def active(obj):
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
with bpy.data.libraries.load(str(source),link=False) as (available,loaded):
 assert all(n in available.objects for n in names)
 loaded.objects=names
objects=[]
for obj in loaded.objects:
 bpy.context.collection.objects.link(obj);obj.hide_set(False);obj.hide_render=False
 active(obj)
 for modifier in list(obj.modifiers):
  if modifier.type=='SUBSURF':modifier.levels=min(modifier.levels,1)
  bpy.ops.object.modifier_apply(modifier=modifier.name)
 obj.data.transform(obj.matrix_world);obj.parent=None;obj.matrix_world=Matrix.Identity(4)
 for v in obj.data.vertices:
  p=v.co.copy()
  v.co=Vector((p.x*.72,p.y*.78-.035,p.z*.55+.63)) if part=='vest' else Vector((p.x*1.05,p.y*.8-.035,p.z*1.20+.12))
  if part=='vest' and fit=='veth_f' and v.co.y<-.02:
   # A stiffened front panel bridges the source's pectoral hollows. This edits
   # the garment once, including its closures, never the approved body.
   front=min(1,max(0,(-v.co.y-.02)/.04))
   top=min(1,max(0,(1.39-v.co.z)/.04))
   bottom=min(1,max(0,(v.co.z-1.13)/.06))
   t=front*top*bottom
   panel=-.175+.05*(abs(v.co.x)/.20)**4+(v.co.y+.13)*.03
   if obj.name!='Vest':panel-=.002
   v.co.y=v.co.y*(1-t)+panel*t
  v.co*=family_scale
  if part=='vest' and fit=='veth_m':v.co.x*=1.30
  if part=='trousers':
   waist=min(1,max(0,(v.co.z-1.01*family_scale)/(.08*family_scale)))
   waist_inset=.25 if fit=='veth_m' else .4
   depth_inset=.22 if fit=='veth_m' else .30
   v.co.x*=1-waist_inset*waist;v.co.y=-.02*family_scale+(v.co.y+.02*family_scale)*(1-depth_inset*waist)
   cuff=min(1,max(0,(.25*family_scale-v.co.z)/(.12*family_scale)))
   ankle=arm.matrix_world@arm.data.bones['foot_'+('l' if v.co.x>0 else 'r')].head_local
   v.co.x=v.co.x*(1-cuff)+(ankle.x+(v.co.x-ankle.x)*.50)*cuff
   v.co.y=v.co.y*(1-cuff)+(ankle.y+(v.co.y-ankle.y)*.45)*cuff
 obj.data.update()
 if obj.data.has_custom_normals:
  obj.data.normals_split_custom_set([(0.,0.,0.)]*len(obj.data.loops))
 objects.append(obj)
materials={}
def material(kind,seam_name):
 key=(kind,seam_name)
 if key in materials:return materials[key]
 m=bpy.data.materials.new('frontier_oiled_leather' if kind=='leather' else ('frontier_wool_trousers_cloth' if kind=='wool' else 'frontier_buckle_iron'))
 m.use_nodes=True;m['scan_attached']=True;nodes=m.node_tree.nodes;links=m.node_tree.links;shader=nodes.get('Principled BSDF')
 shader.inputs['Roughness'].default_value=.72
 if kind=='metal':
  shader.inputs['Base Color'].default_value=(.22,.20,.17,1);shader.inputs['Metallic'].default_value=1;shader.inputs['Roughness'].default_value=.42
 else:
  directory=root/'licensed_sources/materials/Leather014' if kind=='leather' else Path(r'C:\Otherreach_External_Assets\PolyHaven\textures\poly_wool_herringbone')
  suffixes=[('Color' if kind=='leather' else 'diff','Base Color'),('Roughness' if kind=='leather' else 'rough','Roughness'),('NormalGL' if kind=='leather' else 'nor_gl',None)]
  for suffix,socket in suffixes:
   image=bpy.data.images.load(str(next(directory.glob('*'+suffix+'*.jpg'))),check_existing=True).copy()
   if socket!='Base Color':image.colorspace_settings.name='Non-Color'
   image.scale(2048,2048)
   # Physical grain repeats independently of the registered garment stitch atlas.
   import numpy as np
   pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels)
   pixels=pixels.reshape(2048,2048,4)
   pixels=np.tile(pixels[::4,::4,:],(4,4,1))
   image.pixels.foreach_set(pixels.ravel());image.pack()
   if socket=='Base Color':
    import numpy as np
    pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(-1,4)
    pixels[:,:3]*=np.array((1.,1.,1.) if kind=='leather' else (.65,.60,.52),dtype=np.float32)
    image.pixels.foreach_set(pixels.ravel());image.pack()
   if socket is None and seam_name in bpy.data.images:
    # Bake the licensed artist's seam trim into the scanned grain normal once.
    import numpy as np
    seam=bpy.data.images[seam_name].copy();seam.colorspace_settings.name='Non-Color';seam.scale(2048,2048)
    a=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(a);a=a.reshape(-1,4)
    b=np.empty(len(seam.pixels),dtype=np.float32);seam.pixels.foreach_get(b);b=b.reshape(-1,4)
    n=(a[:,:3]*2-1)*.16+(b[:,:3]*2-1)
    n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-6)
    a[:,:3]=n*.5+.5;image.pixels.foreach_set(a.ravel());image.pack()
   node=nodes.new('ShaderNodeTexImage');node.image=image
   if socket:links.new(node.outputs['Color'],shader.inputs[socket])
   else:
    normal=nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.8
    links.new(node.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs[0],shader.inputs['Normal'])
 materials[key]=m;return m
for obj in objects:
 kind='metal' if 'Metal' in obj.name or 'Botton' in obj.name else ('wool' if obj.name=='Pants' else 'leather')
 seam_name='Cloth_Seams_Normal' if obj.name in ('Vest','Vest Detail') else 'Cloth02_Seams_Normal'
 obj.data.materials.clear();obj.data.materials.append(material(kind,seam_name))
 for face in obj.data.polygons:face.material_index=0;face.use_smooth=True
 # Retain the source UV atlas: its sewn border/stitch trim is registered to it.
if part=='vest':
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(root/os.environ.get('OTHERREACH_SOURCE_SET','sources/fits')/fit/'base_shirt/frontier_linen_shirt_native.glb'))
 shirt=next(o for o in set(bpy.data.objects)-old if o.type=='MESH' and o.name=='frontier_linen_shirt')
 matrix=shirt.matrix_world.copy();shirt.parent=None;shirt.matrix_world=matrix
 shirt.data.transform(shirt.matrix_world);shirt.matrix_world=Matrix.Identity(4)
 for modifier in list(shirt.modifiers):shirt.modifiers.remove(modifier)
 # Keep a complete lining under the open armholes. Fit only its concealed
 # torso inside the leather; the licensed loose sleeves keep their volume.
 weights=arm_weights(shirt)
 group=shirt.vertex_groups.new(name='Concealed shirt torso')
 for index,weight in enumerate(weights):
  vertex=shirt.data.vertices[index]
  side='l' if vertex.co.x>0 else 'r'
  start=arm.matrix_world@arm.data.bones['upperarm_'+side].head_local
  direction=arm.matrix_world@(arm.data.bones['lowerarm_'+side].head_local)-start
  fraction=(vertex.co-start).dot(direction)/direction.length_squared
  value=max(max(0,1-weight/.3),max(0,min(1,(.45-fraction)/.20)))
  if value:group.add([index],value,'REPLACE')
 active(shirt);wrap=shirt.modifiers.new('Native concealed lining fit','SHRINKWRAP')
 wrap.target=objects[0];wrap.wrap_method='NEAREST_SURFACEPOINT';wrap.wrap_mode='ABOVE_SURFACE';wrap.offset=-.008
 wrap.vertex_group=group.name;name=group.name;bpy.ops.object.modifier_apply(modifier=wrap.name)
 shirt.vertex_groups.remove(shirt.vertex_groups[name])
 # The lining ends inside the belt; never clip the cuffs with this torso cut.
 edit=bmesh.new();edit.from_mesh(shirt.data);edit.verts.index_update()
 selected=[face for face in edit.faces if sum(weights[v.index] for v in face.verts)/len(face.verts)<.3]
 geom=set(selected)
 for face in selected:geom.update(face.edges);geom.update(face.verts)
 pelvis=(arm.matrix_world@arm.data.bones['pelvis'].head_local).z
 bmesh.ops.bisect_plane(edit,geom=list(geom),plane_co=Vector((0,0,pelvis+.19)),
  plane_no=Vector((0,0,1)),dist=1e-6,clear_inner=True)
 collar=max(v.co.z for v in objects[0].data.vertices)-.06
 bmesh.ops.bisect_plane(edit,geom=list(edit.verts)+list(edit.edges)+list(edit.faces),
  plane_co=Vector((0,0,collar)),plane_no=Vector((0,0,1)),dist=1e-6,clear_outer=True)
 edit.to_mesh(shirt.data);edit.free()
 objects.append(shirt)
for obj in objects:
 obj.vertex_groups.clear()
 for group in body.vertex_groups:obj.vertex_groups.new(name=group.name)
 active(obj);dt=obj.modifiers.new('Standard source skin transfer','DATA_TRANSFER');dt.object=body;dt.use_vert_data=True
 dt.data_types_verts={'VGROUP_WEIGHTS'};dt.vert_mapping='POLYINTERP_NEAREST';dt.layers_vgroup_select_src='ALL';dt.layers_vgroup_select_dst='NAME'
 bpy.ops.object.modifier_apply(modifier=dt.name)
active(objects[0])
for obj in objects:obj.select_set(True)
bpy.ops.object.join();obj=objects[0];obj.name=base
skin=obj.modifiers.new('Approved skeleton','ARMATURE');skin.object=arm;obj.parent=arm;obj.matrix_world=Matrix.Identity(4)
lods=[]
for level,ratio in [(1,.65),(2,.4),(3,.25)]:
 lod=obj.copy();lod.data=obj.data.copy();lod.name=base+f'_LOD{level}';bpy.context.collection.objects.link(lod);active(lod)
 dec=lod.modifiers.new('Native LOD','DECIMATE');dec.ratio=ratio;dec.delimit={'MATERIAL'};bpy.ops.object.modifier_apply(modifier=dec.name);lods.append(lod)
active(obj)
for item in [arm,*lods]:item.select_set(True)
out=root/os.environ.get('OTHERREACH_SOURCE_SET','sources/fits')/fit/part;out.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out/(base+'_native.glb')),export_format='GLB',use_selection=True,export_skins=True,
 export_def_bones=False,export_influence_nb=8,export_animations=False)
bpy.ops.wm.save_as_mainfile(filepath=str(out/(base+'.blend')))
(out/'source.json').write_text(json.dumps({'source':str(source),'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
 'licence':'BlenderKit royalty-free; Giovani França','asset_id':'eb56ba5c-92b0-47db-ad3e-5fa00a5eb077',
 'materials':'ambientCG Leather014 and Poly Haven wool; CC0 scans, OpenGL normal and roughness',
 'authoring_fit':fit,'construction':'Licensed sewn garment fitted once per body family, layered source joined, native Data Transfer',
 'body_edits':False,'acceptance':'PENDING source and motion review'},indent=2))
print('LICENSED_PACK_SOURCE_FIT',part,flush=True)
