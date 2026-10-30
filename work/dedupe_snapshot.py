"""Remove the duplicated ids the merge left in the snapshot and keep the list sorted (goal round 4)."""
import re
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\ValidationTests.cs')
lines = path.read_text(encoding='utf-8').split('\n')
first = next(i for i, line in enumerate(lines) if '"ability.blade.power_strike"' in line)
last = max(i for i, line in enumerate(lines) if '"world.' in line)

block = '\n'.join(lines[first:last + 1])
ids = re.findall(r'"([^"]+)"', block)
seen = []
for value in ids:
    if value not in seen:
        seen.append(value)

rendered = []
for index in range(0, len(seen), 4):
    rendered.append('                ' + ', '.join(f'"{value}"' for value in seen[index:index + 4]) + ',')
path.write_text('\n'.join(lines[:first] + rendered + lines[last + 1:]), encoding='utf-8')
print(f'snapshot deduplicated: {len(ids)} entries -> {len(seen)}')
