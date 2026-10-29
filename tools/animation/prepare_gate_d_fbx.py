"""Preserve owned FBX takes on their own rigs for Epic import (CPU only).

Some motion-only FBX files have no skin. Bone-marker triangles give Epic a
preview mesh without borrowing any character's rest pose or changing keys.
"""
import hashlib
import json
import re
import sys
import os
from pathlib import Path
import bpy
from mathutils import Vector

output = Path(sys.argv[sys.argv.index('--') + 1])
output.mkdir(parents=True, exist_ok=True)
roots = [Path('D:/Otherreach_Depot/Mixamo_20261017'), Path('D:/Otherreach_Depot/Rokoko_Free_263')]
files = sorted(p for root in roots for p in root.rglob('*.fbx'))
if os.environ.get('OTHERREACH_GATE_D_PRIORITY') == '1':
    wanted = {'blacksmithhammering_humanik_tf1', 'chefcooking_mixamo', 'writing', 'drinking', 'talking_1', 'carrying'}
    files = [p for p in files if p.stem.lower() in wanted]
seen = set()
rows = []
for source in files:
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if digest in seen:
        continue
    seen.add(digest)
    identifier = re.sub('[^a-z0-9]+', '_', source.stem.lower()).strip('_') + '_' + digest[:8]
    receipt = output / (identifier + '.json')
    if receipt.exists():
        rows.append(json.loads(receipt.read_text()))
        continue
    try:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(source), automatic_bone_orientation=False)
        rigs = [obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE']
        if len(rigs) != 1:
            raise ValueError(f'Expected one source rig, got {len(rigs)}')
        rig = rigs[0]
        actions = list(bpy.data.actions)
        if not actions:
            raise ValueError('No animation takes')
        for obj in list(bpy.context.scene.objects):
            if obj != rig:
                bpy.data.objects.remove(obj, do_unlink=True)
        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        # A named non-animated root lets the shared retargeter identify planar travel.
        bpy.ops.object.mode_set(mode='EDIT')
        if 'root' not in rig.data.edit_bones:
            tops = [bone for bone in rig.data.edit_bones if bone.parent is None]
            root = rig.data.edit_bones.new('root')
            root.head = (0, 0, 0)
            root.tail = (0, .1, 0)
            for bone in tops:
                matrix = bone.matrix.copy()
                bone.parent = root
                bone.matrix = matrix
        bpy.ops.object.mode_set(mode='OBJECT')
        vertices, faces, groups = [], [], []
        for bone in rig.data.bones:
            start = len(vertices)
            centre = bone.head_local
            vertices.extend(centre + Vector(delta) for delta in [(-.005, 0, 0), (.005, 0, 0), (0, 0, .01)])
            faces.append((start, start + 1, start + 2))
            groups.append((bone.name, [start, start + 1, start + 2]))
        mesh = bpy.data.meshes.new('NativeSourceMarkers')
        mesh.from_pydata(vertices, [], faces)
        preview = bpy.data.objects.new('NativeSourceMarkers', mesh)
        bpy.context.collection.objects.link(preview)
        preview.parent = rig
        for name, indices in groups:
            preview.vertex_groups.new(name=name).add(indices, 1, 'REPLACE')
        preview.modifiers.new('SourceRig', 'ARMATURE').object = rig
        path = output / (identifier + '.glb')
        bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', export_animations=True,
            export_animation_mode='ACTIONS', export_skins=True, export_all_influences=True,
            export_def_bones=False, export_morph=False, export_apply=False, export_frame_range=False)
        row = dict(id=identifier, source=str(source), sha256=digest, path=str(path),
                   takes=[action.name for action in actions], status='SOURCE_EXPORTED_NOT_RETARGETED')
    except Exception as error:
        row = dict(id=identifier, source=str(source), sha256=digest, status='FAILED', error=str(error))
    receipt.write_text(json.dumps(row, indent=2), encoding='utf-8')
    rows.append(row)
    (output / 'inventory.json').write_text(json.dumps(rows, indent=2), encoding='utf-8')
    print('GATE_D_SOURCE', row['status'], identifier, flush=True)
print('GATE_D_SOURCES_COMPLETE', len(rows), flush=True)
