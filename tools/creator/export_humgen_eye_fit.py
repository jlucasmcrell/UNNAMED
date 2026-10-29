"""Export native eye-fitting inputs from the two prepared HumGen bases. CPU only.

Uses the same native bone tail landmarks, live-key deltas, eyeball-size key and
94 * eye-width - 2 formula as Human.height._correct_eyes. No new eye geometry.
"""
import json
import sys
from pathlib import Path
import bpy
import numpy as np
from HumGen3D.human.human import Human
from HumGen3D.human.keys.keys import import_npz_key
from HumGen3D.backend.preferences.preference_func import get_prefs

args = sys.argv[sys.argv.index('--') + 1:]
folder = Path(args[0])
bpy.ops.wm.open_mainfile(filepath=str(folder / 'native_unchanged.blend'))
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
human = Human.from_existing(rig)
body = human.objects.body
eyes = human.objects.eyes
bones = {name: human.pose.get_posebone_by_original_name('eyeball.' + side).bone
         for name, side in [('LeftEye', 'L'), ('RightEye', 'R')]}
assert 'tail_verts' in bones['LeftEye']
if 'tail_verts' not in bones['RightEye']: assert rig.data.use_mirror_x, 'Unsupported asymmetric native eye binding'
indices = {'LeftEye': list(bones['LeftEye']['tail_verts'])}
if 'tail_verts' in bones['RightEye']: indices['RightEye'] = list(bones['RightEye']['tail_verts'])
size_key = eyes.data.shape_keys.key_blocks['eyeball_size']
eye_basis = np.array([tuple(v.co) for v in eyes.data.shape_keys.reference_key.data])
eye_delta = np.array([tuple(v.co) for v in size_key.data]) - eye_basis
eye_current = eye_basis + eye_delta * size_key.value
matrix = np.array(eyes.matrix_world)
def world(points):
    return points @ matrix[:3, :3].T + matrix[:3, 3]
def direction(points):
    return points @ matrix[:3, :3].T
# Eye world coordinates are exported to the engine's Y-up basis by glTF.
def godot(points):
    return np.array(points)[..., [0, 2, 1]] * np.array([1, 1, -1])
data = dict(schema=1, size_default=size_key.value,
            eye_vertices=godot(world(eye_current)).tolist(), eye_size_delta=godot(direction(eye_delta)).tolist(),
            keys={}, key_mapping={})
used = set()
for key in human.keys.all_livekeys:
    if key.name in used or key.name.startswith('height_'):
        continue
    used.add(key.name)
    path = Path(get_prefs().filepath, key.path)
    if not path.stat().st_size:
        assert 'trial' in key.name.lower() and key.value == 0
        continue
    name = f'{key.category[0]}_{{{key.subcategory}}}_{key.name}' if key.subcategory else f'{key.category[0]}_{key.name}'
    delta = import_npz_key(len(body.data.vertices), str(path)).reshape((-1, 3))
    data['key_mapping'][key.name] = name
    tails = {bone: godot(delta[vertices].mean(axis=0)).tolist() for bone, vertices in indices.items()}
    if 'RightEye' not in tails: tails['RightEye'] = (np.array(tails['LeftEye']) * np.array([-1, 1, 1])).tolist()
    data['keys'][name] = dict(default=key.value, width_delta=godot(delta[1109] - delta[7010]).tolist(), tails=tails)
# This is the native width at the prepared preset, including its live-key mix.
from HumGen3D.common.geometry import world_coords_from_obj
coords = world_coords_from_obj(body, data=human.keys.all_deformation_shapekeys)
data['width_default'] = godot(coords[1109] - coords[7010]).tolist()
assert abs(94 * np.linalg.norm(data['width_default']) - 2 - data['size_default']) < .00001
# A focused native-API contract check: move the three eye-fitting controls,
# compare the landmark prediction with HumGen's real refit, then discard memory.
before = {name: godot(bone.tail_local) for name, bone in bones.items()}
changes = [('Eye Depth', .2), ('Eye Distance', -.15), ('Eye Height', .1)]
width = np.array(data['width_default'])
predicted = {name: value.copy() for name, value in before.items()}
for name, change in changes:
    key = human.keys.get(name)
    key.set_without_update(key.value + change)
    delta = data['keys'][data['key_mapping'][name]]
    width += np.array(delta['width_delta']) * change
    for bone in predicted: predicted[bone] += np.array(delta['tails'][bone]) * change
human.keys.update_human_from_key_change(bpy.context)
after = {name: godot(human.pose.get_posebone_by_original_name('eyeball.' + side).bone.tail_local)
         for name, side in [('LeftEye', 'L'), ('RightEye', 'R')]}
errors = {name: float(np.linalg.norm(predicted[name] - value)) for name, value in after.items()}
size_error = abs(float(eyes.data.shape_keys.key_blocks['eyeball_size'].value) - (94 * np.linalg.norm(width) - 2))
assert max(errors.values()) < .00002 and size_error < .00002, (errors, size_error)
data['native_api_check'] = dict(tail_error=errors, size_error=size_error)
(folder / 'eye_fit_source.json').write_text(json.dumps(data, separators=(',', ':')))
print('NATIVE_EYE_FIT_EXPORTED', len(data['keys']), len(eye_current), flush=True)
