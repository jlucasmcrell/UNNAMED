"""Apply the owner's listening report to the V2 manifest.

The audition page exports a report of which candidate the owner preferred for each sound, including
V1 where the old sound won. This turns that into the manifest the game reads, so the picks do not have
to be applied by hand across 228 entries - which is the kind of manual step where a handful of sounds
quietly end up on the wrong file.

Two things it refuses to do:

  It will not claim an audition that did not happen. Ids the report lists as undecided keep
  `human_auditioned: false`. A report where the owner listened to 40 sounds and exported must not
  produce a manifest asserting 228 were heard.

  It will not silently accept an unknown id or candidate. A typo in a hand-edited report would
  otherwise drop a sound or point it at a file that is not there.

Usage:
    python _apply_audition_report.py --report <path.json> --audit
    python _apply_audition_report.py --report <path.json> --apply
"""
import argparse
import io
import json
import os
import sys

ASSETS = r"W:\UNNAMED\assets"
MANIFEST = os.path.join(ASSETS, "manifests", "playable_prototype_audio_v2.json")
V1_DIR = os.path.join(ASSETS, "audio", "v1_stable_audio_open", "delivered")
DELIVERED = os.path.join(ASSETS, "audio", "v2_delivered")

VALID_KEYS = ("V1", "V2-A", "V2-B", "V2-C", "V2-D", "V2-E", "V2-F", "V2-G", "V2-H", "V2-I",
               "V2-J", "V2-K", "V2-L")

# The report's own marker for "I heard these and none of them work". It is a decision, not a
# non-decision: the sound was auditioned and rejected, so it counts as heard but needs new renders.
RERENDER = "NONE"


def find_v1(audio_id):
    for suffix in (".wav", ".flac"):
        path = os.path.join(V1_DIR, audio_id + suffix)
        if os.path.exists(path):
            return path
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True)
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    if not os.path.exists(args.report):
        print(f"  report not found: {args.report}")
        return 1
    with io.open(args.report, encoding="utf-8") as handle:
        report = json.load(handle)

    if report.get("kind") != "otherreach.audio.v2.audition-report":
        print(f"  not an audition report (kind={report.get('kind')!r})")
        return 1

    with io.open(MANIFEST, encoding="utf-8") as handle:
        manifest = json.load(handle)
    by_id = {s["audio_id"]: s for s in manifest["sounds"]}

    selections = report.get("selections", {})
    rejected = report.get("needs_rerender", [])
    unknown_ids = [k for k in list(selections) + list(rejected) if k not in by_id]
    bad_keys = [f"{k}={v}" for k, v in selections.items() if v not in VALID_KEYS]
    if unknown_ids:
        print(f"  unknown ids in report: {unknown_ids[:5]}")
        return 1
    if bad_keys:
        print(f"  invalid selection values: {bad_keys[:5]}")
        return 1

    # A V1 pick needs a V1 file to point at. The four declared aliases never had their own waveform,
    # so "keep V1" is not a choice that exists for them and a report saying so is a mistake worth
    # surfacing rather than writing into the manifest.
    v1_missing = [k for k, v in selections.items()
                  if v == "V1" and not find_v1(k)]
    if v1_missing:
        print(f"  'V1' chosen but no V1 file exists for: {v1_missing[:5]}")
        return 1

    missing_files = []
    for audio_id, key in selections.items():
        if key == "V1":
            continue
        path = os.path.join(DELIVERED, audio_id, f"candidate_{key[-1].lower()}.wav")
        if not os.path.exists(path):
            missing_files.append(f"{audio_id} -> {key}")
    if missing_files:
        print(f"  chosen candidate file not on disk: {missing_files[:5]}")
        return 1

    counts = {}
    for key in selections.values():
        counts[key] = counts.get(key, 0) + 1
    decided = len(selections) + len(rejected)
    undecided = len(manifest["sounds"]) - decided

    print(f"  report        : {args.report}")
    print(f"  generated     : {report.get('generated')}")
    print(f"  total ids     : {len(manifest['sounds'])}")
    print(f"  decided       : {decided}")
    print(f"  to re-render  : {len(rejected)}   (heard and rejected; new seeds needed)")
    print(f"  undecided     : {undecided}   (kept as provisional, human_auditioned false)")
    print(f"  by selection  : {counts}")
    print(f"  notes         : {len(report.get('notes', {}))}")

    if not args.apply:
        print("\n  (audit only; pass --apply to write)")
        return 0

    changed = 0
    for audio_id, entry in by_id.items():
        if audio_id in rejected:
            # Heard, rejected, and deliberately left un-assigned rather than pointed at the least-bad
            # candidate. The game keeps resolving it to the provisional pick until a re-render lands,
            # so nothing breaks in the meantime.
            entry["owner_selection"] = RERENDER
            entry["human_auditioned"] = True
            entry["needs_rerender"] = True
            changed += 1
            continue
        key = selections.get(audio_id)
        entry["needs_rerender"] = False
        if not key:
            entry["owner_selection"] = None
            entry["human_auditioned"] = False
            continue
        entry["owner_selection"] = key
        entry["human_auditioned"] = True
        if key == "V1":
            entry["delivered"] = os.path.join(
                "audio", "v1_stable_audio_open", "delivered",
                os.path.basename(find_v1(audio_id))).replace(os.sep, "/")
            entry["generation_version"] = "v1"
            entry["kept_v1"] = True
        else:
            label = key.split("-")[-1].lower()
            entry["delivered"] = os.path.join(
                "audio", "v2_delivered", audio_id, f"candidate_{label}.wav").replace(os.sep, "/")
            entry["generation_version"] = "v2"
            entry["kept_v1"] = False
            entry["selected_candidate"] = label
        changed += 1

    manifest["audition"] = {
        "report": os.path.basename(args.report),
        "generated": report.get("generated"),
        "decided": decided,
        "undecided": undecided,
        "needs_rerender": rejected,
        "counts_by_selection": counts,
        "notes": report.get("notes", {}),
        "human_auditioned": decided,
    }
    manifest["active_selection"] = "owner"

    with io.open(MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    print(f"\n  applied to {changed} sounds")
    print(f"  active_selection now 'owner'")
    print(f"  manifest {MANIFEST}")
    if undecided:
        print(f"  {undecided} sounds remain provisional and are marked human_auditioned false")
    return 0


if __name__ == "__main__":
    sys.exit(main())
