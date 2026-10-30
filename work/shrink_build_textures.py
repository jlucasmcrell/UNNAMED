"""Downscale the farm build's oversized loose PNGs (the frontier's vfx and material maps) so the game will draw them.

The exported build refuses any packaged texture 2K or larger that is not compressed - `TextureCache.RequireCompressed` throws, and `Main.Start`
turns that into a quit. On the frontier that fires from `ParticleRecipes.Texture` on `art\\vfx\\ext_unity_flame02.png`; the sedge and the gorge never
build that prop effect, which is why they render. This walks the build's own art root and rewrites every PNG over 2048 px to 1024.
"""
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(r'C:\Otherreach_Farm\builds\claude_fauna01\game\art')
LIMIT = 2048


def shrink(path):
    try:
        picture = Image.open(path)
    except Exception:
        return None
    if max(picture.size) < LIMIT:
        return None
    before = picture.size
    scale = 1024 / max(picture.size)
    small = picture.resize((max(1, int(before[0] * scale)), max(1, int(before[1] * scale))), Image.LANCZOS)
    small.save(path)
    return before, small.size


changed = 0
for path in ROOT.rglob('*.png'):
    result = shrink(path)
    if result:
        changed += 1
        before, after = result
        print(f'{path.relative_to(ROOT)}: {before[0]}x{before[1]} -> {after[0]}x{after[1]}')
print(f'{changed} texture(s) rewritten under {ROOT}')
