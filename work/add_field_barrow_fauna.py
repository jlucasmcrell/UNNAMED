"""A herd in the field and a scavenger in the Barrowworks yard: two more areas with more than birds and hares in them.

The field by the gate had only crows and hares; deer graze the turned ground at the edge of the tenders' reach. The Barrowworks ruin had crows and
hares; a dog fox works a ruin for what the spiders leave.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

SPAWNS = {
    'field_deer.yaml': dict(
        id='spawn.field.deer', region='region.starting_frontier', x=840, z=768, radius=12, band='[1, 2]',
        note='Red deer grazing the field edge at first light, two hinds and a stag keeping clear of the tenders',
        creatures=[('creature.beast.doe', 2, 'skittish'), ('creature.beast.buck', 1, 'skittish')]),
    'barrowworks_fox.yaml': dict(
        id='spawn.barrowworks.yard_fox', region='region.barrowworks', x=58, z=96, radius=10, band='[1, 1]',
        note='A dog fox working the Barrowworks yard for what the spiders leave of the hares',
        creatures=[('creature.beast.red_fox', 1, 'wildlife')]),
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
region_ref: {spec['region']}
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
