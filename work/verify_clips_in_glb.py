"""What each fauna model actually carries: skins, joints and the animation clips inside the GLB (animations verified without a GPU).

The game plays clips from `animation/ready/creatures`, and the body must be skinned with joints for a clip to move it. This reads both files and
prints the counts, so "real animation" is a fact about the assets rather than an assumption.
"""
import json
import struct
from pathlib import Path

ART = Path(r'D:\Otherreach_Shared\art_root')
SPECIES = ['doe', 'buck', 'elk', 'moose', 'rabbit', 'red_fox', 'duck', 'crow', 'trout']


def glb_json(path):
    data = path.read_bytes()
    offset = 12
    while offset < len(data):
        length, kind = struct.unpack_from('<II', data, offset)
        if kind == 0x4E4F534A:
            return json.loads(data[offset + 8:offset + 8 + length].decode('utf-8'))
        offset += 8 + length
    return {}


print(f'{"species":<10} {"joints":>6} {"clips":>5}  clip names (frames of animation inside each file)')
for name in SPECIES:
    model = ART / 'rigged' / f'creature_wm_{name}' / f'creature_wm_{name}_rigged.glb'
    if not model.exists():
        print(f'{name:<10} MISSING MODEL')
        continue
    doc = glb_json(model)
    joints = sum(len(skin.get('joints', [])) for skin in doc.get('skins', []))
    clips = sorted(p.name.split('.')[-2] for p in (ART / 'animation' / 'ready' / 'creatures').glob(f'anim.creature.creature_wm_{name}.*.glb'))
    animated = []
    for clip in clips:
        clip_doc = glb_json(ART / 'animation' / 'ready' / 'creatures' / f'anim.creature.creature_wm_{name}.{clip}.glb')
        animated.append(f'{clip}({len(clip_doc.get("animations", []))})')
    print(f'{name:<10} {joints:>6} {len(clips):>5}  {", ".join(animated)}')
