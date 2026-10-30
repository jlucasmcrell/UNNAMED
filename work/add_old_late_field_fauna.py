"""Wildlife for the two frontier areas that still had only fights in them: the Old Late road and the field by the gate.

Both are `starting_frontier` ground, so they are simulated and filmable. Crows and hares suit each: carrion birds over the road's old work, hares in
the field grass between the tenders' ground.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

SPAWNS = {
    'old_late_crows.yaml': dict(
        id='spawn.old_late.crows', x=656, z=742, radius=12,
        note='Crows over the old late road, a mob that keeps to the high stones and follows the traffic up from the barrows',
        creatures=[('creature.beast.crow', 4, 'wildlife')]),
    'old_late_hares.yaml': dict(
        id='spawn.old_late.hares', x=644, z=726, radius=10,
        note='Hares in the grass beside the old late road, living off the strip the carts never wore bare',
        creatures=[('creature.beast.rabbit', 3, 'wildlife')]),
    'field_crows.yaml': dict(
        id='spawn.field.crows', x=860, z=760, radius=14,
        note='Crows over the field by the gate, working the turned ground the tenders leave behind',
        creatures=[('creature.beast.crow', 4, 'wildlife')]),
    'field_hares.yaml': dict(
        id='spawn.field.hares', x=828, z=740, radius=10,
        note='Hares in the field grass, sitting tight between the tenders ground and the breach',
        creatures=[('creature.beast.rabbit', 3, 'wildlife')]),
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
