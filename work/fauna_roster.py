"""The fauna roster as shipped: what stands in each simulated area, with herd sizes (the evidence behind "full scale")."""
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns')
AREAS = ['starting_frontier', 'barrowworks', 'den_gorge_cave', 'sedge_levels']
FAUNA = {'creature.beast.doe': 'hind', 'creature.beast.buck': 'stag', 'creature.beast.elk': 'elk',
         'creature.beast.moose': 'moose', 'creature.beast.rabbit': 'hare', 'creature.beast.red_fox': 'fox',
         'creature.beast.duck': 'duck', 'creature.beast.crow': 'crow', 'creature.fish.trout': 'trout'}

rows = defaultdict(list)
for path in sorted(ROOT.rglob('*.yaml')):
    text = path.read_text(encoding='utf-8')
    region = (re.search(r'region_ref: region\.(\S+)', text) or [None, '?'])[1]
    spawn_id = (re.search(r'id: (\S+)', text) or [None, path.stem])[1]
    for ref, count in re.findall(r'creature_ref: (creature\.[\w.]+), count: \[(\d+), \d+\]', text):
        if ref in FAUNA:
            rows[region].append((FAUNA[ref], int(count), spawn_id))

for area in AREAS:
    entries = rows.get(area, [])
    animals = sum(count for _, count, _ in entries)
    kinds = sorted({kind for kind, _, _ in entries})
    print(f'{area}: {len(entries)} fauna spawns, {animals} animals, kinds: {", ".join(kinds) or "none"}')
    for kind, count, spawn_id in entries:
        print(f'    {count:>2} x {kind:<6} {spawn_id}')
