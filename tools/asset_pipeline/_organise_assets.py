"""Organise the assets tree: group finished run artefacts, consolidate superseded work.

The tree accumulated twelve runs' worth of logs and state files at the root, which buries the
things a person actually opens (README, STATUS, CATALOG). This moves finished runs into a
per-run folder each, so "what happened in that run" is one directory rather than two loose
files with seven siblings.

**Nothing in an active run is touched.** The live queue's log, state file and memory guard are
identified from the running processes and skipped, because moving a log out from under a
writer either loses records or breaks the writer.

Nothing is deleted. Superseded material is consolidated under `_superseded/`, and review
scratch that has served its purpose is consolidated under `_scratch/` so it is out of the way
but still inspectable.

Usage:
    python _organise_assets.py --dry-run
    python _organise_assets.py
"""
import argparse
import os
import shutil
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"

# Finished runs, grouped by the run they belong to. Each becomes runs/<name>/.
FINISHED_RUNS = {
    "overnight": ["overnight.log", "overnight_state.json",
                  "overnight_state.json.prev_run_bak"],
    "queue_hq": ["queue_hq.log", "queue_hq_state.json"],
    "queue_next": ["queue_next.log", "queue_next_state.json"],
    "queue_production": ["queue_production.log", "queue_production_state.json"],
    "retry": ["retry.log", "retry_list.txt", "failed_hq.txt"],
}

# Superseded outputs. Consolidated, never discarded: knowing what was replaced is how a
# regression gets diagnosed later.
SUPERSEDED = ["superseded_isolated"]

# Review scratch whose conclusions are already captured in docs/ and in the kept sheets.
# The review images worth keeping stay at the top of review/.
SCRATCH = ["cleanupdebug", "unified_test", "model_compare", "parity"]


def live_paths():
    """Paths belonging to running processes, which must not move."""
    live = set()
    result = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
         "Select-Object -ExpandProperty CommandLine"],
        capture_output=True, text=True)
    for line in (result.stdout or "").splitlines():
        for token in line.split():
            token = token.strip('"')
            if ASSETS.lower() in token.lower() and os.path.exists(token):
                live.add(os.path.normcase(os.path.abspath(token)))
    # The memory guard's log is written continuously and named nowhere in an argv.
    for name in ("mem_guard.log",):
        live.add(os.path.normcase(os.path.abspath(os.path.join(ASSETS, name))))
    return live


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    live = live_paths()
    print(f"  live paths protected: {len(live)}")
    for p in sorted(live):
        print(f"    {os.path.basename(p)}")

    moves = []

    for run_name, names in FINISHED_RUNS.items():
        for name in names:
            src = os.path.join(ASSETS, name)
            if not os.path.exists(src):
                continue
            if os.path.normcase(os.path.abspath(src)) in live:
                print(f"  SKIP live: {name}")
                continue
            moves.append((src, os.path.join(ASSETS, "runs", run_name, name)))

    for name, dest_root in ([(n, "_superseded") for n in SUPERSEDED]
                            + [(n, "_scratch") for n in SCRATCH]):
        # Superseded outputs sit at the assets root; review scratch sits under review/.
        base = ASSETS if dest_root == "_superseded" else os.path.join(ASSETS, "review")
        src = os.path.join(base, name)
        if os.path.isdir(src):
            moves.append((src, os.path.join(ASSETS, dest_root, name)))

    if not moves:
        print("  nothing to move")
        return 0

    print(f"\n  {len(moves)} move(s):")
    for src, dest in moves:
        rel_src = os.path.relpath(src, ASSETS)
        rel_dest = os.path.relpath(dest, ASSETS)
        print(f"    {rel_src}  ->  {rel_dest}")
        if args.dry_run:
            continue
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        if os.path.exists(dest):
            print(f"      destination exists, skipping")
            continue
        shutil.move(src, dest)

    if args.dry_run:
        print("\n  (dry run - nothing moved)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
