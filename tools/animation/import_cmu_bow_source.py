"""Prepare CMU 79_86 with Blender's native BVH importer/glTF exporter; no target retargeting.

Source-only bone markers let Epic import a skeletal preview. The original BVH keys
and bone proportions are kept; only uniform source units and an identity root are added.
"""
import hashlib
import json
import os
import traceback
from pathlib import Path
import bpy
from mathutils import Vector

root = Path(os.environ['OTHERREACH_HUMGEN_ANIMATION_JOB']) / 'sources/cmu'
clip_id = os.environ.get('OTHERREACH_CMU_SOURCE_ID', '79_86')
path = root / (clip_id + '.bvh')
state = {'source': str(path), 'source_sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
try:
    text = path.read_text()
    motion = text.split('MOTION', 1)[1].strip().splitlines()
    first = [float(v) for v in motion[2].split()]
    assert first[1] > 0, 'Expected positive calibration-frame hip height'
    # BVH units have no standard scale. A one-metre calibration hip height gives
    # the source a useful preview size without changing its proportions/performance.
    scale = 1 / first[1]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.preferences.addon_enable(module='io_anim_bvh')
    bpy.ops.import_anim.bvh(filepath=str(path), global_scale=scale, frame_start=1,
                            update_scene_fps=True, update_scene_duration=True,
                            use_fps_scale=False, use_cyclic=False, rotate_mode='QUATERNION')
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    rig.name = 'CMU' + clip_id + '_Source'
    rig.animation_data.action.name = 'CMU' + clip_id + ('_Bow' if clip_id == '79_86' else '_Idle')
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    hips = rig.data.edit_bones['Hips']
    old_matrix = hips.matrix.copy()
    bone = rig.data.edit_bones.new('root')
    bone.head = (0, 0, 0)
    bone.tail = (0, .2, 0)
    hips.parent = bone
    hips.use_connect = False
    hips.matrix = old_matrix
    bpy.ops.object.mode_set(mode='OBJECT')
    vertices, faces, groups = [], [], []
    for bone in rig.data.bones:
        start = len(vertices)
        center = bone.head_local
        vertices.extend([center + Vector((-.005, 0, 0)), center + Vector((.005, 0, 0)), center + Vector((0, 0, .01))])
        faces.append((start, start + 1, start + 2))
        groups.append((bone.name, [start, start + 1, start + 2]))
    mesh = bpy.data.meshes.new('SourceBoneMarkers')
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    preview = bpy.data.objects.new('SourceBoneMarkers', mesh)
    bpy.context.collection.objects.link(preview)
    preview.parent = rig
    for name, indices in groups:
        preview.vertex_groups.new(name=name).add(indices, 1, 'REPLACE')
    modifier = preview.modifiers.new('SourceRig', 'ARMATURE')
    modifier.object = rig
    scene = bpy.context.scene
    scene.frame_set(1)
    bpy.context.view_layer.update()
    state.update(source_unit_scale=scale, fps=scene.render.fps / scene.render.fps_base,
                 frames=[scene.frame_start, scene.frame_end],
                 bones=[b.name for b in rig.data.bones],
                 hips_reference_height_m=(rig.matrix_world @ rig.data.bones['Hips'].head_local).z,
                 hips_first_frame_height_m=(rig.matrix_world @ rig.pose.bones['Hips'].matrix.translation).z)
    if clip_id != '79_86':
        # CMU's conversion prepends an artificial calibration pose. Export the
        # recorded performance only, using the same native time crop as the bow.
        scene.frame_start = 3
        state['performance_frames'] = [scene.frame_start, scene.frame_end]
    output = root / ('CMU' + clip_id + ('_Bow' if clip_id == '79_86' else '_Idle') + '.glb')
    def export(path):
        bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', export_animations=True,
                                  export_animation_mode='ACTIONS', export_skins=True, export_all_influences=True,
                                  export_def_bones=False, export_morph=False, export_apply=False,
                                  export_frame_range=True, export_anim_slide_to_zero=True)
    export(output)
    state['clips'] = []
    # Crop only time through Blender's native exporter. The original performance
    # draws from 2.4 s, releases around 3.75 s, and lowers the bow by 4.85 s.
    # Its brief, steady full-draw hold supplies the ready stance. No keys are posed.
    for name, first, last in ([('BowReady', 3.45, 3.65), ('BowDraw', 2.4, 4.85), ('BowRelease', 3.7, 4.85)] if clip_id == '79_86' else []):
        rig.animation_data.action.name = 'CMU79_86_' + name
        scene.frame_start = round(first * state['fps']) + 1
        scene.frame_end = round(last * state['fps']) + 1
        clip_path = root / ('CMU79_86_' + name + '.glb')
        export(clip_path)
        state['clips'].append({'path': str(clip_path), 'name': rig.animation_data.action.name,
                               'range_seconds': [first, last], 'sha256': hashlib.sha256(clip_path.read_bytes()).hexdigest()})
    assert hashlib.sha256(path.read_bytes()).hexdigest() == state['source_sha256']
    state.update(status='DONE', path=str(output), sha256=hashlib.sha256(output.read_bytes()).hexdigest())
except Exception:
    state.update(status='FAILED', error=traceback.format_exc())
(root / ('bow_blender_transport.json' if clip_id == '79_86' else clip_id + '_blender_transport.json')).write_text(json.dumps(state, indent=2))
