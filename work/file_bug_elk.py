"""File B-025: the elk's clips carry metres of vertical pelvis motion, measured out of the clip files."""
from pathlib import Path

PATH = Path(r'C:\Otherreach_External_Assets\ROADMAP\BUGS.md')
text = PATH.read_text(encoding='utf-8')
ANCHOR = '| B-023 | P1 |'

ROW = (
    '| B-025 | P1 | The Upland Elk lies down or soars: its clips carry metres of vertical pelvis travel | '
    '`claude_cap_elk_band\\near.png` shows the elk on the ground beside its banner; earlier `claude_motion_elk04` the same. Measured in the clips '
    'themselves by `work/clip_pose_probe.py`: elk `RigPelvis` travels **3.71 m in Y** in `flee`, **3.26 m** in `run`, **0.90 m** in `walk`, and its '
    '`idle` pelvis sits at **0.19-0.20 m** - for a 2 m animal whose idle should hold it near 1.2 m. For comparison the doe pelvis travels 0.05-0.12 m '
    'across every state and the moose 0.05-0.08 m, so this is the elk alone | **Claude - fauna lane** | Root cause candidate: the elk came out of '
    'the converter pitched over and needed the +90 degree root correction (`creatures_new\\fix_glb_pitch.py`); the same axis error in its '
    '*translations* would put a metre of forward travel into Y, which is what 3.26 m of pelvis Y looks like. Fix direction: bake or clamp the '
    'pelvis Y channel in the six elk clips to the standing height the rig implies, keeping rotation and the in-place sway, then re-shoot the band '
    'frame. The elk is the only fauna species with a corrected root, so check the correction script applies to translations as well as rotations. |\n'
)

if 'B-025' in text:
    print('already filed')
else:
    assert ANCHOR in text
    PATH.write_text(text.replace(ANCHOR, ROW + ANCHOR, 1), encoding='utf-8')
    print('filed B-025')
