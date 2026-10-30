"""Fill the frontier out to full scale: a second elk herd, a moor deer herd, a millpond duck raft and a creek trout run (CPU-only work).

The frontier had one elk herd, one doe herd, two stag/deer spawns, one duck raft and one trout shoal, all clustered on the eastern side. These add
the western and southern water and moor - the millpond, the creek at the hollow, the birch east of the road - so a walker meets animals wherever
there is grass or water rather than in three pockets.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

SPAWNS = {
    'moor_west_elk.yaml': dict(
        id='spawn.cairnwold.moor_west_elk', x=1010, z=640, radius=16, band='[3, 3]',
        note='A second elk band on the western moor, hinds and a bull working the open ground above the millrace.',
        creatures=[('creature.beast.elk', 3, 'herd_shy'), ('creature.beast.elk', 1, 'skittish')]),
    'millpond_deer.yaml': dict(
        id='spawn.cairnwold.millpond_deer', x=950, z=712, radius=10, band='[1, 2]',
        note='Red deer down to the millpond at first light - two hinds and a stag, the water at their backs.',
        creatures=[('creature.beast.doe', 2, 'skittish'), ('creature.beast.buck', 1, 'skittish')]),
    'millpond_ducks.yaml': dict(
        id='spawn.cairnwold.millpond_ducks', x=966, z=702, radius=9, band='[1, 1]',
        note='A raft of mallards on the millpond itself, paddling the still water between the jetty and the reeds.',
        creatures=[('creature.beast.duck', 5, 'yard_fowl')]),
    'hollow_creek_trout.yaml': dict(
        id='spawn.hollow.creek_trout', x=496, z=744, radius=6, band='[1, 1]',
        note='Trout in the Ashen Hollow creek where it runs deep under the bank, a shoal a fisher can find by the rise.',
        creatures=[('creature.fish.trout', 4, 'wildlife')]),
    'hollow_crows.yaml': dict(
        id='spawn.hollow.hollow_crows', x=470, z=706, radius=14, band='[1, 1]',
        note='Crows over the hollow field, a mob that follows the plough line and lifts together.',
        creatures=[('creature.beast.crow', 4, 'wildlife')]),
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
level_band: {spec['band']}
respawn: {{ kind: timer, window_ticks: 36000 }}
tier_hint: A
"""
    (ROOT / name).write_text(text, encoding='utf-8', newline='\n')
    print('wrote', name)
