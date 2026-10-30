"""Where in the watch frames is anything moving? Difference two frames and report the busiest region."""
from pathlib import Path

from PIL import Image, ImageChops

WATCH = Path(r'C:\Otherreach_Farm\results\claude_proof_elkvideo02\watch')
OUT = Path(r'F:\otherreach_captures\fauna_areas')

a = Image.open(WATCH / 'watch_0005.png').convert('RGB')
b = Image.open(WATCH / 'watch_0070.png').convert('RGB')
diff = ImageChops.difference(a, b).convert('L')

# 64x36 grid of busy-ness, and the bounding box of the strong changes
w, h = diff.size
cell_x, cell_y = 64, 36
busy = []
strong = diff.point(lambda v: 255 if v > 60 else 0)
for gy in range(0, h, cell_y):
    for gx in range(0, w, cell_x):
        tile = diff.crop((gx, gy, min(gx + cell_x, w), min(gy + cell_y, h)))
        score = sum(tile.getdata()) / (tile.width * tile.height)
        busy.append((score, gx, gy))
busy.sort(reverse=True)
print('busiest tiles (mean diff, x, y):')
for score, gx, gy in busy[:6]:
    print(f'  {score:6.2f} at ({gx},{gy})')

box = strong.getbbox()
print('bounding box of strong change:', box)
if box:
    pad = 40
    crop = a.crop((max(box[0] - pad, 0), max(box[1] - pad, 0), min(box[2] + pad, w), min(box[3] + pad, h)))
    if crop.width and crop.height:
        crop = crop.resize((crop.width * 3, crop.height * 3), Image.LANCZOS)
        out = OUT / 'elk_motion_region.png'
        crop.save(out)
        print('wrote', out, crop.size)

strong.save(OUT / 'elk_motion_mask.png')
print('wrote mask')
