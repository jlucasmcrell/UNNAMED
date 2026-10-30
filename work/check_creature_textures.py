"""What textures the creature GLBs actually reference, and whether those images are in the file at all (the flat-white deer).

A creature that renders flat white either carries no texture reference or carries one the loader cannot resolve. This prints, per model, the
material count, the images the glTF declares, and whether each image is embedded (a bufferView) or an external uri.
"""
import json
import struct
from pathlib import Path

ASSETS = Path(r'D:\Otherreach_Scratch\art_merged\rigged')


def glb_json(path):
    data = path.read_bytes()
    offset = 12
    while offset < len(data):
        length, kind = struct.unpack_from('<II', data, offset)
        if kind == 0x4E4F534A:
            return json.loads(data[offset + 8:offset + 8 + length].decode('utf-8'))
        offset += 8 + length
    raise SystemExit(f'{path}: no JSON chunk')


for folder in sorted(ASSETS.glob('creature_wm_*')):
    model = folder / f'{folder.name}_rigged.glb'
    if not model.exists():
        continue
    doc = glb_json(model)
    images = doc.get('images', [])
    materials = doc.get('materials', [])
    embedded = sum(1 for image in images if 'bufferView' in image)
    external = [image.get('uri') for image in images if 'uri' in image]
    with_base_colour = sum(1 for m in materials if 'baseColorTexture' in m.get('pbrMetallicRoughness', {}))
    print(f'{folder.name}: materials {len(materials)}, with base colour texture {with_base_colour}, '
          f'images {len(images)} (embedded {embedded}, external {len(external)})')
    for uri in external[:3]:
        print('     external:', uri)
