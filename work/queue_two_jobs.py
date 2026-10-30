import json
from pathlib import Path

JOBS = Path(r'C:\Otherreach_Farm\jobs')
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
VISUAL = "terrain=terrain3d,sky=sky3d,clouds=none,plants=classic,aa=off,tier=low,wind=none,vfx=flipbooks,life=none,weather=fair,textures=cache"


def make(name, shot, record, probe):
    job = json.loads(json.dumps(BASE))
    job["args"] += ["--shot", shot]
    if record:
        job["args"] += ["--fixed-fps", "60", "--capture-unfocused"]
    job["args"] += ["--visual", VISUAL]
    job["record"] = record
    if probe:
        job["env"]["OTHERREACH_SCENERY_PROBE"] = "1"
    path = JOBS / f"{name}.json"
    path.write_text(json.dumps(job, indent=2), encoding="utf-8")
    print(name, "queued; shot", shot, "record", record, "probe", bool(probe))


make("claude_fix_boulders01", "636,722,90,-0.12,6", False, True)
make("claude_proof_elkvideo01", "1024,644,90,-0.08,10", True, False)
