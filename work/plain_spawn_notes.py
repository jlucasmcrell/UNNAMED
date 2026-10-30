"""A spawn note must be a plain scalar: no colons and no apostrophes unless quoted (region fauna at full scale)."""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

for path in sorted((ROOT / 'content' / 'spawns' / 'wildlife').glob('*.yaml')):
    text = path.read_text(encoding='utf-8')
    match = re.search(r'(?m)^notes: (.*)$', text)
    if not match:
        continue
    body = match.group(1).strip()
    if body.startswith("'"):
        body = body[1:-1] if body.endswith("'") else body[1:]
    clean = body.replace(":", " -").replace("'", '')
    if clean == match.group(1):
        continue
    path.write_text(text.replace(match.group(0), f'notes: {clean}', 1), encoding='utf-8')
    print('clean', path.name)
