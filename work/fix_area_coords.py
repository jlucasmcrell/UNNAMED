"""Place the area wildlife in each region's own coordinate space (region fauna at full scale).

Barowworks and the gorge mouth were given frontier-scale coordinates, which put them outside their regions entirely - the pack accepted them but
nothing could ever be there. Each region numbers its ground itself: Barrowworks runs roughly 20-75 by 40-110, the gorge 510-545 by 760-790.
"""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')
FIX = {
    'barrowworks_crows.yaml': (66, 80),
    'barrowworks_hares.yaml': (60, 92),
    'gorge_mouth_hares.yaml': (522, 776),
    'gorge_mouth_fox.yaml': (534, 788),
}

for name, (x, z) in FIX.items():
    path = ROOT / name
    text = path.read_text(encoding='utf-8')
    text = re.sub(r'(?m)^  position_m: \[.*\]$', f'  position_m: [{x}, {z}]', text)
    path.write_text(text, encoding='utf-8')
    print(name, '->', x, z)
