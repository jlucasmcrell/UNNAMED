"""Container labels may change during export; their transforms may not disappear."""
import copy
import numpy as np
from stage_native_character import bones


source = {'nodes': [
    {'name': 'Scene', 'children': [1], 'translation': [2, 0, 0]},
    {'name': 'Armature', 'children': [2], 'scale': [2, 2, 2]},
    {'name': 'Hips', 'children': [3], 'translation': [0, 1, 0]},
    {'name': 'ExportWrapper', 'children': [4], 'translation': [0, 0, 1]},
    {'name': 'Head', 'translation': [0, 1, 0]}], 'skins': [{'joints': [2, 4]}]}
expected = bones(source)
assert expected['Hips'][0] is None and expected['Head'][0] == 'Hips'
assert np.allclose(expected['Hips'][1][:3, 3], [2, 2, 0])
assert np.allclose(expected['Head'][1][:3, 3], [0, 1, 1])
renamed = copy.deepcopy(source)
for index in (0, 1, 3): renamed['nodes'][index]['name'] = 'DifferentContainer' + str(index)
assert all(expected[name][0] == parent and np.allclose(expected[name][1], matrix)
           for name, (parent, matrix) in bones(renamed).items())
for index, field, value in ((0, 'translation', [3, 0, 0]), (1, 'scale', [1, 1, 1]),
                            (3, 'translation', [0, 0, 2]), (3, 'rotation', [0, 0, 1, 0])):
    changed = copy.deepcopy(source)
    changed['nodes'][index][field] = value
    assert any(not np.allclose(expected[name][1], matrix) for name, (_, matrix) in bones(changed).items())
changed = copy.deepcopy(source)
changed['nodes'][2]['children'] = [4]
changed['nodes'][3]['children'] = []
assert not np.allclose(expected['Head'][1], bones(changed)['Head'][1])
print('NATIVE_RIG_EQUIVALENCE_CONTROLS_PASS: renamed containers accepted; root/scale/wrapper/rotation changes and lost wrapper rejected')
