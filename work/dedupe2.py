"""Drop the duplicated id lines the merge left, without touching anything outside the snapshot (goal round 4)."""
import re
from pathlib import Path

path = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\tests\Content.Tests\ValidationTests.cs')
lines = path.read_text(encoding='utf-8').split('\n')

def is_id_line(line: str) -> bool:
    stripped = line.strip()
    return stripped.startswith('"') and stripped.rstrip(',').rstrip().endswith('"') and ';' not in stripped

seen_ids: set[str] = set()
out = []
for line in lines:
    if not is_id_line(line):
        out.append(line)
        continue
    ids = re.findall(r'"([^"]+)"', line)
    fresh = [value for value in ids if value not in seen_ids]
    # A line whose ids were all seen already is a merge duplicate and goes.
    if not fresh:
        continue
    seen_ids.update(fresh)
    if len(fresh) == len(ids):
        out.append(line)
    else:
        out.append('                ' + ', '.join(f'"{value}"' for value in fresh) + ',')
path.write_text('\n'.join(out), encoding='utf-8')
print('duplicate id lines removed')
