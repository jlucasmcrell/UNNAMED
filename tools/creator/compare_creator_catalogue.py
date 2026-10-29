"""Compare the regenerated creator catalogue with the checked-in one it replaced (read-only).

Checks that a regeneration from the installed HumGen content adds only the new colour controls,
and loses no face, groom, garment, baked body or per-people control.
"""
import json
import sys
from pathlib import Path

old = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))
new = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))


def ids(part):
    return {entry['id'] for entry in part}


def report(key):
    lost = sorted(ids(old[key]) - ids(new[key]))
    gained = sorted(ids(new[key]) - ids(old[key]))
    print(f'{key}: {len(old[key])} -> {len(new[key])}  lost {lost}  gained {gained}')


for key in ('sliders', 'faces', 'hair', 'facial_hair', 'outfits', 'footwear'):
    report(key)
print('baked bodies identical:', json.dumps(old['baked'], sort_keys=True) == json.dumps(new['baked'], sort_keys=True))
print('races identical:', json.dumps(old['races'], sort_keys=True) == json.dumps(new['races'], sort_keys=True))
for people in old['runtime']['people']:
    before, after = old['runtime']['people'][people], new['runtime']['people'][people]
    print(f"{people}: {len(before)} -> {len(after)}  lost {sorted(set(before) - set(after))}  gained {sorted(set(after) - set(before))}")
# The faces keep every face key the presets set; only the two brow colours may join them.
for before, after in zip(old['faces'], new['faces']):
    lost = set(before['values']) - set(after['values'])
    if lost:
        print('face values lost', before['id'], sorted(lost))
print('every face keeps its native record:', all('native' in face for face in new['faces']))
