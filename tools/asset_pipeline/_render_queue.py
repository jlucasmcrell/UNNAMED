"""Render concept batches one after another against a single ComfyUI server.

Running several _make_concepts.py jobs at once against one server looked harmless but is
not: they queue against the same GPU, so each render slowed from ~125 s to ~440 s, the
total throughput was no better than running them in turn, and the extra concurrent writes
to the W: share contributed to an `OSError: [Errno 22]` that left a corrupt file behind.
This runs a list of request files strictly in order and stops on the first hard failure.

Usage:
    python _render_queue.py --server http://RAZER:8188 ^
        --out \\\\RAZER\\D\\ComfyUI\\output <file.json> [file.json ...]
"""
import argparse
import os
import subprocess
import sys
import time

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
CONCEPTS = os.path.join(TOOL_DIR, "_make_concepts.py")


def count_state(request_path, concepts_dir):
    import json
    from PIL import Image
    with open(request_path, encoding="utf-8") as handle:
        requests = json.load(handle)
    done = pending = 0
    for item in requests:
        path = os.path.join(concepts_dir, item["id"] + ".png")
        if not os.path.exists(path):
            pending += 1
            continue
        try:
            ok = Image.open(path).size[0] >= 1536
        except Exception:
            ok = False
        if ok:
            done += 1
        else:
            pending += 1
    return done, pending


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("requests", nargs="+")
    parser.add_argument("--server", default=os.environ.get("UNNAMED_COMFY_SERVER",
                                                           "http://RAZER:8188"))
    parser.add_argument("--out", default=os.environ.get("UNNAMED_COMFY_OUTPUT",
                                                        r"\\RAZER\D\ComfyUI\output"))
    parser.add_argument("--concepts", default=r"W:\UNNAMED\assets\concepts")
    parser.add_argument("--budget-hours", type=float, default=12.0)
    args = parser.parse_args()

    environment = dict(os.environ)
    environment["UNNAMED_COMFY_SERVER"] = args.server
    environment["UNNAMED_COMFY_OUTPUT"] = args.out

    started = time.time()
    for path in args.requests:
        if not os.path.exists(path):
            print(f"  skipping missing {path}")
            continue
        done, pending = count_state(path, args.concepts)
        if pending == 0:
            print(f"  {os.path.basename(path)}: all {done} already at HQ")
            continue
        if (time.time() - started) / 3600.0 > args.budget_hours:
            print(f"  budget reached; {os.path.basename(path)} left for a later run")
            break
        print(f"  {os.path.basename(path)}: {done} done, {pending} to render", flush=True)
        code = subprocess.run([sys.executable, "-u", CONCEPTS, path, "--force"],
                              env=environment).returncode
        print(f"  {os.path.basename(path)}: exit={code}", flush=True)
        if code != 0:
            print("  stopping: a batch failed, so the next would likely fail too")
            break
    print(f"  render queue finished in {(time.time()-started)/3600.0:.1f}h")
    return 0


if __name__ == "__main__":
    sys.exit(main())
