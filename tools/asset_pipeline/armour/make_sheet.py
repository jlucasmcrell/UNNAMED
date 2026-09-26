"""Tile PNG renders into a labelled JPG contact sheet (system Python + PIL).
python make_sheet.py <out.jpg> <cols> <title> <img|label> ...
Cells take the first image's size; other images are fitted inside (aspect kept).
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

out, cols, title = sys.argv[1], int(sys.argv[2]), sys.argv[3]
items = []
for a in sys.argv[4:]:
    p, _, label = a.partition("|")
    items.append((Image.open(p).convert("RGB"), label))
w, h = items[0][0].size
rows = (len(items) + cols - 1) // cols
pad, top, lab = 6, 34, 22
sheet = Image.new("RGB", (cols * (w + pad) + pad, top + rows * (h + lab + pad) + pad), (24, 24, 26))
d = ImageDraw.Draw(sheet)
try:
    font = ImageFont.truetype("arial.ttf", 18)
    big = ImageFont.truetype("arialbd.ttf", 22)
except OSError:
    font = big = ImageFont.load_default()
d.text((pad + 4, 6), title, fill=(235, 235, 235), font=big)
for k, (im, label) in enumerate(items):
    r, c = divmod(k, cols)
    x = pad + c * (w + pad)
    y = top + r * (h + lab + pad)
    d.text((x + 4, y + 1), label, fill=(210, 210, 210), font=font)
    if im.size != (w, h):
        s = min(w / im.width, h / im.height)
        im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
    sheet.paste(im, (x + (w - im.width) // 2, y + lab + (h - im.height) // 2))
q = 88
while True:
    sheet.save(out, quality=q, optimize=True)
    if os.path.getsize(out) < 600_000 or q <= 50:
        break
    q -= 6
print(out, sheet.size, os.path.getsize(out), "q", q)
