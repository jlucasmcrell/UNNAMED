"""Compose preview frames into a labelled 2x2 sheet, because 4 frames side by side at model
resolution is the largest layout that can still be judged. Usage: sheet.py <dir> <out.png>"""
import glob
import os
import sys

from PIL import Image, ImageDraw

directory, out_path = sys.argv[1], sys.argv[2]
files = sorted(glob.glob(os.path.join(directory, "*.png")))
if not files:
    raise SystemExit(f"no PNGs in {directory}")

cell_w, cell_h = 470, 600
sheet = Image.new("RGB", (cell_w * 2, cell_h * 2), (18, 18, 20))
draw = ImageDraw.Draw(sheet)
for index, path in enumerate(files[:4]):
    image = Image.open(path).convert("RGB")
    image.thumbnail((cell_w, cell_h - 22))
    x = (index % 2) * cell_w + (cell_w - image.width) // 2
    y = (index // 2) * cell_h + 22
    sheet.paste(image, (x, y))
    draw.text(((index % 2) * cell_w + 8, (index // 2) * cell_h + 6),
              os.path.basename(path), fill=(235, 235, 235))
sheet.save(out_path)
print(f"{out_path}  {sheet.width}x{sheet.height}  from {len(files)} frame(s)")
