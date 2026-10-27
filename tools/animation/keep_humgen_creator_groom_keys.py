"""Retain HumGen's own body-vertex binding for runtime face/body changes.

HumGen normally keeps only animated keys on cards and bakes static fitting. A
creator also changes static keys. Transfer those with the SAME native binding,
undoing the base preset's contribution first so default weights stay identical.
"""
import numpy as np
from HumGen3D.human.hair import hair_binding

STAGE = 'before_meshes'


def main(context, human):
    body = human.objects.body
    keys = [key for key in body.data.shape_keys.key_blocks if key.name.startswith(('f_', 'b_', 's_'))]
    rotation = np.array(hair_binding._body_to_rig(human))[:3, :3]
    for obj in human.objects.haircards:
        indices = hair_binding.get_attachment(human, obj.data)
        if indices is None:
            raise ValueError('Native groom binding missing: ' + obj.name)
        if not obj.data.shape_keys:
            obj.shape_key_add(name='Basis', from_mix=False)
        shape_keys = obj.data.shape_keys.key_blocks
        deltas = [(key, hair_binding._key_delta(key)[indices] @ rotation.T) for key in keys]
        correction = sum((delta * key.value for key, delta in deltas), np.zeros((len(indices), 3)))
        for key in list(shape_keys):
            coords = hair_binding.get_coords(key.data) - correction
            key.data.foreach_set('co', coords.ravel())
        basis = hair_binding.get_coords(shape_keys[0].data)
        for source, delta in deltas:
            if np.abs(delta).max(initial=0) < hair_binding.MIN_KEY_DISPLACEMENT:
                continue
            target = obj.shape_key_add(name=source.name, from_mix=False)
            target.data.foreach_set('co', (basis + delta).ravel())
            target.slider_min, target.slider_max = source.slider_min, source.slider_max
            target.value = source.value
        print('CREATOR_GROOM_KEYS', obj.name, len(obj.data.shape_keys.key_blocks), flush=True)
