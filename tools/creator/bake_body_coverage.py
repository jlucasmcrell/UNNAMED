"""Rasterise the exported body-coverage triangles into a mask texture (F1 fix 8).

The part export writes the body triangles a worn garment covers, in the body's own UV space. This paints them into a single-channel
PNG that the skin shader discards against, dilating the edges by a texel or two so a mip or a filtering sample at the seam cannot
leak skin through.
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw
import numpy as np

SIZE = 1024
GROW = 3


def bake(folder):
    source = folder / 'body_coverage.json'
    if not source.exists():
        return None
    data = json.loads(source.read_text(encoding='utf-8'))
    triangles = data.get('uv_triangles') or []
    if not triangles:
        return None
    mask = Image.new('L', (SIZE, SIZE), 0)
    draw = ImageDraw.Draw(mask)
    for t in triangles:
        points = [(t[i] * SIZE, (1.0 - t[i + 1]) * SIZE) for i in (0, 2, 4)]
        draw.polygon(points, fill=255)
    # Grow the coverage a few texels: a filtered sample on the seam must not find skin inside the hole.
    original = np.asarray(mask)
    padded = np.pad(original, GROW, mode='constant')
    grown = original.copy()
    for offset in range(1, GROW + 1):
        shifted = []
        for dx, dy in ((offset, 0), (-offset, 0), (0, offset), (0, -offset)):
            shifted.append((dx, dy))
        for dx, dy in shifted:
            # An atlas is not tiled: wrapping its hem onto the opposite edge
            # erases exposed skin at the neck/face UV border.
            shifted = padded[GROW-dy:GROW-dy+SIZE, GROW-dx:GROW-dx+SIZE]
            grown = np.maximum(grown, shifted)
    grown = Image.fromarray(grown)
    # Padding a seam must never eat into the exposed neck, face or hands.
    exposed = ImageDraw.Draw(grown)
    for t in data.get('protected_uv_triangles') or []:
        exposed.polygon([(t[i] * SIZE, (1.0-t[i+1]) * SIZE) for i in (0, 2, 4)], fill=0)
    out = folder / 'body_coverage.png'
    grown.save(out)
    covered = sum(1 for value in grown.getdata() if value)
    print(f'{folder.name}: {len(triangles)} triangles, {covered} texels covered -> {out.name}')
    return out


if __name__ == '__main__':
    root = Path(sys.argv[1])
    for name in sys.argv[2:]:
        bake(root / name)
