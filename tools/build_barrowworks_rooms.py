"""CPU-only Blender assembly of licensed masonry modules over authoritative room footprints.

Run after convert_barrowworks_kit.ps1. No generation service, network or collision changes.
Fab source files stay in the external depot; this script and provenance are the reproducible deliverable.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import sys

import bpy
from mathutils import Matrix, Vector

parser = argparse.ArgumentParser()
parser.add_argument('--depot', default=r'D:\Otherreach_Depot\barrowworks_codex')
parser.add_argument('--region', default=str(Path(__file__).resolve().parents[1] / 'content/regions/barrowworks.yaml'))
parser.add_argument('--enclosure-only', action='store_true')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
depot = Path(args.depot)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

sources = {}
for kind in ('masonry', 'floor', 'ceiling', 'pillar', 'brazier'):
    asset_id = 'bw_codex_' + kind
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(depot / 'ready' / asset_id / (asset_id + '.glb')))
    imported = set(bpy.data.objects) - before
    meshes = []
    for obj in imported:
        if obj.type == 'MESH':
            data = obj.data.copy()
            data.transform(obj.matrix_world)
            template = bpy.data.objects.new('template_' + kind, data)
            meshes.append(template)
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    assert len(meshes) == 1, (kind, len(meshes))
    template = meshes[0]
    coords = [v.co for v in template.data.vertices]
    low = Vector(tuple(min(v[i] for v in coords) for i in range(3)))
    high = Vector(tuple(max(v[i] for v in coords) for i in range(3)))
    template.data.transform(Matrix.Translation(Vector((-(low.x + high.x)/2, -(low.y + high.y)/2, -low.z))))
    sources[kind] = (template, high - low)

pattern = re.compile(r'- \{ id: (\w+), box_m: \[([^\]]+)\], height_m: ([\d.]+)')
structures = [(name, list(map(float, box.split(','))), float(height))
              for name, box, height in pattern.findall(Path(args.region).read_text(encoding='utf-8'))]
records = [r for r in json.loads((depot/'room_manifest.json').read_text()) if r['asset_id'].startswith('bw_codex_room_')] if args.enclosure_only else []

rock_maps=Path(r'C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary\materials\cw_rock_ground')
rock_material=bpy.data.materials.new('Poly_Haven_rock_ground_CC0')
rock_material.use_nodes=True
rock_nodes=rock_material.node_tree.nodes
rock_bsdf=rock_nodes.get('Principled BSDF')
for suffix,socket in [('basecolor','Base Color'),('normal','Normal')]:
    tex=rock_nodes.new('ShaderNodeTexImage')
    tex.image=bpy.data.images.load(str(rock_maps/f'cw_rock_ground_{suffix}.jpg'))
    tex.image.pack()
    if suffix=='normal':
        tex.image.colorspace_settings.name='Non-Color'
        normal=rock_nodes.new('ShaderNodeNormalMap')
        rock_material.node_tree.links.new(tex.outputs['Color'],normal.inputs['Color'])
        rock_material.node_tree.links.new(normal.outputs['Normal'],rock_bsdf.inputs[socket])
    else: rock_material.node_tree.links.new(tex.outputs['Color'],rock_bsdf.inputs[socket])
rock_bsdf.inputs['Roughness'].default_value=.9

def place(kind, at, scale=(1, 1, 1), yaw=0):
    source, _ = sources[kind]
    obj = bpy.data.objects.new(kind, source.data.copy())
    bpy.context.collection.objects.link(obj)
    obj.location = at
    obj.rotation_euler.z = yaw
    obj.scale = scale
    return obj

def backing(at, dimensions, rock=False):
    # The real kit has recessed/open bays. A solid stone backing must close the collider's entire volume.
    # This is ordinary authored enclosure geometry with the licensed masonry material, not an AI mesh.
    bpy.ops.mesh.primitive_cube_add(size=1, location=at)
    obj=bpy.context.object
    obj.scale=dimensions
    obj.data.materials.append(rock_material if rock else sources['masonry'][0].data.materials[0])
    if rock:
        for polygon in obj.data.polygons:
            axis=max(range(3),key=lambda i:abs(polygon.normal[i]))
            axes=[i for i in range(3) if i!=axis]
            for loop in polygon.loop_indices:
                vertex=obj.data.vertices[obj.data.loops[loop].vertex_index].co
                obj.data.uv_layers.active.data[loop].uv=tuple((at[i]+vertex[i]*dimensions[i])/2 for i in axes)
    return obj

for number in ([] if args.enclosure_only else range(1, 14)):
    prefix = f'r{number:02d}_wall'
    walls = [(box, height) for name, box, height in structures if name.startswith(prefix)]
    x0, z0 = min(b[0] for b, _ in walls), min(b[1] for b, _ in walls)
    x1, z1 = max(b[2] for b, _ in walls), max(b[3] for b, _ in walls)
    cx, cz = (x0+x1)/2, (z0+z1)/2
    pieces = []
    _, size = sources['masonry']
    module_run = size.x * 4 / size.z
    for (ax, az, bx, bz), height in walls:
        pieces.append(backing(((ax+bx)/2-cx,-((az+bz)/2-cz),height/2), (bx-ax,bz-az,height)))
        along_x = bx-ax >= bz-az
        length, thickness = (bx-ax, bz-az) if along_x else (bz-az, bx-ax)
        count = math.ceil(length/module_run)
        for i in range(count):
            begin = i*module_run
            run = min(module_run, length-begin)
            mx = ax+begin+run/2 if along_x else (ax+bx)/2
            mz = (az+bz)/2 if along_x else az+begin+run/2
            pieces.append(place('masonry', (mx-cx, -(mz-cz), 0),
                                (run/size.x, thickness/size.y, height/size.z), 0 if along_x else math.pi/2))
    # Physical floor and ceiling modules use the same footprint as the walls. Floors sit below the walk plane.
    for kind, height in (('floor', -0.22), ('ceiling', 4.0)):
        _, size = sources[kind]
        nx, nz = math.ceil((x1-x0)/5), math.ceil((z1-z0)/5)
        for ix in range(nx):
            for iz in range(nz):
                width, depth = min(5, x1-x0-ix*5), min(5, z1-z0-iz*5)
                mx, mz = x0+ix*5+width/2, z0+iz*5+depth/2
                pieces.append(place(kind, (mx-cx, -(mz-cz), height), (width/size.x, depth/size.y, 1)))
    # Lamps have actual iron fixtures. Locations correspond to the lighting recipe's wall pools.
    lamp_at = [(x0+1.5, z0+1.5), (x1-1.5, z1-1.5)]
    if number == 11:
        lamp_at += [(x1-1.5, z0+1.5), (x0+1.5, z1-1.5)]
    for x, z in lamp_at:
        pieces.append(place('brazier', (x-cx, -(z-cz), 0), (.5, .5, .5)))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in pieces:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    room = bpy.context.object
    room.data = room.data.copy()
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    asset_id = f'bw_codex_room_{number:02d}'
    room.name = asset_id
    output = depot / 'ready' / asset_id
    output.mkdir(parents=True, exist_ok=True)
    files = []
    for level, ratio in enumerate((1, .5, .2)):
        obj = room if level == 0 else room.copy()
        if level:
            obj.data = room.data.copy()
            bpy.context.collection.objects.link(obj)
            bpy.context.view_layer.objects.active = obj
            mod = obj.modifiers.new('LOD', 'DECIMATE')
            mod.ratio = ratio
            bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        name = asset_id + (f'_lod{level}' if level else '') + '.glb'
        bpy.ops.export_scene.gltf(filepath=str(output/name), export_format='GLB', use_selection=True,
                                  export_yup=True, export_apply=True, export_lights=False)
        files.append({'file': name, 'triangles': sum(len(p.vertices)-2 for p in obj.data.polygons),
                      'bytes': (output/name).stat().st_size, 'sha256': hashlib.sha256((output/name).read_bytes()).hexdigest()})
        if level:
            bpy.data.objects.remove(obj, do_unlink=True)
    record = {'asset_id': asset_id, 'category': 'building', 'licence': 'Fab Standard Personal',
              'source_url': 'https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e',
              'creator': 'DanStudios; room assembly by Codex', 'source_modules': list(sources),
              'licence_evidence': r'C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LICENCES.md',
              'outputs': files, 'collision': 'authoritative region footprint; camera proxy only', 'shippable': True}
    (output/(asset_id+'_meta.json')).write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
    records.append(record)
    bpy.data.objects.remove(room, do_unlink=True)
    print('BARROWWORKS_ROOM ' + json.dumps(record), flush=True)
(depot/'room_manifest.json').write_text(json.dumps(records, indent=2)+'\n', encoding='utf-8')

# Solid rock volumes use the same real stone source; corridors have real tiled floors and lids.
# This geometry is derived from the collider data, never an artist's approximation of walkable ground.
corridors = json.loads(Path(__file__).with_name('barrowworks_geometry.json').read_text())['corridors']
for asset_id, boxes in [('bw_codex_rock_mass', [(b,h) for n,b,h in structures if n.startswith('rock_')]),
                        ('bw_codex_corridors', [(b,4.3) for _,b in corridors])]:
    x0,z0=min(b[0] for b,_ in boxes),min(b[1] for b,_ in boxes)
    x1,z1=max(b[2] for b,_ in boxes),max(b[3] for b,_ in boxes)
    cx,cz=(x0+x1)/2,(z0+z1)/2
    pieces=[]
    for (ax,az,bx,bz),height in boxes:
        if asset_id.endswith('mass'):
            pieces.append(backing(((ax+bx)/2-cx,-((az+bz)/2-cz),height/2), (bx-ax,bz-az,height), rock=True))
        else:
            for kind,base in [('floor',-.22),('ceiling',4.0)]:
                _,size=sources[kind]
                nx,nz=math.ceil((bx-ax)/4),math.ceil((bz-az)/4)
                for ix in range(nx):
                    for iz in range(nz):
                        width,depth=min(4,bx-ax-ix*4),min(4,bz-az-iz*4)
                        pieces.append(place(kind,(ax+ix*4+width/2-cx,-(az+iz*4+depth/2-cz),base),
                                            (width/size.x,depth/size.y,1)))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in pieces: obj.select_set(True)
    bpy.context.view_layer.objects.active=pieces[0]
    bpy.ops.object.join()
    obj=bpy.context.object
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    obj.name=asset_id
    output=depot/'ready'/asset_id
    output.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(output/(asset_id+'.glb')),export_format='GLB',use_selection=True,
                              export_yup=True,export_apply=True,export_lights=False)
    record={'asset_id':asset_id,'licence':'Fab Standard Personal','creator':'DanStudios; manual assembly by Codex',
            'source_url':'https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e',
            'sha256':hashlib.sha256((output/(asset_id+'.glb')).read_bytes()).hexdigest(),'shippable':True}
    if asset_id.endswith('mass'):
        record.update(licence='CC0 1.0; first-party enclosure geometry',creator='Poly Haven; geometry by Codex',
                      source_url='https://polyhaven.com/a/rock_ground',source_provenance=json.loads((rock_maps/'cw_rock_ground_provenance.json').read_text()))
    (output/(asset_id+'_meta.json')).write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
    records.append(record)
    bpy.data.objects.remove(obj,do_unlink=True)
(depot/'room_manifest.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
