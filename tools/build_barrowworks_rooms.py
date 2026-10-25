"""CPU-only Blender assembly of the Barrowworks' rooms, corridors and rock over the authoritative collision.

The rock is one shell: the walkable volume (rooms, arched doorways, corridors) voxel-remeshed and pushed outward by noise, so it
never comes inside a collision face and has no seams between rooms and corridors. Timber, rails, carts, tools and rubble are
licensed Old Mine (Megascans) modules from convert_barrowworks_mine.ps1. No generation service, network or collision changes.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import re
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector, noise

parser = argparse.ArgumentParser()
parser.add_argument('--depot', default=r'D:\Otherreach_Depot\barrowworks_codex')
parser.add_argument('--region', default=str(Path(__file__).resolve().parents[1] / 'content/regions/barrowworks.yaml'))
parser.add_argument('--rooms', help='Comma-separated room numbers for an art iteration; retain other exports')
parser.add_argument('--voxel', type=float, default=.12, help='Rock shell resolution in metres')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
depot = Path(args.depot)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def load(path, axis_to_x=False, centre=False):
    """One mesh from a GLB, joined, its bounds centred on the origin (feet on z=0 unless centred), longest axis on X if asked."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    imported = set(bpy.data.objects) - before
    meshes = []
    for source in imported:
        if source.type != 'MESH': continue
        data = source.data.copy(); data.transform(source.matrix_world)
        obj = bpy.data.objects.new(path.stem, data); bpy.context.collection.objects.link(obj); meshes.append(obj)
    for source in imported: bpy.data.objects.remove(source, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]; bpy.ops.object.join(); obj = bpy.context.object
    bpy.context.collection.objects.unlink(obj)
    if axis_to_x:
        extent = [max(v.co[i] for v in obj.data.vertices)-min(v.co[i] for v in obj.data.vertices) for i in range(3)]
        axis = max(range(3), key=lambda i: extent[i])
        if axis == 2: obj.data.transform(Matrix.Rotation(math.pi/2, 4, 'Y'))
        elif axis == 1: obj.data.transform(Matrix.Rotation(math.pi/2, 4, 'Z'))
    low = Vector(tuple(min(v.co[i] for v in obj.data.vertices) for i in range(3)))
    high = Vector(tuple(max(v.co[i] for v in obj.data.vertices) for i in range(3)))
    obj.data.transform(Matrix.Translation(Vector((-(low.x+high.x)/2, -(low.y+high.y)/2, -(low.z+high.z)/2 if centre else -low.z))))
    return obj, high - low

ready = depot/'ready'
sources = {}
for kind in ('beam', 'beam_b', 'rails'):
    sources[kind] = load(ready/f'bw_codex_mine_{kind}'/f'bw_codex_mine_{kind}.glb', axis_to_x=True, centre=kind != 'rails')
for kind in ('cart', 'log_cart', 'pickaxe', 'shovel', 'hammer', 'board', 'slab', 'rubble', 'spoil'):
    sources[kind] = load(ready/f'bw_codex_mine_{kind}'/f'bw_codex_mine_{kind}.glb')
for kind in ('pipe', 'boiler'):
    sources[kind] = load(ready/f'bw_codex_{kind}'/f'bw_codex_{kind}.glb')
# The production lantern (first-party/CC0) at each light, with its flame darkened: the light itself is the binding's.
primary = Path(r'C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary')
sources['lantern'] = load(primary/'ready/prop_ph_wooden_lantern_01/prop_ph_wooden_lantern_01.glb')
for mat in sources['lantern'][0].data.materials:
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if not bsdf: continue
    bsdf.inputs['Emission Strength'].default_value = 0
    bsdf.inputs['Roughness'].default_value = .6
recipes = json.loads(Path(__file__).with_name('barrowworks_art_direction.json').read_text())

text = Path(args.region).read_text(encoding='utf-8')
pattern = re.compile(r'- \{ id: (\w+), box_m: \[([^\]]+)\], height_m: ([\d.]+)')
structures = [(name, list(map(float, box.split(','))), float(height)) for name, box, height in pattern.findall(text)]
corridors = json.loads(Path(__file__).with_name('barrowworks_geometry.json').read_text())['corridors']
selected = [int(n) for n in args.rooms.split(',')] if args.rooms else list(range(1, 14))
existing = json.loads((depot/'room_manifest.json').read_text()) if (depot/'room_manifest.json').exists() else []
records = [r for r in existing if r['asset_id'] not in {f'bw_codex_room_{n:02d}' for n in selected}] if args.rooms else []
WALL = .6

def walls_of(n): return [b for name, b, _ in structures if name.startswith(f'r{n:02d}_wall')]
rooms = {n: (min(b[0] for b in walls_of(n)), min(b[1] for b in walls_of(n)), max(b[2] for b in walls_of(n)), max(b[3] for b in walls_of(n)))
         for n in range(1, 14)}
# Everything a body walks to or reads: dressing keeps clear of these (world x, z).
keep_clear = [((b[0]+b[2])/2, (b[1]+b[3])/2) for name, b, _ in structures if re.match(r'r\d\d_(?!wall)', name)]
keep_clear += [tuple(map(float, m.split(','))) for m in re.findall(r'position_m: \[([^\]]+)\]', text)]
keep_clear += [(50.0, 10.0), (8.0, 38.0)]

def gaps(n):
    """Each doorway in a room's walls: (side, wall line, start, end), the line being the wall's minimum x or z."""
    x0, z0, x1, z1 = rooms[n]
    found = []
    for side in 'nsew':
        horizontal = side in 'ns'
        line = z1 - WALL if side == 'n' else z0 if side == 's' else x0 if side == 'w' else x1 - WALL
        on_side = (lambda b: abs(b[1]-line) < .01 and b[3]-b[1] < WALL+.01) if horizontal else (lambda b: abs(b[0]-line) < .01 and b[2]-b[0] < WALL+.01)
        spans = sorted((b[0], b[2]) if horizontal else (b[1], b[3]) for b in walls_of(n) if on_side(b))
        found += [(side, line, a1, b0) for (_, a1), (b0, _) in zip(spans, spans[1:]) if b0 - a1 > .5]
    return found

def outside_rooms(rect):
    """A corridor rectangle less every room's footprint: the corridors' own walkable pieces."""
    pieces = [rect]
    for (x0, z0, x1, z1) in rooms.values():
        out = []
        for (a0, b0, a1, b1) in pieces:
            if a1 <= x0 or a0 >= x1 or b1 <= z0 or b0 >= z1: out.append((a0, b0, a1, b1)); continue
            if a0 < x0: out.append((a0, b0, x0, b1))
            if a1 > x1: out.append((x1, b0, a1, b1))
            if b0 < z0: out.append((max(a0, x0), b0, min(a1, x1), z0))
            if b1 > z1: out.append((max(a0, x0), z1, min(a1, x1), b1))
        pieces = out
    return [p for p in pieces if p[2]-p[0] > .05 and p[3]-p[1] > .05]
legs = [piece for _, rect in corridors for piece in outside_rooms(rect)]

tex = Path(r'C:\Otherreach_External_Assets\UE_Exports\Scene_OldMine\Textures')
def scan_material(name, stem, gain):
    """A Megascans surface (albedo, normal, roughness from the ORDp pack) at its source resolution. The albedo is lifted by `gain`:
    the Old Mine scene was lit for Lumen bounce, and under a lantern's direct light alone its coal-dark maps read black."""
    material = bpy.data.materials.new(name); material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = nodes['Principled BSDF']
    def image(suffix, colour):
        node = nodes.new('ShaderNodeTexImage'); node.image = bpy.data.images.load(str(tex/f'{stem}_{suffix}.exr'))
        if not colour: node.image.colorspace_settings.name = 'Non-Color'
        return node
    albedo = image('D', True)
    pixels = np.empty(len(albedo.image.pixels), np.float32); albedo.image.pixels.foreach_get(pixels)
    pixels = pixels.reshape(-1, 4); pixels[:, :3] = np.minimum(pixels[:, :3]*gain, 1)
    lifted = bpy.data.images.new(f'{stem}_D', *albedo.image.size, float_buffer=True)
    lifted.pixels.foreach_set(pixels.ravel()); lifted.pack(); albedo.image = lifted
    links.new(albedo.outputs[0], bsdf.inputs['Base Color'])
    normal = nodes.new('ShaderNodeNormalMap'); links.new(image('N', False).outputs[0], normal.inputs['Color'])
    links.new(normal.outputs[0], bsdf.inputs['Normal'])
    split = nodes.new('ShaderNodeSeparateColor'); links.new(image('ORDp', False).outputs[0], split.inputs[0])
    links.new(split.outputs[1], bsdf.inputs['Roughness'])
    return material
rock_material = scan_material('bw_mine_rock', 'T_Wall_Mine_Rock_01', 1.35)
floor_material = scan_material('bw_mine_floor', 'T_Floor_Mine_Coal_01', 2.4)
wood_material = sources['beam'][0].data.materials[0]

def world_uvs(obj, metres):
    """Box-projected UVs in world metres: one texel density everywhere, continuous across rooms and corridors."""
    uv = obj.data.uv_layers.get('UVMap') or obj.data.uv_layers.new(name='UVMap')
    for poly in obj.data.polygons:
        axis = max(range(3), key=lambda i: abs(poly.normal[i])); axes = [i for i in range(3) if i != axis]
        for loop in poly.loop_indices:
            co = obj.matrix_world @ obj.data.vertices[obj.data.loops[loop].vertex_index].co
            uv.data[loop].uv = (co[axes[0]]/metres, co[axes[1]]/metres)

def box(x0, z0, x1, z1, y0, y1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0+x1)/2, -(z0+z1)/2, (y0+y1)/2))
    obj = bpy.context.object; obj.scale = (x1-x0, z1-z0, y1-y0)
    return obj

def vault(x0, z0, x1, z1, spring, rise, along_x):
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=1, depth=1, location=((x0+x1)/2, -(z0+z1)/2, spring))
    obj = bpy.context.object
    if along_x: obj.rotation_euler = (0, math.pi/2, 0); obj.scale = (rise, (z1-z0)/2, x1-x0)
    else: obj.rotation_euler = (math.pi/2, 0, 0); obj.scale = ((x1-x0)/2, rise, z1-z0)
    return obj

def fbm(p, octaves):
    total, amp = 0, .5
    for _ in range(octaves):
        total += amp*noise.noise(p, noise_basis='PERLIN_NEW'); amp *= .5; p = p*2.03
    return total

def rock_shell():
    """The walkable volume - rooms to their 4 m walls under a vault, 3 m doorways arched from 3 m, corridors under a low vault -
    remeshed as one closed surface and displaced outward only, then faced inward."""
    parts = []
    for n, (x0, z0, x1, z1) in rooms.items():
        ix0, iz0, ix1, iz1 = x0+WALL, z0+WALL, x1-WALL, z1-WALL
        parts.append(box(ix0, iz0, ix1, iz1, -.6, 4.05))
        parts.append(vault(ix0, iz0, ix1, iz1, 4.0, .55 + .07*min(ix1-ix0, iz1-iz0), ix1-ix0 >= iz1-iz0))
        for side, line, a, b in gaps(n):
            rect = (a, line-.35, b, line+WALL+.35) if side in 'ns' else (line-.35, a, line+WALL+.35, b)
            parts.append(box(*rect, -.6, 3.05))
            parts.append(vault(*rect, 3.0, .9, side in 'ew'))
    for (a0, b0, a1, b1) in legs:
        parts.append(box(a0, b0, a1, b1, -.6, 4.05))
        parts.append(vault(a0, b0, a1, b1, 4.0, .5, a1-a0 >= b1-b0))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts: obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join(); shell = bpy.context.object
    remesh = shell.modifiers.new('volume', 'REMESH'); remesh.mode = 'VOXEL'; remesh.voxel_size = args.voxel; remesh.adaptivity = 0
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    mesh = bmesh.new(); mesh.from_mesh(shell.data)
    bmesh.ops.delete(mesh, geom=[v for v in mesh.verts if v.co.z < -.3], context='VERTS')
    mesh.normal_update()
    for v in mesh.verts:
        p = v.co
        # Bedding planes (stretched noise), lumps, and Voronoi joints the face has spalled along.
        strata = fbm(Vector((p.x*.3, p.y*.3, p.z*1.7)), 4)
        lumps = fbm(p*.9, 5)
        cells = noise.voronoi(p*1.1, distance_metric='DISTANCE')[0]
        joint = max(0, 1 - (cells[1]-cells[0])/.18)
        f = max(0, min(1, .45 + .75*strata + .7*lumps - .22*joint))
        v.co = p + v.normal*(.05 + (.62 if v.normal.z > .6 else .5)*f)
    mesh.to_mesh(shell.data); mesh.free()
    shell.data.flip_normals()
    budget = shell.modifiers.new('budget', 'DECIMATE'); budget.ratio = .45
    bpy.ops.object.modifier_apply(modifier=budget.name)
    for poly in shell.data.polygons: poly.use_smooth = True
    shell.data.materials.append(rock_material)
    world_uvs(shell, 2.0)
    return shell

def split_shell(shell):
    """The shell's faces by owner: the room whose footprint holds the face, else the corridor leg nearest it - one mesh per leg,
    so each corridor lantern's shadow and the camera cull them leg by leg."""
    owners = list(rooms) + [f'leg{i}' for i in range(len(legs))]
    shell.data.materials.clear()
    for owner in owners: shell.data.materials.append(bpy.data.materials.new(f'owner_{owner}'))
    for poly in shell.data.polygons:
        x, z = poly.center.x, -poly.center.y
        room = next((i for i, n in enumerate(rooms) if rooms[n][0] <= x <= rooms[n][2] and rooms[n][1] <= z <= rooms[n][3]), None)
        poly.material_index = room if room is not None else len(rooms) + min(range(len(legs)), key=lambda i: math.hypot(
            max(legs[i][0]-x, 0, x-legs[i][2]), max(legs[i][1]-z, 0, z-legs[i][3])))
    bpy.ops.object.select_all(action='DESELECT'); shell.select_set(True); bpy.context.view_layer.objects.active = shell
    bpy.ops.mesh.separate(type='MATERIAL')
    parts = {}
    for obj in bpy.context.selected_objects:
        owner = obj.data.materials[obj.data.polygons[0].material_index].name.removeprefix('owner_')
        obj.data.materials.clear(); obj.data.materials.append(rock_material)
        parts[int(owner) if owner.isdigit() else owner] = obj
    return parts

def link(name, data):
    obj = bpy.data.objects.new(name, data); bpy.context.collection.objects.link(obj)
    return obj

def place(kind, at, scale=(1, 1, 1), yaw=0, tilt=(0, 0)):
    obj = link(kind, sources[kind][0].data.copy())
    obj.location = at; obj.rotation_euler = (tilt[0], tilt[1], yaw); obj.scale = scale
    return obj

def fit(kind, at, dimensions, yaw=0):
    size = sources[kind][1]
    return place(kind, at, tuple(dimensions[i]/size[i] for i in range(3)), yaw)

def beam(a, b, size=.24, rng=random):
    """A timber from a to b, in pieces of at most 2.5 m so the grain is never stretched, each a little turned by hand."""
    a, b = Vector(a), Vector(b)
    d = b - a; n = max(1, math.ceil(d.length/2.5)); out = []
    turn = d.to_track_quat('X', 'Z' if abs(d.z) < .9*d.length else 'Y')
    for i in range(n):
        kind = rng.choice(('beam', 'beam_b'))
        source, extent = sources[kind]
        obj = link(kind, source.data.copy())
        roll = Quaternion((1, 0, 0), rng.choice((0, math.pi/2, math.pi, -math.pi/2)) + rng.uniform(-.04, .04))
        obj.matrix_world = Matrix.LocRotScale(a + d*((i+.5)/n), turn @ roll, (d.length/n/extent.x, size/extent.y, size/extent.z))
        out.append(obj)
    return out

def plank(name, at, size, material):
    """A sawn board or shelf in the mine's timber, UV'd in world metres."""
    obj = box(at[0]-size[0]/2, -at[1]-size[1]/2, at[0]+size[0]/2, -at[1]+size[1]/2, at[2]-size[2]/2, at[2]+size[2]/2)
    obj.name = name; obj.data.materials.append(material)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    world_uvs(obj, 1.2)
    return obj

def flat(name, at, size, colour, metallic=0., roughness=.7):
    material = bpy.data.materials.get(name+'_finish')
    if material is None:
        material = bpy.data.materials.new(name+'_finish'); material.use_nodes = True
        bsdf = material.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = (*colour, 1); bsdf.inputs['Metallic'].default_value = metallic
        bsdf.inputs['Roughness'].default_value = roughness
    obj = box(at[0]-size[0]/2, -at[1]-size[1]/2, at[0]+size[0]/2, -at[1]+size[1]/2, at[2]-size[2]/2, at[2]+size[2]/2)
    obj.name = name; obj.data.materials.append(material)
    return obj

def floor(rects):
    """The mine floor 1 cm over the collision plane, under every wall's foot: one quad per rectangle, world UVs. Not on the plane: the
    region's flat terrain is drawn there too, and two coplanar surfaces z-fight, its triangles flickering through under TAA jitter."""
    mesh = bpy.data.meshes.new('bw_floor'); bm = bmesh.new()
    for (x0, z0, x1, z1) in rects:
        bm.faces.new([bm.verts.new((x, -z, .01)) for x, z in ((x0, z0), (x1, z0), (x1, z1), (x0, z1))][::-1])
    bm.to_mesh(mesh); bm.free()
    obj = link('bw_floor', mesh); obj.data.materials.append(floor_material); world_uvs(obj, 3.0)
    return obj

def clear_of(x, z, distance): return all(math.hypot(x-px, z-pz) >= distance for px, pz in keep_clear)

def near_gap(n, x, z, distance):
    for side, line, a, b in gaps(n):
        gx, gz = ((a+b)/2, line+WALL/2) if side in 'ns' else (line+WALL/2, (a+b)/2)
        if math.hypot(x-gx, z-gz) < distance: return True
    return False

def shoring(n, rng):
    """Posts against the long walls carrying wall plates, tie beams across where the span allows; spacing from the room's recipe."""
    x0, z0, x1, z1 = rooms[n]; ix0, iz0, ix1, iz1 = x0+WALL, z0+WALL, x1-WALL, z1-WALL
    ceiling = recipes[str(n)]['ceiling']
    if ceiling in ('low_rock', 'exposed_rock'): spacing, ties = 4.6, False
    elif ceiling == 'court_trusses': spacing, ties = 3.6, False
    else: spacing, ties = 2.4 if ceiling in ('archive_ribs', 'machine_rails', 'heavy_timber') else 3.2, True
    along_x = ix1-ix0 >= iz1-iz0
    length = (ix1-ix0) if along_x else (iz1-iz0)
    count = max(2, round(length/spacing))
    pieces = []
    for side in (0, 1):
        plate = []
        for i in range(count+1):
            t = (ix0 if along_x else iz0) + .3 + i*(length-.6)/count
            x, z = (t, iz0+.08 if side == 0 else iz1-.08) if along_x else (ix0+.08 if side == 0 else ix1-.08, t)
            if near_gap(n, x, z, 2.1) or not clear_of(x, z, 1.4): continue
            pieces += beam((x, -z, -.02), (x, -z, 3.92), .26, rng)
            plate.append((x, z))
            if ties and side == 0:
                ox, oz = (x, iz1-.08) if along_x else (ix1-.08, z)
                if not near_gap(n, ox, oz, 2.1):
                    pieces += beam((x, -z, 3.98), (ox, -oz, 3.98), .26, rng)
        for (ax, az), (bx, bz) in zip(plate, plate[1:]):
            pieces += beam((ax, -az, 3.84), (bx, -bz, 3.84), .22, rng)
    return pieces

def door_frames(n, rng):
    """Every doorway framed on the room's side: posts inside the jambs (the 3 m clear width is kept) and a cap at the arch spring."""
    pieces = []
    for side, line, a, b in gaps(n):
        inner = line + (WALL if side in 'sw' else 0)
        face = inner + (-.13 if side in 'sw' else .13)
        for t in (a-.14, b+.14):
            x, z = (t, face) if side in 'ns' else (face, t)
            pieces += beam((x, -z, -.02), (x, -z, 3.0), .26, rng)
        ends = [(a-.45, face), (b+.45, face)] if side in 'ns' else [(face, a-.45), (face, b+.45)]
        pieces += beam((ends[0][0], -ends[0][1], 3.13), (ends[1][0], -ends[1][1], 3.13), .28, rng)
    return pieces

def lanterns(lights):
    """A lantern at each authored light, hung from an iron rod into the rock above."""
    pieces = []
    for lx, h, lz, colour, energy, distance in lights:
        pieces.append(fit('lantern', (lx, -lz, h-.35), (.30, .30, .48)))
        pieces.append(flat('lantern_rod', (lx, -lz, h+1.43), (.025, .025, 2.6), (.06, .055, .05), .8, .5))
    return pieces

def rubble(n, rng, local):
    """Spoil and fallen slabs in the corners and at a few wall feet, never at a doorway or anything a body uses."""
    x0, z0, x1, z1 = rooms[n]; ix0, iz0, ix1, iz1 = x0+WALL, z0+WALL, x1-WALL, z1-WALL
    pieces = []
    for cx, cz in ((ix0, iz0), (ix1, iz0), (ix0, iz1), (ix1, iz1)):
        x, z = cx + (.55 if cx == ix0 else -.55), cz + (.55 if cz == iz0 else -.55)
        if near_gap(n, x, z, 2.4) or not clear_of(x, z, 1.6): continue
        pieces.append(fit(rng.choice(('rubble', 'spoil')), local(x, z, 0), (1.3, 1.2, .14), rng.uniform(0, 6.28)))
        if rng.random() < .6: pieces.append(fit('slab', local(x+rng.uniform(-.2, .2), z+rng.uniform(-.2, .2), 0), (.9, .55, .2), rng.uniform(0, 6.28)))
    return pieces

def lean(kind, local, x, z, yaw):
    """A tool at its scanned size, stood against a wall with its foot out from the face."""
    return place(kind, local(x, z, 0), yaw=yaw, tilt=(0, .22))

def rails(local, a, b, rng):
    """A wooden-sleepered track at the floor, laid in whole 2.5 m modules."""
    a, b = Vector(a), Vector(b); d = b - a; n = max(1, round(d.length/2.5)); size = sources['rails'][1]
    return [place('rails', local(*(a + d*((i+.5)/n)), 0), (d.length/n/size.x, 1, 1), math.atan2(-d.y, d.x)) for i in range(n)]

def localise(obj, cx, cz):
    obj.location -= Vector((cx, -cz, 0))

def localise_to(obj, cx, cz):
    localise(obj, cx, cz)
    return obj

def joined(pieces, name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in pieces: obj.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    obj = bpy.context.object; obj.data = obj.data.copy()
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.name = name
    return obj

def export_parts(asset_id, groups, record):
    """Several meshes in one model, each culled on its own; no lighter levels."""
    parts = [joined(group, f'{asset_id}_{i:02d}') for i, group in enumerate(groups)]
    output = ready/asset_id; output.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts: obj.select_set(True)
    name = asset_id + '.glb'
    bpy.ops.export_scene.gltf(filepath=str(output/name), export_format='GLB', use_selection=True,
                              export_yup=True, export_apply=True, export_lights=False)
    record = dict(record, asset_id=asset_id, outputs=[{'file': name, 'meshes': len(parts),
                  'triangles': sum(len(p.vertices)-2 for obj in parts for p in obj.data.polygons),
                  'bytes': (output/name).stat().st_size, 'sha256': hashlib.sha256((output/name).read_bytes()).hexdigest()}])
    (output/(asset_id+'_meta.json')).write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
    for obj in parts: bpy.data.objects.remove(obj, do_unlink=True)
    print('BARROWWORKS_ROOM ' + json.dumps(record), flush=True)
    return record

def export(asset_id, pieces, record, lods=True):
    obj = joined(pieces, asset_id)
    output = ready/asset_id; output.mkdir(parents=True, exist_ok=True)
    files = []
    for level, ratio in enumerate((1, .5, .2) if lods else (1,)):
        target = obj if level == 0 else obj.copy()
        if level:
            target.data = obj.data.copy(); bpy.context.collection.objects.link(target)
            bpy.context.view_layer.objects.active = target
            mod = target.modifiers.new('LOD', 'DECIMATE'); mod.ratio = ratio
            bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.ops.object.select_all(action='DESELECT'); target.select_set(True); bpy.context.view_layer.objects.active = target
        name = asset_id + (f'_lod{level}' if level else '') + '.glb'
        bpy.ops.export_scene.gltf(filepath=str(output/name), export_format='GLB', use_selection=True,
                                  export_yup=True, export_apply=True, export_lights=False)
        files.append({'file': name, 'triangles': sum(len(p.vertices)-2 for p in target.data.polygons),
                      'bytes': (output/name).stat().st_size, 'sha256': hashlib.sha256((output/name).read_bytes()).hexdigest()})
        if level: bpy.data.objects.remove(target, do_unlink=True)
    record = dict(record, asset_id=asset_id, outputs=files)
    (output/(asset_id+'_meta.json')).write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
    bpy.data.objects.remove(obj, do_unlink=True)
    print('BARROWWORKS_ROOM ' + json.dumps(record), flush=True)
    return record

provenance = {'category': 'building', 'licence': 'Fab Standard Personal',
              'source_url': 'Fab library: Scene_OldMine (Quixel Megascans), C:\\Otherreach_External_Assets\\Fab\\FabAssets\\Content\\Scene_OldMine',
              'creator': 'Quixel Megascans (Old Mine scene); lantern Poly Haven; rock shell and assembly by Claude',
              'source_modules': [k for k in sources], 'texture_resolutions': ['source', 1024, 512],
              'rock_shell': f'walkable volume voxel-remeshed at {args.voxel} m, displaced outward 0.05-0.67 m; collision faces never crossed',
              'licence_evidence': r'C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LIBRARY_CATALOGUE.md',
              'collision': 'authoritative region footprint; camera proxy only', 'shippable': True}

shell = split_shell(rock_shell())
for number in selected:
    rng = random.Random(number)
    x0, z0, x1, z1 = rooms[number]
    cx, cz = (x0+x1)/2, (z0+z1)/2
    ix0, iz0, ix1, iz1 = x0+WALL, z0+WALL, x1-WALL, z1-WALL
    width, depth = ix1-ix0, iz1-iz0
    def local(x, z, h): return (x-cx, -(z-cz), h)
    recipe = recipes[str(number)]
    room = shell[number]; localise(room, cx, cz)
    pieces = [room, localise_to(floor([(x0, z0, x1, z1)]), cx, cz)]
    world = shoring(number, rng) + door_frames(number, rng)
    for obj in world: localise(obj, cx, cz)
    pieces += world
    pieces += lanterns(recipe['lights'])
    pieces += rubble(number, rng, local)
    # Function first (03_RECOMMENDED_LEVEL_DESIGN): delivery groove at the threshold and intake, two work tracks on the sorting
    # floor, wet troughs in the wash and drain, the still's boiler, the archive's shelves, the headworks' trestle.
    if number == 1:
        pieces += rails(local, (cx, cz+1.5), (cx, iz1+WALL), rng)
    if number == 2:
        pieces += rails(local, (cx, iz0-WALL), (cx, cz-1.0), rng)
        pieces += [localise_to(p, cx, cz) for p in beam((cx-.8, -(cz-1.1), .12), (cx+.8, -(cz-1.1), .12), .22, rng)]
        pieces.append(place('shovel', local(ix0+.5, cz+3.2, .02), yaw=math.pi/2+.2))
    if number in (3, 4):
        x = -width/2+1.1
        for dx in (-.52, .52): pieces.append(flat('trough_iron', (x+dx, 0, .14), (.12, depth-2, .28), (.12, .15, .14), .6, .5))
        pieces.append(flat('shallow_water', (x, 0, .075), (.92, depth-2, .025), (.11, .24, .20), 0, .05))
        for y in (-depth/2+1, depth/2-1): pieces.append(flat('channel_grate', (x, y, .13), (1.1, .14, .12), (.12, .14, .12), .6, .5))
        if number == 4:
            for y in (-2, 0, 2): pieces.append(fit('slab', (width/2-.7, y, 0), (1.3, 1.5, .4), rng.uniform(0, 6.28)))
    if number == 3:
        for x in (-width*.3, width*.3): pieces.append(fit('pipe', (x, 0, 3.7), (.18, depth-.7, .18)))
    if number == 5:
        for z in (cz-2.2, cz+2.2): pieces += rails(local, (ix0+.4, z), (ix1-.4, z), rng)
        pieces.append(place('log_cart', local(ix0+1.6, cz+2.2, .12), yaw=math.pi/2))
        pieces.append(place('cart', local(ix0+1.4, cz-2.2, .12), yaw=math.pi/2))
        for y in (-depth/2+1.1, depth/2-1.1):
            for x in (-4, 0, 4):
                pieces.append(plank('sorting_bin', (x, y, .45), (1.4, 1.1, .9), wood_material))
                pieces.append(fit('spoil', (x, y, .9), (1.1, .8, .1), rng.uniform(0, 6.28)))
        for x in (-width/2+1, width/2-1):
            pieces.append(plank('sorting_table', (x, 0, 1), (1.4, 4, .12), wood_material))
            for y in (-1.5, 1.5): pieces += beam((x, y, 0), (x, y, .94), .16, rng)
        pieces.append(lean('pickaxe', local, ix1-.3, iz0+2.6, 0))
    if number == 6:
        x = width/2-1.6
        pieces.append(fit('boiler', (x, -depth/2+1.8, .1), (2.3, 2.0, 2.5)))
        pieces.append(fit('pipe', (x, -depth/2+3, 2.1), (.18, 2.5, .18)))
        for z in (.6, .9, 1.2, 1.5): pieces.append(fit('pipe', (x, -depth/2+4, z), (1.3, .10, .10)))
        pieces.append(flat('condensing_bath', (x, -depth/2+4, .3), (1.7, 1.1, .6), (.24, .18, .10), .5, .45))
        pieces.append(place('hammer', local(44.6, 64.5, .6), yaw=.4))
    if number in (7, 9):
        for sign in (-1, 1):
            x = sign*(width/2-.85)
            for y in (-depth/2+1.5, 0, depth/2-1.5):
                if not clear_of(cx+x, cz-y, 2.0) or near_gap(number, cx+x, cz-y, 2.6): continue
                for z in (.5, 1.25, 2.0):
                    pieces.append(plank('ledger_shelf', (x, y, z), (.65, 2.3, .06), wood_material))
                    for i in range(10):
                        lying = rng.random() < .15
                        size = (.48, .32, .06) if lying else (.48, .15, .46)
                        pieces.append(flat('bound_ledger', (x, y-1+i*.21, z+.03+size[2]/2), size,
                                           (.20+(i%3)*.04, .12+(i%2)*.03, .06+(i%4)*.012)))
                for dy in (-1.17, 1.17): pieces += beam((x, y+dy, 0), (x, y+dy, 2.45), .12, rng)
    if number == 10:
        for i in range(5): pieces.append(fit('board', local(ix1-.6, iz0+1.2, .04+i*.05), (2.2, .32, .05), rng.uniform(-.08, .08)))
        pieces.append(lean('pickaxe', local, ix0+.3, iz1-1.8, math.pi))
    if number == 12:
        # The measure's trestle: an A-frame at each end of the beam, a ridge over it.
        for z in (127.9, 134.1):
            for dx in (-.55, .55): pieces += beam(local(42.6+dx, z, 0), local(42.6, z, 2.7), .2, rng)
        pieces += beam(local(42.6, 127.6, 2.7), local(42.6, 134.4, 2.7), .22, rng)
        pieces.append(fit('board', local(57.0, 128.0, .02), (2.2, .32, .05), .3))
    records.append(export(f'bw_codex_room_{number:02d}', pieces, dict(provenance, room_identity=recipe)))

(depot/'room_manifest.json').write_text(json.dumps(sorted(records, key=lambda r: r['asset_id']), indent=2)+'\n', encoding='utf-8')
if args.rooms: sys.exit(0)

# The corridors, a mesh per leg: its part of the same shell, its floor, a shoring set every 3 m, and its lantern.
x0, z0 = min(b[0] for _, b in corridors), min(b[1] for _, b in corridors)
x1, z1 = max(b[2] for _, b in corridors), max(b[3] for _, b in corridors)
cx, cz = (x0+x1)/2, (z0+z1)/2
rng = random.Random(99)
groups = []
for index, (a0, b0, a1, b1) in enumerate(legs):
    pieces = [shell[f'leg{index}'], floor([(a0, b0, a1, b1)])]
    along_x = a1-a0 >= b1-b0
    length = (a1-a0) if along_x else (b1-b0)
    sets = max(1, round(length/3.0))
    for i in range(sets):
        t = (a0 if along_x else b0) + (i+.5)*length/sets
        ends = [(t, b0+.08), (t, b1-.08)] if along_x else [(a0+.08, t), (a1-.08, t)]
        # A set standing where two legs cross would stand in the other leg's walkway.
        if any(r != (a0, b0, a1, b1) and r[0]+.3 < x < r[2]-.3 and r[1]+.3 < z < r[3]-.3 for r in legs for x, z in ends): continue
        for x, z in ends: pieces += beam((x, -z, -.02), (x, -z, 3.95), .24, rng)
        pieces += beam((ends[0][0], -ends[0][1], 4.02), (ends[1][0], -ends[1][1], 4.02), .26, rng)
    # The lantern of each corridor whose centre is in this leg, 1.1 m off its centre line at gen_barrowworks_bindings.py's light.
    for _, (c0, d0, c1, d1) in corridors:
        mx, mz = (c0+c1)/2, (d0+d1)/2
        if min(range(len(legs)), key=lambda i: math.hypot(max(legs[i][0]-mx, 0, mx-legs[i][2]), max(legs[i][1]-mz, 0, mz-legs[i][3]))) == index:
            pieces += lanterns([(mx, 2.7, mz-1.1, None, 0, 0) if c1-c0 >= d1-d0 else (mx-1.1, 2.7, mz, None, 0, 0)])
    for obj in pieces: localise(obj, cx, cz)
    groups.append(pieces)
records.append(export_parts('bw_codex_corridors', groups, provenance))

# The rock mass behind the shell: the enclosure boxes drawn 0.75 m back from every inner face, so they occlude light and the camera's
# stray views without ever standing in front of the displaced rock.
boxes = [(b, h) for n, b, h in structures if n.startswith('rock_')]
x0, z0 = min(b[0] for b, _ in boxes), min(b[1] for b, _ in boxes)
x1, z1 = max(b[2] for b, _ in boxes), max(b[3] for b, _ in boxes)
cx, cz = (x0+x1)/2, (z0+z1)/2
pieces = []
# Faces on the enclosure's outer bounds stay put: the model must span the footprint the engine fits it to.
def inset(value, bound, sign): return value if abs(value-bound) < .01 else value + sign*.75
for (ax, az, bx, bz), height in boxes:
    ax, az, bx, bz = inset(ax, x0, 1), inset(az, z0, 1), inset(bx, x1, -1), inset(bz, z1, -1)
    if bx-ax < .1 or bz-az < .1: continue
    obj = box(ax-cx, az-cz, bx-cx, bz-cz, 0, height)
    obj.data.materials.append(rock_material); pieces.append(obj)
for obj in pieces:
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
for obj in pieces: world_uvs(obj, 2.0)
records.append(export('bw_codex_rock_mass', pieces, dict(provenance, creator='rock enclosure by Claude; Megascans Old Mine rock surface'), lods=False))
(depot/'room_manifest.json').write_text(json.dumps(sorted(records, key=lambda r: r['asset_id']), indent=2)+'\n', encoding='utf-8')
