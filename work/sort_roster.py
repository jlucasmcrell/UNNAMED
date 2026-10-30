"""Put the roster's new species where ordinal order wants them (region fauna at full scale)."""
from pathlib import Path
import re

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\CombatContentTests.cs')
code = path.read_text(encoding='utf-8')

# The roster is one literal per line; sort the quoted ids in those lines so an insertion cannot land out of order.
lines = code.split('\n')
ID_LINE = re.compile(r'^\s*"(?:[\w.]+)"(?:,\s*"[\w.]+")*,?\s*$')
for index, line in enumerate(lines):
    if ID_LINE.match(line) and 'creature.' in line:
        ids = re.findall(r'"([^"]+)"', line)
        lines[index] = '                ' + ', '.join(f'"{value}"' for value in sorted(ids, key=str.lower)) + ','
path.write_text('\n'.join(lines), encoding='utf-8')
print('roster lines sorted')
