"""Add the original native eyeball-size delta to the preserved iris/cornea GLB.

Matches the native source vertices after the exporter Y-up conversion. No
remeshing, synthetic depth, changed textures, eye replacement or extra bodies.
"""
import itertools
import json
import math
import struct
from pathlib import Path


def keep_radius(body, source):
    body = Path(body)
    fit = json.loads(Path(source).read_text())
    blob = body.read_bytes()
    length, _ = struct.unpack_from('<II', blob, 12)
    gltf = json.loads(blob[20:20 + length])
    binary_length, _ = struct.unpack_from('<II', blob, 20 + length)
    binary = bytearray(blob[28 + length:28 + length + binary_length])
    grid = {}
    def bucket(point): return tuple(round(v * 100000) for v in point)
    for point, delta in zip(fit['eye_vertices'], fit['eye_size_delta']):
        grid.setdefault(bucket(point), []).append((point, delta))
    maximum = 0
    count = 0
    for node in gltf['nodes']:
        if not node.get('name', '').endswith(('_Eyes_LOD0', '_Cornea_LOD0')): continue
        mesh = gltf['meshes'][node['mesh']]
        assert not mesh.get('weights') and not mesh.get('extras', {}).get('targetNames'), 'Native eye morph already changed'
        for primitive in mesh['primitives']:
            accessor = gltf['accessors'][primitive['attributes']['POSITION']]
            view = gltf['bufferViews'][accessor['bufferView']]
            assert accessor['componentType'] == 5126 and accessor['type'] == 'VEC3'
            offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
            stride = view.get('byteStride', 12)
            deltas = []
            for index in range(accessor['count']):
                point = struct.unpack_from('<fff', binary, offset + stride * index)
                cell = bucket(point)
                candidates = [item for neighbor in itertools.product((-1, 0, 1), repeat=3)
                              for item in grid.get(tuple(a + b for a, b in zip(cell, neighbor)), [])]
                assert candidates, ('Eye vertex is not the original native source', point)
                original, delta = min(candidates, key=lambda item: math.dist(point, item[0]))
                error = math.dist(point, original)
                assert error < .000002, ('Eye source changed', error)
                maximum = max(maximum, error)
                deltas.append(delta)
            while len(binary) % 4: binary.append(0)
            offset = len(binary)
            for delta in deltas: binary.extend(struct.pack('<fff', *delta))
            view_index = len(gltf['bufferViews'])
            gltf['bufferViews'].append(dict(buffer=0, byteOffset=offset, byteLength=len(deltas) * 12))
            accessor_index = len(gltf['accessors'])
            gltf['accessors'].append(dict(bufferView=view_index, componentType=5126, count=len(deltas), type='VEC3',
                                          min=[min(p[i] for p in deltas) for i in range(3)], max=[max(p[i] for p in deltas) for i in range(3)]))
            primitive['targets'] = [dict(POSITION=accessor_index)]
            count += len(deltas)
        mesh.setdefault('extras', {})['targetNames'] = ['s_eye_radius']
        mesh['weights'] = [0]
    assert count > 0, 'No native eye layers'
    while len(binary) % 4: binary.append(0)
    gltf['buffers'][0]['byteLength'] = len(binary)
    header = json.dumps(gltf, separators=(',', ':')).encode()
    header += b' ' * (-len(header) % 4)
    body.write_bytes(struct.pack('<III', 0x46546C67, 2, 28 + len(header) + len(binary))
                     + struct.pack('<II', len(header), 0x4E4F534A) + header
                     + struct.pack('<II', len(binary), 0x004E4942) + binary)
    fit.pop('eye_vertices')
    fit.pop('eye_size_delta')
    (body.parent / 'eye_fit.json').write_text(json.dumps(fit, separators=(',', ':')))
    print('NATIVE_EYE_RADIUS_RETAINED', count, 'vertices; source max error', maximum, flush=True)
