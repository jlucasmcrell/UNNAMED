"""Bake a source armature's constant unit scale without changing world skin motion.

Keep original files; express joints in scene units and compensate inverse binds.
The companion hierarchy flattening also makes the imported reference and animated
coordinate systems agree; a sideways reference causes enormous retarget scaling.
Original timestamps, rotations, mesh positions and world skin matrices are retained.
This is source transport normalization only, never a change to a HumGen target.
"""
import argparse
import copy
import json
from pathlib import Path
import struct
import numpy as np
from audit_humgen_transport import Glb


def normalize(source, destination):
    old = Glb(source)
    doc = copy.deepcopy(old.doc)
    data = bytearray(old.binary)
    joint_set = set(old.joints)
    candidates = set()
    for joint in joint_set:
        parent = old.parents.get(joint)
        while parent is not None:
            if parent not in joint_set:
                scale = old.nodes[parent].get('scale', [1, 1, 1])
                if max(scale) - min(scale) < 1e-6 and abs(scale[0] - 1) > 1e-4:
                    candidates.add(parent)
            parent = old.parents.get(parent)
    if not candidates:
        return dict(status='ALREADY_SCENE_UNITS', source=str(source))
    assert len(candidates) == 1, ('Ambiguous source unit ancestors', source, candidates)
    ancestor = candidates.pop()
    factor = float(old.nodes[ancestor]['scale'][0])
    assert factor > 0 and factor < 1000
    descendants = set()
    def visit(node):
        for child in doc['nodes'][node].get('children', []):
            descendants.add(child)
            visit(child)
    visit(ancestor)
    assert joint_set <= descendants
    doc['nodes'][ancestor]['scale'] = [1, 1, 1]
    for node in descendants:
        value = doc['nodes'][node]
        if 'translation' in value:
            value['translation'] = [v * factor for v in value['translation']]
        if 'matrix' in value:
            value['matrix'][12:15] = [v * factor for v in value['matrix'][12:15]]
        assert 'mesh' not in value or 'skin' in value, ('Unskinned child requires its own scale bake', source, node)
    converted = {}
    def accessor(index, kind, scale):
        key = index, kind, scale
        if key in converted:
            return converted[key]
        values = old.accessor(index).astype('<f4')
        if kind == 'inverse_bind':
            # Column-major matrices: left multiplication by the unit scale.
            values[:, [0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14]] *= scale
        else:
            values *= scale
        while len(data) % 4:
            data.append(0)
        view = len(doc['bufferViews'])
        doc['bufferViews'].append(dict(buffer=0, byteOffset=len(data), byteLength=values.nbytes))
        data.extend(values.tobytes())
        item = copy.deepcopy(doc['accessors'][index])
        item.update(bufferView=view, byteOffset=0, componentType=5126)
        item.pop('sparse', None)
        if 'min' in item:
            item['min'] = values.min(axis=0).tolist()
        if 'max' in item:
            item['max'] = values.max(axis=0).tolist()
        result = len(doc['accessors'])
        doc['accessors'].append(item)
        converted[key] = result
        return result
    for skin in doc['skins']:
        assert set(skin['joints']) <= descendants
        skin['inverseBindMatrices'] = accessor(skin['inverseBindMatrices'], 'inverse_bind', factor)
    for animation in doc.get('animations', []):
        original_samplers = list(animation['samplers'])
        for channel in animation['channels']:
            node, kind = channel['target']['node'], channel['target']['path']
            scale = factor if node in descendants and kind == 'translation' else 1 / factor if node == ancestor and kind == 'scale' else None
            if scale is None:
                continue
            sampler = dict(original_samplers[channel['sampler']])
            sampler['output'] = accessor(sampler['output'], kind, scale)
            channel['sampler'] = len(animation['samplers'])
            animation['samplers'].append(sampler)
    doc['buffers'][0]['byteLength'] = len(data)
    encoded = json.dumps(doc, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    data.extend(b'\0' * (-len(data) % 4))
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(encoded) + len(data))
        + struct.pack('<II', len(encoded), 0x4E4F534A) + encoded
        + struct.pack('<II', len(data), 0x004E4942) + data)
    new = Glb(destination)
    assert set(old.names) == set(new.names)
    old_binds = old.accessor(old.doc['skins'][0]['inverseBindMatrices'])
    new_binds = new.accessor(new.doc['skins'][0]['inverseBindMatrices'])
    duration = max(float(old.accessor(s['input'])[-1, 0]) for s in old.doc['animations'][0]['samplers'])
    error = 0.0
    # Check the skinning transform itself, not just a standing preview or a rig height.
    for at in [None, *np.linspace(0, duration, 5)]:
        before, after = old.globals(at), new.globals(at)
        for index, joint in enumerate(old.joints):
            name = old.nodes[joint]['name']
            a = before[name] @ old_binds[index].reshape(4, 4).T
            b = after[name] @ new_binds[index].reshape(4, 4).T
            error = max(error, float(np.max(np.abs(a - b))))
    assert error < .0001, ('World skin motion changed during source unit conversion', source, error)
    return dict(status='SCENE_UNITS_VERIFIED', source=str(source), output=str(destination),
                factor=factor, world_skin_matrix_error=error, seconds=duration)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    result = normalize(args.source, args.destination)
    print(json.dumps(result, indent=2))
