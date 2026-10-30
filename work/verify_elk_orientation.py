"""Is the shipped elk standing up? Read the root node rotations in the GLB the game loads.

The elk came out of the converter pitched over; the fix bakes +90 degrees on X into every root node. That is a claim about the file, so this reads
the file: a corrected elk's roots carry a rotation near (0.707, 0.707, 0, 0), and an uncorrected one carries none.
"""
import json
import math
import struct
from pathlib import Path

ART = Path(r'D:\Otherreach_Shared\art_root\rigged')

for species in ('elk', 'doe', 'moose'):
    model = ART / f'creature_wm_{species}' / f'creature_wm_{species}_rigged.glb'
    data = model.read_bytes()
    offset, doc = 12, None
    while offset < len(data):
        length, kind = struct.unpack_from('<II', data, offset)
        if kind == 0x4E4F534A:
            doc = json.loads(data[offset + 8:offset + 8 + length].decode('utf-8'))
            break
        offset += 8 + length
    children = {child for node in doc['nodes'] for child in node.get('children', [])}
    roots = [node for index, node in enumerate(doc['nodes']) if index not in children]
    print(f'{species}: {len(roots)} root node(s)')
    for node in roots[:4]:
        rotation = node.get('rotation')
        if rotation is None:
            print(f'    {node.get("name", "?")[:40]}: no rotation')
            continue
        x, y, z, w = rotation
        angle = 2.0 * math.degrees(math.acos(max(-1.0, min(1.0, w))))
        print(f'    {node.get("name", "?")[:40]}: quat ({x:.3f}, {y:.3f}, {z:.3f}, {w:.3f}) = {angle:.1f} deg about its axis')
