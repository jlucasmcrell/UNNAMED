"""Crop the chest of one NPC frame at 2x, to judge whether skin shows through the cloth (F1 Gate C fix 2)."""
import sys
from pathlib import Path

from PIL import Image

image = Image.open(sys.argv[1]).convert("RGB")
left = int(image.width * float(sys.argv[2]))
top = int(image.height * float(sys.argv[3]))
right = int(image.width * float(sys.argv[4]))
bottom = int(image.height * float(sys.argv[5]))
crop = image.crop((left, top, right, bottom))
crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
out = Path(sys.argv[6])
out.parent.mkdir(parents=True, exist_ok=True)
crop.save(out)
print(out, crop.size)
