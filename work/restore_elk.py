"""Bring the elk back into the shipped fauna now that its rig is levelled, and give the deer family their herds (fauna goal).

The elk's asset is corrected at file level (+90 degrees about X on every root node of the body and every clip); this puts the creature,
its loot, its spawn and its armour back, and adds a doe herd beside the stag herd so the four owners' animals all stand in bands.
"""
import json
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
GATEC = Path(r'D:\Otherreach_Scratch\ds_gate_d_wt')

# The content written before the withdrawal, brought back from the commit that held it.
import subprocess

def show(commit_path):
    return subprocess.run(['git', '-C', str(GATEC), 'show', commit_path], capture_output=True, text=True, check=True).stdout

files = {
    'content/creatures/beast/elk.yaml': 'content/creatures/beast/elk.yaml',
    'content/loot/elk.yaml': 'content/loot/elk.yaml',
}
for source, target in files.items():
    (ROOT / target).parent.mkdir(parents=True, exist_ok=True)
    (ROOT / target).write_text(show(f'0c98aa2d:{source}'), encoding='utf-8')

# A herd name that says what stands in it, and the doe band that goes with the stag band.
(ROOT / 'content/spawns/wildlife/moor_elk_herd.yaml').write_text("""id: spawn.cairnwold.moor_elk_herd
kind: spawn
schema: 1
display_key: spawn.cairnwold.moor_elk_herd.name
tags: [spawn]
notes: Upland elk on the high birch east of the road - hinds grazing with a bull at the herd's edge, drifting between the trees and the open moor.
region_ref: region.starting_frontier
creatures:
  - { creature_ref: creature.beast.elk, count: [3, 3], role: herd_shy }
  - { creature_ref: creature.beast.elk, count: [1, 1], role: skittish }
at:
  position_m: [930, 690]
  radius_m: 14
level_band: [3, 3]
respawn: { kind: timer, window_ticks: 54000 }
tier_hint: A
""", encoding='utf-8')

(ROOT / 'content/spawns/wildlife/charwood_doe_herd.yaml').write_text("""id: spawn.hollow.charwood_doe_herd
kind: spawn
schema: 1
display_key: spawn.hollow.charwood_doe_herd.name
tags: [spawn]
notes: A hind band in the Charwood's edge, does and their yearlings, with the stag that keeps them standing off in the birches.
region_ref: region.starting_frontier
creatures:
  - { creature_ref: creature.beast.doe, count: [4, 4], role: herd_shy }
  - { creature_ref: creature.beast.buck, count: [1, 1], role: skittish }
at:
  position_m: [640, 706]
  radius_m: 12
level_band: [1, 2]
respawn: { kind: timer, window_ticks: 36000 }
tier_hint: A
""", encoding='utf-8')

bindings_path = ROOT / 'content' / '_art_bindings.json'
bindings = json.loads(bindings_path.read_text(encoding='utf-8'))
bindings['creatures']['creature.beast.elk'] = {
    'model': 'creature_wm_elk',
    'clips': 'creature.creature_wm_elk',
    'voice': '',
    'flesh': 'hide',
    'blow': 'gore',
    'steps': {'walk': 'walk', 'run': 'run', 'flee': 'flee'},
    'note': 'WildMesh SK_Elk from the 100 MODELS pack; own idle, walk, trot and sprint takes. Its rig arrives pitched, so the shipped '
            'body and every clip carry a +90 degree root rotation. Silent.',
}
bindings_path.write_text(json.dumps(bindings, indent=1), encoding='utf-8')

for test_name, extra in (('ValidationTests.cs', []), ('CombatContentTests.cs', [])):
    path = ROOT / 'tests' / 'Content.Tests' / test_name
    code = path.read_text(encoding='utf-8')
    if test_name == 'ValidationTests.cs':
        code = code.replace('"creature.beast.duck", "creature.beast.elk", ', '"creature.beast.duck", ')
        code = code.replace('"creature.beast.elk", "creature.beast.hound_pup"', '"creature.beast.elk", "creature.beast.hound_pup"')
    path.write_text(code, encoding='utf-8')
print('elk and the doe herd are in the shipped fauna')
