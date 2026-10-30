"""One-process sweep: push every GLB in a build's art tree through the texture shrinker (the game refuses 2K+ embedded maps).

Per-file subprocesses made this minutes long; importing the shrinker once and calling it in a loop keeps it to the files themselves.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work')))
from shrink_glb_textures import shrink  # noqa: E402  (runs its argv loop, which is empty here)

ROOTS = [Path(p) for p in sys.argv[1:]] or [
    Path(r'C:\Otherreach_Farm\builds\claude_fauna02\game\art\ready'),
    Path(r'C:\Otherreach_Farm\builds\claude_fauna02\game\art\rigged'),
]

scanned = touched = 0
for root in ROOTS:
    for glb in root.rglob('*.glb'):
        scanned += 1
        try:
            resized = shrink(glb)
        except Exception as error:
            print(f'FAILED {glb.name}: {error}')
            continue
        if resized:
            touched += 1
            print(f'rewrote {glb.parent.name}: {len(resized)} texture(s)')
print(f'{scanned} model(s) scanned, {touched} rewritten')
