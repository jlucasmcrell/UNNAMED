"""Native material contact frames with CPU Cycles, retaining all original embedded maps. No GPU lease used."""
import json
from pathlib import Path
import sys
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--')+1:]
art, output = map(Path, args)
output.mkdir(parents=True, exist_ok=True)
report = {'renderer': 'Cycles CPU', 'threads': 2, 'samples': 48, 'in_game_verdict': False, 'assets': []}
for folder in sorted((art/'ready').iterdir()):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(folder/(folder.name+'.glb')))
    objects = [o for o in bpy.data.objects if o.type == 'MESH']
    corners = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    low = Vector(tuple(min(p[i] for p in corners) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in corners) for i in range(3)))
    size, centre = high-low, (high+low)/2
    extent = max(size)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES';scene.cycles.device = 'CPU'
    scene.cycles.samples = 48;scene.cycles.use_denoising = True
    scene.cycles.denoiser = 'OPENIMAGEDENOISE'
    scene.render.threads_mode = 'FIXED';scene.render.threads = 2
    scene.render.resolution_x = 960;scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Neutral source review world');scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.12,.13,.15,1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = .25
    scene.view_settings.view_transform = 'AgX'
    bpy.ops.mesh.primitive_plane_add(size=extent*5, location=(centre.x,centre.y,low.z-.005))
    floor = bpy.context.object
    material = bpy.data.materials.new('Review floor');material.diffuse_color = (.11,.12,.13,1)
    floor.data.materials.append(material)
    for name, offset, power, light_size in [('Key',(-1,-1.5,2),700,1.0),('Fill',(1.5,-.5,1),220,1.5),('Rim',(.5,1.5,1.5),650,.8)]:
        data = bpy.data.lights.new(name,'AREA');data.energy=power*(extent/3)**2;data.shape='DISK';data.size=extent*light_size
        light = bpy.data.objects.new(name,data);scene.collection.objects.link(light)
        light.location = centre+Vector(offset)*extent
        light.rotation_euler=(centre-light.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Native source contact camera');data.type='ORTHO';data.ortho_scale=extent*1.35
    camera=bpy.data.objects.new('Native source contact camera',data);scene.collection.objects.link(camera);scene.camera=camera
    for view, offset in [('front',(0,-2,.7)),('threequarter',(1.6,-2,1.0))]:
        camera.location=centre+Vector(offset)*extent
        camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(output/(folder.name+'_'+view+'.png'))
        bpy.ops.render.render(write_still=True)
    report['assets'].append({'id':folder.name,'native_dimensions_m':list(size),'meshes':len(objects)})
(output/'CPU_NATIVE_MATERIAL_RECEIPT.json').write_text(json.dumps(report,indent=2))
