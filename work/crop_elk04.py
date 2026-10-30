"""Crop the animals out of the elk04 frames at full resolution to judge what is actually in them."""
from pathlib import Path

from PIL import Image

CROPS = {
    'elk04_near_left': ('C:\\Otherreach_Farm\\results\\claude_motion_elk04\\near.png', (280, 400, 640, 560)),
    'elk04_mid_left': ('C:\\Otherreach_Farm\\results\\claude_motion_elk04\\mid.png', (200, 380, 700, 600)),
}

for name, (source, rect) in CROPS.items():
    image = Image.open(source)
    crop = image.crop(rect)
    crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
    out = Path(r'F:\otherreach_captures\fauna_areas') / f'{name}.png'
    crop.save(out)
    print(out, crop.size)
