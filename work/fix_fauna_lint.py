"""The spawn count and the snapshot order both need the four new spawns (region fauna at full scale)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

combat = ROOT / 'tests' / 'Content.Tests' / 'CombatContentTests.cs'
code = combat.read_text(encoding='utf-8')
code = code.replace('Assert.Equal(50, spawns.Count);', 'Assert.Equal(54, spawns.Count);', 1)
combat.write_text(code, encoding='utf-8')
print('count:', 'Assert.Equal(54, spawns.Count);' in combat.read_text(encoding='utf-8'))

# The snapshot wants ids in ordinal order inside one big list; rebuild that list sorted so an insertion cannot land out of place.
path = ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs'
lines = path.read_text(encoding='utf-8').split('\n')
first = next(i for i, line in enumerate(lines) if '"ability.blade.power_strike"' in line)
last = max(i for i, line in enumerate(lines) if '"world.' in line)
ids = []
for line in lines[first:last + 1]:
    ids.extend(re.findall(r'"([^"]+)"', line))
unique = sorted(dict.fromkeys(ids), key=lambda value: value.lower())
rendered = ['                ' + ', '.join(f'"{value}"' for value in unique[index:index + 4]) + ','
            for index in range(0, len(unique), 4)]
path.write_text('\n'.join(lines[:first] + rendered + lines[last + 1:]), encoding='utf-8')
print('snapshot rebuilt:', len(unique), 'ids')
