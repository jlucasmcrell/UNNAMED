"""Bind the four species this branch just gained to the models and clips the converter installed (region fauna at full scale)."""
import json
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')
path = ROOT / 'content' / '_art_bindings.json'
data = json.loads(path.read_text(encoding='utf-8'))

BINDINGS = {
    'creature.beast.moose': {
        'model': 'creature_wm_moose', 'clips': 'creature.creature_wm_moose', 'voice': '',
        'flesh': 'hide', 'blow': 'gore', 'steps': {'walk': 'walk', 'run': 'run', 'flee': 'flee'},
        'note': 'WildMesh bull moose; own idle, walk, trot and sprint takes. Silent.',
    },
    'creature.beast.duck': {
        'model': 'creature_wm_duck', 'clips': 'creature.creature_wm_duck', 'voice': '',
        'flesh': 'fowl', 'blow': 'none', 'steps': {'walk': 'walk', 'run': 'run', 'flee': 'flee'},
        'note': 'WildMesh mallard; paddles and grazes on its own takes. Silent.',
    },
    'creature.beast.crow': {
        'model': 'creature_wm_crow', 'clips': 'creature.creature_wm_crow', 'voice': '',
        'flesh': 'fowl', 'blow': 'none', 'steps': {'walk': 'walk', 'run': 'run', 'flee': 'flee'},
        'note': 'WildMesh crow; hops and flies on its own takes. Silent.',
    },
    'creature.fish.trout': {
        'model': 'creature_wm_trout', 'clips': 'creature.creature_wm_trout', 'voice': '',
        'flesh': 'fish', 'blow': 'none', 'steps': {'walk': 'swim', 'run': 'swim', 'flee': 'swim'},
        'note': 'WildMesh trout; a shoal fish whose only gait is the swim it holds station with.',
    },
}

added = [key for key in BINDINGS if key not in data['creatures']]
for key in added:
    data['creatures'][key] = BINDINGS[key]
path.write_text(json.dumps(data, indent=1) + '\n', encoding='utf-8')
print('bound:', ', '.join(added) if added else 'nothing new')
