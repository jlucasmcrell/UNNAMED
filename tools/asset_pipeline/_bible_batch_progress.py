"""Report progress of the Phase-1 bible landmark batch: which concepts and ready assets exist.

Kept as a file rather than an inline command because the request ids contain no shell metacharacters
but the f-string quoting does not survive a PowerShell heredoc.
"""
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
REQUESTS = os.path.join(ASSETS, "requests", "phase1_bible_landmarks.json")


def main():
    with io.open(REQUESTS, encoding="utf-8") as handle:
        entries = json.load(handle)
    art_only = {e["id"] for e in entries if e.get("build") is False}
    buildable = [e for e in entries if e.get("build") is not False]

    done, ready, missing = [], [], []
    for entry in buildable:
        asset_id = entry["id"]
        concept = os.path.join(ASSETS, "concepts", f"{asset_id}.png")
        glb = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")
        if os.path.exists(concept):
            done.append(asset_id)
        else:
            missing.append(asset_id)
        if os.path.exists(glb):
            ready.append(asset_id)

    print(f"  build targets: {len(buildable)}   (art direction only: {len(art_only)})")
    print(f"  concepts     : {len(done)}/{len(buildable)}")
    print(f"  ready GLB    : {len(ready)}/{len(buildable)}")
    if missing:
        print("  awaiting concepts: " + ", ".join(missing))
    if len(done) > len(ready):
        print("  awaiting 3D build: " + ", ".join(a for a in done if a not in ready))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
