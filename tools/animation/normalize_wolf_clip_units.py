"""Normalize a mesh-free vendor transport's units while preserving its world-space bone motion."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'character_build'))
from glb import Glb
from audit_humgen_transport import Glb as Measure


def normalize(path, raw):
    if not raw.exists():
        shutil.copy2(path, raw)
    model = Glb(raw)
    assert not model.doc.get('meshes'), 'This operation is for animation-only source transports'
    roots = model.doc['scenes'][model.doc.get('scene', 0)]['nodes']
    assert len(roots) == 1
    root = roots[0]
    scale = np.array(model.doc['nodes'][root]['scale'])
    assert np.max(np.abs(scale-scale[0])) < 1e-7 and .0099 < scale[0] < .0101
    factor = float(scale[0])
    assert model.doc['nodes'][root].get('translation', [0, 0, 0]) == [0, 0, 0]
    model.doc['nodes'][root]['scale'] = [1, 1, 1]
    for i, node in enumerate(model.doc['nodes']):
        if i != root and 'translation' in node:
            node['translation'] = (np.array(node['translation'])*factor).tolist()
    outputs, root_scales = set(), set()
    for animation in model.doc['animations']:
        for channel in animation['channels']:
            if channel['target']['node'] == root:
                if channel['target']['path'] == 'scale':
                    root_scales.add(animation['samplers'][channel['sampler']]['output'])
            elif channel['target']['path'] == 'translation':
                outputs.add(animation['samplers'][channel['sampler']]['output'])
    for index in outputs:
        model.write(index, model.read(index)*factor)
    assert not outputs & root_scales
    for index in root_scales:
        model.write(index, model.read(index)/factor)
    # Recompute only this mesh-free source's binds in the normalized representation.
    # Target body/rig files are never opened for writing.
    model.save(path)
    measure = Measure(path)
    rests = measure.globals()
    for skin in model.doc['skins']:
        matrices = [np.linalg.inv(rests[model.doc['nodes'][joint]['name']]).T.reshape(16) for joint in skin['joints']]
        model.write(skin['inverseBindMatrices'], np.array(matrices))
    model.save(path)
    before, after = Measure(raw), Measure(path)
    end = max(before.accessor(s['input'])[-1, 0] for s in before.doc['animations'][0]['samplers'])
    position_error = orientation_error = 0.0
    for at in [None, *np.linspace(0, end, 13)]:
        a, b = before.globals(at), after.globals(at)
        assert a.keys() == b.keys()
        for name in a:
            position_error = max(position_error, float(np.max(np.abs(a[name][:3, 3]-b[name][:3, 3]))))
            x, y = a[name][:3, :3], b[name][:3, :3]
            x = x/np.linalg.norm(x, axis=0); y = y/np.linalg.norm(y, axis=0)
            orientation_error = max(orientation_error, float(np.max(np.abs(x-y))))
    assert position_error < .00005 and orientation_error < .0001
    return {'factor':factor, 'raw_source_clip':str(raw), 'raw_sha256':hashlib.sha256(raw.read_bytes()).hexdigest(),
            'normalized_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
            'max_world_bone_position_error_m':position_error, 'max_world_orientation_error':orientation_error}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    args = parser.parse_args()
    receipt_path = args.root/'CMB16_WOLF_SOURCE.json'
    receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
    raw_dir = args.root/'raw_centimetre_transports';raw_dir.mkdir(exist_ok=True)
    for entry in receipt['clips']:
        path = Path(entry['path']);entry['unit_normalization'] = normalize(path, raw_dir/path.name)
        entry['sha256'] = hashlib.sha256(path.read_bytes()).hexdigest()
        print(path.name, 'world error m', entry['unit_normalization']['max_world_bone_position_error_m'])
    receipt['status'] = 'UNIT_NORMALIZED_SOURCE_CANDIDATES_NOT_BOUND'
    receipt['unit_scope'] = 'Mesh-free source scale baked into joint/key translations and source binds; measured world-space bone origins/orientations preserved at rest and13 timestamps. Original vendor FBX, target body and target rig unchanged. Native target gate/render review still required.'
    receipt_path.write_text(json.dumps(receipt, indent=2), encoding='utf-8')
