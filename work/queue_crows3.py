"""Queue a crow stand 10 m from the new verge mob, looking down into it."""
import json
from pathlib import Path

JOBS = Path(r'C:\Otherreach_Farm\jobs')
VISUAL = "terrain=terrain3d,sky=sky3d,clouds=none,plants=classic,aa=off,tier=low,wind=none,vfx=flipbooks,life=none,weather=fair,textures=cache"

job = {
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
        "--shot", "752,752,90,-0.28,6",
        "--visual", VISUAL,
    ],
    "timeout_s": 900,
    "record": False,
    "env": {
        "OTHERREACH_CHARACTER_ROOT": "{BUILD}/game/humgen_characters",
        "OTHERREACH_LIGHTING_FAST": "1",
    },
}

path = JOBS / "claude_cap_crows3.json"
path.write_text(json.dumps(job, indent=2), encoding="utf-8")
print("queued", path.name, "10 m from the verge mob, looking down")
