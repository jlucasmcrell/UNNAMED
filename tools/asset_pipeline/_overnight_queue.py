"""Unattended overnight queue: run a list of asset-generation stages in order.

Designed to survive being interrupted or the machine rebooting: each stage is
skipped if already marked done, and every stage's output is appended to a log file
as it happens rather than buffered.

Run:
    python _overnight_queue.py --plan overnight.json
    python _overnight_queue.py --plan overnight.json --resume
    python _overnight_queue.py --plan overnight.json --budget-hours 8
"""
import argparse
import json
import os
import re
import subprocess
import threading
import urllib.request
import sys
import time

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
PYTHON = sys.executable
ASSETS = r"W:\UNNAMED\assets"

CONCEPTS = os.path.join(TOOL_DIR, "_make_concepts.py")
ASSETS_TOOL = os.path.join(TOOL_DIR, "_make_assets.py")
CATALOG = os.path.join(TOOL_DIR, "_catalog_assets.py")
STATUS = os.path.join(TOOL_DIR, "_write_status.py")
PROMPTFILES = os.path.join(TOOL_DIR, "_requests_to_promptfiles.py")
NOTIFY = os.path.join(TOOL_DIR, "_notify.py")

# Watchdog settings. A stalled ComfyUI spins the GPU at 100% while writing nothing to its
# log and advancing no CPU time, so the log is the reliable signal. Recovery is a restart,
# which loses the asset in flight but keeps the rest of the batch alive.
COMFY_LOG = os.environ.get("UNNAMED_COMFY_LOG", r"C:\Users\jluca\ComfyUI\user\comfyui.log")
COMFY_LAUNCHER = os.environ.get("UNNAMED_COMFY_LAUNCHER",
                                r"C:\Users\jluca\Desktop\start-comfyui.bat")
# Measured against a healthy run: ComfyUI writes a timestamped line every few seconds
# throughout a build, and the whole log contains no gap of 20s or more. So the length of
# the silence does not have to distinguish a stall from a slow step - there is no slow
# step that goes quiet.
#
# The threshold is set well above that anyway, because of a complication: a stalled asset
# leaves its prompt in ComfyUI's queue for ever, so "the queue is busy" cannot be used to
# avoid restarting a healthy server mid-prompt. A short threshold combined with a busy
# check would therefore never fire, and a short threshold without one destroys innocent
# assets queued behind it. Ten minutes of total log silence is unambiguous, and far longer
# than anything observed during real work.
STALL_SECONDS = int(os.environ.get("UNNAMED_STALL_SECONDS", 600))
WATCH_INTERVAL = 30
# How long to sample ComfyUI's CPU time for, to tell a slow bake from a wedge. This was
# referenced in StallWatchdog.run() but never defined here, so the watchdog thread raised
# NameError on its first cycle and died silently - a 31-minute stall went unnoticed while the
# GPU sat at 46% and the log stopped at "Bake texture: finalize".
CPU_SAMPLE_SECONDS = 20


def log_idle_seconds():
    try:
        return time.time() - os.path.getmtime(COMFY_LOG)
    except OSError:
        return None


def comfy_processes():
    """PIDs of the ComfyUI server, identified by its own command line.

    Uses Get-CimInstance rather than wmic: wmic has been removed from current Windows
    builds, and a missing tool made this return an empty list, which would have stopped
    the watchdog ever firing while looking like it was working.
    """
    script = ("Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
              "Where-Object { $_.CommandLine -like '*main.py*' } | "
              "Select-Object -ExpandProperty ProcessId")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", script],
                             capture_output=True, text=True, timeout=60).stdout
    except (OSError, subprocess.SubprocessError):
        return []
    return [int(line.strip()) for line in out.splitlines() if line.strip().isdigit()]


def comfy_cpu_seconds(pids):
    """Total user-mode CPU seconds across the given processes, or None if unreadable.

    Used to tell a long GPU bake (which writes no log output but burns CPU handing work
    to the device) from a genuinely wedged server (which gains no CPU at all).
    """
    if not pids:
        return None
    script = ("Get-Process -Id " + ",".join(str(p) for p in pids) +
              " -ErrorAction SilentlyContinue | "
              "Measure-Object -Property CPU -Sum | "
              "Select-Object -ExpandProperty Sum")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", script],
                             capture_output=True, text=True, timeout=60).stdout.strip()
        return float(out) if out else None
    except (OSError, subprocess.SubprocessError, ValueError):
        return None


def restart_comfy():
    """Kill a wedged server and relaunch it. Returns True if it came back."""
    import subprocess as sp
    for pid in comfy_processes():
        try:
            sp.run(["taskkill", "/PID", str(pid), "/F"],
                   capture_output=True, timeout=30)
        except (OSError, sp.SubprocessError):
            pass
    time.sleep(6)
    try:
        sp.Popen(["cmd", "/c", COMFY_LAUNCHER],
                 stdout=sp.DEVNULL, stderr=sp.DEVNULL)
    except OSError:
        return False
    deadline = time.time() + 300
    while time.time() < deadline:
        time.sleep(10)
        try:
            with urllib.request.urlopen("http://127.0.0.1:8188/system_stats",
                                        timeout=15):
                return True
        except Exception:
            continue
    return False


class StallWatchdog(threading.Thread):
    """Restart ComfyUI when it stops making progress mid-asset.

    Without this, one unbuildable mesh costs the whole stage: the server wedges, every
    later asset fails for an unrelated reason, and somebody has to notice and intervene.
    The watchdog turns that into a logged restart and a single skipped asset.
    """

    def __init__(self, handle):
        super().__init__(daemon=True)
        self.handle = handle
        self.stop_event = threading.Event()
        self.fired = False

    def run(self):
        while not self.stop_event.wait(WATCH_INTERVAL):
            idle = log_idle_seconds()
            if idle is None or idle < STALL_SECONDS:
                continue
            pids = comfy_processes()
            if not pids:
                continue
            # Log silence alone does NOT mean stalled. A bake is a long GPU operation that
            # writes nothing to the log, so a busy server looks silent: one observation
            # showed 92% GPU utilisation and 31.6s of CPU time gained across 40s while the
            # log sat untouched. CPU time is the signal that separates working from wedged,
            # and a genuinely hung server gains no CPU because nothing is executing.
            before = comfy_cpu_seconds(pids)
            time.sleep(CPU_SAMPLE_SECONDS)
            after = comfy_cpu_seconds(comfy_processes())
            if before is not None and after is not None and after - before > 0.5:
                continue
            log(f"    watchdog: ComfyUI log idle {idle:.0f}s AND no CPU progress "
                f"({before} -> {after}) - restarting it", self.handle)
            self.fired = True
            if restart_comfy():
                log("    watchdog: ComfyUI is back; the stalled asset is lost, "
                    "continuing", self.handle)
                sound("attention", "HEY JOE, ComfyUI stalled and I restarted it")
            else:
                log("    watchdog: restart failed", self.handle)
                sound("attention", "HEY JOE, ComfyUI stalled and would not restart")
            return


def log(message, handle):
    stamp = time.strftime("%H:%M:%S")
    line = f"[{stamp}] {message}"
    print(line, flush=True)
    if handle is not None:
        handle.write(line + "\n")
        handle.flush()


def run_streamed(command, handle, label, ring_assets=False):
    """Run a command with output appended live to the log.

    Output is teed line by line rather than handed straight to the file, so a long stage
    can report individual completions as they happen. A captured pipe would hide hours of
    progress until the child exits, and handing the file to the child would hide it from
    us entirely.
    """
    log(f"    $ {' '.join(command)}", handle)
    started = time.time()
    process = subprocess.Popen(command, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, bufsize=1)
    for line in process.stdout:
        if handle is not None:
            handle.write(line)
            handle.flush()
        stripped = line.rstrip()
        if stripped:
            print(stripped, flush=True)
        # A verified asset is the moment something actually landed, so it is the right
        # thing to ring for. _make_assets.py prints this only for assets it genuinely
        # built; skipped assets take an earlier path and never reach it.
        if ring_assets and "verify" in line and "OK" in line and "INCOMPLETE" not in line:
            sound("asset")
    process.wait()
    elapsed = time.time() - started
    log(f"    exit={process.returncode} in {elapsed:.0f}s", handle)
    return process.returncode, elapsed


def sound(event, message="HEY JOE"):
    """Fire a notification without letting it ever break the run.

    A missing audio device or a failed speech call must not take down a batch, so this
    is deliberately fire-and-forget.
    """
    try:
        subprocess.Popen([PYTHON, NOTIFY, event, "--message", message],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    except OSError:
        pass


def concepts_done(request_path, concepts_dir):
    """Count how many of a request file's ids already have a concept on disk."""
    try:
        with open(request_path, encoding="utf-8") as handle:
            requests = json.load(handle)
    except (OSError, ValueError) as exc:
        return 0, 0, str(exc)
    have = sum(1 for request in requests
               if os.path.exists(os.path.join(concepts_dir, request["id"] + ".png")))
    return have, len(requests), None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--plan", required=True, help="Queue plan JSON")
    parser.add_argument("--log", default=os.path.join(ASSETS, "overnight.log"))
    parser.add_argument("--state", default=os.path.join(ASSETS, "overnight_state.json"))
    parser.add_argument("--budget-hours", type=float, default=10.0,
                        help="Stop starting new stages after this many hours")
    parser.add_argument("--resume", action="store_true",
                        help="Skip stages already recorded complete")
    parser.add_argument("--only", default=None, help="Run only stages whose name contains this")
    args = parser.parse_args()

    os.makedirs(os.path.dirname(args.log), exist_ok=True)
    state = {"started": time.strftime("%Y-%m-%d %H:%M:%S"), "stages": {}}
    if args.resume and os.path.exists(args.state):
        with open(args.state, encoding="utf-8") as handle:
            state = json.load(handle)

    deadline = time.time() + args.budget_hours * 3600

    def next_stage():
        """Re-read the plan each time so edits to the run order take effect on a
        queue that is already going. The plan is the orchestration document, and
        needing a restart to reorder stages would be a trap.

        Failed stages are skipped by default so one bad stage cannot stall the run,
        but --only re-runs a stage regardless, which is how you retry one.
        """
        # "partial" means an earlier run did some of the work and stopped on purpose;
        # it must not be picked up again ahead of stages that have not run at all,
        # or a deliberately deprioritised stage would jump the queue.
        #
        # Naming a stage with --only overrides that, and only "done" still blocks. A
        # supervisor retrying a partial stage by name previously matched nothing and the
        # queue exited reporting no work, so the stage could never be retried.
        settled = ("done",) if args.only else ("done", "partial")
        skipped = settled if args.only else settled + ("failed",)
        with open(args.plan, encoding="utf-8") as plan_handle:
            stages = json.load(plan_handle)["stages"]
        for candidate in stages:
            candidate_name = candidate["name"]
            if args.only and args.only.lower() not in candidate_name.lower():
                continue
            if state["stages"].get(candidate_name, {}).get("status") in skipped:
                continue
            return candidate, len(stages)
        return None, len(stages)

    with open(args.log, "a", encoding="utf-8") as handle:
        stage_count = len(json.load(open(args.plan, encoding="utf-8"))["stages"])
        log(f"=== queue start: {stage_count} stage(s), "
            f"budget {args.budget_hours}h ===", handle)

        while True:
            stage, stage_count = next_stage()
            if stage is None:
                log("--- no stages left to run", handle)
                break

            name = stage["name"]
            if time.time() > deadline:
                log(f"--- {name}: budget exhausted, stopping", handle)
                state["stopped"] = "budget"
                break

            log(f"--- {name}", handle)
            state["stages"][name] = {"status": "running",
                                     "started": time.strftime("%H:%M:%S")}
            with open(args.state, "w", encoding="utf-8") as state_handle:
                json.dump(state, state_handle, indent=2)

            kind = stage["kind"]
            code = 0
            elapsed = 0.0

            if kind == "concepts":
                requests = os.path.join(ASSETS, "requests", stage["requests"])
                have, total, error = concepts_done(requests, os.path.join(ASSETS, "concepts"))
                if error:
                    log(f"    cannot read {requests}: {error}", handle)
                    code = 1
                elif have >= total:
                    log(f"    all {total} concepts already present", handle)
                else:
                    log(f"    {have}/{total} already present, rendering the rest", handle)
                    code, elapsed = run_streamed(
                        [PYTHON, "-u", CONCEPTS, requests, "--skip-existing"], handle, name)

            elif kind == "assets3d":
                command = [PYTHON, "-u", ASSETS_TOOL,
                           "--faces", str(stage.get("faces", 25000)),
                           "--texture-size", str(stage.get("texture_size", 2048)),
                           # Resume safety: skip assets an earlier run finished, and
                           # give this stage a stable manifest name.
                           "--skip-existing",
                           "--run-id", re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_")]
                if stage.get("only"):
                    command.extend(["--only", stage["only"]])
                if stage.get("trellis2"):
                    command.append("--trellis2")
                if stage.get("lod_faces"):
                    command.extend(["--lod-faces", stage["lod_faces"]])
                # Rigging is cheap next to a build, so stages opt in rather than
                # every category acquiring a skeleton it has no use for.
                if stage.get("rig"):
                    command.append("--rig")
                # Quality knobs. This pipeline is not time-constrained, so stages can
                # ask for more sampler steps, bigger bakes and cleaner occlusion.
                if stage.get("steps"):
                    command.extend(["--steps", str(int(stage["steps"]))])
                if stage.get("bake_resolution"):
                    command.extend(["--bake-resolution", str(int(stage["bake_resolution"]))])
                if stage.get("ao_samples"):
                    command.extend(["--ao-samples", str(int(stage["ao_samples"]))])
                if stage.get("upsample_resolution"):
                    command.extend(["--upsample-resolution", str(int(stage["upsample_resolution"]))])
                # Rebuild passes re-run assets that already exist, to apply new settings.
                if stage.get("force"):
                    command.append("--force")
                # An optional per-stage cap bounds a category that cannot finish in
                # the remaining budget, so one long stage cannot eat the whole night.
                if stage.get("max_seconds"):
                    command.extend(["--max-seconds", str(int(stage["max_seconds"]))])
                # Asset stages talk to ComfyUI, so they are the ones that can wedge. The
                # watchdog restarts a stalled server instead of losing the whole stage.
                watchdog = StallWatchdog(handle)
                watchdog.start()
                try:
                    code, elapsed = run_streamed(command, handle, name, ring_assets=True)
                finally:
                    watchdog.stop_event.set()

            elif kind == "catalog":
                code, elapsed = run_streamed([PYTHON, "-u", CATALOG], handle, name)

            elif kind == "promptfiles":
                # Keep the LoadPromptsFromFile files in step with the request JSON,
                # so the GUI workflow and the queue render the same prompts.
                code, elapsed = run_streamed([PYTHON, "-u", PROMPTFILES], handle, name)

            else:
                log(f"    unknown stage kind {kind!r}", handle)
                code = 1

            state["stages"][name] = {
                "status": "done" if code == 0 else "failed",
                "exit": code,
                "seconds": round(elapsed, 1),
                "finished": time.strftime("%H:%M:%S"),
            }
            with open(args.state, "w", encoding="utf-8") as state_handle:
                json.dump(state, state_handle, indent=2)
            log(f"--- {name}: {'done' if code == 0 else 'FAILED'}", handle)

            # A stage boundary is rare enough to be worth hearing, and a failed stage is
            # one of the few things that genuinely wants Joe's attention.
            if code == 0:
                sound("stage")
            else:
                sound("attention", f"HEY JOE, stage {name} failed")

            # Refresh the summary after every stage so progress is visible during the
            # run, not only at the end. If the machine dies mid-run the last written
            # summary still describes what had completed.
            run_streamed([PYTHON, "-u", STATUS], handle, "status summary")

        # Final catalog and status so the morning review sees everything.
        if not state.get("stopped"):
            run_streamed([PYTHON, "-u", CATALOG], handle, "final catalog")
        run_streamed([PYTHON, "-u", STATUS], handle, "status summary")
        state["finished"] = time.strftime("%Y-%m-%d %H:%M:%S")
        with open(args.state, "w", encoding="utf-8") as state_handle:
            json.dump(state, state_handle, indent=2)
        log("=== queue finished ===", handle)
        sound("attention", "HEY JOE, the queue has finished")

    return 0


if __name__ == "__main__":
    sys.exit(main())
