"""Blender CPU-only: export owned native wolf control candidates, never the body or textures."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import bpy

args = sys.argv[sys.argv.index('--') + 1:]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('source', type=Path)
parser.add_argument('output', type=Path)
options = parser.parse_args(args)
digest = hashlib.sha256(options.source.read_bytes()).hexdigest()
assert digest == '37791915026629d3fb834a0e1ea9e88ef01c0798e5a25777ca4a3c813f48f0b1', 'Unexpected owned wolf source'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(options.source), use_anim=True, ignore_leaf_bones=False,
                         automatic_bone_orientation=False, use_custom_props=False)
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
rest = {bone.name: tuple(v for row in bone.matrix_local for v in row) for bone in rig.data.bones}
actions = list(bpy.data.actions)
for obj in list(bpy.context.scene.objects):
    if obj is not rig:
        bpy.data.objects.remove(obj, do_unlink=True)
selected = {
    'stumble_left': ('Hit_Stand_L01', False),
    'stumble_right': ('Hit_Stand_R01', False),
    'prone_hit': ('Hit_Lying_R01', False),
    'prone_idle': ('Lying_Breathing_01', True),
    'prone_fall': ('Trans_Stand_to_Lying', False),
    'prone_rise': ('Trans_Lying_to_Stand', False),
}
ready = options.output / 'animation/ready/creatures'
records = options.output / 'animation/clips'
ready.mkdir(parents=True, exist_ok=True)
records.mkdir(parents=True, exist_ok=True)
rows = []
for state, (tail, loop) in selected.items():
    action = next(a for a in actions if a.name.split('|')[-1] == tail)
    rig.animation_data_clear()
    rig.animation_data_create()
    rig.animation_data.action = action
    if action.slots:
        rig.animation_data.action_slot = action.slots[0]
    first, last = (int(round(value)) for value in action.frame_range)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = first, last
    scene.frame_set(first)
    for obj in scene.objects:
        obj.select_set(obj is rig)
    identifier = 'creature.cmb16_wolf.' + state
    path = ready / ('anim.' + identifier + '.glb')
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB', use_selection=True,
        export_apply=False, export_yup=True, export_materials='NONE', export_skins=True,
        export_animations=True, export_animation_mode='ACTIVE_ACTIONS', export_force_sampling=True,
        export_frame_range=True, export_anim_slide_to_zero=True, export_def_bones=False)
    seconds = (last-first)/(scene.render.fps/scene.render.fps_base)
    record = {'schema_version':'1', 'animation_id':'anim.'+identifier,
        'skeleton_family':'wildmesh_creature_wm_grey_wolf_b', 'category':'combat',
        'animation_family':'creature', 'clip_type':state, 'loop':loop, 'root_motion':False,
        'duration_s':seconds, 'events':[], 'hand_profile':{'left':'none','right':'none'},
        'tags':['creature','wildmesh','native','control-candidate'],
        'source':{'kind':'owned vendor-authored native clip', 'pack':'WildMesh UNITY - Animated Animals Realistic Pack.unitypackage',
            'author':'WildMesh 3D', 'license':'Existing owner-cleared WildMesh Standard Licence receipt; no AI/ML use',
            'file':options.source.name, 'action':action.name, 'sha256':digest}}
    (records/('anim.'+identifier+'.json')).write_text(json.dumps(record, indent=2), encoding='utf-8')
    rows.append({'state':state, 'path':str(path), 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                 'source_action':action.name, 'frames':[first,last], 'seconds':seconds})
    assert rest == {bone.name:tuple(v for row in bone.matrix_local for v in row) for bone in rig.data.bones}
    print('CMB16_WOLF_CANDIDATE', state, flush=True)
assert hashlib.sha256(options.source.read_bytes()).hexdigest() == digest
(options.output/'CMB16_WOLF_SOURCE.json').write_text(json.dumps({'status':'SOURCE_CANDIDATES_NOT_BOUND',
    'source':str(options.source), 'source_sha256':digest, 'blender':bpy.app.version_string,
    'scope':'Existing native wolf rig/actions only, exported through the existing C1 FBX/glTF route. No body/texture export or skeleton rest edits. Standing side-hit takes require native stumble review; controlled lie/rise are not asserted to be trip falls. Native lying hit is never a standing-hit rename. Target-rest conversion/contact/render verdict still required.',
    'clips':rows}, indent=2), encoding='utf-8')
