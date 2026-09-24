"""Remove the round-2 candidates I rendered from a synthetic test report.

Those six candidates were produced to prove the re-render path works, not because the owner asked for
them. Leaving them in place would show two unrelated sounds with seven versions on the audition page
and imply a rejection that never happened.

Usage:
    python _cleanup_test_round2.py --apply
"""
import argparse
import glob
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
CANDIDATES = os.path.join(ASSETS, "audio", "v2_candidates")
DELIVERED = os.path.join(ASSETS, "audio", "v2_delivered")
REVIEW = os.path.join(ASSETS, "review", "audio_v2")
STATE = os.path.join(ASSETS, "sa3_generate_state.json")

IDS = ["sfx.ui.error", "sfx.magic.strain.high.01"]
LABELS = ["d", "e", "f"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    doomed = []
    for audio_id in IDS:
        for label in LABELS:
            doomed.append(os.path.join(CANDIDATES, audio_id, "candidate_%s.flac" % label))
            doomed.append(os.path.join(DELIVERED, audio_id, "candidate_%s.wav" % label))
        for path in glob.glob(os.path.join(REVIEW, "*", audio_id, "V2-[DEF].flac")):
            doomed.append(path)

    present = [p for p in doomed if os.path.exists(p)]
    print("  test round-2 files present: %d" % len(present))
    for path in present:
        print("    " + os.path.relpath(path, ASSETS))

    if not args.apply:
        print("\n  (audit only; pass --apply to remove)")
        return 0

    for path in present:
        os.remove(path)
    with io.open(STATE, encoding="utf-8") as handle:
        state = json.load(handle)
    for audio_id in IDS:
        for label in LABELS:
            state["candidates"].pop("%s#%s" % (audio_id, label), None)
    with io.open(STATE, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=2)
        handle.write("\n")
    print("  removed %d files and their state entries" % len(present))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
