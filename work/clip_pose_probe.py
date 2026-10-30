"""Is the elk's idle clip actually a lying-down pose? Compare the pelvis height each clip holds.

A creature that stands correctly in `run` but lies in `idle` is not a rig problem: the state-to-clip mapping is fine (the clips exist) but the clip
labelled `idle` may hold the rest/lying pose from the source set. This reads the pelvis translation keyframes out of each clip and prints the Y range,
so "the elk is always lying down in the world" can be answered from the files.
"""
import json
import struct
from pathlib import Path

CLIPS = Path(r'D:\Otherreach_Shared\art_root\animation\ready\creatures')
SPECIES = ['elk', 'moose', 'doe']


def load(path):
    data = path.read_bytes()
    offset, doc, binary = 12, None, b''
    while offset < len(data):
        length, kind = struct.unpack_from('<II', data, offset)
        payload = data[offset + 8:offset + 8 + length]
        if kind == 0x4E4F534A:
            doc = json.loads(payload.decode('utf-8'))
        elif kind == 0x004E4942:
            binary = payload
        offset += 8 + length
    return doc, binary


def samples(doc, binary, accessor):
    view = doc['bufferViews'][accessor['bufferView']]
    start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    count = accessor['count']
    kind = accessor['type']
    width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[kind]
    stride = view.get('byteStride') or width * 4
    out = []
    for i in range(count):
        base = start + i * stride
        out.append(struct.unpack_from('<' + 'f' * width, binary, base))
    return out


for species in SPECIES:
    print(f'== {species}')
    for clip in CLIPS.glob(f'anim.creature.creature_wm_{species}.*.glb'):
        state = clip.name.split('.')[-2]
        doc, binary = load(clip)
        # the joint whose Y travels furthest is the pelvis on a quadruped
        best = None
        for animation in doc.get('animations', []):
            for channel in animation['channels']:
                if channel['target']['path'] != 'translation':
                    continue
                accessor = doc['accessors'][animation['samplers'][channel['sampler']]['output']]
                values = samples(doc, binary, accessor)
                if not values or len(values[0]) != 3:
                    continue
                ys = [v[1] for v in values]
                spread = max(ys) - min(ys)
                node = doc['nodes'][channel['target']['node']].get('name', '?')
                if best is None or spread > best[1]:
                    best = (node, spread, min(ys), max(ys))
        if best:
            node, spread, low, high = best
            print(f'  {state:<10} {node[:24]:<24} y {low:6.2f} .. {high:6.2f}  (travels {spread:5.2f} m)')
