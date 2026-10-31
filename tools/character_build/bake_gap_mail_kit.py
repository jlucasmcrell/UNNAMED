"""Bake the original four-in-one ring geometry with native Cycles CPU, never a generative model.

An original seamless 50.4x48mm tile supplies a shared material for the mail pieces.
The working mesh, full4K bakes and recipe are retained. No existing source is edited.
"""
import bpy,math,sys,json,hashlib
from pathlib import Path
from mathutils import Vector

out=Path(sys.argv[sys.argv.index('--')+1]);out.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=8
scene.render.threads_mode='FIXED';scene.render.threads=4
scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=.018;scene.render.bake.max_ray_distance=.03;scene.render.bake.margin=8
def material(name,colour):
 m=bpy.data.materials.new(name);m.use_nodes=True;m.node_tree.nodes.clear()
 emit=m.node_tree.nodes.new('ShaderNodeEmission');emit.inputs['Color'].default_value=(*colour,1)
 output=m.node_tree.nodes.new('ShaderNodeOutputMaterial');m.node_tree.links.new(emit.outputs[0],output.inputs['Surface']);return m,emit
metal,metal_emit=material('Original woven steel, unlit colour bake only',(.26,.29,.27))
back,back_emit=material('Coverage background',(0,0,0))
vertices=[];faces=[]
for row in range(-1,10):
 for column in range(-1,10):
  x=(column+(row%2)*.5)*.0063;y=row*.006;tilt=.38 if row%2 else -.38;start=len(vertices)
  for v in range(13):
   a=v*2*math.pi/12
   for u in range(65):
    b=u*2*math.pi/64;r=.0041+.00085*math.cos(a)
    local=Vector((r*math.cos(b),r*math.sin(b),.00085*math.sin(a)))
    vertices.append((x+local.x,y+local.y*math.cos(tilt)-local.z*math.sin(tilt),local.y*math.sin(tilt)+local.z*math.cos(tilt)))
  for v in range(12):
   for u in range(64):
    i=start+v*65+u;faces.append((i,i+1,i+66,i+65))
mesh=bpy.data.meshes.new('Original4in1Rings');mesh.from_pydata(vertices,[],faces);mesh.update()
high=bpy.data.objects.new('Original4in1Rings',mesh);scene.collection.objects.link(high);mesh.materials.append(metal)
for face in mesh.polygons:face.use_smooth=True
def plane(name,z):
 m=bpy.data.meshes.new(name);m.from_pydata([(0,0,z),(.0504,0,z),(.0504,.048,z),(0,.048,z)],[],[(0,1,2,3)]);m.update()
 uv=m.uv_layers.new(name='UVMap')
 for loop,value in zip(uv.data,[(0,0),(1,0),(1,1),(0,1)]):loop.uv=value
 obj=bpy.data.objects.new(name,m);scene.collection.objects.link(obj);return obj
background=plane('Backing for rays through actual ring holes',-.004);background.data.materials.append(back)
low=plane('Native tangent bake target',-.006);target=bpy.data.materials.new('Bake target');target.use_nodes=True;low.data.materials.append(target)
node=target.node_tree.nodes.new('ShaderNodeTexImage');target.node_tree.nodes.active=node
for obj in bpy.context.selected_objects:obj.select_set(False)
for obj in [high,background,low]:obj.select_set(True)
bpy.context.view_layer.objects.active=low
for name,kind in [('mail_normal','NORMAL'),('mail_colour','EMIT'),('mail_coverage','EMIT')]:
 if name=='mail_coverage':metal_emit.inputs['Color'].default_value=(1,1,1,1)
 image=bpy.data.images.new(name,4096,4096,alpha=True,float_buffer=True)
 image.colorspace_settings.name='Non-Color' if name!='mail_colour' else 'sRGB';node.image=image
 bpy.ops.object.bake(type=kind)
 image.filepath_raw=str(out/(name+'.png'));image.file_format='PNG';image.save()
 print('NATIVE_MAIL_BAKE',name,flush=True)
metal_emit.inputs['Color'].default_value=(.26,.29,.27,1)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'original_4in1_mail_tile.blend'),compress=True)
recipe=dict(status='ORIGINAL_GEOMETRY_NATIVE_CPU_BAKES_REQUIRE_REVIEW',tile_m=[.0504,.048],ring_radius_m=.0041,wire_radius_m=.00085,pitch_m=[.0063,.006],alternating_tilt_radians=.38,periodic_margin_rings=True,source_vertices=len(vertices),source_quads=len(faces),cycles_device=scene.cycles.device,cycles_samples=scene.cycles.samples,dimensions=[4096,4096],normal='OpenGL tangent normal, native selected-to-active rays',colour='Unlit steel base colour from original ring mesh; emission is a bake tool only, never a runtime emissive material',coverage='Native geometry coverage pass; separate original PNG retained',license='Original OTHERREACH authored geometry and bakes, no third-party bitmap or generative model',files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.glob('*.png')})
(out/'MAIL_KIT_SOURCE.json').write_text(json.dumps(recipe,indent=2))
