"""Hold the Upland Elk's locomotion pelvis in place: its walk, run and flee carry the forward travel as Y.

The scan restricted to the body root shows the elk alone with metres of Y travel in locomotion - flee 3.71 m, run 3.26 m, walk 0.90 m - while every
other species travels only in its death clip (a fall, and on X/Z). That is the +90 degree root correction reaching the rotations and not the
translations: the elk walks along its own up axis, so it rises instead of moving. The game moves creatures itself, so the clips are made to
animate in place by holding the pelvis translation at the clip's first key. Rotations are untouched.
"""
from pathlib import Path
import struct
import shutil

import strip_root_motion as srm

CLIP_DIRS = [
    Path(r'D:\Otherreach_Shared\art_root\animation\ready\creatures'),
    Path(r'C:\Otherreach_Farm\builds\f1_gate_d_20261029_01\primary\animation\ready\creatures'),
]
STATES = ('walk', 'run', 'flee')
BACKUP = Path(r'D:\Otherreach_Shared\art_root\animation\ready\creatures_backup_elk_rootmotion')


def hold_pelvis(path):
    data, doc, binary, _ = srm.load(path)
    touched = 0
    for animation in doc.get('animations', []):
        for channel in animation['channels']:
            if channel['target']['path'] != 'translation':
                continue
            node = doc['nodes'][channel['target']['node']].get('name', '')
            if node != 'RigPelvis':
                continue
            accessor_ref = animation['samplers'][channel['sampler']]['output']
            values, start, stride, width = srm.read_accessor(doc, binary, doc['accessors'][accessor_ref])
            held = values[0]
            buf = bytearray(binary)
            for i in range(len(values)):
                struct.pack_into('<' + 'f' * width, buf, start + i * stride, *held)
            binary = bytes(buf)
            touched += 1
    if touched:
        srm.write(path, doc, binary)
    return touched


total = 0
for directory in CLIP_DIRS:
    if not directory.exists():
        print('missing', directory)
        continue
    for state in STATES:
        clip = directory / f'anim.creature.creature_wm_elk.{state}.glb'
        if not clip.exists():
            print('missing clip', clip)
            continue
        if directory == CLIP_DIRS[0]:
            BACKUP.mkdir(parents=True, exist_ok=True)
            shutil.copy2(clip, BACKUP / clip.name)
        n = hold_pelvis(clip)
        total += n
        print(f'{clip.parent.parent.parent.parent.name if directory == CLIP_DIRS[1] else "shared"} {state}: {n} pelvis channel(s) held')
print('channels held:', total)
