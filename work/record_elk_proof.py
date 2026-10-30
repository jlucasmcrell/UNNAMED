"""Record the elk root-motion fix and the probe's negative result in the two documents."""
from pathlib import Path

Bugs = Path(r'C:\Otherreach_External_Assets\ROADMAP\BUGS.md')
text = Bugs.read_text(encoding='utf-8')
OLD = '| **Claude - fauna lane** | Root cause candidate: the elk came out of'
NEW = ('| **Claude - fauna lane** | **FIXED in this lane the same day**: measured across every creature set, the elk is the only species whose '
       '*locomotion* clips travel (flee 3.71 m, run 3.26 m, walk 0.90 m, all on Y) - every other species travels only in its death clip, on X/Z. '
       '`work/fix_elk_rootmotion.py` holds the pelvis of walk/run/flee at its first key in the shared art root and the farm primary (rotations '
       'untouched), and `clip_pose_probe.py` re-measures 0.00 m. Proof clip: `F:\\otherreach_captures\\fauna_areas\\elk_motion_proof.mp4` (82 in-game '
       'frames, 20 s) - the elk ground with its banner, but the animal is far from the camera in it, so a closer pass is still owed. **Root cause '
       'candidate**: the elk came out of')
if OLD in text:
    Bugs.write_text(text.replace(OLD, NEW, 1), encoding='utf-8')
    print('B-025 updated with the fix')
else:
    print('B-025 anchor missing')

Cap = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\docs\phase_b\FAUNA_CAPTURES.md')
cap = Cap.read_text(encoding='utf-8').rstrip()
cap += '''

## Elk motion proof (2026-10-30 17:05)

`claude_proof_elkvideo02` wrote 82 in-game screenshots (`--watch-shots`, every 15 frames at 60 fps, 18 s of walking the elk ground) and they are
assembled into `F:\\otherreach_captures\\fauna_areas\\elk_motion_proof.mp4` (20.5 s, 1280x720, 12 MB).

What it shows: the "UPLAND ELK - Level 3: Hard. Neutral - can be hunted." banner over the elk ground, the camera walking the stand, and a small tan
animal far to the left at ~7 s. **The elk is too far from the camera to judge its gait**, so this is a motion *record* of the ground, not yet a proof
that the animal walks correctly. A closer pass is owed.

The farm cannot record with the runner's desktop grab: `record: true` came back with
`[Parsed_ddagrab] Failed to enumerate DXGI output 0 ... Error opening input file ddagrab=framerate=30`, so the frame-sequence route above is the only
video path on that host.
'''
Cap.write_text(cap, encoding='utf-8')
print('capture doc records the elk video and the ddagrab limit')
