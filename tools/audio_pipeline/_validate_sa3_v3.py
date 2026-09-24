"""Stage the V3 set for Godot and validate that the engine can import every file.

`_validate_sa3.py` points at V2 and writes V2's manifest; running it would leave V3 unvalidated while
reporting a pass. This is the same job against the V3 paths, so the delivered set is proven loadable
rather than assumed.

The staging directory is cleared first. It held V2's 228 files, which have the same ids and therefore
the same filenames - staging V3 over them without clearing would leave a mixture, and the earlier V2
run already showed what that costs: Godot read a stale manifest, validated the wrong set, and reported
a clean pass.

Usage:
    python _validate_sa3_v3.py --apply
    python _validate_sa3_v3.py --apply --godot
"""
import argparse
import io
import json
import os
import shutil
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v3.json")
MANIFEST = os.path.join(ASSETS, "manifests", "playable_prototype_audio_v3.json")
DELIVERED = os.path.join(ASSETS, "audio", "v3_delivered")

GODOT_PROJECT = os.path.join(r"W:\UNNAMED\tools", "godot_validate")
GODOT_STAGED = os.path.join(GODOT_PROJECT, "assets", "audio")
GODOT_MANIFEST = os.path.join(GODOT_PROJECT, "assets", "audio_manifest.json")
GODOT = os.environ.get(
    "UNNAMED_GODOT", r"W:\UNNAMED\tools\godot\Godot_v4.7.2-stable_win64_console.exe")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--godot", action="store_true")
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)

    missing = [e["audio_id"] for e in spec["sounds"]
               if not os.path.exists(os.path.join(DELIVERED, e["audio_id"] + ".wav"))]
    print(f"  ids            : {len(spec['sounds'])}")
    print(f"  missing files  : {len(missing)}  {missing[:4]}")

    if not args.apply:
        print("\n  (audit only; pass --apply to stage)")
        return 0

    if os.path.isdir(GODOT_STAGED):
        removed = 0
        for name in os.listdir(GODOT_STAGED):
            if name.endswith((".wav", ".import")):
                os.remove(os.path.join(GODOT_STAGED, name))
                removed += 1
        print(f"  cleared {removed} files from the previous staging")
    os.makedirs(GODOT_STAGED, exist_ok=True)

    # Godot caches imported resources under .godot/imported and keys them by the source path. Replacing
    # a wav without dropping the cache leaves a stale entry, and the engine then reports the file as
    # unimportable while `--import` claims success. Clearing only the staged wavs is not enough.
    cache = os.path.join(GODOT_PROJECT, ".godot", "imported")
    if os.path.isdir(cache):
        shutil.rmtree(cache, ignore_errors=True)
        print("  cleared the Godot import cache")

    staged = 0
    for entry in spec["sounds"]:
        audio_id = entry["audio_id"]
        source = os.path.join(DELIVERED, audio_id + ".wav")
        if not os.path.exists(source):
            continue
        # `validate_audio.gd` resolves `res://assets/audio/<audio_id>.wav` from the raw id.
        shutil.copy2(source, os.path.join(GODOT_STAGED, audio_id + ".wav"))
        staged += 1
    print(f"  staged         : {staged} V3 files")

    godot_manifest = {
        "sample_rate": 48000,
        "sounds": [{"audio_id": e["audio_id"], "channels": e["channels"],
                    "seconds": e["seconds"], "loop": e["loop"]} for e in spec["sounds"]],
    }
    with io.open(GODOT_MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(godot_manifest, handle, indent=2)
        handle.write("\n")
    print(f"  manifest       : {GODOT_MANIFEST}")

    if args.godot:
        return run_godot()


def run_godot():
    if not os.path.exists(GODOT):
        print(f"  Godot not found at {GODOT}")
        return 1
    # Import first: ResourceLoader.exists() resolves an imported resource, not a loose wav.
    subprocess.run([GODOT, "--headless", "--path", GODOT_PROJECT, "--import"],
                   capture_output=True, text=True, timeout=1200)
    result = subprocess.run(
        [GODOT, "--headless", "--path", GODOT_PROJECT, "--script", "validate_audio.gd"],
        capture_output=True, text=True, timeout=1200)
    for line in result.stdout.splitlines():
        if line.startswith("AUDIO_RESULT "):
            report = json.loads(line[len("AUDIO_RESULT "):])
            print(f"\n  GODOT: ok={report.get('ok')} sounds={report.get('sounds')} "
                  f"imported={report.get('imported')} mono={report.get('mono')} "
                  f"stereo={report.get('stereo')} looped={report.get('looped')}")
            for key in ("rate_mismatch", "channel_mismatch", "length_mismatch",
                        "loop_failures", "clipped", "silent"):
                print(f"     {key:>16}: {report.get(key)}")
            for problem in report.get("problems", [])[:10]:
                print(f"     {problem}")
            return 0 if report.get("ok") else 1
    print(f"  no AUDIO_RESULT. stdout tail: {result.stdout[-400:]!r}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
