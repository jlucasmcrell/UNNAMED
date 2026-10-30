"""Crop the animal out of the farm frame at full resolution, because at 1066 px across a deer 20 m away is only a few dozen pixels."""
from pathlib import Path

from PIL import Image

for name, box in {
    'deer03_mid_right': ('C:\\Otherreach_Farm\\results\\claude_motion_deer03\\mid.png', (780, 280, 1080, 460)),
    'elk03_near_bottom': ('C:\\Otherreach_Farm\\results\\claude_motion_elk03\\near.png', (760, 560, 1280, 720)),
}.items():
    source, rect = box
    image = Image.open(source)
    crop = image.crop(rect)
    crop = crop.resize((crop.width * 2, crop.height * 2), Image.LANCZOS)
    out = Path(r'F:\otherreach_captures\fauna_areas') / f'{name}.png'
    out.parent.mkdir(parents=True, exist_ok=True)
    crop.save(out)
    print(out, crop.size)
