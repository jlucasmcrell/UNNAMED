"""Where the existing spawns of each area actually sit, so new placements land inside the same coordinate space."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns')

for folder in sorted(p for p in ROOT.iterdir() if p.is_dir()):
    rows = []
    for path in sorted(folder.glob('*.yaml')):
        text = path.read_text(encoding='utf-8')
        spawn_id = (re.search(r'id: (\S+)', text) or [None, '?'])[1] if re.search(r'id: (\S+)', text) else '?'
        region = (re.search(r'region_ref: (\S+)', text) or [None, '?'])[1] if re.search(r'region_ref: (\S+)', text) else '?'
        match = re.search(r'position_m:\s*\[?\s*(-?\d+)[,\s]+(-?\d+)', text) or re.search(
            r'position_m:\s*\n\s*-\s*(-?\d+)\s*\n\s*-\s*(-?\d+)', text)
        position = f'{match.group(1)},{match.group(2)}' if match else '?'
        rows.append((spawn_id, region.replace('region.', ''), position))
    if rows:
        print(folder.name)
        for row in rows:
            print('   ', ' | '.join(row))
