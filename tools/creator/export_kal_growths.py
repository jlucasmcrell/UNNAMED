"""Export the Kal's stone growths as head-attached parts, authored on the real Kal head (brief F1 fix 7, route A).

Every growth is built in Blender against the creator base body. Landmarks are directions in a basis taken from the rig itself
(up is world up, forward comes from the jaw bone, side is derived), so the script does not guess which way the base faces.
For each landmark the real skin surface point in that direction is found, the shard is sunk 4 mm into it and grown outward
along the surface normal, and the whole growth is skinned 100% to Head. It then ships as a part, so the creator loads it with
the same contract and it lands on the head for every face preset.

Usage in Blender: -b --python export_kal_growths.py -- <work-dir> <catalogue-json> [<style> ...]
"""
import json
import math
import re
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
work = Path(args[0])
only = set(args[2:])

# Each shard is (forward, side, up, length, base width, sides) as weights on the rig-derived basis. Forward points out of the
# face, up is the top of the skull, side is the character's left.
STYLES = {
    'crown_crest': [
        (0.55, 0.0, 0.85, 0.55, 0.30, 5), (0.15, 0.0, 1.0, 0.70, 0.34, 5), (-0.25, 0.0, 1.0, 0.66, 0.33, 5),
        (-0.65, 0.0, 0.75, 0.50, 0.29, 5), (-0.9, 0.0, 0.42, 0.36, 0.24, 5),
    ],
    'temple_ridges': [
        (0.42, 0.55, 0.72, 0.34, 0.26, 4), (0.05, 0.62, 0.78, 0.30, 0.24, 4), (-0.42, 0.6, 0.68, 0.26, 0.22, 4),
        (0.42, -0.55, 0.72, 0.34, 0.26, 4), (0.05, -0.62, 0.78, 0.30, 0.24, 4), (-0.42, -0.6, 0.68, 0.26, 0.22, 4),
    ],
    'crystal_outcrops': [
        (0.1, 0.0, 1.0, 0.62, 0.28, 4), (0.45, 0.42, 0.8, 0.44, 0.24, 4), (0.35, -0.4, 0.85, 0.4, 0.22, 4),
        (-0.55, 0.3, 0.78, 0.36, 0.2, 4), (-0.6, -0.34, 0.72, 0.34, 0.2, 4),
    ],
    'brow_horns': [
        (0.55, 0.42, 0.5, 0.66, 0.24, 4), (0.55, -0.42, 0.5, 0.66, 0.24, 4),
    ],
    'jaw_ridges': [
        (0.72, 0.5, -0.4, 0.26, 0.2, 4), (0.86, 0.3, -0.55, 0.24, 0.2, 4),
        (0.72, -0.5, -0.4, 0.26, 0.2, 4), (0.86, -0.3, -0.55, 0.24, 0.2, 4),
    ],
    'calcite_fringe': [
        (0.85, 0.22, -0.62, 0.40, 0.2, 4), (0.92, 0.0, -0.7, 0.44, 0.2, 4), (0.85, -0.22, -0.62, 0.40, 0.2, 4),
        (0.7, 0.12, -0.8, 0.36, 0.18, 4), (0.7, -0.12, -0.8, 0.36, 0.18, 4),
    ],
}
SLOTS = {'crown_crest': 'hair', 'temple_ridges': 'hair', 'crystal_outcrops': 'hair', 'brow_horns': 'hair',
         'jaw_ridges': 'beard', 'calcite_fringe': 'beard'}


def slug(value):
    return re.sub(r'[^a-z0-9]+', '_', value.lower()).strip('_')


def basis(arm):
    """Up, forward and side for this base body, read from the rig: the jaw joint points forward and down from the head joint."""
    head = arm.matrix_world @ arm.pose.bones['Head'].head
    up = Vector((0, 0, 1))
    jaw = next((b for b in arm.pose.bones if b.name == 'Jaw'), None)
    if jaw is None:
        raise SystemExit('KAL_GROWTH_NO_JAW: the rig has no Jaw bone to take the face direction from')
    forward = (arm.matrix_world @ jaw.head) - head
    forward.z = 0.0
    forward.normalize()
    side = forward.cross(up).normalized()
    return head, up, forward, side


def surface(direction, origin, reach, skin):
    """The furthest skin vertex from the head joint along a direction: that is the real surface there."""
    wanted = direction.normalized()
    best, best_at = None, -1e9
    for vertex in skin.data.vertices:
        point = skin.matrix_world @ vertex.co
        offset = point - origin
        if offset.length > reach or offset.length < 1e-6:
            continue
        aim = offset.normalized().dot(wanted)
        if aim > best_at:
            best, best_at = (point, offset.normalized()), aim
    return best if best else (origin + wanted * reach * .5, wanted)


def shard(bm, base, normal, length, width, sides):
    """A pyramid from a sunk base to a tip along the surface normal."""
    up = normal.normalized()
    side = up.cross(Vector((0, 1, 0)))
    if side.length < .5:
        side = up.cross(Vector((1, 0, 0)))
    side.normalize()
    other = up.cross(side).normalized()
    tip = bm.verts.new(base + up * length)
    ring = [bm.verts.new(base + (side * math.cos(2 * math.pi * i / sides) + other * math.sin(2 * math.pi * i / sides)) * width)
            for i in range(sides)]
    for i in range(sides):
        bm.faces.new((ring[i], ring[(i + 1) % sides], tip))
    bm.faces.new(tuple(reversed(ring)))


def build(base_glb, style, destination):
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(base_glb))
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    skin = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
    origin, up, forward, side = basis(arm)
    reach = max((skin.matrix_world @ v.co - origin).length for v in skin.data.vertices
                if (skin.matrix_world @ v.co).z > origin.z + .01)
    mesh = bpy.data.meshes.new('kal_growth_' + style)
    item = bpy.data.objects.new('kal_growth_' + style, mesh)
    bpy.context.collection.objects.link(item)
    bm = bmesh.new()
    for f, s, u, length, width, sides in STYLES[style]:
        direction = (forward * f + side * s + up * u).normalized()
        point, normal = surface(direction, origin, min(reach * 1.4, .3), skin)
        shard(bm, point - normal * .004, normal, length * reach * .62, width * reach * .5, sides)
    bm.to_mesh(mesh)
    bm.free()
    stone = bpy.data.materials.new('kal_growth_stone')
    stone.use_nodes = True
    principled = stone.node_tree.nodes['Principled BSDF']
    principled.inputs['Base Color'].default_value = (.34, .34, .36, 1.0)
    principled.inputs['Roughness'].default_value = .92
    item.data.materials.append(stone)
    item.vertex_groups.new(name='Head').add([v.index for v in mesh.vertices], 1.0, 'REPLACE')
    item.parent = arm
    item.modifiers.new('Armature', 'ARMATURE').object = arm
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    item.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=str(destination), export_format='GLB', use_selection=True,
                              export_animations=False, export_morph=False, export_skins=True, export_yup=True)
    return reach


for sex, base_name in (('m', 'humgen_creator_male'), ('f', 'humgen_creator_female')):
    base_glb = work / 'game/primary/rigged' / base_name / (base_name + '_rigged.glb')
    identities = json.loads((work / 'game/primary/rigged' / base_name / 'identity_weights.json').read_text())
    for style in STYLES:
        if only and style not in only:
            continue
        identifier = 'humgen_creator_' + sex + '_growth_' + slug(style)
        folder = work / 'game/primary/rigged' / identifier
        folder.mkdir(parents=True, exist_ok=True)
        reach = build(base_glb, style, folder / (identifier + '_rigged.glb'))
        (folder / 'creator_part.json').write_text(json.dumps(dict(kind='growth', option=style, slot=SLOTS[style],
            target_base=base_name, target_body_sha256=identities['target_body_sha256'], head_reach=round(reach, 5),
            source='authored on the creator base head'), indent=2))
        print('KAL_GROWTH_DONE', identifier, round(reach, 4), flush=True)
print('KAL_GROWTHS_COMPLETE', flush=True)
