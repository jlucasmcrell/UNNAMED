"""Link every exported native creator part into the art root a gate run reads.

The creator loads its bases, preset materials and modular parts through the run's fallback art root
(work/creator/game/world_fallback), which holds one junction per folder of game/primary/rigged. A part
exported after that root was first built is invisible to the run until it is linked here, which is what
made the FACE step draw bald heads even though every groom had been exported.
"""
import os
import subprocess
import sys
from pathlib import Path

work = Path(sys.argv[1] if len(sys.argv) > 1 else r"D:\Otherreach_Scratch\codex_f1_siann_wt\work\creator")
primary = work / "game/primary/rigged"
fallback = work / "game/world_fallback/rigged"

linked, present, failed = 0, 0, []
for folder in sorted(primary.iterdir()):
    if not folder.is_dir() or not folder.name.startswith("humgen_creator_"):
        continue
    link = fallback / folder.name
    if link.exists():
        present += 1
        continue
    result = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(folder)],
                            capture_output=True, text=True)
    if result.returncode == 0:
        linked += 1
    else:
        failed.append((folder.name, (result.stderr or result.stdout).strip()))
print(f"creator part folders in the preview root: {linked + present} ({linked} linked now, {present} already there)")
for name, why in failed:
    print("FAILED", name, why)
if failed:
    raise SystemExit(1)
