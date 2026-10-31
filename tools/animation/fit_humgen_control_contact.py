"""Fit a control take's whole Root height to its unchanged native skin, offline."""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
from audit_humgen_transport import Glb


def surfaces(path, feet_only):
    model = Glb(path)
    selected = [node for node in model.nodes if 'mesh' in node and
                node.get('name', '').endswith('LOD0') and
                (('Boots' in node.get('name', '')) if feet_only else
                 ('Body_LOD0' in node.get('name', '') or 'Frontier_' in node.get('name', '')))]
    result = []
    for node in selected:
        skin = model.doc['skins'][node['skin']]
        names = [model.nodes[i]['name'] for i in skin['joints']]
        assert len(names) == 59
        bind = model.accessor(skin['inverseBindMatrices']).reshape((-1, 4, 4)).transpose(0, 2, 1)
        for primitive in model.doc['meshes'][node['mesh']]['primitives']:
            attrs = primitive['attributes']
            positions = model.accessor(attrs['POSITION'])
            joints = model.accessor(attrs['JOINTS_0']).astype(int)
            weights = model.accessor(attrs['WEIGHTS_0'])
            assert np.max(np.abs(weights.sum(axis=1) - 1)) < .001
            result.append((names, bind, np.column_stack((positions, np.ones(len(positions)))), joints, weights))
    return model, result


def lowest(parts, pose):
    height = float('inf')
    for names, bind, points, joints, weights in parts:
        matrices = np.array([pose[name] @ bind[i] for i, name in enumerate(names)])
        vertices = sum(weights[:, k, None] * np.einsum('nij,nj->ni', matrices[joints[:, k]], points)
                       for k in range(4))
        height = min(height, float(vertices[:, 1].min()))
    return height


def fit(manifest):
    body, body_parts = surfaces(manifest['body'], False)
    boots, boot_parts = surfaces(manifest['footwear'], True)
    _, garment_parts = surfaces(manifest['footwear'], False)
    assert body_parts and boot_parts
    rest = body.globals()
    assert max(np.max(np.abs(rest[n] - boots.globals()[n])) for n in rest) < .0001
    for entry in manifest['clips']:
        if not entry.get('contact_fit'):
            continue
        assert entry['contact_fit'] in ('feet', 'body')
        clip = Glb(entry['path'])
        assert max(np.max(np.abs(rest[n] - clip.globals()[n])) for n in rest) < .0001
        animation = clip.doc['animations'][0]
        times = sorted(set(float(t) for sampler in animation['samplers']
                           for t in clip.accessor(sampler['input'])[:, 0]))
        # Window values use playback time; fitting samples retain the raw transport clock.
        rate = abs(entry.get('source_rate_scale', 1))
        first = entry.get('start_seconds', 0) * rate
        last = entry.get('end_seconds', times[-1] / rate) * rate
        times = sorted(set([first, last] + [t for t in times if first < t < last]))
        parts = boot_parts if entry['contact_fit'] == 'feet' else body_parts + garment_parts
        samples = [[t, -lowest(parts, clip.globals(t))] for t in times]
        assert all(np.isfinite(offset) and abs(offset) < .5 for _, offset in samples)
        entry['root_height_samples'] = samples
        entry['contact_fit_proof'] = {
            'body_sha256': hashlib.sha256(Path(manifest['body']).read_bytes()).hexdigest(),
            'footwear_sha256': hashlib.sha256(Path(manifest['footwear']).read_bytes()).hexdigest(),
            'transport_sha256': hashlib.sha256(Path(entry['path']).read_bytes()).hexdigest(),
            'min_offset_m': min(offset for _, offset in samples),
            'max_offset_m': max(offset for _, offset in samples),
            'scope': 'Unchanged native LOD0 skin and 59-joint binds; whole Root Y only. Candidate; native visual/contact review still required.',
        }
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('manifest', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    args.output.write_text(json.dumps(fit(json.loads(args.manifest.read_text())), indent=2))
