"""Batch runner for _rebuild_lods.py: rebuild _lod1/_lod2/_lod3 from each asset's LOD0.

Writes to staging by default and never touches ready/ unless --promote is given:
  python _rebuild_lods_batch.py --ids container_barrel_oak,flora_oak_tree --jobs 4 --render
  python _rebuild_lods_batch.py --all --jobs 6
  python _rebuild_lods_batch.py --ids container_barrel_oak --promote   # copies staged LODs that passed

Staging: <repo>/assets/_staging/lods/<id>/ holds <id>_lod1..3.glb, <id>_lod_report.json,
<id>_lod_compare.jpg (with --render) and <id>_rebuild.log. A run summary is written to
<staging-root>/_batch_<timestamp>.json.

--promote copies staged LODs into ready/<id>/ only for assets whose report passed every gate and
whose LOD0 is byte-identical to the one the LODs were built from. It first backs the replaced files
up to <staging-root>/_promote_backup/<timestamp>/<id>/, then updates the meta 'lods' faces/ratio
(requested_faces is kept), sets lod_status "present" and records a 'lod_rebuild' block. The runner
rebuilds before promoting; add --skip-existing to promote what is already staged and passed.

A Blender run that crashes (Windows exit 3221225477 = 0xC0000005 in the GPU driver is common while
other jobs render), or that stops on a GPU error such as "Failed to retain CUDA context (Out of
memory)", is rerun up to --retries times after --retry-backoff x attempt seconds. Other script
errors exit 1 and are not rerun.
"""
import argparse
import concurrent.futures as cf
import datetime as dt
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
SCRIPT = os.path.join(HERE, "_rebuild_lods.py")
DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
DEFAULT_READY = os.path.join(REPO, "assets", "ready")
DEFAULT_STAGING = os.path.join(REPO, "assets", "_staging", "lods")
LEVELS = (1, 2, 3)


def parse_args():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sel = p.add_mutually_exclusive_group(required=True)
    sel.add_argument("--ids", help="comma-separated asset ids")
    sel.add_argument("--all", action="store_true", help="every ready asset that has a LOD chain")
    p.add_argument("--jobs", type=int, default=4, help="parallel Blender processes")
    p.add_argument("--route", default=None, help="passed to _rebuild_lods.py (default: its own)")
    p.add_argument("--tex-sizes", default=None, help="passed through, e.g. 1024,512,256")
    p.add_argument("--image-format", default=None, choices=["AUTO", "JPEG", "WEBP"])
    p.add_argument("--render", action="store_true", help="also write <id>_lod_compare.jpg")
    p.add_argument("--extra", default="", help="extra arguments for _rebuild_lods.py, one string")
    p.add_argument("--ready-root", default=DEFAULT_READY)
    p.add_argument("--staging-root", default=DEFAULT_STAGING)
    p.add_argument("--blender", default=DEFAULT_BLENDER)
    p.add_argument("--timeout", type=int, default=1800, help="seconds per asset")
    p.add_argument("--retries", type=int, default=3,
                   help="reruns of an asset whose Blender process crashed (e.g. exit 3221225477)")
    p.add_argument("--retry-backoff", type=float, default=15.0, help="seconds x attempt before a rerun")
    p.add_argument("--skip-existing", action="store_true", help="skip ids whose staged report passed")
    p.add_argument("--promote", action="store_true",
                   help="copy passing staged LODs into ready/<id>/ (backs up the files it replaces)")
    return p.parse_args()


def is_under(path, root):
    path, root = os.path.normcase(os.path.abspath(path)), os.path.normcase(os.path.abspath(root))
    return path == root or path.startswith(root.rstrip("\\/") + os.sep)


def has_lod_chain(ready_root, asset_id):
    folder = os.path.join(ready_root, asset_id)
    if not os.path.exists(os.path.join(folder, f"{asset_id}.glb")):
        return False
    meta_path = os.path.join(folder, f"{asset_id}_meta.json")
    meta = {}
    if os.path.exists(meta_path):
        with open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
    if meta.get("lod_policy") == "none":
        return False
    return bool(meta.get("lods")) or os.path.exists(os.path.join(folder, f"{asset_id}_lod1.glb"))


def select_ids(args):
    if args.ids:
        ids = [i.strip() for i in args.ids.split(",") if i.strip()]
        missing = [i for i in ids if not os.path.exists(os.path.join(args.ready_root, i, f"{i}.glb"))]
        if missing:
            raise SystemExit(f"no LOD0 for: {', '.join(missing)}")
        return ids
    return sorted(d for d in os.listdir(args.ready_root)
                  if os.path.isdir(os.path.join(args.ready_root, d)) and has_lod_chain(args.ready_root, d))


def read_report(outdir, asset_id):
    path = os.path.join(outdir, f"{asset_id}_lod_report.json")
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def run_one(args, asset_id):
    src = os.path.join(args.ready_root, asset_id, f"{asset_id}.glb")
    outdir = os.path.join(args.staging_root, asset_id)
    if args.skip_existing:
        rep = read_report(outdir, asset_id)
        if rep and rep.get("passed"):
            return asset_id, "skipped", rep, 0.0, None
    os.makedirs(outdir, exist_ok=True)
    stale = os.path.join(outdir, f"{asset_id}_lod_report.json")
    if os.path.exists(stale):
        os.remove(stale)
    # --python-exit-code: a script error exits 1 (not retried); a crash exits with an NTSTATUS
    cmd = [args.blender, "--background", "--factory-startup", "--python-exit-code", "1", "--python", SCRIPT, "--",
           "--input", src, "--outdir", outdir, "--id", asset_id]
    if args.route:
        cmd += ["--route", args.route]
    if args.tex_sizes:
        cmd += ["--tex-sizes", args.tex_sizes]
    if args.image_format:
        cmd += ["--image-format", args.image_format]
    if args.render:
        cmd += ["--render"]
    if args.extra:
        cmd += args.extra.split()
    t0 = time.time()
    log_path = os.path.join(outdir, f"{asset_id}_rebuild.log")
    crashes = []
    for attempt in range(1, args.retries + 2):
        with open(log_path, "w" if attempt == 1 else "a", encoding="utf-8", errors="replace") as log:
            log.write(f"=== attempt {attempt} {dt.datetime.now().isoformat(timespec='seconds')}\n")
            log.flush()
            start = log.tell()
            try:
                proc = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT, timeout=args.timeout)
                code = proc.returncode
            except subprocess.TimeoutExpired:
                code = "timeout"
        if code == 1 and gpu_error_in(log_path, start):
            code = "gpu error (exit 1)"
        if not is_crash(code):
            break
        crashes.append(code)
        if attempt > args.retries:
            break
        # a driver crash under GPU contention (other Blender jobs rendering) is transient
        time.sleep(args.retry_backoff * attempt)
    rep = read_report(outdir, asset_id)
    status = "ok" if code == 0 and rep else f"failed ({code})"
    return asset_id, status, rep, round(time.time() - t0, 1), {"attempts": attempt, "crash_codes": crashes}


def is_crash(code):
    """Process killed by the OS (Windows NTSTATUS such as 0xC0000005, or a POSIX signal), or a
    script error raised by the GPU (see gpu_error_in)."""
    return (isinstance(code, int) and (code < 0 or code >= 0xC0000000)) or code == "gpu error (exit 1)"


GPU_ERRORS = ("out of memory", "failed to retain cuda", "cuda error", "optix error", "gpu context")


def gpu_error_in(log_path, start):
    """True when this attempt's log shows a GPU failure (another job holding the card), which a
    rerun can get past; other script errors are deterministic and are not rerun."""
    with open(log_path, encoding="utf-8", errors="replace") as handle:
        handle.seek(start)
        text = handle.read().lower()
    return "traceback" in text and any(k in text for k in GPU_ERRORS)


def summarise(asset_id, status, rep, seconds, attempts):
    row = {"asset_id": asset_id, "status": status, "seconds": seconds, "attempts": attempts}
    if rep:
        row["passed"] = rep.get("passed")
        row["routes"] = rep.get("routes")
        row["lods"] = {k: {"route": v.get("route"), "triangles": v.get("triangles"), "target": v.get("target"),
                           "file_bytes": v.get("file_bytes"),
                           "open_edge_m": v.get("welded_boundary_length_m"),
                           "new_shards": v.get("new_shard_components"),
                           "hole_share_max_pct": (v.get("render") or {}).get("hole_share_max_pct"),
                           "blotch_patch_max_pct": (v.get("render") or {}).get("blotch_patch_max_pct"),
                           "gate_failures": v.get("gate_failures")}
                       for k, v in rep.get("lods", {}).items()}
        row["lod0_open_edge_m"] = rep.get("lod0", {}).get("welded_boundary_length_m")
        hidden = rep.get("hidden") or {}
        row["hidden_dropped"] = hidden.get("removed", 0) if hidden.get("accepted") else 0
    return row


def promote(args, asset_id, rep, stamp):
    """Copy staged LODs over ready/<id>/ after backing the old ones up; update meta lods."""
    if not rep or not rep.get("passed"):
        return "not promoted: report missing or failed gates"
    folder = os.path.join(args.ready_root, asset_id)
    staged = os.path.join(args.staging_root, asset_id)
    with open(os.path.join(folder, f"{asset_id}.glb"), "rb") as handle:
        if hashlib.sha1(handle.read()).hexdigest() != rep.get("input_sha1"):
            return "not promoted: LOD0 changed since these LODs were built"
    names = [f"{asset_id}_lod{i}.glb" for i in LEVELS]
    if not all(os.path.exists(os.path.join(staged, n)) for n in names):
        return "not promoted: staged LOD files missing"
    backup = os.path.join(args.staging_root, "_promote_backup", stamp, asset_id)
    os.makedirs(backup, exist_ok=True)
    meta_path = os.path.join(folder, f"{asset_id}_meta.json")
    for name in names + [os.path.basename(meta_path)]:
        path = os.path.join(folder, name)
        if os.path.exists(path):
            shutil.copy2(path, os.path.join(backup, name))
    for name in names:
        tmp = os.path.join(folder, name + ".promote_tmp")
        shutil.copy2(os.path.join(staged, name), tmp)
        if os.path.getsize(tmp) != os.path.getsize(os.path.join(staged, name)):
            os.remove(tmp)
            return f"not promoted: size mismatch copying {name}"
        os.replace(tmp, os.path.join(folder, name))
    if os.path.exists(meta_path):
        with open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
        lods = meta.get("lods") if isinstance(meta.get("lods"), dict) else {}
        src_faces = rep.get("source_faces") or rep["lod0"]["triangles"]
        for i in LEVELS:
            key = f"{asset_id}_lod{i}"
            entry = dict(lods.get(key) or {})
            level = rep["lods"][f"lod{i}"]
            entry["faces"] = level["triangles"]
            entry.setdefault("requested_faces", level.get("requested", level.get("target")))
            entry["ratio"] = round(level["triangles"] / src_faces, 4)
            lods[key] = entry
        meta["lods"] = lods
        meta["lod_status"] = "present"
        meta["lod_rebuild"] = {"routes": [rep["lods"][f"lod{i}"]["route"] for i in LEVELS],
                               "tex_sizes": rep.get("tex_sizes"),
                               "date": stamp, "tool": "tools/asset_pipeline/_rebuild_lods.py",
                               "backup": backup}
        tmp = meta_path + ".promote_tmp"
        with open(tmp, "w", encoding="utf-8") as handle:
            json.dump(meta, handle, indent=2)
        os.replace(tmp, meta_path)
    return f"promoted (backup {backup})"


def main():
    args = parse_args()
    args.ready_root = os.path.abspath(args.ready_root)
    args.staging_root = os.path.abspath(args.staging_root)
    if is_under(args.staging_root, args.ready_root):
        raise SystemExit("staging root must not be inside the ready root")
    if not os.path.exists(args.blender):
        raise SystemExit(f"Blender not found: {args.blender}")
    ids = select_ids(args)
    os.makedirs(args.staging_root, exist_ok=True)
    stamp = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
    print(f"{len(ids)} assets -> {args.staging_root} ({args.jobs} jobs)", flush=True)
    rows = []
    with cf.ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        futures = [pool.submit(run_one, args, i) for i in ids]
        for fut in cf.as_completed(futures):
            asset_id, status, rep, seconds, attempts = fut.result()
            row = summarise(asset_id, status, rep, seconds, attempts)
            rows.append(row)
            tris = "/".join(str(v["triangles"]) for v in row.get("lods", {}).values())
            chosen = "/".join(str(v["route"]) for v in row.get("lods", {}).values())
            print(f"{asset_id:44s} {status:12s} passed={row.get('passed')} tris={tris} routes={chosen} {seconds}s",
                  flush=True)
    rows.sort(key=lambda r: r["asset_id"])
    if args.promote:
        for row in rows:
            rep = read_report(os.path.join(args.staging_root, row["asset_id"]), row["asset_id"])
            row["promotion"] = promote(args, row["asset_id"], rep, stamp)
            print(f"{row['asset_id']}: {row['promotion']}", flush=True)
    summary = {"stamp": stamp, "ready_root": args.ready_root, "staging_root": args.staging_root,
               "route": args.route, "promote": args.promote, "count": len(rows),
               "passed": sum(1 for r in rows if r.get("passed")), "assets": rows}
    path = os.path.join(args.staging_root, f"_batch_{stamp}.json")
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(summary, handle, indent=2)
    print(f"{summary['passed']}/{len(rows)} passed; summary {path}", flush=True)
    return 0 if summary["passed"] == len(rows) else 1


if __name__ == "__main__":
    sys.exit(main())
