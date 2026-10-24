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
parser.add_argument('--rooms',help='Comma-separated room numbers for an art iteration; retain other exports')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
depot = Path(args.depot)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

sources = {}
for kind in ('masonry', 'floor', 'ceiling', 'pillar', 'brazier', 'relief_rock', 'relief_masonry', 'plain_slab', 'pipe', 'boiler'):
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
    if kind=='pillar': template.data.transform(Matrix.Rotation(math.pi/2,4,'Y'))
    coords = [v.co for v in template.data.vertices]
    low = Vector(tuple(min(v[i] for v in coords) for i in range(3)))
    high = Vector(tuple(max(v[i] for v in coords) for i in range(3)))
    template.data.transform(Matrix.Translation(Vector((-(low.x + high.x)/2, -(low.y + high.y)/2, -low.z))))
    if kind=='relief_masonry':
        bpy.context.collection.objects.link(template)
        bpy.context.view_layer.objects.active=template
        modifier=template.modifiers.new('module_budget','DECIMATE');modifier.ratio=min(1,1800/max(1,len(template.data.polygons)))
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        bpy.context.collection.objects.unlink(template)
    sources[kind] = (template, high - low)

# Production props reused at source scale before fitting; these are verified first-party/CC0 assets.
primary=Path(r'C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary')
for kind,asset_id in [('timber','prop_bp_beam_mossy'),('lantern','prop_ph_wooden_lantern_01')]:
    before=set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(primary/'ready'/asset_id/(asset_id+'.glb')))
    imported=set(bpy.data.objects)-before
    meshes=[]
    for source in imported:
        if source.type!='MESH': continue
        data=source.data.copy();data.transform(source.matrix_world)
        obj=bpy.data.objects.new(kind,data);bpy.context.collection.objects.link(obj);meshes.append(obj)
    for source in imported:bpy.data.objects.remove(source,do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:obj.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0];bpy.ops.object.join();obj=bpy.context.object
    if kind=='lantern':
        for mat in obj.data.materials:
            bsdf=mat.node_tree.nodes.get('Principled BSDF')
            if not bsdf: continue
            bsdf.inputs['Emission Strength'].default_value=0
            bsdf.inputs['Roughness'].default_value=.6
            base=bsdf.inputs['Base Color']
            if base.links:
                original=base.links[0].from_socket
                mix=mat.node_tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1;mix.inputs[2].default_value=(.25,.21,.14,1)
                mat.node_tree.links.new(original,mix.inputs[1]);mat.node_tree.links.new(mix.outputs[0],base)
    if kind=='timber':
        extent=[max(v.co[i] for v in obj.data.vertices)-min(v.co[i] for v in obj.data.vertices) for i in range(3)]
        axis=max(range(3),key=lambda i:extent[i])
        if axis==2: obj.data.transform(Matrix.Rotation(math.pi/2,4,'Y'))
        elif axis==1: obj.data.transform(Matrix.Rotation(math.pi/2,4,'Z'))
    low=Vector(tuple(min(v.co[i] for v in obj.data.vertices) for i in range(3)))
    high=Vector(tuple(max(v.co[i] for v in obj.data.vertices) for i in range(3)))
    obj.data.transform(Matrix.Translation(Vector((-(low.x+high.x)/2,-(low.y+high.y)/2,-low.z))))
    sources[kind]=obj,high-low
recipes=json.loads(Path(__file__).with_name('barrowworks_art_direction.json').read_text())

pattern = re.compile(r'- \{ id: (\w+), box_m: \[([^\]]+)\], height_m: ([\d.]+)')
structures = [(name, list(map(float, box.split(','))), float(height))
              for name, box, height in pattern.findall(Path(args.region).read_text(encoding='utf-8'))]
selected=[int(n) for n in args.rooms.split(',')] if args.rooms else list(range(1,14))
existing=json.loads((depot/'room_manifest.json').read_text()) if (depot/'room_manifest.json').exists() else []
records=([r for r in existing if r['asset_id'] not in {f'bw_codex_room_{n:02d}' for n in selected}] if args.rooms else
         [r for r in existing if r['asset_id'].startswith('bw_codex_room_')] if args.enclosure_only else [])

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

def texture_level(obj, originals, resolution):
    """The lighter levels carry smaller copies of the textures. The full level keeps the source modules' own maps (Ultra first):
    they are shared BC7 resources in the native scene cache, so a room's full textures cost once, not once per room."""
    materials, images = [], []
    if resolution is None: return materials, images
    for index, original in enumerate(originals):
        if original is None: continue
        material = original.copy()
        materials.append(material)
        for node in material.node_tree.nodes:
            if node.type != 'TEX_IMAGE' or node.image is None: continue
            image = node.image.copy()
            image.name = node.image.name + f'_{resolution}'
            ratio = min(1, resolution / max(image.size))
            if ratio < 1: image.scale(max(1, round(image.size[0]*ratio)), max(1, round(image.size[1]*ratio)))
            image.pack()
            node.image = image
            images.append(image)
        obj.data.materials[index] = material
    return materials, images

def release_texture_level(obj, originals, temporary):
    for index, material in enumerate(originals): obj.data.materials[index] = material
    for material in temporary[0]: bpy.data.materials.remove(material)
    for image in temporary[1]: bpy.data.images.remove(image)

def solid(name,at,size,colour):
    obj=backing(at,size,rock=True);obj.name=name
    material=bpy.data.materials.get(name+'_finish')
    if material is None:
        material=bpy.data.materials.new(name+'_finish');material.diffuse_color=(*colour,1);material.use_nodes=True
        bsdf=material.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(*colour,1);bsdf.inputs['Roughness'].default_value=.7
    obj.data.materials.clear();obj.data.materials.append(material)
    return obj

for number in ([] if args.enclosure_only else selected):
    prefix = f'r{number:02d}_wall'
    walls = [(box, height) for name, box, height in structures if name.startswith(prefix)]
    x0, z0 = min(b[0] for b, _ in walls), min(b[1] for b, _ in walls)
    x1, z1 = max(b[2] for b, _ in walls), max(b[3] for b, _ in walls)
    cx, cz = (x0+x1)/2, (z0+z1)/2
    pieces = []
    recipe=recipes[str(number)]
    def fit(kind,at,dimensions,yaw=0):
        _,size=sources[kind]
        return place(kind,at,tuple(dimensions[i]/size[i] for i in range(3)),yaw)
    wall_kind='relief_'+recipe['wall']
    for (ax,az,bx,bz),height in walls:
        along_x=bx-ax>=bz-az
        length,thickness=(bx-ax,bz-az) if along_x else (bz-az,bx-ax)
        # A thin recessed backing closes cracks; the real sculpted blocks/rock provide the entire visible relief.
        backing_size=(bx-ax,.12,height) if along_x else (.12,bz-az,height)
        pieces.append(backing(((ax+bx)/2-cx,-((az+bz)/2-cz),height/2),backing_size,rock=True))
        count=math.ceil(length/(3.0 if recipe['wall']=='masonry' else 4.5))
        rows=4 if recipe['wall']=='masonry' else 1
        for row in range(rows):
            for i in range(count):
                run=length/count;mx=ax+(i+.5)*run if along_x else (ax+bx)/2
                mz=(az+bz)/2 if along_x else az+(i+.5)*run
                pieces.append(fit(wall_kind,(mx-cx,-(mz-cz),row*height/rows),
                                  (run,thickness*.95,height/rows),0 if along_x else math.pi/2))
    # Tiled floor modules remain at the collision plane. Roof silhouettes and support patterns are room specific.
    nx,nz=math.ceil((x1-x0)/5),math.ceil((z1-z0)/5)
    for ix in range(nx):
        for iz in range(nz):
            width,depth=min(5,x1-x0-ix*5),min(5,z1-z0-iz*5)
            at=(x0+ix*5+width/2-cx,-(z0+iz*5+depth/2-cz),-.22)
            _,size=sources['floor'];pieces.append(place('floor',at,(width/size.x,depth/size.y,1)))
            # Rock lids retain their scan's underside relief; slabs are plain quarried stone, with no temple rosettes.
            lid='relief_rock' if 'rock' in recipe['ceiling'] else 'plain_slab'
            pieces.append(fit(lid,(at[0],at[1],4.0),(width+.03,depth+.03,.55 if lid=='relief_rock' else .22)))
    ceiling=recipe['ceiling'];width,depth=x1-x0,z1-z0
    # Scanned slabs have broken edges; solid rock above them closes every seam without flattening their underside relief.
    pieces.append(backing((0,0,4.32),(width,depth,.24),rock=True))
    timber='timber' if 'timber' in ceiling or ceiling in ('pipe_runs','machine_rails') else 'pipe' if ceiling=='cross_pipes' else 'pillar'
    if ceiling in ('pipe_runs','machine_rails'):
        for x in (-width*.3,width*.3):pieces.append(fit('timber',(x,0,3.85),(.22,depth-.7,.26)))
    elif ceiling not in ('low_rock','exposed_rock'):
        spacing=2.4 if ceiling in ('archive_ribs','machine_rails','court_trusses') else 3.5
        for i in range(max(1,int(depth/spacing))):
            y=-depth/2+(i+.5)*depth/max(1,int(depth/spacing))
            pieces.append(fit(timber,(0,y,3.85),(width-.8,.22,.28)))
            if ceiling in ('coffer_beams','court_trusses','timber_grid'):
                for x in (-width*.28,width*.28):
                    pieces.append(fit(timber,(x,0,3.86),(.22,depth-.8,.25)))
    # Each pool has a physical lantern at the exact light position, rather than repeating corner braziers.
    for lx,h,lz,colour,energy,distance in recipe['lights']:
        pieces.append(fit('lantern',(lx,-lz,h-.35),(.30,.30,.48)))
    # Working fixtures sit against the walls, clear of the authored walking routes and interactable footprints.
    if number in (3,4):
        x=-width/2+1.1
        for dx in (-.52,.52):pieces.append(solid('trough_iron',(x+dx,0,.14),(.12,depth-2,.28),(.12,.15,.14)))
        pieces.append(solid('shallow_water',(x,0,.075),(.92,depth-2,.025),(.11,.24,.20)))
        for y in (-depth/2+1,depth/2-1):pieces.append(solid('channel_grate',(x,y,.13),(1.1,.14,.12),(.12,.14,.12)))
        if number==4:
            for y in (-2,0,2):pieces.append(fit('relief_rock',(width/2-.7,y,0),(1.3,1.5,.5)))
    if number==5:
        for y in (-depth/2+1.1,depth/2-1.1):
            for x in (-4,0,4):
                pieces.append(solid('sorting_bin',(x,y,.45),(1.4,1.1,.9),(.22,.15,.08)))
                pieces.append(fit('relief_rock',(x,y,.9),(1.2,.9,.22)))
        for x in (-width/2+1,width/2-1):
            pieces.append(solid('sorting_table',(x,0,1),(1.4,4,.15),(.25,.18,.10)))
            for y in (-1.5,1.5):pieces.append(fit('timber',(x,y,0),(.18,.18,1)))
            chute=solid('ore_chute',(x,2,1.5),(1.1,2.5,.12),(.18,.16,.12));chute.rotation_euler.x=.4
    if number==6:
        x=width/2-1.6
        pieces.append(fit('boiler',(x,-depth/2+1.8,.1),(2.3,2.0,2.5)))
        pieces.append(fit('pipe',(x,-depth/2+3,2.1),(.18,2.5,.18)))
        for z in (.6,.9,1.2,1.5):pieces.append(fit('pipe',(x,-depth/2+4,z),(1.3,.10,.10)))
        pieces.append(solid('condensing_bath',(x,-depth/2+4,.3),(1.7,1.1,.6),(.24,.18,.10)))
    if number in (7,9):
        for sign in (-1,1):
            x=sign*(width/2-.85)
            for y in (-depth/2+1.5,0,depth/2-1.5):
                for z in (.5,1.25,2.0):
                    pieces.append(solid('ledger_shelf',(x,y,z),(.65,2.3,.12),(.17,.10,.05)))
                    for i in range(10):
                        pieces.append(solid('bound_ledger',(x,y-1+i*.21,z+.29),(.48,.15,.46),(.22+(i%3)*.035,.14+(i%2)*.025,.07)))
                for dy in (-1.13,1.13):pieces.append(fit('timber',(x,y+dy,0),(.12,.14,2.6)))
    # Tinted glass over the lantern's darkened face catches its local light without a white emissive billboard.
    for lx,h,lz,colour,energy,distance in recipe['lights']:
        glass=solid('amber_glass',(lx,-lz-.12,h-.14),(.19,.025,.25),(.28,.19,.09))
        bsdf=glass.data.materials[0].node_tree.nodes.get('Principled BSDF');bsdf.inputs['Metallic'].default_value=.3;bsdf.inputs['Roughness'].default_value=.16;bsdf.inputs['Alpha'].default_value=.42;glass.data.materials[0].surface_render_method='DITHERED'
        pieces.append(glass)
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
    originals = list(room.data.materials)
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
        temporary = texture_level(obj, originals, (None, 1024, 512)[level])
        bpy.ops.export_scene.gltf(filepath=str(output/name), export_format='GLB', use_selection=True,
                                  export_yup=True, export_apply=True, export_lights=False)
        files.append({'file': name, 'triangles': sum(len(p.vertices)-2 for p in obj.data.polygons),
                      'bytes': (output/name).stat().st_size, 'sha256': hashlib.sha256((output/name).read_bytes()).hexdigest()})
        release_texture_level(obj, originals, temporary)
        if level:
            bpy.data.objects.remove(obj, do_unlink=True)
    record = {'asset_id': asset_id, 'category': 'building', 'licence': 'Fab Standard Personal',
              'source_url': 'https://www.fab.com/listings/48148dbe-67c8-40a1-af9b-041fa4b9354e',
              'creator': 'DanStudios; Meshingun Studio; Poly Haven; room assembly by Codex', 'source_modules': list(sources),
              'room_identity':recipe, 'additional_source_url':'https://www.fab.com/listings/5d9d932b-9e6e-42d0-8afc-45249e7535a5',
              'backing_material': {'creator': 'Rob Tuytel / Poly Haven', 'licence': 'CC0 1.0', 'source_url': 'https://polyhaven.com/a/rock_ground'},
              'texture_resolutions': ['source',1024,512],
              'licence_evidence': r'C:\Otherreach_Recovery\ASSET_REBUILD\FAB_LICENCES.md',
              'outputs': files, 'collision': 'authoritative region footprint; camera proxy only', 'shippable': True}
    (output/(asset_id+'_meta.json')).write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
    records.append(record)
    bpy.data.objects.remove(room, do_unlink=True)
    print('BARROWWORKS_ROOM ' + json.dumps(record), flush=True)
(depot/'room_manifest.json').write_text(json.dumps(records, indent=2)+'\n', encoding='utf-8')

if args.rooms: sys.exit(0)

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
            pieces.append(backing(((ax+bx)/2-cx,-((az+bz)/2-cz),4.64),(bx-ax,bz-az,.18),rock=True))
            for kind,base in [('floor',-.22),('plain_slab',4.0)]:
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
    originals=list(obj.data.materials)
    temporary=texture_level(obj,originals,None)
    bpy.ops.export_scene.gltf(filepath=str(output/(asset_id+'.glb')),export_format='GLB',use_selection=True,
                              export_yup=True,export_apply=True,export_lights=False)
    release_texture_level(obj,originals,temporary)
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
