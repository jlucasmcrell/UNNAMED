"""Put the elk's ids and the doe herd into the content snapshots (fauna goal)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
NEW = ['creature.beast.elk', 'loot.elk', 'spawn.cairnwold.moor_elk_herd', 'spawn.hollow.charwood_doe_herd']

path = ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs'
lines = path.read_text(encoding='utf-8').split('\n')
first = next(i for i, line in enumerate(lines) if '"ability.blade.power_strike"' in line)
last = max(i for i, line in enumerate(lines) if '"world.cairnwold' in line)
for new_id in NEW:
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

combat = ROOT / 'tests' / 'Content.Tests' / 'CombatContentTests.cs'
code = combat.read_text(encoding='utf-8')
code = code.replace('"creature.beast.doe", "creature.beast.duck",', '"creature.beast.doe", "creature.beast.duck", "creature.beast.elk",')
if 'Assert.Equal(50, spawns.Count);' in code:
    code = code.replace('Assert.Equal(50, spawns.Count);', 'Assert.Equal(52, spawns.Count);')
else:
    code = re.sub(r'Assert\.Equal\((\d+), spawns\.Count\);', lambda m: f'Assert.Equal({int(m.group(1)) + 2}, spawns.Count);', code, count=1)
combat.write_text(code, encoding='utf-8')
print('snapshots updated')
