"""Keep a machine working: run queue stages back to back, restarting if one dies.

Sessions have repeatedly stalled because a job finished (or crashed) and nothing picked
up the next one. This runs stages in order, re-invoking the queue runner for any stage the
plan still leaves undone, and keeps going until there is genuinely nothing left.

The queue runner treats a stage with any failed asset as `failed`, so a stage normally
needs a second pass to pick up stragglers. That is expected: each pass is cheaper than the
last, and the loop stops once a full pass changes nothing.

Usage:
    python _supervise.py --plan <plan.json> --hours 10
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
QUEUE = os.path.join(TOOL_DIR, "_overnight_queue.py")


def stage_states(state_path):
    try:
        with open(state_path, encoding="utf-8") as handle:
            return json.load(handle).get("stages", {})
    except (OSError, ValueError):
        return {}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--plan", required=True)
    parser.add_argument("--state", default=None)
    parser.add_argument("--log", default=None)
    parser.add_argument("--hours", type=float, default=10.0)
    parser.add_argument("--passes", type=int, default=4,
                        help="Maximum passes over the plan; each pass retries failures")
    args = parser.parse_args()

    state = args.state or os.path.join(
        os.path.dirname(args.plan), "supervised_state.json")
    log = args.log or os.path.join(
        os.path.dirname(args.plan), "supervised.log")

    with open(args.plan, encoding="utf-8") as handle:
        plan = json.load(handle)
    wanted = [s["name"] for s in plan["stages"] if s["name"] != "catalog"]

    started = time.time()
    for attempt in range(1, args.passes + 1):
        states = stage_states(state)
        pending = [n for n in wanted
                   if states.get(n, {}).get("status") not in ("done",)]
        if not pending:
            print(f"  attempt {attempt}: everything done")
            break

        elapsed_h = (time.time() - started) / 3600.0
        if elapsed_h > args.hours:
            print(f"  budget reached after {elapsed_h:.1f}h; {len(pending)} stage(s) left")
            break

        # Prefer a stage that has never run over one that already failed.
        #
        # Always taking the first pending stage means one asset that cannot be built blocks
        # every stage behind it: `world materials: items` failed on a single slow item 32
        # times while eight later stages, holding 94 buildable assets, never started. Retrying
        # is still wanted, but not at the cost of never attempting the rest of the plan.
        untouched = [n for n in pending
                     if states.get(n, {}).get("status") is None]
        name = untouched[0] if untouched else pending[0]
        if untouched:
            print(f"  attempt {attempt}: running '{name}' "
                  f"({elapsed_h:.1f}h elapsed, {len(untouched)} stage(s) never run, "
                  f"{len(pending)} pending)", flush=True)
        else:
            print(f"  attempt {attempt}: retrying '{name}' "
                  f"({elapsed_h:.1f}h elapsed, {len(pending)} stage(s) pending)", flush=True)
        subprocess.run([sys.executable, "-u", QUEUE, "--plan", args.plan,
                        "--state", state, "--log", log,
                        "--budget-hours", str(max(args.hours - elapsed_h, 0.5)),
                        "--only", name, "--resume"])
        time.sleep(5)

    with open(log, "a", encoding="utf-8") as handle:
        handle.write(f"\n=== supervise finished after "
                     f"{(time.time()-started)/3600.0:.1f}h ===\n")
    print(f"  supervise finished in {(time.time()-started)/3600.0:.1f}h")
    return 0


if __name__ == "__main__":
    sys.exit(main())
