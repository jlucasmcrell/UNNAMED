"""Export the Kal's stone growths as head-attached parts, authored on the real Kal head (brief F1 fix 7, route A).

Each growth is built in Blender against the creator base body: for a landmark direction the script finds the actual head
surface point (the furthest vertex of the skin in that direction from the head joint), sinks the shard 3 mm into that
surface and grows it outward along the surface normal, then skins the whole thing 100% to Head. Exported like any other
part, so the creator loads it with the same contract and it lands exactly on the head for every face preset (the presets
are shape keys on this same head; the 3 mm sink absorbs their displacement).

Usage in Blender: -b --python export_kal_growths.py -- <work-dir> <catalogue-json> [<option-id> ...]
"""
import json
import re
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
work = Path(args[0])
catalogue = json.loads(Path(args[1]).read_text(encoding='utf-8'))
only = set(args[2:])

# The directions are unit vectors in world space from the head joint: (1,0,0) is the character's right, +Z is up, +Y is
# forward. Each shard is (direction, shard length as a share of the head radius, base radius share, sides).
STYLES = {
    'crown_crest': [
        ((0.0, 0.62, 0.78), 0.55, 0.16, 4), ((0.0, 0.30, 0.95), 0.72, 0.19, 4), ((0.0, -0.15, 0.99), 0.68, 0.18, 4),
        ((0.0, -0.58, 0.82), 0.52, 0.16, 4), ((0.0, -0.86, 0.50), 0.38, 0.13, 4),
    ],
    'temple_ridges': [
        ((0.55, 0.42, 0.72), 0.34, 0.13, 4), ((0.62, 0.02, 0.78), 0.30, 0.12, 4), ((0.60, -0.42, 0.68), 0.26, 0.11, 4),
        ((-0.55, 0.42, 0.72), 0.34, 0.13, 4), ((-0.62, 0.02, 0.78), 0.30, 0.12, 4), ((-0.60, -0.42, 0.68), 0.26, 0.11, 4),
    ],
    'crystal_outcrops': [
        ((0.0, 0.10, 1.0), 0.62, 0.15, 3), ((0.42, 0.45, 0.80), 0.44, 0.12, 3), ((-0.40, 0.35, 0.85), 0.40, 0.11, 3),
        ((0.30, -0.55, 0.78), 0.36, 0.10, 3), ((-0.34, -0.60, 0.72), 0.34, 0.10, 3),
    ],
    'brow_horns': [
        ((0.42, 0.70, 0.58), 0.62, 0.10, 4), ((-0.42, 0.70, 0.58), 0.62, 0.10, 4),
    ],
    'jaw_ridges': [
        ((0.72, 0.28, -0.36), 0.26, 0.09, 4), ((0.62, 0.52, -0.52), 0.24, 0.08, 4),
        ((-0.72, 0.28, -0.36), 0.26, 0.09, 4), ((-0.62, 0.52, -0.52), 0.24, 0.08, 4),
    ],
    'calcite_fringe': [
        ((0.30, 0.72, -0.62), 0.40, 0.08, 4), ((0.0, 0.82, -0.57), 0.44, 0.08, 4), ((-0.30, 0.72, -0.62), 0.40, 0.08, 4),
        ((0.16, 0.62, -0.78), 0.36, 0.07, 4), ((-0.16, 0.62, -0.78), 0.36, 0.07, 4),
    ],
}
# Which slot the creator offers it in: a scalp growth takes the Kal's hair step, a jaw growth the beard slot.
SLOTS = {'crown_crest': 'hair', 'temple_ridges': 'hair', 'crystal_outcrops': 'hair', 'brow_horns': 'hair',
         'jaw_ridges': 'beard', 'calcite_fringe': 'beard'}


def slug(value):
    return re.sub(r'[^a-z0-9]+', '_', value.lower()).strip('_')


def head_surface(direction, origin, radius, skin):
    """The furthest skin vertex from the head joint along a direction: that is the real surface there."""
    best, best_at = None, -1e9
    wanted = Vector(direction).normalized()
    for vertex in skin.data.vertices:
        point = skin.matrix_world @ vertex.co
        offset = point - origin
        if offset.length > radius * 1.6:
            continue
        reach = offset.normalized().dot(wanted)
        if reach > best_at:
            best, best_at = (point, offset.normalized()), reach
    return best if best else (origin + wanted * radius, wanted)


def shard(bm, base, normal, length, width, sides):
    """A pyramid from a sunk base to a tip along the surface normal."""
    up = normal.normalized()
    side = up.cross(Vector((0, 1, 0)))
    if side.length < 0.5:
        side = up.cross(Vector((1, 0, 0)))
    side.normalize()
    other = up.cross(side).normalized()
    tip = bm.verts.new(base + up * length)
    ring = []
    for i in range(sides):
        angle = 6.283185307179586 * i / sides
        ring.append(bm.verts.new(base + (side * __import__('math').cos(angle) + other * __import__('math').sin(angle)) * width))
    for i in range(sides):
        bm.faces.new((ring[i], ring[(i + 1) % sides], tip))
    bm.faces.new(tuple(reversed(ring)))


def build(base_glb, sex, style, destination):
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(base_glb))
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    skin = max((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: len(o.data.vertices))
    head = next(b for b in arm.pose.bones if b.name == 'Head')
    origin = arm.matrix_world @ head.head
    # The head's own size: the furthest skin vertex from the head joint inside the head region.
    # How far the skin reaches ABOVE the head joint is the head's height; the first cut took the furthest vertex
    # anywhere in the region (a shoulder-width point) and grew shards a third of a metre long.
    radius = min(max((skin.matrix_world @ v.co - origin).length for v in skin.data.vertices
                     if (skin.matrix_world @ v.co).z > origin.z + 0.01), 0.13)
    mesh = bpy.data.meshes.new('kal_growth_' + style)
    item = bpy.data.objects.new('kal_growth_' + style, mesh)
    bpy.context.collection.objects.link(item)
    bm = bmesh.new()
    for direction, length, width, sides in STYLES[style]:
        point, normal = head_surface(direction, origin, radius, skin)
        shard(bm, point - normal * 0.003, normal, length * radius * 0.7, width * radius * 0.7, sides)
    bm.to_mesh(mesh)
    bm.free()
    # A stone surface, so the part ships with a material the creator can tint to the chosen stone.
    stone = bpy.data.materials.new('kal_growth_stone')
    stone.use_nodes = True
    principled = stone.node_tree.nodes['Principled BSDF']
    principled.inputs['Base Color'].default_value = (0.55, 0.55, 0.57, 1.0)
    principled.inputs['Roughness'].default_value = 0.8
    item.data.materials.append(stone)
    group = item.vertex_groups.new(name='Head')
    group.add([v.index for v in mesh.vertices], 1.0, 'REPLACE')
    item.parent = arm
    modifier = item.modifiers.new('Armature', 'ARMATURE')
    modifier.object = arm
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    item.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=str(destination), export_format='GLB', use_selection=True,
                              export_animations=False, export_morph=False, export_skins=True, export_yup=True)
    return radius


identity = json.loads((work / 'game/primary/rigged' / 'humgen_creator_male' / 'identity_weights.json').read_text())
for sex, base_name in (('m', 'humgen_creator_male'), ('f', 'humgen_creator_female')):
    base_glb = work / 'game/primary/rigged' / base_name / (base_name + '_rigged.glb')
    identities = json.loads((work / 'game/primary/rigged' / base_name / 'identity_weights.json').read_text())
    for style in STYLES:
        if only and style not in only:
            continue
        identifier = 'humgen_creator_' + sex + '_growth_' + slug(style)
        folder = work / 'game/primary/rigged' / identifier
        folder.mkdir(parents=True, exist_ok=True)
        radius = build(base_glb, sex, style, folder / (identifier + '_rigged.glb'))
        (folder / 'creator_part.json').write_text(json.dumps(dict(kind='growth', option=style, slot=SLOTS[style],
            target_base=base_name, target_body_sha256=identities['target_body_sha256'],
            head_radius=round(radius, 5), source='authored on the creator base head'), indent=2))
        print('KAL_GROWTH_DONE', identifier, round(radius, 4), flush=True)
print('KAL_GROWTHS_COMPLETE', flush=True)
