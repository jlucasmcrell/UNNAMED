"""Fit licensed source garments once per Average body family.

No game garment or approved body is edited. Other fits use the existing transfer.
"""
import bpy,bmesh,hashlib,json,math,sys,os
from pathlib import Path
from mathutils import Vector,Matrix
sys.path.insert(0,str(Path(__file__).parent))
from native_clothing import approved_surface,round_hood_crown

ROOT=Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
LIB=Path(r'C:\Otherreach_External_Assets')
part=sys.argv[sys.argv.index('--')+1]
BASE={'base_shirt':'frontier_linen_shirt',
      'standing_cap':'frontier_standing_cap','boots':'frontier_sealed_boots',
      'cloak':'frontier_wool_cloak'}[part]
fit=os.environ.get('OTHERREACH_SOURCE_BODY','veth_f')
assert fit in ('veth_f','veth_m'), 'Author one Average source per body family'
family_scale=1.126 if fit=='veth_m' else 1.0
OUT=ROOT/os.environ.get('OTHERREACH_SOURCE_SET','sources/fits')/fit/part
OUT.mkdir(parents=True,exist_ok=True)
approved=ROOT/'approved_clothing_sources'/(fit+'.glb')
digest=hashlib.sha256(approved.read_bytes()).hexdigest()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(approved))
arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
body=approved_surface(fit);body.shape_key_clear()
for mod in list(body.modifiers):body.modifiers.remove(mod)
body.data.transform(body.matrix_world);body.matrix_world=Matrix.Identity(4)
refs=set(bpy.data.objects)

def active(obj):
 bpy.ops.object.select_all(action='DESELECT');obj.hide_set(False)
 obj.select_set(True);bpy.context.view_layer.objects.active=obj

def apply(obj,mod):
 active(obj);bpy.ops.object.modifier_apply(modifier=mod.name)

def scan(name,folder,tint=None):
 mat=bpy.data.materials.new(name);mat.use_nodes=True;mat['scan_attached']=True
 nodes=mat.node_tree.nodes;links=mat.node_tree.links;bs=nodes.get('Principled BSDF')
 bs.inputs['Roughness'].default_value=.85
 path=LIB/'PolyHaven/textures'/folder
 for channel,socket,linear in [('diff','Base Color',False),('rough','Roughness',True),('nor_gl',None,True)]:
  files=list(path.glob(f'*_{channel}_4k.jpg'))
  if not files and channel=='diff':files=list(path.glob('*_albedo_4k.jpg'))
  if not files:continue
  image=bpy.data.images.load(str(files[0]),check_existing=True).copy()
  image.scale(2048,2048)
  if linear:image.colorspace_settings.name='Non-Color'
  node=nodes.new('ShaderNodeTexImage');node.image=image
  if channel=='diff' and tint:
   import numpy as np
   pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels)
   pixels=pixels.reshape(-1,4);pixels[:,:3]*=np.array(tint,dtype=np.float32)
   image.pixels.foreach_set(pixels.ravel());image.pack()
  if socket:links.new(node.outputs['Color'],bs.inputs[socket])
  else:
   normal=nodes.new('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.22
   links.new(node.outputs['Color'],normal.inputs['Color']);links.new(normal.outputs[0],bs.inputs['Normal'])
 return mat

def uv_scan(obj):
 active(obj);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
 bpy.ops.uv.cube_project(cube_size=.35);bpy.ops.object.mode_set(mode='OBJECT')

def shell(obj,thickness):
 active(obj);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
 bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
 if obj.data.has_custom_normals:obj.data.normals_split_custom_set([(0,0,0)]*len(obj.data.loops))
 mod=obj.modifiers.new('Sewn material thickness','SOLIDIFY');mod.thickness=thickness;mod.offset=1
 apply(obj,mod)

def import_obj(path):
 old=set(bpy.data.objects)
 bpy.ops.wm.obj_import(filepath=str(path),forward_axis='NEGATIVE_Z',up_axis='Y')
 obj=next(o for o in set(bpy.data.objects)-old if o.type=='MESH')
 obj.data.transform(obj.matrix_world);obj.matrix_world=Matrix.Identity(4)
 return obj

source=None;licence=None
if part=='base_shirt':
 source=LIB/'MakeHuman/community/clothes/1800_Elvs_Male_shirt_untucked_bd1/mens_shirt_untuck_elvbhp1f.obj'
 licence='CC-BY; Elvaerwyn; source OBJ and MHClO headers agree'
 obj=import_obj(source)
 for v in obj.data.vertices:
  p=v.co.copy();v.co=Vector((p.x*.087,p.y*.084-.01,p.z*.074+.895))
  # The source's long sleeves use a straighter arm; place their cuffs on this A-pose once.
  sleeve=max(0,min(1,(abs(v.co.x)-.20)/.23))
  v.co.y+=.055*sleeve
  v.co.z-=.100*sleeve
  v.co*=family_scale
  if fit=='veth_m':
   side='l' if v.co.x>0 else 'r'
   along=arm.matrix_world.to_3x3()@(arm.data.bones['hand_'+side].head_local-arm.data.bones['lowerarm_'+side].head_local)
   cuff=max(0,min(1,(abs(v.co.x)-.40*family_scale)/(.09*family_scale)))
   v.co+=along.normalized()*.035*cuff
 # This licensed shirt already has sewn panels, collar, buttons and hem.
 # Keep its garment volume; do not simulate detached buttons as free cloth.
 shell(obj,.002)
 obj.data.materials.clear();obj.data.materials.append(scan('frontier_linen_cloth','rough_linen'))
 uv_scan(obj)
elif part=='standing_cap':
 source=LIB/'MakeHuman/community/clothes/3235_Monk_s_robe_hood/Monks_Hood.obj'
 licence='CC0; Donitz; source OBJ and MHClO headers agree'
 obj=import_obj(source)
 for v in obj.data.vertices:
  p=v.co.copy();v.co=Vector((p.x*.122,p.y*.104+.002,p.z*.105+.731))
 round_hood_crown(obj,bpy.data.objects['native_'+fit+'_head'])
 shell(obj,.004)
 obj.data.materials.clear();obj.data.materials.append(scan('frontier_hood_wool','poly_wool_herringbone',(.65,.60,.52)))
 uv_scan(obj)
elif part=='boots':
 source=LIB/'Sourced_BlenderKit_20261018/armour/medieval_leather_boots_59193b/medieval_leather_boots_59193b_game.glb'
 licence='BlenderKit royalty-free; Polary Studios; see held credits.txt'
 old=set(bpy.data.objects);bpy.ops.import_scene.gltf(filepath=str(source))
 base=next(o for o in set(bpy.data.objects)-old if o.type=='MESH' and not '_LOD' in o.name)
 base.data.transform(base.matrix_world);base.matrix_world=Matrix.Identity(4)
 obj=base.copy();obj.data=base.data.copy();bpy.context.collection.objects.link(obj)
 ankle=arm.matrix_world@arm.data.bones['foot_l'].head_local
 for v in obj.data.vertices:
  p=v.co.copy();v.co=Vector((ankle.x+p.x*.94,p.y*.94-.071,p.z*1.16+.001))
 # Native Mirror preserves winding/tangents; reflecting vertex coordinates alone
 # reverses one boot and makes its armour-face selection incomplete.
 active(obj);mirror=obj.modifiers.new('Paired boot with correct winding','MIRROR')
 mirror.use_axis=(True,False,False);apply(obj,mirror)
 bpy.data.objects.remove(base,do_unlink=True)
elif part=='cloak':
 licence='Original panel pattern; scanned Poly Haven wool CC0'
 verts=[];faces=[];segments=32;rings=10
 # Open-front short back mantle. Its hem hangs beside the back, leaving the
 # moving arms free instead of making a wide umbrella over their sleeves.
 for row in range(rings):
  t=row/(rings-1)
  for j in range(segments):
   a=math.pi/6+(2*math.pi/3)*j/(segments-1)
   x=(.23+.02*t)*math.cos(a)
   y=.02+(.08+.05*t)*math.sin(a)
   # A short hanging back panel, with no horizontal shoulder wings. Its
   # authored rest shape is supported by the spine rather than moving arms.
   z=1.43-.05*abs(math.cos(a))-.24*t+.004*math.sin(a*4)*t
   verts.append((x,y,z))
 for row in range(rings-1):
  for j in range(segments-1):
   k=row*segments+j;n=k+1
   faces.append((k,k+segments,n+segments,n))
 mesh=bpy.data.meshes.new('Shoulder mantle panels');mesh.from_pydata(verts,[],faces);mesh.update()
 obj=bpy.data.objects.new('Shoulder mantle',mesh);bpy.context.collection.objects.link(obj)
 active(obj)
 sub=obj.modifiers.new('Sewn back panel resolution','SUBSURF');sub.levels=1;apply(obj,sub)
 clearance=obj.modifiers.new('Native back panel clearance','SHRINKWRAP')
 clearance.target=body;clearance.wrap_method='NEAREST_SURFACEPOINT';clearance.wrap_mode='OUTSIDE';clearance.offset=.035
 apply(obj,clearance)
 # Sew the top edge to the actual jerkin; the lower panel keeps its hanging
 # rest shape. This closes the gap without binding the hem to the arms.
 old=set(bpy.data.objects)
 path=ROOT/os.environ.get('OTHERREACH_SOURCE_SET','sources/fits')/fit/'vest/frontier_sealed_vest_native.glb'
 bpy.ops.import_scene.gltf(filepath=str(path))
 under=next(o for o in set(bpy.data.objects)-old if o.type=='MESH' and o.name=='frontier_sealed_vest')
 group=obj.vertex_groups.new(name='Sewn mantle collar')
 for vertex in obj.data.vertices:
  weight=max(0,min(1,(vertex.co.z-1.35)/.04))
  if weight:group.add([vertex.index],weight,'REPLACE')
 active(obj);collar=obj.modifiers.new('Native sewn mantle collar','SHRINKWRAP')
 collar.target=under;collar.wrap_method='NEAREST_SURFACEPOINT';collar.wrap_mode='OUTSIDE_SURFACE'
 collar.offset=.008;collar.vertex_group=group.name
 name=group.name;apply(obj,collar);obj.vertex_groups.remove(obj.vertex_groups[name])
 for extra in set(bpy.data.objects)-old:bpy.data.objects.remove(extra,do_unlink=True)
 shell(obj,.004)
 obj.data.materials.append(scan('frontier_mantle_wool','poly_wool_herringbone',(.65,.60,.52)))
 uv_scan(obj)

obj.name=BASE
for poly in obj.data.polygons:
 poly.use_smooth=True
 if part not in ('boots','trousers'):poly.material_index=0
obj.vertex_groups.clear()
if part=='standing_cap':obj.vertex_groups.new(name='head').add(list(range(len(obj.data.vertices))),1,'REPLACE')
else:
 target=body
 for group in target.vertex_groups:obj.vertex_groups.new(name=group.name)
 dt=obj.modifiers.new('Standard approved skin binding','DATA_TRANSFER');dt.object=target
 dt.use_vert_data=True;dt.data_types_verts={'VGROUP_WEIGHTS'};dt.vert_mapping='POLYINTERP_NEAREST'
 dt.layers_vgroup_select_src='ALL';dt.layers_vgroup_select_dst='NAME';apply(obj,dt)
skin=obj.modifiers.new('Approved skeleton','ARMATURE');skin.object=arm
obj.parent=arm;obj.matrix_world=Matrix.Identity(4)
lods=[]
for level,ratio in [(1,.65),(2,.4),(3,.25)]:
 lod=obj.copy();lod.data=obj.data.copy();lod.name=BASE+f'_LOD{level}';bpy.context.collection.objects.link(lod)
 dec=lod.modifiers.new('Native LOD','DECIMATE');dec.ratio=ratio;dec.delimit={'MATERIAL'};apply(lod,dec);lods.append(lod)
active(obj)
for item in [*lods,arm]:item.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(OUT/(BASE+'_native.glb')),export_format='GLB',use_selection=True,
 export_skins=True,export_def_bones=False,export_influence_nb=8,export_animations=False)
for reference in refs:
 if reference!=arm:reference.hide_render=True
for lod in lods:lod.hide_render=True
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/(BASE+'.blend')))
assert hashlib.sha256(approved.read_bytes()).hexdigest()==digest
provenance={'source':str(source),'licence':licence,'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest() if source else None,
 'approved_body_sha256':digest,'authoring_fit':fit,'body_edits':False,
 'construction':'Licensed sewn source or original short mantle; Solidify and standard skin binding',
 'acceptance':'PENDING player-visible review; not transferred to other fits'}
(OUT/'source.json').write_text(json.dumps(provenance,indent=2))
print('SOURCE_READY_FOR_REVIEW',part,flush=True)
