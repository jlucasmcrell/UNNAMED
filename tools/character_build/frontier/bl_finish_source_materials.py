"""Finish garment-source PBR and the hood/mantle on feminine Average, then transfer once."""
import argparse,shutil
import bpy,bmesh,json,os,sys,hashlib
import numpy as np
from pathlib import Path
sys.path.insert(0,str(Path(__file__).parent))
from native_clothing import bind_mantle_torso
from mathutils import Vector
LIB=Path(r'C:\Otherreach_External_Assets')
PARTS={'base_shirt':'frontier_linen_shirt','vest':'frontier_sealed_vest','boots':'frontier_sealed_boots',
       'standing_cap':'frontier_standing_cap','cloak':'frontier_wool_cloak','lamework_jack':'frontier_lamework_jack',
       'lame_coif':'frontier_lame_coif','lame_sabatons':'frontier_lame_sabatons'}
parser=argparse.ArgumentParser()
parser.add_argument('--source-root',required=True,type=Path)
parser.add_argument('--output-root',required=True,type=Path)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
SOURCE=args.source_root.resolve(strict=True)
OUT=args.output_root.resolve()
assert SOURCE!=OUT, 'Keep the source snapshot; finish into a separate directory'
for unchanged in ('trousers','gloves'):
 shutil.copytree(SOURCE/unchanged,OUT/unchanged,dirs_exist_ok=True)

def active(obj):
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj

def picture(path,linear=False,transform=None):
 image=bpy.data.images.load(str(path),check_existing=False)
 if linear:image.colorspace_settings.name='Non-Color'
 image.scale(2048,2048)
 if transform:
  pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels)
  pixels=pixels.reshape(-1,4);pixels[:,:3]=transform(pixels[:,:3])
  image.pixels.foreach_set(pixels.ravel())
 image.pack();return image

def pbr(kind):
 mat=bpy.data.materials.new('frontier_lamework_metal_blackened' if kind=='blackened_iron' else 'frontier_'+kind+'_pbr');mat.use_nodes=True;mat.use_backface_culling=False
 mat['scan_attached']=True
 nodes=mat.node_tree.nodes;links=mat.node_tree.links;shader=nodes.get('Principled BSDF')
 if kind=='oatmeal_linen':
  directory=LIB/'PolyHaven/textures/rough_linen'
  colour=directory/'rough_linen_diff_4k.jpg';normal=directory/'rough_linen_nor_gl_4k.jpg';rough=directory/'rough_linen_arm_4k.jpg'
  # This actual scan is blue. Bake out the dye while retaining its photographed weave.
  tint=lambda rgb:(rgb@np.array([.2126,.7152,.0722],dtype=np.float32))[:,None]*np.array([1.0,.88,.69],dtype=np.float32)
  strength=.55
 elif kind=='brown_boot_leather':
  directory=LIB/'PolyHaven/textures/brown_leather'
  colour=directory/'brown_leather_albedo_4k.jpg';normal=directory/'brown_leather_nor_gl_4k.jpg';rough=directory/'brown_leather_arm_4k.jpg'
  tint=None;strength=.35
 else:
  directory=LIB/'Sourced_CC0_20261018/cloth_rope_leather_metal/Metal013/2K-JPG'
  colour=directory/'Metal013_2K-JPG_Color.jpg';normal=directory/'Metal013_2K-JPG_NormalGL.jpg';rough=directory/'Metal013_2K-JPG_Roughness.jpg'
  tint=lambda rgb:(.08+.22*(rgb@np.array([.2126,.7152,.0722],dtype=np.float32)))[:,None]*np.array([.98,.99,1.],dtype=np.float32)
  strength=.18
 texture=nodes.new('ShaderNodeTexImage');texture.image=picture(colour,transform=tint)
 links.new(texture.outputs['Color'],shader.inputs['Base Color'])
 texture=nodes.new('ShaderNodeTexImage')
 if kind=='blackened_iron':
  texture.image=picture(rough,True,lambda rgb:np.clip(rgb,.56,.60))
  links.new(texture.outputs['Color'],shader.inputs['Roughness'])
  metal=nodes.new('ShaderNodeTexImage');metal.image=picture(directory/'Metal013_2K-JPG_Metalness.jpg',True,lambda rgb:np.clip(rgb,.85,1.))
  links.new(metal.outputs['Color'],shader.inputs['Metallic'])
 else:
  texture.image=picture(rough,True)
  separate=nodes.new('ShaderNodeSeparateColor');separate.mode='RGB'
  links.new(texture.outputs['Color'],separate.inputs['Color']);links.new(separate.outputs['Green'],shader.inputs['Roughness'])
  shader.inputs['Metallic'].default_value=0
 normal_image=nodes.new('ShaderNodeTexImage');normal_image.image=picture(normal,True)
 normal_node=nodes.new('ShaderNodeNormalMap');normal_node.inputs['Strength'].default_value=strength
 links.new(normal_image.outputs['Color'],normal_node.inputs['Color']);links.new(normal_node.outputs['Normal'],shader.inputs['Normal'])
 return mat

for part,base in PARTS.items():
 bpy.ops.wm.read_factory_settings(use_empty=True)
 part_source=SOURCE/part
 src=part_source/(base+'_native.glb');before=hashlib.sha256(src.read_bytes()).hexdigest()
 bpy.ops.import_scene.gltf(filepath=str(src))
 obj=bpy.data.objects[base];arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
 for other in list(bpy.data.objects):
  if other.type=='MESH' and other!=obj:bpy.data.objects.remove(other,do_unlink=True)
 active(obj)
 for mod in list(obj.modifiers):obj.modifiers.remove(mod)
 if part in ('standing_cap','lame_coif'):
  # Keep crown/face clearance. Only the rear, high peak falls towards the shoulders.
  inverse=obj.matrix_world.inverted()
  for v in obj.data.vertices:
   p=obj.matrix_world@v.co
   t=max(0,min(1,(p.y-.025)/.10))*max(0,min(1,(p.z-1.55)/.085))
   p.y+=.065*t;p.z-=.105*t
   v.co=inverse@p
 if part in ('lamework_jack','lame_coif'):
  # Close the conspicuous vertical two-millimetre gaps, keeping horizontal
  # lame overlaps. The accepted backing/clearance and silhouette stay intact.
  edit=bmesh.new();edit.from_mesh(obj.data);remaining=set(edit.verts)
  while remaining:
   start=remaining.pop();island={start};stack=[start]
   while stack:
    v=stack.pop()
    for edge in v.link_edges:
     n=edge.other_vert(v)
     if n in remaining:remaining.remove(n);island.add(n);stack.append(n)
   center=sum((obj.matrix_world@v.co).x for v in island)/len(island)
   inverse=obj.matrix_world.inverted()
   for v in island:
    point=obj.matrix_world@v.co;point.x=center+(point.x-center)*1.04;v.co=inverse@point
  edit.to_mesh(obj.data);edit.free()
 if part=='cloak':
  # Preserve the accepted drape; remove the skin-helper deformation field.
  bind_mantle_torso(obj,arm)
 materials={}
 for i,old in enumerate(obj.data.materials):
  kind='oatmeal_linen' if 'linen' in old.name else ('brown_boot_leather' if part=='boots' else ('blackened_iron' if 'lamework_metal' in old.name else None))
  if kind:
   if kind not in materials:materials[kind]=pbr(kind)
   obj.data.materials[i]=materials[kind]
 if part in ('lamework_jack','lame_coif','lame_sabatons'):
  active(obj);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
  bpy.ops.uv.cube_project(cube_size=.25);bpy.ops.object.mode_set(mode='OBJECT')
 skin=obj.modifiers.new('Approved skeleton','ARMATURE');skin.object=arm
 lods=[]
 for level,ratio in [(1,.65),(2,.4),(3,.25)]:
  lod=obj.copy();lod.data=obj.data.copy();lod.name=base+f'_LOD{level}';bpy.context.collection.objects.link(lod);active(lod)
  dec=lod.modifiers.new('Native LOD','DECIMATE');dec.ratio=ratio;dec.delimit={'MATERIAL'};bpy.ops.object.modifier_apply(modifier=dec.name);lods.append(lod)
 active(obj)
 for item in [arm,*lods]:item.select_set(True)
 out=OUT/part;out.mkdir(parents=True,exist_ok=True)
 bpy.ops.export_scene.gltf(filepath=str(out/(base+'_native.glb')),export_format='GLB',use_selection=True,
  export_skins=True,export_def_bones=False,export_influence_nb=8,export_animations=False,export_tangents=True)
 for lod in lods:lod.hide_render=True
 bpy.ops.wm.save_as_mainfile(filepath=str(out/(base+'.blend')))
 assert hashlib.sha256(src.read_bytes()).hexdigest()==before
 original=json.loads((part_source/'source.json').read_text())
 original.update(source_accepted_shape=str(src),source_accepted_shape_sha256=before,
  review8_finish='PBR colour/ARM channels, rear hood fall, primary torso/shoulder mantle binding',
  acceptance='PENDING four-fit native review9',body_edits=False)
 (out/'source.json').write_text(json.dumps(original,indent=2))
 print('REVIEW8_SOURCE_FINISH',part,flush=True)
