"""Sweep a whole asset tree's GLBs through the texture shrinker, skipping the ones that are already within the limit."""
import subprocess
import sys
from pathlib import Path

SHRINKER = Path(r'D:\Otherreach_Scratch\ds_fish2_wt\work\shrink_glb_textures.py')
ROOTS = [Path(p) for p in sys.argv[1:]] or [Path(r'C:\Otherreach_Farm\builds\claude_fauna02\game\art\ready'),
                                              Path(r'C:\Otherreach_Farm\builds\claude_fauna02\game\art\rigged')]

touched = 0
scanned = 0
for root in ROOTS:
    for glb in root.rglob('*.glb'):
        scanned += 1
        result = subprocess.run(['C:\\Python314\\python.exe', '-X', 'utf8', str(SHRINKER), str(glb)],
                                capture_output=True, text=True)
        if result.stdout.strip():
            touched += 1
            print(f'{glb.relative_to(root.parent)}: {result.stdout.strip().splitlines()[0]}')
print(f'{scanned} model(s) scanned, {touched} rewritten')
