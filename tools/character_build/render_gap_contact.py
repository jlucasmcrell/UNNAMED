"""CPU Workbench authoring thumbnails on the full native body; no runtime verdict is implied."""
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index('--')+1:]
body, garments, out = map(Path, args)
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(body))
native = set(bpy.data.objects)
for obj in native:
    if obj.type == 'MESH':
        obj.hide_render = not ('_LOD0' in obj.name and any(p in obj.name for p in ('Body', 'Head', 'Eyes', 'Hair', 'Eyebrow', 'Eyelash')))
pieces = {}
for directory in sorted(garments.glob('gap_*')):
    if not directory.name.endswith('__'+body.stem): continue
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(directory/(directory.name+'_rigged.glb')))
    pieces[directory.name.split('__')[0]] = [o for o in set(bpy.data.objects)-before if o.type == 'MESH']
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = scene.render.resolution_y = 640
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.display.shading.light = 'STUDIO'
scene.display.shading.studio_light = 'paint.sl'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = 'BOTH'
scene.display.shading.curvature_ridge_factor = 1.5
scene.display.shading.curvature_valley_factor = 1.1
scene.display.shading.background_type = 'WORLD'
scene.world = bpy.data.worlds.new('Authoring world')
scene.world.color = (.08, .08, .08)
scene.view_settings.view_transform = 'Standard'
camera = bpy.data.objects.new('Garment authoring camera', bpy.data.cameras.new('Garment authoring camera'))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = 'ORTHO'
camera.data.ortho_scale = .64
rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
def turn(rig, name, axis, angle):
    bone = rig.pose.bones.get(name)
    if bone is None: return
    world = rig.matrix_world @ bone.matrix
    pivot = world.translation.copy()
    rotation = Matrix.Rotation(math.radians(angle), 4, axis)
    bone.matrix = rig.matrix_world.inverted() @ Matrix.Translation(pivot) @ rotation @ Matrix.Translation(-pivot) @ world
    bpy.context.view_layer.update()


def reset():
    for rig in rigs:
        for bone in rig.pose.bones: bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


for part in pieces:
    for name, meshes in pieces.items():
        for obj in meshes: obj.hide_render = name != part
    for view, eye in [('front', (0, -2, 1.40)), ('threequarter', (1.5, -2, 1.43)), ('back', (0, 2, 1.40))]:
        camera.location = eye
        camera.rotation_euler = (Vector((0, 0, 1.32))-camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(out/(part+'_'+view+'.png'))
        bpy.ops.render.render(write_still=True)
    # Authoring stress pose only: the garment and body receive identical native joint transforms.
    # No stress-pose keys or altered body rests are exported into the game.
    for rig in rigs:
        for name in ('LeftUpperArm', 'RightUpperArm'):
            turn(rig, name, 'Y', -65 if name.startswith('Left') else 65)
    bpy.context.view_layer.update()
    camera.location = (0, -2, 1.42)
    camera.rotation_euler = (Vector((0, 0, 1.32))-camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(out/(part+'_arms_raised_QA.png'))
    bpy.ops.render.render(write_still=True)
    reset()
    for label, axis, angle in [('head_left','Z',80),('head_right','Z',-80),('head_down','X',30),('head_up','X',-30)]:
        for rig in rigs:
            turn(rig, 'Neck', axis, angle*.45)
            turn(rig, 'Head', axis, angle*.55)
        scene.render.filepath = str(out/(part+'_'+label+'_QA.png'))
        bpy.ops.render.render(write_still=True)
        reset()
for meshes in pieces.values():
    for obj in meshes: obj.hide_render = False
bpy.ops.wm.save_as_mainfile(filepath=str(out/'gap_authoring_review.blend'))
(out/'preview_receipt.json').write_text(json.dumps({'renderer': 'CPU Workbench authoring thumbnails',
    'runtime_verdict': False, 'body': str(body), 'garments': str(garments), 'frames': 24}, indent=2))
