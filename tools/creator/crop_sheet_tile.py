"""Crop one NPC tile from a face sheet at 2x, to judge dress and face detail (F1 Gate C)."""
import sys
from pathlib import Path

from PIL import Image

sheet = Image.open(sys.argv[1])
row, column = int(sys.argv[2]), int(sys.argv[3])
tile_width = sheet.width // 3
tile_height = 790 // 1 if False else None
# The builder writes 512-px tiles with a 26-px caption; derive the tile pitch from the sheet's own grid.
pitch = sheet.height // max(1, round(sheet.height / 790)) if False else None
tile = sheet.width // 3
label = 26
rows = (sheet.height + 0) // (tile + label)
step = sheet.height / max(1, rows)
x, y = column * tile_width, int(row * step)
crop = sheet.crop((x, y, x + tile_width, y + int(step)))
crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
out = Path(sys.argv[4])
out.parent.mkdir(parents=True, exist_ok=True)
crop.save(out)
print(out, crop.size)
