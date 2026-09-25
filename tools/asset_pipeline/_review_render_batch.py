"""Run the standard review render (_review_render.py) over many GLBs, several Blender processes at once.

Targets can be GLB paths, asset ids, or glob patterns:
  * an id resolves to ready/<id>/<id>.glb; "<id>_lod1".."_lod3" to ready/<id>/<id>_lodN.glb;
    "<id>_rigged" to rigged/<id>/<id>_rigged.glb
  * --glob patterns are relative to the assets root unless absolute

Each GLB gets one Blender process (killed after --timeout seconds) writing to
<out-root>/<file stem>/. A target is skipped when its <stem>_review.json says it was rendered from
the same file (path, size, mtime), by the same _review_render.py (sha256) and the same lean estimator
(estimator_digest: the functions and thresholds it uses from _asset_qa.py), at the same size and
passes, and every file it lists still exists; --force renders anyway. A review folder that already
holds a render of a different input file is never overwritten silently: that target is reported as
a conflict unless --force is given.

Writes a batch summary to <out-root>/_batch/batch_<timestamp>.json and prints one line per asset.

Usage:
  python _review_render_batch.py container_barrel_oak flora_pine_tree --jobs 3
  python _review_render_batch.py --glob "ready/*/*_lod2.glb" --jobs 4
  python _review_render_batch.py path\\to\\staging.glb --size 1536 --passes all
"""
import argparse
import glob
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor, as_completed

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
ASSETS = os.path.join(REPO, "assets")
BLENDER = os.environ.get("UNNAMED_BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
RENDER_TOOL = os.path.join(TOOL_DIR, "_review_render.py")
ESTIMATOR = os.path.join(TOOL_DIR, "_asset_qa.py")  # imported by the render tool for the lean measure
PASS_NAMES = ("normals", "wire", "detached")
# The parts of _asset_qa.py the lean measure runs. Edits elsewhere in that file do not change a review.
ESTIMATOR_FUNCTIONS = ("T", "_angle", "_cube_bins", "flat_clusters", "up_from_flats", "_describe_up", "_hull2d",
                       "base_plane_tilt")
ESTIMATOR_THRESHOLDS = ("tilt_deg", "flat_min_share", "flat_min_core")


def estimator_digest(path):
    """sha256 over the source of the estimator functions, UP and the three threshold values in `path`."""
    import ast
    with open(path, encoding="utf-8") as handle:
        source = handle.read()
    parts = {}
    for node in ast.parse(source).body:
        if isinstance(node, ast.FunctionDef) and node.name in ESTIMATOR_FUNCTIONS:
            parts[node.name] = ast.get_source_segment(source, node)
        elif isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name):
            if node.targets[0].id == "UP":
                parts["UP"] = ast.get_source_segment(source, node)
            elif node.targets[0].id == "THRESHOLDS" and isinstance(node.value, ast.Dict):
                for key, value in zip(node.value.keys, node.value.values):
                    if isinstance(key, ast.Constant) and key.value in ESTIMATOR_THRESHOLDS:
                        parts[key.value] = ast.get_source_segment(source, value.elts[0])
    missing = [n for n in ESTIMATOR_FUNCTIONS + ESTIMATOR_THRESHOLDS + ("UP",) if n not in parts]
    if missing:
        raise ValueError(f"{path} lacks {missing}")
    return hashlib.sha256("\n".join(f"{k}={parts[k]}" for k in sorted(parts)).encode("utf-8")).hexdigest()


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("targets", nargs="*", help="GLB paths or asset ids")
    parser.add_argument("--ids", nargs="*", default=[], help="asset ids (same resolution as positional ids)")
    parser.add_argument("--glob", nargs="*", default=[], dest="globs", help="GLB glob patterns")
    parser.add_argument("--jobs", type=int, default=2, help="Blender processes at once")
    parser.add_argument("--timeout", type=float, default=300.0, help="seconds per asset before the process is killed")
    parser.add_argument("--size", type=int, default=1024, help="1024 default, 1536 for hero assets")
    parser.add_argument("--passes", default="", help="comma list of normals,wire,detached or 'all'")
    parser.add_argument("--force", action="store_true", help="render even when up to date / overwrite conflicts")
    parser.add_argument("--assets", default=ASSETS, help="assets root for ids and relative globs")
    parser.add_argument("--out-root", default=None, help="default <assets>/review/standard")
    parser.add_argument("--blender", default=BLENDER)
    args = parser.parse_args()
    raw = [p.strip() for p in args.passes.split(",") if p.strip()]
    args.passes = list(PASS_NAMES) if raw == ["all"] else raw
    bad = [p for p in args.passes if p not in PASS_NAMES]
    if bad:
        parser.error(f"unknown pass(es) {bad}")
    args.out_root = args.out_root or os.path.join(args.assets, "review", "standard")
    return args


def resolve_id(asset_id, assets):
    candidates = [os.path.join(assets, "ready", asset_id, f"{asset_id}.glb")]
    for suffix in ("_lod1", "_lod2", "_lod3"):
        if asset_id.endswith(suffix):
            base = asset_id[: -len(suffix)]
            candidates.append(os.path.join(assets, "ready", base, f"{asset_id}.glb"))
    if asset_id.endswith("_rigged"):
        base = asset_id[: -len("_rigged")]
        candidates.append(os.path.join(assets, "rigged", base, f"{asset_id}.glb"))
    return next((c for c in candidates if os.path.isfile(c)), None)


def collect(args):
    paths, problems = [], []
    for target in list(args.targets) + list(args.ids):
        if target.lower().endswith(".glb") or os.path.sep in target or "/" in target:
            path = target if os.path.isabs(target) else os.path.abspath(target)
            (paths if os.path.isfile(path) else problems).append(path)
        else:
            path = resolve_id(target, args.assets)
            if path:
                paths.append(path)
            else:
                problems.append(target)
    for pattern in args.globs:
        full = pattern if os.path.isabs(pattern) else os.path.join(args.assets, pattern)
        matched = sorted(p for p in glob.glob(full) if p.lower().endswith(".glb"))
        if not matched:
            problems.append(pattern)
        paths += matched
    unique, seen = [], set()
    for path in paths:
        key = os.path.normcase(os.path.abspath(path))
        if key not in seen:
            seen.add(key)
            unique.append(os.path.abspath(path))
    return unique, problems


def sha256(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def listed_files(record):
    files = [record.get("sheet")] + [v.get("file") for v in record.get("views", {}).values()]
    files += list(record.get("pass_sheets", {}).values())
    for written in record.get("passes", {}).values():
        files += [v.get("file") for v in written.values()]
    return [f for f in files if f]


def plan(path, args, script_sha, estimator_sha):
    """Return (name, out_dir, action, reason) where action is render / skip / conflict."""
    name = os.path.splitext(os.path.basename(path))[0]
    out_dir = os.path.join(args.out_root, name)
    json_path = os.path.join(out_dir, f"{name}_review.json")
    if not os.path.isfile(json_path):
        return name, out_dir, "render", "new"
    try:
        with open(json_path, encoding="utf-8") as handle:
            record = json.load(handle)
    except (OSError, ValueError):
        return name, out_dir, "render", "unreadable review json"
    if os.path.normcase(record.get("input", "")) != os.path.normcase(path):
        if not args.force:
            return name, out_dir, "conflict", f"folder holds a render of {record.get('input')}"
        return name, out_dir, "render", "forced over a different input"
    if args.force:
        return name, out_dir, "render", "forced"
    stat = os.stat(path)
    settings = record.get("settings", {})
    checks = [
        (record.get("input_bytes") == stat.st_size and record.get("input_mtime") == stat.st_mtime, "input changed"),
        (record.get("script_sha256") == script_sha, "render script changed"),
        (record.get("estimator_digest") == estimator_sha, "lean estimator changed"),
        (settings.get("size") == args.size, "size differs"),
        (sorted(settings.get("passes", [])) == sorted(args.passes), "passes differ"),
        (not settings.get("offset_z_m"), "previous render was a self-test"),
        (all(os.path.isfile(os.path.join(out_dir, f)) for f in listed_files(record)), "output file missing"),
    ]
    for ok, reason in checks:
        if not ok:
            return name, out_dir, "render", reason
    return name, out_dir, "skip", "up to date"


def remove_previous(out_dir, name):
    """Delete the files a previous render of this asset listed, so stale pass images do not linger."""
    json_path = os.path.join(out_dir, f"{name}_review.json")
    try:
        with open(json_path, encoding="utf-8") as handle:
            record = json.load(handle)
    except (OSError, ValueError):
        return
    for file_name in listed_files(record) + [os.path.basename(json_path)]:
        target = os.path.join(out_dir, file_name)
        if os.path.isfile(target) and os.path.dirname(os.path.abspath(target)) == os.path.abspath(out_dir):
            os.remove(target)


def render(path, name, out_dir, args):
    os.makedirs(out_dir, exist_ok=True)
    remove_previous(out_dir, name)
    log_path = os.path.join(out_dir, f"{name}_render.log")
    command = [args.blender, "--background", "--factory-startup", "--python", RENDER_TOOL, "--",
               "--input", path, "--out", out_dir, "--name", name, "--size", str(args.size),
               "--estimator", args.estimator_snapshot]
    if args.passes:
        command += ["--passes", ",".join(args.passes)]
    started = time.time()
    status, output = "failed", ""
    try:
        result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace",
                                timeout=args.timeout)
        output = (result.stdout or "") + (result.stderr or "")
        marker = [line for line in output.splitlines() if line.startswith("REVIEW_RESULT ")]
        status = "ok" if result.returncode == 0 and marker else "failed"
    except subprocess.TimeoutExpired as expired:
        status = "timeout"
        output = ((expired.stdout or b"").decode("utf-8", "replace") if isinstance(expired.stdout, bytes)
                  else (expired.stdout or "")) + f"\nKILLED after {args.timeout:.0f} s"
    wall = time.time() - started
    with open(log_path, "w", encoding="utf-8") as handle:
        handle.write(" ".join(f'"{c}"' if " " in c else c for c in command) + "\n\n" + output)
    entry = {"name": name, "input": path, "status": status, "seconds_wall": round(wall, 2),
             "out_dir": out_dir, "log": log_path}
    json_path = os.path.join(out_dir, f"{name}_review.json")
    if status == "ok" and os.path.isfile(json_path):
        with open(json_path, encoding="utf-8") as handle:
            record = json.load(handle)
        entry.update({
            "seconds_in_blender": record["seconds"].get("total_in_blender"),
            "dims_m": record["dims_m"], "low_z_m": record["low_z_m"], "grounding": record["grounding"],
            "detached_island_count": record["detached_island_count"], "px_per_m": record["px_per_m"],
            "lean_deg": record["lean"]["tilt_deg"], "lean_verdict": record["lean"]["verdict"],
            "support_share": record["contact"]["support_share"],
            "contact_hull_share": record["contact"]["contact_hull_share"],
            "scale_reference_in_frame": all(v.get("scale_reference_in_frame") for v in record["views"].values()),
            "triangles": record["triangles"], "textured": record["textured"],
            "sheet": os.path.join(out_dir, record["sheet"]),
        })
    else:
        entry["error_tail"] = output.strip().splitlines()[-12:]
    return entry


def main():
    args = parse_args()
    if not os.path.isfile(args.blender):
        print(f"  blender not found: {args.blender}")
        return 2
    started = time.time()
    paths, problems = collect(args)
    for problem in problems:
        print(f"  NOT FOUND  {problem}")
    # One copy of the estimator for the whole batch, so every asset is measured by the same version
    # even while _asset_qa.py is being edited.
    snapshot_dir = tempfile.mkdtemp(prefix="review_estimator_")
    args.estimator_snapshot = os.path.join(snapshot_dir, "_asset_qa.py")
    shutil.copyfile(ESTIMATOR, args.estimator_snapshot)
    script_sha, estimator_sha = sha256(RENDER_TOOL), estimator_digest(args.estimator_snapshot)
    entries, work = [], []
    names = {}
    for path in paths:
        name, out_dir, action, reason = plan(path, args, script_sha, estimator_sha)
        if name in names:
            entries.append({"name": name, "input": path, "status": "conflict",
                            "reason": f"same file name as {names[name]} in this batch"})
            continue
        names[name] = path
        if action == "render":
            work.append((path, name, out_dir))
        else:
            entries.append({"name": name, "input": path, "status": "skipped" if action == "skip" else "conflict",
                            "reason": reason, "out_dir": out_dir})
    for entry in entries:
        print(f"  {entry['status'].upper():9s} {entry['name']}  ({entry.get('reason')})")

    with ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        futures = {pool.submit(render, path, name, out_dir, args): name for path, name, out_dir in work}
        for future in as_completed(futures):
            entry = future.result()
            entries.append(entry)
            extra = (f"{entry.get('grounding', '')} low {entry.get('low_z_m', 0):+.3f} m  "
                     f"lean {entry.get('lean_deg')} {entry.get('lean_verdict')}  support {entry.get('support_share')}  "
                     f"detached {entry.get('detached_island_count')}  blender {entry.get('seconds_in_blender')} s"
                     if entry["status"] == "ok" else f"see {entry['log']}")
            print(f"  {entry['status'].upper():9s} {entry['name']}  {entry['seconds_wall']:.1f} s wall  {extra}",
                  flush=True)
    estimator_file_sha = sha256(args.estimator_snapshot)
    shutil.rmtree(snapshot_dir, ignore_errors=True)

    counts = {}
    for entry in entries:
        counts[entry["status"]] = counts.get(entry["status"], 0) + 1
    summary = {
        "started": time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(started)),
        "seconds_wall": round(time.time() - started, 2),
        "settings": {"size": args.size, "passes": args.passes, "jobs": args.jobs, "timeout": args.timeout,
                     "force": args.force},
        "render_script_sha256": script_sha,
        "estimator_digest": estimator_sha,
        "estimator_sha256": estimator_file_sha,
        "blender": args.blender,
        "counts": counts,
        "not_found": problems,
        "assets": sorted(entries, key=lambda e: e["name"]),
    }
    batch_dir = os.path.join(args.out_root, "_batch")
    os.makedirs(batch_dir, exist_ok=True)
    stamp = time.strftime("batch_%Y%m%d_%H%M%S", time.localtime(started))
    summary_path = os.path.join(batch_dir, f"{stamp}.json")
    suffix = 2
    while os.path.exists(summary_path):
        summary_path = os.path.join(batch_dir, f"{stamp}_{suffix}.json")
        suffix += 1
    with open(summary_path, "w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2)
    if not paths:
        print("  nothing to render")
    print(f"  BATCH {counts}  {summary['seconds_wall']:.1f} s wall  summary {summary_path}")
    return 0 if paths and set(counts) <= {"ok", "skipped"} and not problems else 1


if __name__ == "__main__":
    sys.exit(main())
