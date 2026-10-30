"""Put the hollow's wildlife where the game actually simulates: the frontier's Ashen Hollow ground (region fauna at full scale).

region.ashen_hollow is a 200 m region file the session does not load, so its four spawns were declared and never placed. The frontier carries the
same hollow on its own grid (existing hollow spawns run 418-588 by 620-784), so the four move there and keep their names.
"""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')
MOVE = {
    'ashen_field_hares.yaml': (470, 700),
    'ashen_east_deer.yaml': (560, 664),
    'ashen_waystation_crows.yaml': (455, 744),
    'ashen_ravine_fox.yaml': (432, 690),
}

for name, (x, z) in MOVE.items():
    path = ROOT / name
    text = path.read_text(encoding='utf-8')
    text = text.replace('region_ref: region.ashen_hollow', 'region_ref: region.starting_frontier')
    text = re.sub(r'(?m)^  position_m: \[.*\]$', f'  position_m: [{x}, {z}]', text)
    path.write_text(text, encoding='utf-8')
    print(name, '-> frontier', x, z)
