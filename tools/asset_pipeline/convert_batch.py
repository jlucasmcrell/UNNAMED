"""Run convert_asset.py over a job list, then validate every result in headless Godot.

    python convert_batch.py jobs.json [--out DIR] [--skip-existing] [--no-validate]

jobs.json is a list of objects. "source" and "asset_id" are required; every other key is a convert_asset.py option
with dashes written as underscores (res, category, mesh, lods, max_tris, target_height, scale, yaw, collision,
image_format, jpeg_quality, licence, licence_evidence, source_url, region, note, preview):

  [
    {"source": "C:/Otherreach_External_Assets/Cosmos_Premium/Assets/Big Rock (MVV_LargeRocksSet_BigRock01).zip",
     "mesh": "BigRock_01", "asset_id": "rock_cos_bigrock_01", "res": 4096, "category": "rock", "preview": true},
    {"source": "C:/Otherreach_External_Assets/PolyHaven/models/tree_stump_01/tree_stump_01_4k.gltf",
     "asset_id": "env_ph_tree_stump_01", "res": 2048, "category": "environment"}
  ]

Blender runs one job per process (a fresh scene each time, and a crash costs one asset). Writes
<out>/_batch_report.json with each job's CONVERT_RESULT or error and the Godot verdicts.
"""
import argparse
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.environ.get("UNNAMED_BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("jobs")
    ap.add_argument("--out", default=r"C:\Otherreach_Recovery\asset_work\ready")
    ap.add_argument("--skip-existing", action="store_true", help="skip a job whose <id>_meta.json already exists")
    ap.add_argument("--no-validate", action="store_true")
    a = ap.parse_args()
    jobs = json.load(open(a.jobs, encoding="utf-8"))
    results = []
    for i, job in enumerate(jobs, 1):
        aid = job["asset_id"]
        out = job.get("out", a.out)
        if a.skip_existing and os.path.exists(os.path.join(out, aid, f"{aid}_meta.json")):
            print(f"[{i}/{len(jobs)}] {aid}: exists, skipped")
            continue
        cmd = [BLENDER, "--background", "--factory-startup", "--python-exit-code", "1",
               "--python", os.path.join(HERE, "convert_asset.py"), "--", "--out", out]
        for key, value in job.items():
            if key == "out" or value is False or value is None:
                continue
            cmd.append("--" + key.replace("_", "-"))
            if value is not True:
                cmd.append(str(value))
        started = time.time()
        run = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        line = next((l for l in run.stdout.splitlines() if l.startswith("CONVERT_RESULT ")), None)
        entry = {"asset_id": aid, "seconds": round(time.time() - started, 1), "exit": run.returncode}
        if line and run.returncode == 0:
            entry["result"] = json.loads(line.split(" ", 1)[1])
            print(f"[{i}/{len(jobs)}] {aid}: {entry['result']['triangles']} tris, lods {list(entry['result']['lods'].values())}, "
                  f"{entry['seconds']} s, {entry['result']['status']}")
        else:
            tail = [l for l in (run.stdout + run.stderr).splitlines() if l.strip()][-12:]
            entry["error"] = tail
            print(f"[{i}/{len(jobs)}] {aid}: FAILED (exit {run.returncode})\n    " + "\n    ".join(tail))
        results.append(entry)
    verdict = 0
    converted = [os.path.join(job.get("out", a.out), r["asset_id"]) for job, r in
                 ((next(j for j in jobs if j["asset_id"] == r["asset_id"]), r) for r in results) if "result" in r]
    if converted and not a.no_validate:
        report = os.path.join(a.out, "_godot_validation.json")
        verdict = subprocess.run([sys.executable, os.path.join(HERE, "validate_godot.py"), *converted, "--json", report]).returncode
        if os.path.exists(report):
            godot = {r["asset_id"]: r for r in json.load(open(report, encoding="utf-8"))}
            for r in results:
                if r["asset_id"] in godot:
                    r["godot_ok"] = godot[r["asset_id"]]["ok"]
                    r["godot_problems"] = godot[r["asset_id"]]["problems"]
    os.makedirs(a.out, exist_ok=True)
    with open(os.path.join(a.out, "_batch_report.json"), "w", encoding="utf-8") as fh:
        json.dump(results, fh, indent=1)
    failed = [r["asset_id"] for r in results if "error" in r or r.get("godot_ok") is False]
    print(f"{len(results) - len(failed)} of {len(results)} converted and validated" + (f"; failed: {failed}" if failed else ""))
    return 1 if failed or verdict else 0


if __name__ == "__main__":
    sys.exit(main())
