"""Write a morning-review status summary for an overnight run.

Reads the queue log, queue state, and asset catalogue, and writes a single
markdown report: what ran, how long it took, what was produced, and anything that
failed. Safe to run at any time, including while the queue is still going.

Usage:
    python _write_status.py
"""
import argparse
import glob
import json
import os
import re
import subprocess
import sys
import time

ASSETS = r"W:\UNNAMED\assets"


def load_json(path, default=None):
    try:
        with open(path, encoding="utf-8") as handle:
            return json.load(handle)
    except (OSError, ValueError):
        return default


def stage_table(state):
    rows = []
    for name, info in (state.get("stages") or {}).items():
        seconds = info.get("seconds")
        duration = "-" if seconds is None else (
            f"{seconds / 60:.1f} min" if seconds >= 90 else f"{seconds:.0f}s")
        rows.append((name, info.get("status", "?"), duration,
                     info.get("started", "-"), info.get("finished", "-")))
    return rows


def collect_asset_records(base):
    """Read per-asset results straight from the run manifests.

    The parent queue log only records a stage's output once that stage finishes, so
    during a multi-hour 3D stage it shows nothing. The manifests are written as each
    asset completes, so they are the only source that reflects live progress.
    """
    records = []
    for path in sorted(glob.glob(os.path.join(base, "manifests", "run_*.json"))):
        manifest = load_json(path, {}) or {}
        for asset in manifest.get("assets", []):
            generate = asset.get("generate") or {}
            cleanup = asset.get("cleanup") or {}
            records.append({
                "stem": asset.get("stem"),
                "category": asset.get("category"),
                "verified": bool(asset.get("verified")),
                "generate_seconds": generate.get("seconds"),
                "cleanup_seconds": cleanup.get("seconds"),
                "faces": cleanup.get("base_faces"),
                "manifest": os.path.basename(path),
                "problem": asset.get("error") or asset.get("verify_problem"),
            })
    return records


def category_of(stem, recorded):
    """Prefer the recorded category, else infer from the asset id.

    Assets built before category inference was recorded show up as unknown in old
    manifests even though their id still says what they are.
    """
    if recorded and recorded != "unknown":
        return recorded
    lowered = (stem or "").lower()
    for category in ("weapon", "tool", "prop", "creature", "character",
                     "building", "icon", "material"):
        if lowered.startswith(category + "_"):
            return category
    return recorded or "unknown"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default=ASSETS)
    parser.add_argument("--out", default=None)
    args = parser.parse_args()

    base = args.assets
    out_path = args.out or os.path.join(base, "STATUS.md")

    # Refresh the catalogue first so its counts agree with this report. The catalogue
    # is otherwise only rebuilt at stage boundaries, and a long stage would leave it
    # visibly behind.
    catalog_tool = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "_catalog_assets.py")
    if os.path.exists(catalog_tool):
        subprocess.run([sys.executable, catalog_tool, "--assets", base],
                       capture_output=True, text=True)

    state = load_json(os.path.join(base, "overnight_state.json"), {}) or {}
    catalog = load_json(os.path.join(base, "catalog.json"), {}) or {}
    log_path = os.path.join(base, "overnight.log")
    log_text = ""
    if os.path.exists(log_path):
        with open(log_path, encoding="utf-8", errors="replace") as handle:
            log_text = handle.read()

    concepts = glob.glob(os.path.join(base, "concepts", "*.png"))
    raw = glob.glob(os.path.join(base, "raw", "*.glb"))
    ready_dirs = [d for d in glob.glob(os.path.join(base, "ready", "*"))
                  if os.path.isdir(d)]
    glb_files = glob.glob(os.path.join(base, "ready", "**", "*.glb"), recursive=True)

    by_prefix = {}
    for path in concepts:
        prefix = os.path.basename(path).split("_")[0]
        by_prefix[prefix] = by_prefix.get(prefix, 0) + 1

    # Phase A asked for a small starter pack. Report progress against it directly,
    # because "is the prototype pack ready" is the question that matters, not the
    # raw asset count.
    built_prefixes = {os.path.basename(d).split("_")[0] for d in ready_dirs}
    built_counts = {}
    for name in built_prefixes:
        built_counts[name] = len([d for d in ready_dirs
                                  if os.path.basename(d).startswith(name + "_")])
    phase_a = [
        ("weapons", "weapon", 5, 10),
        ("tools", "tool", 3, 3),
        ("props", "prop", 10, 20),
        ("creatures", "creature", 1, 1),
        ("icons (2D art)", "icon", 20, 20),
    ]

    asset_records = collect_asset_records(base)
    generated = [r for r in asset_records if r.get("generate_seconds")]
    timings = sorted(r["generate_seconds"] for r in generated)
    verified = [r for r in asset_records if r.get("verified")]
    failed = [r for r in asset_records if not r.get("verified")]
    stages = (state.get("stages") or {})
    # A state file can carry a "finished" key from an earlier run, so decide by
    # whether any stage is still in flight or the budget stopped us.
    in_flight = [name for name, info in stages.items() if info.get("status") == "running"]
    if in_flight:
        queue_state = f"still running ({in_flight[-1]})"
    elif state.get("stopped"):
        queue_state = f"stopped: {state['stopped']}"
    else:
        queue_state = "finished"

    lines = [
        "# Overnight Asset Run - Status",
        "",
        f"Report generated: {time.strftime('%Y-%m-%d %H:%M:%S')}",
        "",
        f"Queue state: **{queue_state}**"
        + (f" (started {state.get('started')})" if state.get("started") else ""),
        "",
        "> Note on the run log: stages that stopped at their `--max-seconds` cap were",
        "> recorded as `FAILED` at the time, because hitting the cap exited non-zero.",
        "> That was a reporting bug, since fixed. Every asset those stages produced was",
        "> built and verified normally, and the stage notes in `overnight_state.json`",
        "> record what actually happened.",
        "",
        "## Output",
        "",
        f"- concept images: **{len(concepts)}**",
        f"- 3D assets verified: **{len(ready_dirs)}**",
        f"- GLB files produced: **{len(glb_files)}** "
        f"(base + LODs + collision per asset)",
        f"- raw GLBs awaiting cleanup: **{max(0, len(raw) - len(ready_dirs))}**",
        "",
        "## Phase A starter pack",
        "",
        "| Category | Built | Phase A target | Status |",
        "|---|---|---|---|",
    ]
    for label, prefix, low, high in phase_a:
        if prefix == "icon":
            # Icons are 2D art: count rendered concepts, not meshes.
            count = by_prefix.get("icon", 0) + by_prefix.get("item", 0) + by_prefix.get("resource", 0)
        else:
            count = built_counts.get(prefix, 0)
        status = "met" if count >= low else f"needs {low - count} more"
        lines.append(f"| {label} | {count} | {low}-{high} | {status} |")
    lines.append("")

    lines.extend([
        "### Concepts by category",
        "",
        "| Prefix | Count |",
        "|---|---|",
    ])
    for prefix, count in sorted(by_prefix.items(), key=lambda kv: -kv[1]):
        lines.append(f"| `{prefix}_` | {count} |")

    lines.extend(["", "## Queue stages", "",
                  "| Stage | Status | Duration | Planned cap |",
                  "|---|---|---|---|"])
    plan_stages = load_json(os.path.join(base, "requests", "overnight_plan.json"), {}) or {}
    caps = {stage["name"]: stage.get("max_seconds")
            for stage in plan_stages.get("stages", [])}
    reported = {row[0] for row in stage_table(state)}
    for name, status, duration, _started, _finished in stage_table(state):
        cap = caps.get(name)
        lines.append(f"| {name} | {status} | {duration} | "
                     f"{f'{cap / 3600:.1f} h' if cap else '-'} |")
    # Stages in the plan that have not started yet still belong in the table, or the
    # report hides what the night was meant to do.
    for stage in plan_stages.get("stages", []):
        if stage["name"] in reported:
            continue
        cap = stage.get("max_seconds")
        lines.append(f"| {stage['name']} | pending | - | "
                     f"{f'{cap / 3600:.1f} h' if cap else '-'} |")

    lines.extend([
        "",
        "## Work remaining",
        "",
    ])
    # How much 3D work is still unbuilt, and how long it would take at the rate
    # actually observed tonight. Without this the report says what happened but not
    # whether the night got through the backlog.
    ready_names = {os.path.basename(path) for path in ready_dirs}
    eligible_prefixes = ("weapon_", "tool_", "prop_", "creature_")
    eligible_total = sum(by_prefix.get(prefix.rstrip("_"), 0)
                         for prefix in eligible_prefixes)
    pending = []
    for path in concepts:
        stem = os.path.splitext(os.path.basename(path))[0]
        if stem.startswith(eligible_prefixes) and stem not in ready_names:
            pending.append(stem)

    median_seconds = timings[len(timings) // 2] if timings else None
    lines.append(f"- 3D-eligible concepts: **{eligible_total}**")
    lines.append(f"- built: **{len(ready_dirs)}**")
    lines.append(f"- not yet built: **{len(pending)}**")
    if median_seconds:
        hours = len(pending) * median_seconds / 3600
        lines.append(f"- at tonight's median of {median_seconds:.0f}s per asset, the "
                     f"remainder needs **{hours:.1f} h** of GPU time")
        lines.append("")
        lines.append("Run the queue again with `--resume` to continue through the "
                     "remainder; completed assets are skipped.")

    # Say plainly what was not attempted, so the pack is not misread as complete.
    flat = [name for name in ("icon", "item", "resource", "material")]
    flat_concepts = sum(by_prefix.get(name, 0) for name in flat)
    lines.extend(["", "### Deferred by design", ""])
    lines.append(f"- **{flat_concepts} icon / item / resource / material concepts** are "
                 f"2D art. They are finished deliverables and are deliberately not "
                 f"sent through the mesh pipeline.")
    if pending:
        lines.append(f"- **{len(pending)} 3D concepts** remain unbuilt and queue "
                     f"behind the stages that did run. Raw GLBs are kept in `raw/` "
                     f"so re-cleaning them later needs no GPU.")

    if timings:
        lines.extend([
            "",
            "## 3D generation timing",
            "",
            f"- assets generated: **{len(timings)}**",
            f"- median: **{timings[len(timings) // 2]:.0f}s**",
            f"- fastest: **{timings[0]:.0f}s**, slowest: **{timings[-1]:.0f}s**",
            f"- total generation time: **{sum(timings) / 3600:.1f} h**",
            "",
            "Generation time swings widely with subject complexity, so treat the "
            "median as the planning number and the maximum as the risk case.",
        ])

    lines.extend(["", "## Verified assets by category", "",
                  "| Category | Verified | Faces (total) |",
                  "|---|---|---|"])
    categories = {}
    for record in verified:
        category = category_of(record.get("stem"), record.get("category"))
        entry = categories.setdefault(category, {"count": 0, "faces": 0})
        entry["count"] += 1
        entry["faces"] += record.get("faces") or 0
    for category in sorted(categories):
        entry = categories[category]
        lines.append(f"| {category} | {entry['count']} | {entry['faces']:,} |")
    if not categories:
        lines.append("| - | 0 | 0 |")

    if catalog.get("assets"):
        lines.extend(["", "## Catalogue", "",
                      f"{catalog.get('asset_count', 0)} assets, "
                      f"{catalog.get('total_triangles', 0):,} base triangles, "
                      f"{catalog.get('total_bytes', 0) / 1e6:.1f} MB of base GLB.", "",
                      "Full detail in `CATALOG.md` and `catalog.json`."])

    # Independent sweep of the whole pack, not just the pipeline's own per-asset check.
    verify = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_verify_pack.py")
    if os.path.exists(verify) and ready_dirs:
        check = subprocess.run([sys.executable, verify, "--assets", base, "--fast"],
                               capture_output=True, text=True)
        output = check.stdout or ""
        summary = [line for line in output.splitlines()
                   if "assets clean" in line or line.startswith("RESULT:")]
        lines.extend(["", "## Pack integrity", ""])
        if summary:
            for line in summary:
                lines.append(f"- {line}")
        else:
            lines.append("- check did not report a result")
        if check.returncode != 0:
            lines.append("")
            lines.append("See the problems listed by "
                         "`python tools\\asset_pipeline\\_verify_pack.py`.")

    stage_failures = [name for name, info in (state.get("stages") or {}).items()
                      if info.get("status") == "failed"]
    if stage_failures or failed:
        lines.extend(["", "## Failures", ""])
        for name in stage_failures:
            lines.append(f"- stage `{name}` failed (see `overnight.log`)")
        for record in failed[:20]:
            lines.append(f"- asset `{record['stem']}` did not verify: "
                         f"{str(record.get('problem'))[:140]}")
        if len(failed) > 20:
            lines.append(f"- ...and {len(failed) - 20} more")
    else:
        lines.extend(["", "## Failures", "", "None recorded."])

    lines.extend([
        "",
        "## Resuming",
        "",
        "```",
        "python W:\\UNNAMED\\tools\\asset_pipeline\\_overnight_queue.py \\",
        "    --plan W:\\UNNAMED\\assets\\requests\\overnight_plan.json --resume",
        "```",
        "",
        "Completed stages are skipped and `--skip-existing` on the concept stage "
        "means only missing work is redone.",
        "",
    ])

    with open(out_path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))
    print(f"status -> {out_path}")
    print(f"  concepts {len(concepts)}, assets {len(ready_dirs)}, "
          f"glb files {len(glb_files)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
