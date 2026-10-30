"""Snapshot and count for the hollow's four spawns (region fauna at full scale)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
NEW = ['spawn.ashen.east_deer', 'spawn.ashen.field_hares', 'spawn.ashen.ravine_fox', 'spawn.ashen.waystation_crows']

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
path.write_text('\n'.join(lines), encoding='utf-8')

combat = ROOT / 'tests' / 'Content.Tests' / 'CombatContentTests.cs'
code = combat.read_text(encoding='utf-8')
code = code.replace('Assert.Equal(54, spawns.Count);', 'Assert.Equal(58, spawns.Count);', 1)
combat.write_text(code, encoding='utf-8')
print('hollow spawns added to the snapshot; count 58')
