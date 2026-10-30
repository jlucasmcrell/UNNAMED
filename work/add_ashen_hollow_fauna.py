"""Wildlife for region.ashen_hollow itself, on its own 200 m grid (the fifth region the pack defines).

The hollow's four earlier spawns were moved to the frontier's hollow ground because the session never loads this region file. These are separate
spawns placed in the hollow's own coordinates, so the area is populated the moment anything boots `region.ashen_hollow` - which is also the lightest
region in the pack, and therefore the one most likely to render where the frontier faults.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

SPAWNS = {
    'ashen_hollow_deer.yaml': dict(
        id='spawn.ashen_hollow.deer', x=160, z=110, radius=14,
        note='Red deer in the hollow trees on the eastern side, two hinds and the stag that keeps them',
        creatures=[('creature.beast.doe', 2, 'skittish'), ('creature.beast.buck', 1, 'skittish')]),
    'ashen_hollow_hares.yaml': dict(
        id='spawn.ashen_hollow.hares', x=120, z=60, radius=12,
        note='Hares in the open south field of the hollow, running the grass down toward the ravine',
        creatures=[('creature.beast.rabbit', 3, 'wildlife')]),
    'ashen_hollow_crows.yaml': dict(
        id='spawn.ashen_hollow.crows', x=48, z=150, radius=14,
        note='Crows on the hollow waystation, walking the roofs and the road out before they lift',
        creatures=[('creature.beast.crow', 4, 'wildlife')]),
    'ashen_hollow_fox.yaml': dict(
        id='spawn.ashen_hollow.fox', x=70, z=30, radius=10,
        note='A dog fox on the ravine side of the hollow, working the wall for what the hares leave',
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
region_ref: region.ashen_hollow
creatures:
{rows}
at:
  position_m: [{spec['x']}, {spec['z']}]
  radius_m: {spec['radius']}
level_band: [1, 2]
respawn: {{ kind: timer, window_ticks: 36000 }}
tier_hint: A
"""
    (ROOT / name).write_text(text, encoding='utf-8', newline='\n')
    print('wrote', name)
