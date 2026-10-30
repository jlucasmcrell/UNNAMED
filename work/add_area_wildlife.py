"""Wildlife for the three other build-2 areas: birds, fish and small game where only hostile spawns stood (region fauna at full scale).

Barrowworks had spiders, a husk and a construct; the gorge had spiders; the sedge had drowned, raiders and eels. None of them had anything that
is simply alive there - no birds, no fish shoal, no small game - which is what makes a place read as a place rather than a gauntlet. Every
species used here already exists in the pack, with its bindings, loot and carcass.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

SPAWNS = {
    # Barrowworks: crows on the ruin and hares in the sorting yard's grass.
    'barrowworks_crows.yaml': dict(
        id='spawn.barrowworks.ruin_crows', region='barrowworks', x=1180, z=560, radius=16, band='[1, 2]',
        note='Crows on the Barrowworks ruin: a mob that walks the broken wall and the yard, and lifts the moment anyone comes in.',
        creatures=[('creature.beast.crow', 4, 'wildlife')]),
    'barrowworks_hares.yaml': dict(
        id='spawn.barrowworks.yard_hares', region='barrowworks', x=1160, z=600, radius=10, band='[1, 1]',
        note='Hares in the grass between the yard and the sorting floor, living under the spiders\' notice.',
        creatures=[('creature.beast.rabbit', 3, 'wildlife')]),
    # The gorge mouth: small game in the scree, a fox working it.
    'gorge_mouth_hares.yaml': dict(
        id='spawn.den_gorge.mouth_hares', region='den_gorge_cave', x=640, z=470, radius=12, band='[1, 1]',
        note='Hares on the scree outside the gorge mouth, where the spiders do not hunt.',
        creatures=[('creature.beast.rabbit', 3, 'wildlife')]),
    'gorge_mouth_fox.yaml': dict(
        id='spawn.den_gorge.mouth_fox', region='den_gorge_cave', x=650, z=486, radius=10, band='[1, 1]',
        note='A dog fox on the gorge mouth, working the scree for what the hares leave.',
        creatures=[('creature.beast.red_fox', 1, 'wildlife')]),
    # The sedge: ducks on the pools, a trout shoal in the channel, hares on the dry hummocks.
    'sedge_pool_ducks.yaml': dict(
        id='spawn.sedge.pool_ducks', region='sedge_levels', x=4870, z=742, radius=8, band='[1, 1]',
        note='Mallards on the sedge pools, paddling the shallows between the eel channels and grazing the bank.',
        creatures=[('creature.beast.duck', 4, 'yard_fowl')]),
    'sedge_channel_trout.yaml': dict(
        id='spawn.sedge.channel_trout', region='sedge_levels', x=4884, z=744, radius=6, band='[1, 1]',
        note='A trout shoal holding in the sedge channel where the water runs clear over sand.',
        creatures=[('creature.fish.trout', 4, 'wildlife')]),
    'sedge_hummock_hares.yaml': dict(
        id='spawn.sedge.hummock_hares', region='sedge_levels', x=4850, z=720, radius=12, band='[1, 1]',
        note='Hares on the dry hummocks above the waterline, the only ground the drowned do not walk.',
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
notes: '{spec['note']}'
region_ref: region.{spec['region']}
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
