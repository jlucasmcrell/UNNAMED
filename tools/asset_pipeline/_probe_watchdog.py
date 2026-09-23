"""Test the stall watchdog's inputs directly.

The watchdog stayed silent through a 30-minute stall, so either its inputs are wrong or it
never ran. This prints what it actually sees, rather than reading the code and guessing.

Run:  python _probe_watchdog.py
"""
import importlib.util
import os
import time

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location(
    "oq", os.path.join(TOOL_DIR, "_overnight_queue.py"))
oq = importlib.util.module_from_spec(spec)
spec.loader.exec_module(oq)

print("STALL_SECONDS      =", oq.STALL_SECONDS)
print("WATCH_INTERVAL     =", oq.WATCH_INTERVAL)
print("CPU_SAMPLE_SECONDS =", oq.CPU_SAMPLE_SECONDS)

idle = oq.log_idle_seconds()
print("log_idle_seconds() =", idle)

pids = oq.comfy_processes()
print("comfy_processes()  =", pids)

if pids:
    before = oq.comfy_cpu_seconds(pids)
    print("comfy_cpu_seconds  =", before)
    time.sleep(oq.CPU_SAMPLE_SECONDS)
    after = oq.comfy_cpu_seconds(oq.comfy_processes())
    print("after %ds         = %s   (delta %s)" % (
        oq.CPU_SAMPLE_SECONDS, after,
        None if (before is None or after is None) else round(after - before, 2)))
else:
    print("!! no ComfyUI process found, so the watchdog skips every cycle and stays silent")

if idle is not None and idle > oq.STALL_SECONDS and not pids:
    print("!! the watchdog is silent because comfy_processes() is empty")
