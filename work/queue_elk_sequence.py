"""Queue the elk motion proof as an in-game frame sequence (the farm's desktop grab cannot work there)."""
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
        "--shot", "1024,644,90,-0.08,18",
        "--watch-shots", "{OUT}/watch",
        "--watch-every", "15",
        "--fixed-fps", "60",
        "--capture-unfocused",
        "--visual", VISUAL,
    ],
    "timeout_s": 900,
    "record": False,
    "env": {
        "OTHERREACH_CHARACTER_ROOT": "{BUILD}/game/humgen_characters",
        "OTHERREACH_LIGHTING_FAST": "1",
    },
}

path = JOBS / "claude_proof_elkvideo02.json"
path.write_text(json.dumps(job, indent=2), encoding="utf-8")
print("queued", path.name, "watch shots every 15 frames for 18 s")
