"""Which shipped mesh in the outfit part is far larger than a garment? That is the stray lining seen beside the figure (fix 8)."""
import json
import struct
import sys
from pathlib import Path

folder = Path(sys.argv[1])
glb = next(folder.glob('*_rigged.glb'))
data = glb.read_bytes()
length, _ = struct.unpack_from('<II', data, 12)
doc = json.loads(data[20:20 + length].decode('utf-8'))
for node in doc.get('nodes', []):
    if 'mesh' not in node:
        continue
    mesh = doc['meshes'][node['mesh']]
    spans = []
    for prim in mesh['primitives']:
        accessor = doc['accessors'][prim['attributes']['POSITION']]
        if 'min' not in accessor:
            continue
        spans.append((accessor['max'][0] - accessor['min'][0], accessor['max'][1] - accessor['min'][1],
                      accessor['max'][2] - accessor['min'][2]))
    if not spans:
        continue
    widest = max(max(span) for span in spans)
    if widest > .6:
        print(f'{widest:6.3f}  {node.get("name", "?")}')
