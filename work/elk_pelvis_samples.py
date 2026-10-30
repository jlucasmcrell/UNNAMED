"""Print the elk pelvis translation over a clip, so the axis that carries metres of travel can be identified before anything is rotated."""
import json
import struct
from pathlib import Path

CLIPS = Path(r'D:\Otherreach_Shared\art_root\animation\ready\creatures')


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


def read(doc, binary, accessor):
    view = doc['bufferViews'][accessor['bufferView']]
    start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[accessor['type']]
    stride = view.get('byteStride') or width * 4
    return [struct.unpack_from('<' + 'f' * width, binary, start + i * stride) for i in range(accessor['count'])]


for state in ('idle', 'walk', 'run'):
    clip = CLIPS / f'anim.creature.creature_wm_elk.{state}.glb'
    doc, binary = load(clip)
    for animation in doc['animations']:
        for channel in animation['channels']:
            node = doc['nodes'][channel['target']['node']].get('name', '')
            if node != 'RigPelvis' or channel['target']['path'] != 'translation':
                continue
            values = read(doc, binary, doc['accessors'][animation['samplers'][channel['sampler']]['output']])
            picked = values[:: max(1, len(values) // 6)][:6]
            print(f'{state:<5} {len(values):>3} keys')
            for v in picked:
                print(f'   x {v[0]:7.3f}  y {v[1]:7.3f}  z {v[2]:7.3f}')
