"""Add the eleven new spawn ids to the pack snapshot (region fauna at full scale)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
NEW = [
    'spawn.cairnwold.moor_crows', 'spawn.cairnwold.millrace_trout',
    'spawn.cairnwold.foldscar_moose_ground',
    'spawn.field.deer', 'spawn.barrowworks.yard_fox',
    'spawn.ashen_hollow.deer', 'spawn.ashen_hollow.hares', 'spawn.ashen_hollow.crows', 'spawn.ashen_hollow.fox',
    'spawn.field.crows', 'spawn.field.hares', 'spawn.old_late.crows', 'spawn.old_late.hares',
    'spawn.ashen.east_deer', 'spawn.ashen.field_hares', 'spawn.ashen.ravine_fox', 'spawn.ashen.waystation_crows',
    'spawn.barrowworks.ruin_crows', 'spawn.barrowworks.yard_hares',
    'spawn.den_gorge.mouth_fox', 'spawn.den_gorge.mouth_hares',
    'spawn.sedge.channel_trout', 'spawn.sedge.hummock_hares', 'spawn.sedge.pool_ducks',
]

path = ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs'
lines = path.read_text(encoding='utf-8').split('\n')
first = next(i for i, line in enumerate(lines) if '"ability.blade.power_strike"' in line)
last = max(i for i, line in enumerate(lines) if '"world.' in line)

added = 0
for new_id in NEW:
    if any(f'"{new_id}"' in line for line in lines[first:last + 1]):
        continue
    for index in range(first, last + 1):
        ids = re.findall(r'"([^"]+)"', lines[index])
        after = next((value for value in ids if value.lower() > new_id.lower()), None)
        if after is None:
            continue
        lines[index] = lines[index].replace(f'"{after}"', f'"{new_id}", "{after}"', 1)
        added += 1
        break
path.write_text('\n'.join(lines), encoding='utf-8')
print('added', added, 'ids')
