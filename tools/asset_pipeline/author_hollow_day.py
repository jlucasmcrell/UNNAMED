"""Hollow Day's native scenery: sourced CC0 stall, scanned linen and wood, original ropes and hanging staves.

Non-rendering Blender authoring; writes only --out. Originals are read unchanged.
No generative service, reduced maps or proxy replacement is used.
"""
import hashlib
import json
import math
import struct
from pathlib import Path
import sys

import bpy
from mathutils import Vector

out = Path(sys.argv[sys.argv.index('--')+1]).resolve()
out.mkdir(parents=True, exist_ok=True)
source = Path('C:/Otherreach_External_Assets')
stall = source/'Sourced_20261018/itch/free-cc0-medieval-props/extracted/MedievalPropsPack/MarketStall'
scans = source/'PolyHaven/textures'
bpy.ops.wm.read_factory_settings(use_empty=True)
sources = {}
scanned_materials = {}


def texture(path):
    path = Path(path)
    sources[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    image = bpy.data.images.load(str(path), check_existing=True)
    image.pack()
    return image


def material(name, colour, folder=None):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*colour, 1)
    result.use_nodes = True
    bsdf = result.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*colour, 1)
    bsdf.inputs['Roughness'].default_value = .78
    if folder:
        scanned_materials[name] = (folder, colour)
        for suffix, socket in (('diff', 'Base Color'), ('nor_gl', None)):
            path = next((scans/folder).glob('*_'+suffix+'_4k.jpg'))
            node = result.node_tree.nodes.new('ShaderNodeTexImage')
            node.image = texture(path)
            if socket:
                multiply = result.node_tree.nodes.new('ShaderNodeMixRGB')
                multiply.blend_type = 'MULTIPLY'
                multiply.inputs[0].default_value = 1
                multiply.inputs[2].default_value = (*colour, 1)
                result.node_tree.links.new(node.outputs['Color'], multiply.inputs[1])
                result.node_tree.links.new(multiply.outputs[0], bsdf.inputs[socket])
            else:
                node.image.colorspace_settings.name = 'Non-Color'
                normal = result.node_tree.nodes.new('ShaderNodeNormalMap')
                normal.inputs['Strength'].default_value = .45
                result.node_tree.links.new(node.outputs['Color'], normal.inputs['Color'])
                result.node_tree.links.new(normal.outputs[0], bsdf.inputs['Normal'])
    return result


wood = material('Weathered tally staves - Poly Haven rough wood', (.78, .69, .52), 'rough_wood')
rope = material('Twisted unbleached rope', (.24, .19, .12), 'rough_linen')
iron = material('Blackened iron pins', (.055, .060, .063))
iron.node_tree.nodes.get('Principled BSDF').inputs['Metallic'].default_value = .85
cloth = [material('Hollow Day linen '+name, colour, 'rough_linen') for name, colour in
         [('bone', (.78, .72, .58)), ('ochre', (.54, .32, .10)), ('lichen', (.28, .33, .18)), ('iron blue', (.18, .26, .29))]]


def active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def beam(name, a, b, width, depth, surface=wood):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(a+b)/2)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    obj.dimensions = (width, depth, (b-a).length)
    active(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = obj.modifiers.new('Worn softened edges', 'BEVEL')
    bevel.width, bevel.segments = .003, 3
    obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    obj.data.materials.append(surface)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cube_project(cube_size=.5)
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj


def cord(name, points, radius=.004):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth, curve.bevel_resolution = radius, 3
    spline = curve.splines.new('POLY')
    spline.points.add(len(points)-1)
    for point, coordinates in zip(spline.points, points): point.co = (*coordinates, 1)
    obj = bpy.data.objects.new(name, curve)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(rope)
    return obj


def hang(name, pivot, objects):
    group = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(group)
    group.location = pivot
    bpy.context.view_layer.update()
    for obj in objects:
        world = obj.matrix_world.copy()
        obj.parent = group
        obj.matrix_world = world
    return group


def export(identifier, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    folder = out/'ready'/identifier
    folder.mkdir(parents=True, exist_ok=True)
    path = folder/(identifier+'.glb')
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', use_selection=True,
                              export_animations=False, export_apply=True, export_yup=True)
    # Exporters can cache a shared image's last tinted bake across several materials.
    # Keep the acquired JPEG bytes once per scan and express every tint as glTF's native factor.
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20+size])
    binary_size = struct.unpack_from('<I', raw, 20+size)[0]
    binary = bytearray(raw[28+size:28+size+binary_size])
    textures = {}
    for mat in doc.get('materials', []):
        if mat.get('name') not in scanned_materials: continue
        scan, tint = scanned_materials[mat['name']]
        if scan not in textures:
            image = next((scans/scan).glob('*_diff_4k.jpg'))
            image_bytes = image.read_bytes()
            binary.extend(b'\0'*(-len(binary) % 4))
            offset = len(binary);binary.extend(image_bytes)
            view = len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':offset,'byteLength':len(image_bytes)})
            index = len(doc['images']);doc['images'].append({'bufferView':view,'mimeType':'image/jpeg','name':image.name})
            textures[scan] = len(doc['textures']);doc['textures'].append({'source':index})
        pbr = mat.setdefault('pbrMetallicRoughness', {})
        pbr['baseColorFactor'] = [*tint,1]
        pbr['baseColorTexture'] = {'index':textures[scan]}
    doc['buffers'][0]['byteLength'] = len(binary)
    encoded = json.dumps(doc,separators=(',',':')).encode();encoded += b' '*(-len(encoded) % 4)
    binary.extend(b'\0'*(-len(binary) % 4))
    path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(encoded)+len(binary))
        +struct.pack('<I4s',len(encoded),b'JSON')+encoded+struct.pack('<I4s',len(binary),b'BIN\0')+binary)
    return {'id': identifier, 'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


assets = []
before = set(bpy.data.objects)
for x in (-3, 3):
    beam('Bunting oak pole', (x, 0, 0), (x, 0, 3.15), .065, .065)
    beam('Pole iron foot', (x, 0, 0), (x, 0, .20), .075, .075, iron)
cord('Sagging bunting hawser', [(x, 0, 3.1-.38*(1-(x/3)**2)) for x in [i*6/96-3 for i in range(97)]], .006)
for flag in range(17):
    centre = -2.75+flag*.34
    top = 3.1-.38*(1-(centre/3)**2)
    verts, faces = [], []
    for row in range(13):
        t = row/12
        for column in range(13):
            u = column/12
            x = centre+(u-.5)*.28*(1-t)
            y = .012*math.sin(u*math.pi*3+flag*.6)*t+.028*math.sin(t*math.pi)
            verts.append((x, y, top-.37*t))
    for row in range(12):
        for column in range(12):
            a = row*13+column
            faces.append((a, a+1, a+14, a+13))
    mesh = bpy.data.meshes.new('Cut and creased linen pennant')
    mesh.from_pydata(verts, [], faces)
    mesh.uv_layers.new()
    for polygon in mesh.polygons:
        polygon.use_smooth = True
        for loop in polygon.loop_indices:
            v = mesh.loops[loop].vertex_index
            mesh.uv_layers.active.data[loop].uv = ((v % 13)/12, (v//13)/12)
    obj = bpy.data.objects.new('Linen pennant %02d' % flag, mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(cloth[flag % 4])
    solid = obj.modifiers.new('Folded linen thickness', 'SOLIDIFY')
    solid.thickness = .0007
    hem = cord('Pennant hem %02d' % flag, [verts[i] for i in range(0, 169, 13)]+list(reversed([verts[i] for i in range(12, 169, 13)])), .0009)
    hang('HollowDayPennant_%02d' % flag, (centre, 0, top), [obj, hem])
assets.append(export('festival_hollow_bunting', list(set(bpy.data.objects)-before)))

before = set(bpy.data.objects)
for x in (-.65, .65):
    for y in (-.45, .45):
        beam('Tally tower splayed leg', (x, y, 0), (x*.5, y*.5, 4.15), .095, .095)
beam('Tally tower crown beam', (-.95, 0, 4.13), (.95, 0, 4.13), .11, .11)
for y in (-.3, .3):
    beam('Tally tower crossbar', (-.85, y, 3.2), (.85, y, 3.2), .075, .075)
for x in (-.65, .65):
    beam('Tower diagonal brace', (x, -.45, .55), (-x*.6, -.25, 2.65), .055, .055)
for row in range(3):
    for stave in range(13):
        x, y = (stave-6)*.106, (row-1)*.19
        z = 2.0+.18*math.sin(stave*1.7+row)
        suspended = set(bpy.data.objects)
        cord('Restrung stave cord', [(x, y, 3.2), (x+.012*math.sin(stave), y, z+.54)], .0028)
        beam('Hanging tally stave %02d_%02d' % (row, stave), (x, y, z), (x+.012, y, z+.54), .035, .022)
        for notch in range((stave % 5)+1):
            beam('Tally cut', (x-.018, y-.012, z+.08+notch*.025), (x+.018, y-.012, z+.08+notch*.025), .002, .002, iron)
        hang('HollowDayStave_%02d_%02d' % (row, stave), (x, y, 3.2), list(set(bpy.data.objects)-suspended))
assets.append(export('festival_hollow_tally_tower', list(set(bpy.data.objects)-before)))

before = set(bpy.data.objects)
sources[str(stall/'MarketStall.fbx')] = hashlib.sha256((stall/'MarketStall.fbx').read_bytes()).hexdigest()
bpy.ops.import_scene.fbx(filepath=str(stall/'MarketStall.fbx'))
objects = list(set(bpy.data.objects)-before)
for obj in objects:
    if obj.type != 'MESH': continue
    for old in list(obj.data.materials):
        if old: old.use_nodes = True
    mat = material('3DModelsCC0 original stall atlas', (1, 1, 1))
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    for filename, socket in [('MarketStall_Base_Color.png', 'Base Color'), ('MarketStall_Roughness.png', 'Roughness'), ('MarketStall_Normal.png', None)]:
        node = mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = texture(stall/filename)
        if socket:
            if socket == 'Roughness': node.image.colorspace_settings.name = 'Non-Color'
            mat.node_tree.links.new(node.outputs['Color'], bsdf.inputs[socket])
        else:
            node.image.colorspace_settings.name = 'Non-Color'
            normal = mat.node_tree.nodes.new('ShaderNodeNormalMap')
            mat.node_tree.links.new(node.outputs['Color'], normal.inputs['Color'])
            mat.node_tree.links.new(normal.outputs[0], bsdf.inputs['Normal'])
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for polygon in obj.data.polygons: polygon.material_index = 0
assets.append(export('festival_hollow_market_stall', objects))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'HollowDay_Source.blend'))
assert all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in sources.items())
(out/'HOLLOW_DAY_ART_RECEIPT.json').write_text(json.dumps({'status': 'AUTHORING_ONLY', 'assets': assets, 'sources': sources,
    'licences': ['CC0 - 3DModelsCC0 Free CC0 Medieval Props; provenance retained in External Assets', 'CC0 - Poly Haven rough_linen and rough_wood'],
    'canon': 'A forest of hanging staves that clack in the wind; small frontier restrung frame, not the towers of Talvess.',
    'review': 'No in-game verdict, placement or animation acceptance yet'}, indent=2))
