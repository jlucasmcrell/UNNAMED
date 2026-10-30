"""A second moose band in the Foldscar wet ground: the one herding animal that still stood in a single place.

Elk have two bands, deer four; the moose had one of three. A cow and a calf in the spring ground south of the first band makes the moor's largest
animal read as a population rather than an encounter.
"""
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\spawns\wildlife')

text = """id: spawn.cairnwold.foldscar_moose_ground
kind: spawn
schema: 1
display_key: spawn.cairnwold.foldscar_moose_ground.name
tags: [spawn]
notes: A cow moose and her calf in the wet ground below the Foldscar spring, where the willow scrub is thickest
region_ref: region.starting_frontier
creatures:
  - { creature_ref: creature.beast.moose, count: [2, 2], role: wildlife }
at:
  position_m: [570, 612]
  radius_m: 12
level_band: [4, 4]
respawn: { kind: timer, window_ticks: 36000 }
tier_hint: A
"""
(ROOT / 'foldscar_moose_ground.yaml').write_text(text, encoding='utf-8', newline='\n')
print('wrote foldscar_moose_ground.yaml')
