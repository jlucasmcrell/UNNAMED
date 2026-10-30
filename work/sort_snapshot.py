"""Sort the two id literals the tests compare against, in place and without touching anything else (region fauna at full scale).

Both lists are ordinal-sorted tables of ids; two rounds of insertions left them out of order, which the tests report as a collection difference.
"""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
ID_LINE = re.compile(r'^\s*"[^"]+"(?:,\s*"[^"]+")*,?\s*$')


def sort_block(path, seed_id):
    lines = path.read_text(encoding='utf-8').split('\n')
    block = [i for i, line in enumerate(lines) if ID_LINE.match(line) and '"' in line]
    if not block:
        raise SystemExit(f'{path.name}: no id lines')
    first, last = block[0], block[-1]
    ids = []
    for index in range(first, last + 1):
        if ID_LINE.match(lines[index]):
            ids.extend(re.findall(r'"([^"]+)"', lines[index]))
    unique = sorted(dict.fromkeys(ids), key=lambda value: value.lower())
    rendered = ['                ' + ', '.join(f'"{value}"' for value in unique[at:at + 4]) +
                (',' if at + 4 < len(unique) else ',') for at in range(0, len(unique), 4)]
    path.write_text('\n'.join(lines[:first] + rendered + lines[last + 1:]), encoding='utf-8')
    print(f'{path.name}: sorted {len(unique)} ids over {len(rendered)} lines')


sort_block(ROOT / 'tests' / 'Content.Tests' / 'ValidationTests.cs', 'ability.blade.power_strike')
