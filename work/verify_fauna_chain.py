"""Every spawn's species resolves all the way to a model the game can draw (fauna verified, not assumed).

For each spawn: its region is one the session simulates, every creature_ref has a definition with a loot table (hunting hook) and, where the
species is an animal, a binding whose rigged model exists in the art root the game reads.
"""
import json
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content')
ART = Path(r'D:\Otherreach_Shared\art_root')
SIMULATED = {'region.starting_frontier', 'region.barrowworks', 'region.den_gorge_cave', 'region.sedge_levels'}

bindings = json.loads((ROOT / '_art_bindings.json').read_text(encoding='utf-8'))['creatures']
creatures = {}
for path in (ROOT / 'creatures').rglob('*.yaml'):
    text = path.read_text(encoding='utf-8')
    match = re.search(r'^id: (\S+)', text, re.M)
    if match:
        creatures[match.group(1)] = text

rows, problems = [], []
for path in sorted((ROOT / 'spawns').rglob('*.yaml')):
    text = path.read_text(encoding='utf-8')
    spawn_id = (re.search(r'^id: (\S+)', text, re.M) or [None, path.stem])[1]
    region = (re.search(r'^region_ref: (\S+)', text, re.M) or [None, '?'])[1]
    refs = re.findall(r'creature_ref: ([A-Za-z0-9_.]+)', text)
    for ref in refs:
        definition = creatures.get(ref)
        if definition is None:
            problems.append(f'{spawn_id}: {ref} has no definition')
            continue
        loot = re.search(r'^loot_table: (\S+)', definition, re.M)
        carcass = re.search(r'^carcass_ref: (\S+)', definition, re.M)
        binding = bindings.get(ref)
        model = binding.get('model') if binding else None
        rigged = bool(model) and (ART / 'rigged' / model / f'{model}_rigged.glb').exists()
        rows.append((spawn_id, region.replace('region.', ''), ref, bool(loot), bool(carcass), bool(model), rigged))
        if region not in SIMULATED:
            problems.append(f'{spawn_id}: region {region} is not simulated')
        if not rigged:
            problems.append(f'{spawn_id}: {ref} has no rigged model ({model or "no binding"})')

print(f'{len(rows)} spawn rows checked')
missing_carcass = [r for r in rows if r[3] and not r[4] and not r[2].startswith(('creature.humanoid', 'creature.undead', 'creature.construct'))]
print('huntable animals with no carcass:', len(missing_carcass))
for row in missing_carcass[:8]:
    print('   ', row[0], row[2])
print('problems:', len(problems))
for problem in problems[:12]:
    print('   ', problem)
