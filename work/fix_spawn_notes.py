"""Spawn notes are single-quoted YAML: no apostrophes inside them (region fauna at full scale).

The first drafts read "the spiders' notice" and "the hollow's eastern trees", which end the quoted scalar early and made the pack unparseable.
This rewrites every generated spawn note to a version with no apostrophe, straight from the notes the generators carry.
"""
import re
from pathlib import Path

ROOT = Path(r'D:\Otherreach_Scratch\ds_fish2_wt')

REWRITES = {
    'content/spawns/wildlife/ashen_east_deer.yaml':
        'Red deer along the eastern trees of the hollow: two hinds grazing the edge and the stag that keeps them.',
    'content/spawns/wildlife/ashen_field_hares.yaml':
        'Hares in the open south field of the hollow, where the grass runs down toward the ravine and there is nowhere to hide but speed.',
    'content/spawns/wildlife/ashen_waystation_crows.yaml':
        'Crows on the waystation: a mob that walks the roofs, the wall and the road out, and is gone the moment anyone looks up.',
    'content/spawns/wildlife/ashen_ravine_fox.yaml':
        'A dog fox on the ravine side of the hollow, working the wall for what the hares leave.',
    'content/spawns/wildlife/barrowworks_crows.yaml':
        'Crows on the Barrowworks ruin: a mob that walks the broken wall and the yard, and lifts the moment anyone comes in.',
    'content/spawns/wildlife/barrowworks_hares.yaml':
        'Hares in the grass between the yard and the sorting floor, living under the spiders notice.',
    'content/spawns/wildlife/gorge_mouth_hares.yaml':
        'Hares on the scree outside the gorge mouth, where the spiders do not hunt.',
    'content/spawns/wildlife/gorge_mouth_fox.yaml':
        'A dog fox on the gorge mouth, working the scree for what the hares leave.',
    'content/spawns/wildlife/sedge_pool_ducks.yaml':
        'Mallards on the sedge pools, paddling the shallows between the eel channels and grazing the bank.',
    'content/spawns/wildlife/sedge_channel_trout.yaml':
        'A trout shoal holding in the sedge channel where the water runs clear over sand.',
    'content/spawns/wildlife/sedge_hummock_hares.yaml':
        'Hares on the dry hummocks above the waterline, the only ground the drowned do not walk.',
}

for relative, note in REWRITES.items():
    path = ROOT / relative
    if not path.exists():
        print('missing (not regenerated):', relative)
        continue
    text = path.read_text(encoding='utf-8')
    text = re.sub(r'(?m)^notes: .*$', f'notes: {note}', text)
    path.write_text(text, encoding='utf-8')
    print('noted', path.name)
