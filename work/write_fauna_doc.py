"""Write the fauna roster document from the pack, so the shipped state is readable without loading the game.

Regenerate with: python work/write_fauna_doc.py
"""
import json
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
CONTENT = ROOT / 'content'
DOC = ROOT / 'docs' / 'phase_b' / 'FAUNA_ROSTER.md'

FAUNA = {
    'creature.beast.doe': 'red deer hind', 'creature.beast.buck': 'red deer stag',
    'creature.beast.elk': 'elk', 'creature.beast.moose': 'moose',
    'creature.beast.rabbit': 'hare', 'creature.beast.red_fox': 'red fox',
    'creature.beast.duck': 'mallard', 'creature.beast.crow': 'crow',
    'creature.fish.trout': 'trout',
}

creatures = {}
for path in (CONTENT / 'creatures').rglob('*.yaml'):
    text = path.read_text(encoding='utf-8')
    match = re.search(r'^id: (\S+)', text, re.M)
    if match:
        creatures[match.group(1)] = (
            re.search(r'^name: (.+)$', text, re.M).group(1).strip() if re.search(r'^name: (.+)$', text, re.M) else '?',
            bool(re.search(r'^loot_table:', text, re.M)),
            (re.search(r'^carcass_ref: (\S+)', text, re.M) or [None, None])[1] if re.search(r'^carcass_ref: (\S+)', text, re.M) else None,
        )

rows = defaultdict(list)
for path in sorted((CONTENT / 'spawns').rglob('*.yaml')):
    text = path.read_text(encoding='utf-8')
    region = (re.search(r'^region_ref: region\.(\S+)', text, re.M) or [None, '?'])[1]
    spawn_id = (re.search(r'^id: (\S+)', text, re.M) or [None, path.stem])[1]
    for ref, count in re.findall(r'creature_ref: (creature\.[\w.]+), count: \[(\d+), \d+\]', text):
        if ref in FAUNA:
            rows[region].append((spawn_id, FAUNA[ref], int(count), ref))

SIMULATED = ['starting_frontier', 'barrowworks', 'den_gorge_cave', 'sedge_levels']

lines = [
    '# Fauna roster',
    '',
    'Generated from `content/` by `work/write_fauna_doc.py` - regenerate rather than editing by hand.',
    '',
    'Every animal below can be hunted (it names a `loot_table`) and skinned (it names a `carcass_ref`, a node that trains `skill.skinning`).',
    '',
    '## Species',
    '',
    '| creature | name | loot | carcass |',
    '|---|---|---|---|',
]
for ref, label in FAUNA.items():
    name, loot, carcass = creatures.get(ref, ('not defined', False, None))
    lines.append(f'| `{ref}` | {name} | {"yes" if loot else "**no**"} | {("`" + carcass + "`") if carcass else "**none**"} |')

for region in SIMULATED + ['ashen_hollow']:
    entries = rows.get(region, [])
    animals = sum(count for _, _, count, _ in entries)
    header = f'## region.{region}'
    lines += ['', header, '']
    if region == 'ashen_hollow':
        lines.append('Declared only: the session does not boot this region, so these animals are placed for the day it is loaded.')
        lines.append('')
    if not entries:
        lines.append('No fauna spawns.')
        continue
    lines.append(f'{len(entries)} spawn(s), {animals} animal(s).')
    lines += ['', '| spawn | animals | kind |', '|---|---|---|']
    for spawn_id, label, count, _ in sorted(entries):
        lines.append(f'| `{spawn_id}` | {count} | {label} |')

DOC.parent.mkdir(parents=True, exist_ok=True)
DOC.write_text('\n'.join(lines) + '\n', encoding='utf-8')
print(f'wrote {DOC.relative_to(ROOT)} ({len(lines)} lines)')
