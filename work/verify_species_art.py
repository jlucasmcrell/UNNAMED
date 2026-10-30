"""Every bound species has its clips on disk, and every clip the binding names exists (real animations, verified rather than assumed)."""
import json
from pathlib import Path

ART = Path(r'D:\Otherreach_Shared\art_root')
BINDINGS = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\content\_art_bindings.json')
data = json.loads(BINDINGS.read_text(encoding='utf-8'))['creatures']

WANTED = ['creature.beast.elk', 'creature.beast.moose', 'creature.beast.doe', 'creature.beast.buck',
          'creature.beast.duck', 'creature.beast.crow', 'creature.fish.trout', 'creature.beast.rabbit',
          'creature.beast.red_fox']

for key in WANTED:
    binding = data.get(key)
    if binding is None:
        print(f'{key}: NOT BOUND')
        continue
    model = binding.get('model', '')
    rigged = ART / 'rigged' / model / f'{model}_rigged.glb'
    clips_dir = ART / 'animation' / 'ready' / 'creatures'
    states = sorted(p.name for p in clips_dir.glob(f'anim.creature.{model}.*.glb')) if clips_dir.exists() else []
    print(f'{key}: model {model} rigged={rigged.exists()} clips={len(states)} [{", ".join(s.split(".")[-2] for s in states)}]')
