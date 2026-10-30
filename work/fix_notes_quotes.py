"""Quoted YAML notes cannot carry a bare apostrophe; drop the ones inside the quotes (region fauna at full scale)."""
from pathlib import Path

for path in Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife').glob('*.yaml'):
    lines = path.read_text(encoding='utf-8').split('\n')
    changed = False
    for index, line in enumerate(lines):
        if not line.startswith('notes: '):
            continue
        body = line[len('notes: '):]
        quote = "'"
        if body.startswith(quote) and body.endswith(quote) and quote in body[1:-1]:
            lines[index] = 'notes: ' + body.replace(quote, '')
            changed = True
    if changed:
        path.write_text('\n'.join(lines), encoding='utf-8')
        print('fixed', path.name)
