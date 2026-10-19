"""New rigid lames cut with Blender Bisect from the new native-fit backing.
No old garment refit, body edit, cloth solver or custom corrective is used.
"""
from pathlib import Path
import hashlib
import json
import math
import os
import sys
import bpy
import bmesh
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

root = Path(r'D:\Otherreach_Scratch\frontier_native_20261018')
args = sys.argv[sys.argv.index('--') + 1:]
piece = args[0]
assert piece in ('lamework_jack', 'lame_sabatons','lame_coif')
backing = {'lamework_jack':'vest','lame_sabatons':'boots','lame_coif':'standing_cap'}[piece]
backing_asset = 'frontier_standing_cap' if piece=='lame_coif' else 'frontier_sealed_' + backing
assert os.environ.get('OTHERREACH_CLOTHING_BODY','veth_f')=='veth_f', 'Author sources once on feminine Average'
fit = native = 'veth_f'
output = root / os.environ.get('OTHERREACH_SOURCE_SET','sources/fits') / 'veth_f'
source = output / backing / (backing_asset + '_native.glb')
before = hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
cloth = bpy.data.objects[backing_asset]
bpy.ops.import_scene.gltf(filepath=str(root / 'approved_clothing_sources' / 'veth_f.glb'))
body = bpy.data.objects['native_' + native + ('_head' if piece=='lame_coif' else '_body')]
body_bvh = BVHTree.FromPolygons([body.matrix_world @ v.co for v in body.data.vertices],
                                [tuple(p.vertices) for p in body.data.polygons])
if piece == 'lame_coif':
    # Use the same licensed outer pattern as the hood, before shell thickness.
    pattern_source=Path(r'C:/Otherreach_External_Assets/MakeHuman/community/clothes/3235_Monk_s_robe_hood/Monks_Hood.obj')
    previous=set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=str(pattern_source),forward_axis='NEGATIVE_Z',up_axis='Y')
    pattern=next(o for o in set(bpy.data.objects)-previous if o.type=='MESH')
    pattern.data.transform(pattern.matrix_world);pattern.matrix_world=Matrix.Identity(4)
    for vertex in pattern.data.vertices:
        point=vertex.co.copy();vertex.co=Vector((point.x*.122,point.y*.104+.002,point.z*.105+.731))
        t=max(0,min(1,(vertex.co.y-.025)/.10))*max(0,min(1,(vertex.co.z-1.55)/.085))
        vertex.co.y+=.065*t;vertex.co.z-=.105*t
    pattern.data.update()
    bpy.context.view_layer.objects.active=pattern
    clearance=pattern.modifiers.new('Outer pattern stand-off','DISPLACE')
    clearance.strength=.012;clearance.mid_level=0
    bpy.ops.object.modifier_apply(modifier=clearance.name)
    positions=[v.co.copy() for v in pattern.data.vertices]
    faces=[tuple(p.vertices) for p in pattern.data.polygons]
else:
    positions = [cloth.matrix_world @ v.co for v in cloth.data.vertices]
    faces = []
    for polygon in cloth.data.polygons:
        if piece=='lamework_jack' and not cloth.data.materials[polygon.material_index].name.startswith('frontier_oiled_leather'):
            continue
        if cloth.data.materials[polygon.material_index].name.endswith('_opening'):
            continue
        center = cloth.matrix_world @ polygon.center
        near, normal, _, _ = body_bvh.find_nearest(center)
        world_normal=(cloth.matrix_world.to_3x3().inverted().transposed() @ polygon.normal).normalized()
        if normal is not None and world_normal.dot(normal) > .3:
            faces.append(tuple(polygon.vertices))
used=sorted({i for face in faces for i in face});remap={old:new for new,old in enumerate(used)}
positions=[positions[i] for i in used]
faces=[tuple(remap[i] for i in face) for face in faces]
iron = bpy.data.materials.new('frontier_lamework_metal')
iron.use_nodes = True
shader = iron.node_tree.nodes.get('Principled BSDF')
shader.inputs['Base Color'].default_value = (.32, .30, .27, 1)
shader.inputs['Metallic'].default_value = 1
shader.inputs['Roughness'].default_value = .48
panels = []
specs = []
if piece in ('lamework_jack','lame_coif'):
    torso_top=(arm.matrix_world @ arm.data.bones['upperarm_l'].head_local).z-.055
    width=.04 if piece=='lame_coif' else .05
    for x in range(math.floor(min(p.x for p in positions) / width), math.ceil(max(p.x for p in positions) / width)):
        for z in range(math.floor(min(p.z for p in positions) / .03), math.ceil(max(p.z for p in positions) / .03)):
            if piece=='lamework_jack' and (z+1)*.03>torso_top:
                continue  # Leather shoulder bridges carry the jack; no plates spanning the armholes.
            if piece=='lame_coif' and z*.03<1.46:continue
            specs.append((x * width + .001, (x + 1) * width - .001,
                          z * .03 + .001, (z + 1) * .03 - .001, 0))
else:
    for side, sign in (('l', 1), ('r', -1)):
        foot = arm.matrix_world @ arm.data.bones['foot_' + side].head_local
        for band in range(-7, 4):
            specs.append((0 if sign == 1 else -.3, .3 if sign == 1 else 0,
                          band * .04 + .001, (band + 1) * .04 - .001, foot.z + .024, side))

for number, spec in enumerate(specs):
    edit = bmesh.new()
    for point in positions:
        edit.verts.new(point)
    edit.verts.ensure_lookup_table()
    for face in faces:
        edit.faces.new([edit.verts[i] for i in face])
    if piece in ('lamework_jack','lame_coif'):
        xmin, xmax, zmin, zmax, _ = spec
        cuts = [((xmin, 0, 0), (1, 0, 0)), ((xmax, 0, 0), (-1, 0, 0)),
                ((0, 0, zmin), (0, 0, 1)), ((0, 0, zmax), (0, 0, -1))]
    else:
        xmin, xmax, ymin, ymax, zmax, side = spec
        cuts = [((xmin, 0, 0), (1, 0, 0)), ((xmax, 0, 0), (-1, 0, 0)),
                ((0, ymin, 0), (0, 1, 0)), ((0, ymax, 0), (0, -1, 0)),
                ((0, 0, .015), (0, 0, 1)), ((0, 0, zmax), (0, 0, -1))]
    for point, normal in cuts:
        bmesh.ops.bisect_plane(edit, geom=list(edit.verts) + list(edit.edges) + list(edit.faces),
                              dist=1e-6, plane_co=point, plane_no=normal, clear_inner=True)
    if not edit.faces:
        edit.free()
        continue
    bmesh.ops.delete(edit, geom=[v for v in edit.verts if not v.link_faces], context='VERTS')
    if piece == 'lame_coif':
        # Form each disconnected tile separately, never across the face opening.
        remaining=set(edit.verts);components=[]
        while remaining:
            todo=[remaining.pop()];component=set(todo)
            while todo:
                vertex=todo.pop()
                for edge in vertex.link_edges:
                    neighbour=edge.other_vert(vertex)
                    if neighbour in remaining:
                        remaining.remove(neighbour);component.add(neighbour);todo.append(neighbour)
            components.append(component)
        bmesh.ops.delete(edit,geom=list(edit.faces),context='FACES_ONLY')
        for component in components:
            bmesh.ops.convex_hull(edit,input=list(component),use_existing_faces=False)
        bmesh.ops.delete(edit,geom=[v for v in edit.verts if not v.link_faces],context='VERTS')
    center = sum((v.co for v in edit.verts), Vector()) / len(edit.verts)
    if piece == 'lamework_jack':
        bone = min(('spine_01', 'spine_02', 'spine_03', 'spine_04', 'spine_05'),
                   key=lambda name: abs((arm.matrix_world @ arm.data.bones[name].head_local).z - center.z))
    elif piece=='lame_coif':
        bone='head'
    else:
        bone = 'foot_' + side
    mesh = bpy.data.meshes.new(f'lame_{number}')
    edit.to_mesh(mesh)
    edit.free()
    obj = bpy.data.objects.new(mesh.name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if piece == 'lame_sabatons' and len(obj.data.vertices) > 30:
        density = obj.modifiers.new('Rigid plate surface density', 'DECIMATE')
        density.ratio = max(.2, 20 / len(obj.data.vertices))
        bpy.ops.object.modifier_apply(modifier=density.name)
    if piece != 'lame_coif':
        clearance = obj.modifiers.new('Backing clearance', 'DISPLACE')
        clearance.strength = 0 if piece=='lame_coif' else .0035
        clearance.mid_level = 0
        bpy.ops.object.modifier_apply(modifier=clearance.name)
    thickness = obj.modifiers.new('Iron thickness', 'SOLIDIFY')
    thickness.thickness = .0015
    thickness.offset = 1
    bpy.ops.object.modifier_apply(modifier=thickness.name)
    bevel = obj.modifiers.new('Lame edge', 'BEVEL')
    bevel.width = .0006
    bevel.segments = 1
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40), keep_sharp_edges=True)
    obj.data.materials.append(iron)
    if piece in ('lamework_jack','lame_sabatons'):
        # Standard skin-weight transfer selects the rigid attachment for each
        # plate; shoulder plates follow the backing's shoulder rather than an
        # unrelated nearest-height spine joint. Each plate remains fully rigid.
        for group in cloth.vertex_groups:
            obj.vertex_groups.new(name=group.name)
        transfer = obj.modifiers.new('Backing attachment weights', 'DATA_TRANSFER')
        transfer.object = cloth
        transfer.use_vert_data = True
        transfer.data_types_verts = {'VGROUP_WEIGHTS'}
        transfer.vert_mapping = 'POLYINTERP_NEAREST'
        transfer.layers_vgroup_select_src = 'ALL'
        transfer.layers_vgroup_select_dst = 'NAME'
        bpy.ops.object.modifier_apply(modifier=transfer.name)
        totals = {}
        for vertex in obj.data.vertices:
            for influence in vertex.groups:
                name = obj.vertex_groups[influence.group].name
                totals[name] = totals.get(name, 0.) + influence.weight
        assert totals
        bone = max(totals, key=totals.get)
        obj.vertex_groups.clear()
    obj.vertex_groups.new(name=bone).add(list(range(len(obj.data.vertices))), 1, 'REPLACE')
    skin = obj.modifiers.new('Rigid native joint', 'ARMATURE')
    skin.object = arm
    panels.append(obj)
bpy.ops.object.select_all(action='DESELECT')
assert panels, 'No rigid lames generated'
for obj in panels:
    obj.select_set(True)
bpy.context.view_layer.objects.active = panels[0]
bpy.ops.object.join()
obj = panels[0]
obj.name = 'frontier_' + piece
position = obj.matrix_world.copy()
obj.parent = arm
obj.matrix_world = position
assert all(len(v.groups) == 1 and abs(v.groups[0].weight - 1) < 1e-6 for v in obj.data.vertices)
for level, ratio in ((1, .6), (2, .35), (3, .18)):
    lod = obj.copy()
    lod.data = obj.data.copy()
    lod.name = obj.name + f'_LOD{level}'
    bpy.context.collection.objects.link(lod)
    bpy.context.view_layer.objects.active = lod
    density = lod.modifiers.new('Native armour LOD', 'DECIMATE')
    density.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=density.name)
    assert all(len(v.groups) == 1 and abs(v.groups[0].weight - 1) < 1e-6 for v in lod.data.vertices)
    lod.select_set(True)
arm.select_set(True)
out = output / piece
out.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(out / (obj.name + '_native.glb')), export_format='GLB', use_selection=True,
                          export_skins=True, export_def_bones=False, export_influence_nb=8, export_animations=False)
bpy.ops.wm.save_as_mainfile(filepath=str(out / (obj.name + '.blend')))
assert hashlib.sha256(source.read_bytes()).hexdigest() == before
(out / 'source.json').write_text(json.dumps({'backing': str(source), 'sha256': before, 'lames': len(panels),
    'rigid_weights': True, 'body_edits': False, 'nominal_backing_clearance_m': .012 if piece=='lame_coif' else .0035,
    **({'outer_pattern':str(pattern_source),'outer_pattern_sha256':hashlib.sha256(pattern_source.read_bytes()).hexdigest(),'forming':'native connected plate hulls from the hood outer pattern'} if piece=='lame_coif' else {}),
    'status': 'UNACCEPTED: inter-layer clearance, animation, all-body fit, art and LOD gates pending'}, indent=2))
print('NATIVE_LAME_SOURCE', piece, len(panels), len(obj.data.vertices))
