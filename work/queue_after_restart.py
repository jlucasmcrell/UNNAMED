"""Queue the two jobs the owner asked for after the restart: the boulder probe (now scatter-aware) and the close elk sequence."""
import json
from pathlib import Path

JOBS = Path(r'C:\Otherreach_Farm\jobs')
VISUAL = "terrain=terrain3d,sky=sky3d,clouds=none,plants=classic,aa=off,tier=low,wind=none,vfx=flipbooks,life=none,weather=fair,textures=cache"

BASE = {
    "build": "claude_min",
    "exe": "game/Otherreach.exe",
    "args": [
        "--resolution", "1280x720", "--windowed", "--",
        "--region", "region.starting_frontier",
        "--content-root", "C:/Otherreach_Test/claude_min/game/content",
        "--bindings-overlay", "C:/Otherreach_Test/claude_min/game/content/_art_bindings.json",
        "--asset-root", "C:/Otherreach_Test/f1_gate_d_20261029_01/primary",
        "--asset-fallback", "C:/Otherreach_Test/claude_min/game/animals",
        "--scatter-rules", "C:/Otherreach_Test/claude_min/game/content/_scatter_rules.json",
        "--profile", "{OUT}/profile",
        "--shot-out", "{OUT}",
    ],
    "timeout_s": 900,
    "record": False,
    "env": {
        "OTHERREACH_CHARACTER_ROOT": "{BUILD}/game/humgen_characters",
        "OTHERREACH_LIGHTING_FAST": "1",
    },
}


def queue(name, shot, probe, watch=None, every=None):
    job = json.loads(json.dumps(BASE))
    job["args"] += ["--shot", shot]
    if watch:
        job["args"] += ["--watch-shots", f"{{OUT}}/{watch}", "--watch-every", str(every)]
        job["args"] += ["--fixed-fps", "60", "--capture-unfocused"]
    job["args"] += ["--visual", VISUAL]
    if probe:
        job["env"]["OTHERREACH_SCENERY_PROBE"] = "1"
    (JOBS / f"{name}.json").write_text(json.dumps(job, indent=2), encoding="utf-8")
    print("queued", name)


queue("claude_fix_boulders02", "636,722,90,-0.12,6", probe=True)
queue("claude_proof_elk_close02", "1006,646,110,-0.05,12", probe=False, watch="watch", every=8)
