"""Put the fishing ids into the whole-content snapshot (goal round 2)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
NEW = ['config.fishing', 'item.material.bait_fly', 'item.material.bait_worm', 'item.tool.fishing_rod']
path = ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs'
lines = path.read_text(encoding='utf-8').split('\n')
first = next(i for i, line in enumerate(lines) if '"ability.blade.power_strike"' in line)
last = max(i for i, line in enumerate(lines) if '"world.' in line)
for new_id in NEW:
    if any(f'"{new_id}"' in line for line in lines[first:last + 1]):
        continue
    for index in range(first, last + 1):
        ids = re.findall(r'"([^"]+)"', lines[index])
        after = next((value for value in ids if value.lower() > new_id.lower()), None)
        if after is None:
            continue
        lines[index] = lines[index].replace(f'"{after}"', f'"{new_id}", "{after}"', 1)
        break
    else:
        lines[last] = lines[last].rstrip().rstrip(',') + f', "{new_id}",'
path.write_text('\n'.join(lines), encoding='utf-8')
print('fishing ids added to the snapshot')
