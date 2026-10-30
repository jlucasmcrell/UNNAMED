"""Every spawn's level band must equal its members' band, and all five species here are band 1 (region fauna at full scale)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')
NEW_FILES = [
    'ashen_east_deer.yaml', 'ashen_field_hares.yaml', 'ashen_waystation_crows.yaml', 'ashen_ravine_fox.yaml',
    'barrowworks_crows.yaml', 'barrowworks_hares.yaml', 'gorge_mouth_hares.yaml', 'gorge_mouth_fox.yaml',
    'sedge_pool_ducks.yaml', 'sedge_channel_trout.yaml', 'sedge_hummock_hares.yaml',
]

for name in NEW_FILES:
    path = ROOT / name
    if not path.exists():
        print('missing', name)
        continue
    text = path.read_text(encoding='utf-8')
    band = '[1, 1]'
    if 'creature.beast.doe' in text:
        band = '[1, 2]'   # the doe spawn carries a buck, which is band 1-2 like its deer sisters
    text = re.sub(r'(?m)^level_band: .*$', f'level_band: {band}', text)
    path.write_text(text, encoding='utf-8')
    print(name, band)
