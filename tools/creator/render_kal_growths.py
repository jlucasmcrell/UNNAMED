"""Render every Kal growth part on the real creator head, CPU only, so all six can be judged without the GPU lock.

Each growth is a shipped part on the creator base body; this loads that base, brings in the part, frames the head and renders it
with Workbench so it costs no GPU and no scene on the game's behalf. The images are the exported art, not a procedural stand-in.
"""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
root = Path(args[0])
out = Path(args[1])
out.mkdir(parents=True, exist_ok=True)
STYLES = ['crown_crest', 'temple_ridges', 'crystal_outcrops', 'brow_horns', 'jaw_ridges', 'calcite_fringe']

bpy.context.scene.render.engine = 'BLENDER_WORKBENCH'
bpy.context.scene.render.resolution_x = 700
bpy.context.scene.render.resolution_y = 700
bpy.context.scene.render.film_transparent = False
bpy.context.scene.display.shading.light = 'STUDIO'
bpy.context.scene.display.shading.color_type = 'SINGLE'
bpy.context.scene.display.shading.single_color = (0.72, 0.72, 0.74)
bpy.context.scene.display.shading.show_cavity = True

for sex, base in (('m', 'humgen_creator_male'), ('f', 'humgen_creator_female')):
    body_glb = root / base / (base + '_rigged.glb')
    for style in STYLES:
        part = root / ('humgen_creator_' + sex + '_growth_' + style)
        glb = next(part.glob('*_rigged.glb'), None)
        if glb is None:
            print('MISSING', sex, style, flush=True)
            continue
        bpy.ops.wm.read_homefile(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=str(body_glb))
        base_objects = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(glb))
        growth = [o for o in bpy.data.objects if o not in base_objects and o.type == 'MESH']
        if not growth:
            print('NO_GROWTH_MESH', sex, style, flush=True)
            continue
        # Frame from the growth's own bounds plus the head joint, so the camera cannot miss either.
        head = None
        for o in bpy.data.objects:
            if o.type == 'ARMATURE':
                for b in o.data.bones:
                    if b.name == 'Head':
                        head = o.matrix_world @ b.head_local
        low = Vector((1e9, 1e9, 1e9))
        high = Vector((-1e9, -1e9, -1e9))
        for o in growth:
            for corner in o.bound_box:
                point = o.matrix_world @ Vector(corner)
                low = Vector((min(low.x, point.x), min(low.y, point.y), min(low.z, point.z)))
                high = Vector((max(high.x, point.x), max(high.y, point.y), max(high.z, point.z)))
        centre = (low + high) / 2
        span = max((high - low).length, .05)
        if head is not None:
            centre = (centre + head) / 2
            span = max(span, (centre - head).length * 2 + .05)
        camera = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
        bpy.context.collection.objects.link(camera)
        camera.data.lens = 50
        offset = Vector((.55, -.95, .35)).normalized() * span * 2.4
        camera.location = centre + offset
        camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
        bpy.context.scene.camera = camera
        path = out / ('growth_' + sex + '_' + style + '.png')
        bpy.context.scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)
        print('GROWTH_RENDERED', sex, style, path.name, flush=True)
print('GROWTHS_RENDERED', flush=True)
