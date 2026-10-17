"""Take one screenshot per room with --shot, the cheap path.

`--survey` does thirteen stations at three standpoints each and measured 8-16 minutes per standpoint here -
6 of ~39 frames in 31 minutes. `--shot "x,z,yaw_deg[,pitch][,seconds]"` does one place at three standpoints
and returns in well under a minute, so thirteen runs is the practical way to photograph a blockout.

The GPU lock is held once for the whole batch and released at the end. Not a --survey, and not a substitute
for one: this is the blockout's record, and a final pass after P3's art kit is a different job.
"""
import io
import os
import subprocess
import time

ROOT = r"C:\Otherreach_Build\barrowworks_wt"
GODOT = r"C:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
LOCK = r"C:\Otherreach_Recovery\gpu_lock.ps1"
OUT = r"C:\Otherreach_Build\barrowworks_shots\perroom"
OWNER = "deepseek barrowworks P2 per-room shots"
# The art root and the bindings. Without them the rooms render as greybox boxes, which is what the first pass
# through this script did - a picture of the blockout, not of the ring.
ASSET_ROOT = r"C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary"

# Room centres in the blockout's own metres, and the way to face: north is +Z, which looks on towards the
# next room along the critical path.
ROOMS = [
    ("R01_threshold", 50, 10, 0),
    ("R02_intake_hall", 50, 28, 0),
    ("R03_wash_passage", 28, 38, 0),
    ("R04_drain_mouth", 8, 38, 0),
    ("R05_sorting_floor", 28, 60, 0),
    ("R06_still_bay", 50, 60, 0),
    ("R07_gauge_archive", 72, 60, 0),
    ("R08_return_gallery", 72, 38, 180),
    ("R09_blind_store", 92, 60, 270),
    ("R10_service_vestibule", 50, 84, 0),
    ("R11_measuring_court", 50, 106, 0),
    ("R12_headworks", 50, 131, 0),
    ("R13_inspection_bay", 72, 84, 270),
]

os.makedirs(OUT, exist_ok=True)


def lock(action):
    subprocess.run(["powershell", "-ExecutionPolicy", "Bypass", "-File", LOCK, action, OWNER],
                   capture_output=True, text=True)


# A caller that already holds the GPU lock runs this with BARROWWORKS_SKIP_LOCK=1. Without it the tool asks for
# the lock itself, which deadlocks against a holder inside this same batch - and a batch that waited on itself sat
# for forty minutes producing nothing. BARROWWORKS_ONLY=Name,Name limits the run to those rooms.
if os.environ.get("BARROWWORKS_SKIP_LOCK") == "1":
    print(f"  running under the caller's lock (this tool did not acquire one)")
else:
    lock("acquire")
    print(f"  lock acquired by: {OWNER}")
ROOMS = [r for r in ROOMS if not os.environ.get("BARROWWORKS_ONLY") or r[0] in os.environ["BARROWWORKS_ONLY"].split(",")]
started = time.time()
done, failed = [], []
try:
    for name, x, z, yaw in ROOMS:
        room_dir = os.path.join(OUT, name)
        os.makedirs(room_dir, exist_ok=True)
        log = os.path.join(room_dir, "run.log")
        began = time.time()
        try:
            with io.open(log, "w", encoding="utf-8", errors="replace") as handle:
                subprocess.run(
                    [GODOT, "--path", os.path.join(ROOT, "src", "Presentation"), "--",
                     "--region", "region.barrowworks",
                     "--content-root", os.path.join(ROOT, "content"),
                     "--asset-root", ASSET_ROOT,
                     "--bindings-overlay", os.path.join(ROOT, "content", "_art_bindings_barrowworks.json"),
                     "--visual", "barrowworks=1",
                     # The sixth field is the standoff scale. The harness's 9 m and 26 m are landscape-scale and
                     # walk the character straight through a room's wall; 0.15 gives 0.03 / 1.35 / 3.9 m, which
                     # fits a chamber 10 m across.
                     "--shot", f"{x},{z},{yaw},-0.08,3,0.15",
                     "--shot-out", room_dir],
                    stdout=handle, stderr=subprocess.STDOUT, timeout=900)
        except subprocess.TimeoutExpired:
            # A room whose standpoints the character cannot walk to spins rather than failing, and a room it
            # walks to slowly does the same for longer. R02 did the first in P2. In P3 R03 took 534 s while the
            # other twelve took 58-94 s - so at a 120 s cap the CAP was the bug, and R03 was skipped for being
            # slow rather than for being unreachable. 900 s is fifteen minutes: past every room measured, and
            # short enough that a batch still finishes.
            print(f"  {name:24} TIMED OUT after 900s - skipped")
            failed.append(name)
            continue
        shots = [f for f in os.listdir(room_dir) if f.endswith(".png")]
        took = time.time() - began
        if shots:
            done.append(name)
            print(f"  {name:24} {len(shots)} frames  {took:5.0f}s")
        else:
            failed.append(name)
            print(f"  {name:24} NO FRAMES  {took:5.0f}s  (see {log})")
finally:
    # Only release what this run took. gpu_lock.ps1 matches the owner by its first word, so a release from
    # here while running under a caller lock would drop THAT lock mid-render - which it did once.
    if os.environ.get("BARROWWORKS_SKIP_LOCK") != "1":
        lock("release")
        print(f"  lock released")

print(f"\n  {len(done)} of {len(ROOMS)} rooms photographed in {(time.time()-started)/60:.1f} min")
if failed:
    print(f"  no frames for: {', '.join(failed)}")
