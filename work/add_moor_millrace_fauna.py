"""Crows on the moor and trout in the millrace: the two frontier places that still had only one kind of animal.

The moor carried an elk band, moose, hares and a fox but no birds; the millpond carried ducks and deer but no fish. Both are plausible and neither
needs a new species - only a spawn in the right ground.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

SPAWNS = {
    'moor_crows.yaml': dict(
        id='spawn.cairnwold.moor_crows', x=960, z=660, radius=16,
        note='Crows over the open moor, working the sheep ground and the wall tops in a loose mob',
        creatures=[('creature.beast.crow', 5, 'wildlife')]),
    'millrace_trout.yaml': dict(
        id='spawn.cairnwold.millrace_trout', x=972, z=708, radius=6,
        note='Trout holding in the millrace below the pond, where the water runs fast over stone',
        creatures=[('creature.fish.trout', 4, 'wildlife')]),
}

for name, spec in SPAWNS.items():
    rows = '\n'.join(f'  - {{ creature_ref: {creature}, count: [{count}, {count}], role: {role} }}'
                     for creature, count, role in spec['creatures'])
    text = f"""id: {spec['id']}
kind: spawn
schema: 1
display_key: {spec['id']}.name
tags: [spawn]
notes: {spec['note']}
region_ref: region.starting_frontier
creatures:
{rows}
at:
  position_m: [{spec['x']}, {spec['z']}]
  radius_m: {spec['radius']}
level_band: [1, 1]
respawn: {{ kind: timer, window_ticks: 36000 }}
tier_hint: A
"""
    (ROOT / name).write_text(text, encoding='utf-8', newline='\n')
    print('wrote', name)
