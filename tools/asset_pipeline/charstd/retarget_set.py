"""A character's clip set on its own rig (charstd): every entry of a clip plan retargeted onto the character's rigged GLB by the
animation retarget factory (_retarget_clip.py), a few Blender processes at a time.

    python retarget_set.py --plan clip_plans/player.json --target assets/rigged/<id>/<id>_rigged.glb --prefix player.veth_wanderer_std
        [--only state1,state2] [--jobs 6]

A plan entry either replays an existing clip's recorded source and settings onto the new rig ({"state", "from_record": <clip id>}) -
the record names the pack archive, the file inside it, the clip, the map and the retarget options - or names a source directly
({"state", "archive"|"file", "member", "clip", "map", "root", "scale", "feet", "loop", "category", "clip_type", "hand_left",
"hand_right", "events": {"id": seconds}}). The new clip's id is <prefix>.<entry "id" or the recorded id's last part or the state>.
Prints one line per clip and writes <prefix>.plan_result.json beside the records.
"""
import argparse
import concurrent.futures as cf
import glob
import json
import os
import subprocess
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
ASSETS = os.path.join(REPO, "assets")
TOOL = os.path.join(REPO, "tools", "asset_pipeline", "_retarget_clip.py")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
PACK_ROOTS = [r"F:\Otherreach_External_Assets\animations", r"F:\Otherreach_External_3D\cmu_work\cycles"]


def find_source(name):
    for root in PACK_ROOTS:
        hits = glob.glob(os.path.join(glob.escape(root), "**", glob.escape(name)), recursive=True)
        if hits:
            return hits[0]
    raise SystemExit(f"source {name} not found under {PACK_ROOTS}")


def from_record(clip_id):
    r = json.load(open(os.path.join(ASSETS, "animation", "clips", f"anim.{clip_id}.json"), encoding="utf-8"))
    s, rt = r["source"], r["retarget"]
    e = {"clip": s["clip"], "map": s["map"], "root": rt.get("root_mode", "inplace"), "scale": rt.get("scale_mode", "legs"),
         "feet": rt.get("feet", "ik"), "fps": rt.get("fps", 60.0), "loop": r["loop"], "category": r["category"],
         "clip_type": r["clip_type"], "hand_left": r["hand_profile"]["left"], "hand_right": r["hand_profile"]["right"],
         "events": {ev["id"]: ev["time"] for ev in r.get("events", [])}, "id": clip_id.split(".")[-1]}
    if s.get("archive"):
        e["archive"], e["member"] = s["archive"], s["file"]
    else:
        e["file"] = s["file"]
    return e


def command(entry, target, clip_id):
    src = find_source(entry["archive"]) if entry.get("archive") else find_source(entry["file"])
    cmd = [BLENDER, "--background", "--factory-startup", "--python", TOOL, "--", "--source", src, "--clip", entry["clip"],
           "--map", os.path.join(REPO, "tools", "asset_pipeline", entry["map"]), "--target", target, "--id", clip_id,
           "--root", entry.get("root", "inplace"), "--scale", str(entry.get("scale", "legs")), "--feet", entry.get("feet", "ik"),
           "--fps", str(entry.get("fps", 60.0)), "--category", entry.get("category", "locomotion"),
           "--clip-type", entry.get("clip_type", "transition"), "--hand-left", entry.get("hand_left", "open"),
           "--hand-right", entry.get("hand_right", "open"), "--assets", ASSETS]
    if entry.get("member"):
        cmd += ["--member", entry["member"]]
    if entry.get("loop"):
        cmd.append("--loop")
    for k, t in entry.get("events", {}).items():
        cmd += ["--event", f"{k}:{t}"]
    return cmd


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--plan", required=True)
    p.add_argument("--target", required=True)
    p.add_argument("--prefix", required=True)
    p.add_argument("--only", default="")
    p.add_argument("--jobs", type=int, default=6)
    a = p.parse_args()
    plan = json.load(open(a.plan, encoding="utf-8"))
    only = set(filter(None, a.only.split(",")))
    target = os.path.abspath(a.target)
    jobs = []
    for raw in plan["entries"]:
        if only and raw["state"] not in only:
            continue
        entry = {**from_record(raw["from_record"]), **{k: v for k, v in raw.items() if k != "from_record"}} if raw.get("from_record") else dict(raw)
        entry["map"] = plan.get("maps", {}).get(entry["map"], entry["map"])   # the plan's rig family: a recorded map -> its variant
        clip_id = f"{a.prefix}.{entry.get('id') or entry['state']}"
        jobs.append((raw["state"], clip_id, command(entry, target, clip_id)))
    results = {}

    def run(job):
        state, clip_id, cmd = job
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        ok = r.returncode == 0 and os.path.exists(os.path.join(ASSETS, "animation", "clips", f"anim.{clip_id}.json"))
        tail = (r.stdout + r.stderr).strip().splitlines()[-3:]
        return state, clip_id, ok, tail

    with cf.ThreadPoolExecutor(a.jobs) as pool:
        for state, clip_id, ok, tail in pool.map(run, jobs):
            results[state] = {"clip": clip_id, "ok": ok}
            print(("OK  " if ok else "FAIL"), state, clip_id, "" if ok else " | ".join(tail))
    json.dump(results, open(os.path.join(ASSETS, "animation", "clips", f"{a.prefix}.plan_result.json"), "w"), indent=1)
    sys.exit(0 if all(r["ok"] for r in results.values()) else 1)


if __name__ == "__main__":
    main()
