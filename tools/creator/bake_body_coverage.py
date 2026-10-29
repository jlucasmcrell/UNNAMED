"""Rasterise the exported body-coverage triangles into a mask texture (F1 fix 8).

The part export writes the body triangles a worn garment covers, in the body's own UV space. This paints them into a single-channel
PNG that the skin shader discards against, dilating the edges by a texel or two so a mip or a filtering sample at the seam cannot
leak skin through.
"""
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw

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
    grown = mask
    for offset in range(1, GROW + 1):
        shifted = []
        for dx, dy in ((offset, 0), (-offset, 0), (0, offset), (0, -offset)):
            shifted.append((dx, dy))
        for dx, dy in shifted:
            grown = Image.fromarray(
                __import__('numpy').maximum(__import__('numpy').asarray(grown),
                                            __import__('numpy').roll(__import__('numpy').asarray(mask), (dy, dx), (0, 1))))
    out = folder / 'body_coverage.png'
    grown.save(out)
    covered = sum(1 for value in grown.getdata() if value)
    print(f'{folder.name}: {len(triangles)} triangles, {covered} texels covered -> {out.name}')
    return out


if __name__ == '__main__':
    root = Path(sys.argv[1])
    for name in sys.argv[2:]:
        bake(root / name)
