"""Build one asset twice, under two settings, and report time and mesh cost.

The quality raise took median creature build time from ~200 s to ~2700 s, and stalls went
up with it. That is a large enough change to justify measuring what it actually buys
instead of assuming more is better. Both builds use the same concept image and the same
seed, so the only difference is the settings.

Usage:
    python _ab_settings.py --only creature_slime_marsh_frog
"""
import argparse
import importlib.util
import os
import shutil
import sys
import time

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("ma", os.path.join(TOOL_DIR, "_make_assets.py"))
ma = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ma)

VARIANTS = [
    ("lean", ["--faces", "25000", "--texture-size", "2048", "--steps", "12",
              "--bake-resolution", "2048", "--ao-samples", "64",
              "--upsample-resolution", "1024", "--lod-faces", "8000,2500,600"]),
    ("hq",   ["--faces", "40000", "--texture-size", "4096", "--steps", "30",
              "--bake-resolution", "4096", "--ao-samples", "256",
              "--upsample-resolution", "1024", "--lod-faces", "12000,4000,1000"]),
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", required=True)
    parser.add_argument("--out", default=r"W:\UNNAMED\assets\review\ab_settings")
    args = parser.parse_args()

    os.makedirs(args.out, exist_ok=True)
    results = []
    for label, flags in VARIANTS:
        # Build into a scratch tree so nothing overwrites the real asset.
        scratch = os.path.join(args.out, label)
        shutil.rmtree(scratch, ignore_errors=True)
        os.makedirs(scratch, exist_ok=True)
        command = [sys.executable, os.path.join(TOOL_DIR, "_make_assets.py"),
                   r"W:\UNNAMED\assets\concepts",
                   "--out", scratch, "--run-id", f"ab_{label}"]
        command += flags
        if args.only:
            command += ["--only", args.only]
        print(f"=== {label}: {' '.join(flags)}", flush=True)
        started = time.time()
        code = os.system(" ".join(f'"{c}"' if " " in c else c for c in command))
        elapsed = time.time() - started
        asset_dir = os.path.join(scratch, "ready", args.only)
        size = 0
        if os.path.isdir(asset_dir):
            base = os.path.join(asset_dir, f"{args.only}.glb")
            if os.path.exists(base):
                size = os.path.getsize(base)
        results.append((label, elapsed, code, size, os.path.isdir(asset_dir)))
        print(f"    {label}: {elapsed:.0f}s  exit={code}  base GLB {size/1e6:.1f} MB", flush=True)

    print()
    print(f"{'variant':<8} {'seconds':>9} {'base MB':>9}  built")
    for label, elapsed, code, size, built in results:
        print(f"{label:<8} {elapsed:>9.0f} {size/1e6:>9.1f}  {built}")
    if len(results) == 2 and results[0][1] > 0:
        ratio = results[1][1] / results[0][1]
        print(f"\nHQ costs {ratio:.1f}x the time of lean for {results[1][3]/max(results[0][3],1):.1f}x the file size")
    return 0


if __name__ == "__main__":
    sys.exit(main())
