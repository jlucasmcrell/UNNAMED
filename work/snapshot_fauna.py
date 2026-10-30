"""Add the ids the four new species bring to the whole-content snapshot (region fauna at full scale)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
NEW = [
    'spawn.cairnwold.moor_west_elk', 'spawn.cairnwold.millpond_deer', 'spawn.cairnwold.millpond_ducks', 'spawn.hollow.creek_trout', 'spawn.hollow.hollow_crows',
    'node.carcass.moose', 'node.carcass.duck', 'node.carcass.crow', 'node.carcass.trout',
    'node.carcass.elk', 'node.carcass.rabbit', 'node.carcass.red_fox',
    'resource.carcass.elk', 'resource.carcass.rabbit', 'resource.carcass.red_fox',
    'resource.carcass.moose', 'resource.carcass.duck', 'resource.carcass.crow', 'resource.carcass.trout',
    'ability.creature.tail_slap',
    'creature.beast.crow', 'creature.beast.duck', 'creature.beast.moose', 'creature.fish.trout',
    'loot.fowl', 'loot.moose', 'loot.trout',
    'spawn.cairnwold.outfall_ducks', 'spawn.cairnwold.outfall_trout',
    'spawn.cairnwold.foldscar_moose', 'spawn.hollow.charwood_crows',
]

path = ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs'
lines = path.read_text(encoding='utf-8').split('\n')
first = next(i for i, line in enumerate(lines) if '"ability.blade.power_strike"' in line)
last = max(i for i, line in enumerate(lines) if '"world.' in line)

for new_id in NEW:
    if any(f'"{new_id}"' in line for line in lines[first:last + 1]):
        continue
    placed = False
    for index in range(first, last + 1):
        ids = re.findall(r'"([^"]+)"', lines[index])
        after = next((value for value in ids if value.lower() > new_id.lower()), None)
        if after is None:
            continue
        lines[index] = lines[index].replace(f'"{after}"', f'"{new_id}", "{after}"', 1)
        placed = True
        break
    if not placed:
        lines[last] = lines[last].rstrip().rstrip(',') + f', "{new_id}",'
path.write_text('\n'.join(lines), encoding='utf-8')
print('snapshot ids added')
