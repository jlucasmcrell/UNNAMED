"""Watch ComfyUI's host memory and restart it before it exhausts the machine.

ComfyUI accumulates committed host memory across a long run. One observed server reached
**244 GB committed on a 64 GB machine** after 5.5 hours and 66 built assets, still growing
at ~160 MB/s, with the 192 GB page file 96% consumed. It then failed with:

    DefaultCPUAllocator: not enough memory: you tried to allocate 2147483648 bytes

That error is misleading: the 2 GB allocation is a *consequence*. The Trellis2 NAF
high-resolution path pre-allocates its output on `out_device`, and under memory pressure
that device falls back to CPU - so a GPU operation starts asking the host for 2 GB it does
not have.

A restart clears it, and `_run_3d_asset.py` already detects a restarted server and abandons
the lost prompt quickly, so a restart costs one asset rather than a stall.

Usage:
    python _comfy_mem_guard.py --limit-gb 40 --check-seconds 60

Exit codes: 0 healthy, 1 restarted (or failed to).
"""
import argparse
import os
import subprocess
import sys
import time

LAUNCHER = os.environ.get("UNNAMED_COMFY_LAUNCHER",
                          r"C:\Users\jluca\Desktop\start-comfyui.bat")


def comfy_pids():
    script = ("Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
              "Where-Object { $_.CommandLine -like '*main.py*' } | "
              "Select-Object -ExpandProperty ProcessId")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", script],
                             capture_output=True, text=True, timeout=60).stdout
    except (OSError, subprocess.SubprocessError):
        return []
    return [int(x) for x in out.split() if x.isdigit()]


def committed_gb(pids):
    """Sum of private committed bytes across the given processes, in GB."""
    if not pids:
        return None
    ids = ",".join(str(p) for p in pids)
    script = (f"Get-Process -Id {ids} -ErrorAction SilentlyContinue | "
              "Measure-Object -Property PrivateMemorySize64 -Sum | "
              "Select-Object -ExpandProperty Sum")
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command", script],
                             capture_output=True, text=True, timeout=60).stdout.strip()
        return float(out) / 1e9 if out else None
    except (OSError, subprocess.SubprocessError, ValueError):
        return None


def restart():
    for pid in comfy_pids():
        subprocess.run(["taskkill", "/PID", str(pid), "/F"],
                       capture_output=True, timeout=30)
    time.sleep(6)
    subprocess.Popen(["cmd", "/c", LAUNCHER],
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.time() + 300
    import urllib.request
    while time.time() < deadline:
        time.sleep(10)
        try:
            urllib.request.urlopen("http://127.0.0.1:8188/system_stats", timeout=15)
            return True
        except Exception:
            continue
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--limit-gb", type=float, default=40.0)
    parser.add_argument("--check-seconds", type=float, default=60.0)
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--log", default=None)
    args = parser.parse_args()

    def note(message):
        line = f"{time.strftime('%H:%M:%S')}  {message}"
        print(line, flush=True)
        if args.log:
            with open(args.log, "a", encoding="utf-8") as handle:
                handle.write(line + "\n")

    while True:
        gb = committed_gb(comfy_pids())
        if gb is None:
            note("mem-guard: ComfyUI not running")
        elif gb > args.limit_gb:
            note(f"mem-guard: ComfyUI committed {gb:.1f} GB (limit {args.limit_gb} GB) "
                 f"- restarting before it exhausts host memory")
            if restart():
                note("mem-guard: restarted; the in-flight asset is lost, continuing")
            else:
                note("mem-guard: restart FAILED")
                return 1
        else:
            note(f"mem-guard: committed {gb:.1f} GB, healthy")
        if args.once:
            return 0
        time.sleep(args.check_seconds)


if __name__ == "__main__":
    sys.exit(main())
