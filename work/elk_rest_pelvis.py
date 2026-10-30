"""What does the elk's own skeleton say the pelvis sits at? The clips put it at 0.19 m, which is where the body sinks."""
import json
import struct
from pathlib import Path

MODELS = [
    Path(r'D:\Otherreach_Shared\art_root\models\creatures\creature_wm_elk.glb'),
    Path(r'C:\Otherreach_Farm\builds\claude_min\game\animals\creature_wm_elk.glb'),
]


def load(path):
    data = path.read_bytes()
    offset, doc = 12, None
    while offset < len(data):
        length, kind = struct.unpack_from('<II', data, offset)
        payload = data[offset + 8:offset + 8 + length]
        if kind == 0x4E4F534A:
            doc = json.loads(payload.decode('utf-8'))
        offset += 8 + length
    return doc


for path in MODELS:
    if not path.exists():
        print('missing', path)
        continue
    doc = load(path)
    print(path.name)
    for node in doc.get('nodes', []):
        name = node.get('name', '')
        if name in ('RigPelvis', 'RigRoot', 'root', 'Rig'):
            print(f"   {name:<12} translation {node.get('translation')} scale {node.get('scale')}")
