"""Compare the whole-content snapshot in the test against the ids the pack actually declares (region fauna at full scale)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

declared = set()
for path in (ROOT / 'content').rglob('*.yaml'):
    for line in path.read_text(encoding='utf-8').split('\n'):
        match = re.match(r'^id:\s*(\S+)\s*$', line)
        if match:
            declared.add(match.group(1))
            break

lines = (ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs').read_text(encoding='utf-8').split('\n')
first = next(i for i, line in enumerate(lines) if '"ability.blade.power_strike"' in line)
last = max(i for i, line in enumerate(lines) if '"world.' in line)
listed = []
for line in lines[first:last + 1]:
    listed.extend(re.findall(r'"([^"]+)"', line))

print('declared', len(declared), 'listed', len(listed))
missing = sorted(declared - set(listed))
extra = sorted(set(listed) - declared)
print('missing from the snapshot:', missing)
print('in the snapshot but not declared:', extra[:12])
ordered = all(listed[i].lower() <= listed[i + 1].lower() for i in range(len(listed) - 1))
print('snapshot sorted:', ordered)
if not ordered:
    for i in range(len(listed) - 1):
        if listed[i].lower() > listed[i + 1].lower():
            print('  out of order at', i, listed[i], '>', listed[i + 1])
            break
