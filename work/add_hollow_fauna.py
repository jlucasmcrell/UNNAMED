"""Give the Ashen Hollow its own wildlife, so the fauna is not only on the frontier (region fauna at full scale).

The hollow is a 200 m square with the waystation round (30-60, 138-158); every creature here is one the pack already defines, placed where its
ground makes sense: hares in the open field, deer along the eastern trees, crows on the waystation, a fox on the ravine side.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

SPAWNS = {
    'ashen_field_hares.yaml': ('spawn.ashen.field_hares',
        'Hares in the open south field of the hollow, where the grass runs down toward the ravine and there is nowhere to hide but speed.',
        '    - { creature_ref: creature.beast.rabbit, count: [3, 3], role: wildlife }', '120, 60', 10, '[1, 1]'),
    'ashen_east_deer.yaml': ('spawn.ashen.east_deer',
        'Red deer along the hollow\'s eastern trees: two hinds grazing the edge and the stag that keeps them.',
        '    - { creature_ref: creature.beast.doe, count: [2, 2], role: herd_shy }\n'
        '    - { creature_ref: creature.beast.buck, count: [1, 1], role: skittish }', '160, 110', 8, '[1, 2]'),
    'ashen_waystation_crows.yaml': ('spawn.ashen.waystation_crows',
        'Crows on the waystation: a mob that walks the roofs, the wall and the road out, and is gone the moment anyone looks up.',
        '    - { creature_ref: creature.beast.crow, count: [4, 4], role: wildlife }', '48, 150', 14, '[1, 1]'),
    'ashen_ravine_fox.yaml': ('spawn.ashen.ravine_fox',
        'A dog fox on the ravine side of the hollow, working the wall for what the hares leave.',
        '    - { creature_ref: creature.beast.red_fox, count: [1, 1], role: wildlife }', '70, 30', 12, '[1, 2]'),
}

for name, (spawn_id, note, creatures, position, radius, band) in SPAWNS.items():
    x, z = position.split(',')
    text = f"""id: {spawn_id}
kind: spawn
schema: 1
display_key: {spawn_id}.name
tags: [spawn]
notes: '{note}'
region_ref: region.ashen_hollow
creatures:
{creatures}
at:
  position_m: [{x.strip()}, {z.strip()}]
  radius_m: {radius}
level_band: {band}
respawn: {{ kind: timer, window_ticks: 36000 }}
tier_hint: A
"""
    (ROOT / name).write_text(text, encoding='utf-8', newline='\n')
    print('wrote', name)
