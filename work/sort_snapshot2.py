"""Sort the snapshot's id lines in place, keeping every other line exactly where it was (region fauna at full scale).

The list is an ordinal-sorted table; insertions from two rounds left it out of order. Only the lines that are nothing but quoted ids are touched -
everything else, including the brackets that close the array, is left alone.
"""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
ID_LINE = re.compile(r'^\s*"[^"]+"(?:,\s*"[^"]+")*,?\s*$')

path = ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs'
lines = path.read_text(encoding='utf-8').split('\n')
indexes = [i for i, line in enumerate(lines) if ID_LINE.match(line)]

ids = []
for index in indexes:
    ids.extend(re.findall(r'"([^"]+)"', lines[index]))
unique = sorted(dict.fromkeys(ids), key=lambda value: value.lower())
groups = [', '.join(f'"{value}"' for value in unique[at:at + 4]) for at in range(0, len(unique), 4)]
rendered = ['                ' + group + ',' for group in groups]

out = []
for i, line in enumerate(lines):
    if i == indexes[0]:
        out.extend(rendered)
    elif i in indexes:
        continue
    else:
        out.append(line)
path.write_text('\n'.join(out), encoding='utf-8')
print(f'sorted {len(unique)} ids into {len(rendered)} lines (was {len(indexes)})')
