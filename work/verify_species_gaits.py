"""Every species' binding asks for clips that exist, state by state (real animations, verified).

A binding names a clip family and the states it drives (walk, run, flee, idle). If a state has no clip the animal freezes in that gait, which
looks exactly like a broken rig from outside. This checks each state's clip file in the art root the game reads.
"""
import json
from pathlib import Path

ART = Path(r'D:\Otherreach_Shared\art_root')
BINDINGS = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\_art_bindings.json')
creatures = json.loads(BINDINGS.read_text(encoding='utf-8'))['creatures']

FAUNA = ['creature.beast.doe', 'creature.beast.buck', 'creature.beast.elk', 'creature.beast.moose',
         'creature.beast.rabbit', 'creature.beast.red_fox', 'creature.beast.duck', 'creature.beast.crow',
         'creature.fish.trout']

problems = 0
for key in FAUNA:
    binding = creatures[key]
    model = binding['model']
    states = binding.get('clips', '')
    available = {}
    for path in (ART / 'animation' / 'ready' / 'creatures').glob(f'anim.creature.{model}.*.glb'):
        state = path.name.split('.')[-2]
        available[state] = True
    wanted = set((binding.get('steps') or {}).values())
    missing = sorted(state for state in wanted if state not in available)
    print(f'{key}: {len(available)} clip(s) [{", ".join(sorted(available))}]')
    if missing:
        problems += 1
        print('    MISSING gait clip(s):', ', '.join(missing))
    for state in ('idle', 'walk', 'run', 'flee'):
        if state not in available:
            print(f'    no {state} clip')
print('species with a missing gait clip:', problems)
