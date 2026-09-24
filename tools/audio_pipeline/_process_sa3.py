"""Process the Stable Audio 3 candidates, run technical QA, and pick one per id provisionally.

Reuses `_process_audio.py` wholesale for the model-independent half - resampling, onset detection,
trimming, anti-phase-aware downmixing, loop crossfading, BS.1770 loudness targeting, spectral stats.
That code was written for Stable Audio Open and none of it depends on which model produced the file,
so rewriting it would only introduce new bugs.

What is specifically NOT inherited is any assumption about SA3's output. V1 needed a peak ceiling
because its saver wrote clipped masters and an anti-phase fallback because 44 of its mono sounds were
near-cancelling; SA3 clips nothing and emits dual-mono. Both behaviours are therefore **detected and
recorded per candidate** rather than assumed, and a V1 workaround never silently processes clean V2
audio. If SA3 ever does produce a cancelling pair, the existing detection catches it and the QA record
says so.

**Selection is deliberately not aesthetic.** The brief is explicit that technical QA must not claim a
sound is convincing, and must not pick a winner on spectral centroid. So the rules here are: reject
what is objectively broken, and among the survivors choose by the smallest duration error, with label
order as a deterministic tie-break. The centroid is recorded for the owner to read and is not a
ranking input. The chosen candidate is marked provisional_selection with human_auditioned false.

Usage:
    python _process_sa3.py --audit
    python _process_sa3.py --apply
"""
import argparse
import importlib.util
import io
import json
import os

import numpy as np
import soundfile as sf

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")
CANDIDATES = os.path.join(ASSETS, "audio", "v2_candidates")
DELIVERED = os.path.join(ASSETS, "audio", "v2_delivered")
QA_OUT = os.path.join(ASSETS, "manifests", "audio_qa_v2.json")

LABELS = ("a", "b", "c")

# Objective rejection thresholds. Each one is a condition under which a file is broken rather than
# merely unattractive, which is the only kind of judgement technical QA is allowed to make.
NEAR_SILENT_DBFS = -40.0
DURATION_TOLERANCE_S = 0.35      # beyond this the trim captured the wrong part of the render
MIN_ONSET_FRACTION = 0.02


def load_processor():
    path = os.path.join(TOOL_DIR, "_process_audio.py")
    spec = importlib.util.spec_from_file_location("_process_audio", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def reject_reasons(qa, entry):
    """Objective breakage only. Returns a list of reasons, empty when the candidate is usable."""
    reasons = []
    if qa.get("near_silent"):
        reasons.append("near_silent")
    if qa.get("clipped_samples", 0) > 0:
        reasons.append(f"clipped({qa['clipped_samples']})")
    if qa.get("channels") != entry["channels"]:
        reasons.append(f"channels {qa.get('channels')} != {entry['channels']}")
    if qa.get("sample_rate") != 48000:
        reasons.append(f"rate {qa.get('sample_rate')}")
    error = qa.get("duration_error_s")
    if error is not None and error > DURATION_TOLERANCE_S:
        reasons.append(f"duration_error {error}")
    if qa.get("peak_dbfs") is not None and qa["peak_dbfs"] < NEAR_SILENT_DBFS:
        reasons.append("peak_below_floor")
    if entry["loop"]:
        seam = qa.get("loop")
        if not isinstance(seam, dict) or seam.get("ratio") is None:
            reasons.append("loop_seam_unmeasured")
    return reasons


def rank_key(qa, label):
    """Transparent, non-aesthetic ordering. Duration fidelity first, then label order.

    Spectral centroid is deliberately absent: the brief forbids choosing on it, and a candidate is not
    better because its energy sits where the pipeline expected.
    """
    return (round(qa.get("duration_error_s") or 0.0, 4), LABELS.index(label))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--only", nargs="*", default=None)
    args = parser.parse_args()

    review = load_processor()
    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)

    entries = spec["sounds"]
    if args.only:
        wanted = set(args.only)
        entries = [e for e in entries if e["audio_id"] in wanted]

    results = {}
    ready = 0
    for entry in entries:
        audio_id = entry["audio_id"]
        produced = []
        for label in LABELS:
            path = os.path.join(CANDIDATES, audio_id, f"candidate_{label}.flac")
            if not os.path.exists(path):
                continue
            try:
                audio, rate, qa = review.process(entry, path)
            except (ValueError, OSError) as error:
                produced.append({"candidate": label, "ok": False,
                                 "reasons": [f"processing failed: {error}"]})
                continue
            reasons = reject_reasons(qa, entry)
            record = {
                "candidate": label,
                "ok": not reasons,
                "reasons": reasons,
                "qa": qa,
                "correlation": None,
            }
            raw, master_rate = sf.read(path, always_2d=True, dtype="float64")
            if raw.shape[1] > 1:
                left, right = raw[:, 0], raw[:, 1]
                if left.std() > 0 and right.std() > 0:
                    record["correlation"] = round(float(np.corrcoef(left, right)[0, 1]), 4)
            produced.append(record)
            if not reasons:
                ready += 1
                if args.apply:
                    target_dir = os.path.join(DELIVERED, audio_id)
                    os.makedirs(target_dir, exist_ok=True)
                    sf.write(os.path.join(target_dir, f"candidate_{label}.wav"), audio,
                             rate, subtype="PCM_24")

        survivors = [r for r in produced if r.get("ok")]
        chosen = None
        if survivors:
            chosen = min(survivors, key=lambda r: rank_key(r["qa"], r["candidate"]))
        results[audio_id] = {
            "candidates": produced,
            "survivors": [r["candidate"] for r in survivors],
            "provisional": chosen["candidate"] if chosen else None,
            "provisional_selection": bool(chosen),
            "human_auditioned": False,
        }

    total_candidates = sum(len(v["candidates"]) for v in results.values())
    total_survivors = sum(len(v["survivors"]) for v in results.values())
    no_survivor = [k for k, v in results.items() if not v["survivors"]]
    no_candidates = [k for k, v in results.items() if not v["candidates"]]

    print(f"  ids                  : {len(entries)}")
    print(f"  candidates present   : {total_candidates}")
    print(f"  technically passing  : {total_survivors}")
    print(f"  ids with no candidates yet: {len(no_candidates)}")
    print(f"  ids with no survivor : {len(no_survivor)}  {no_survivor[:4]}")
    tally = {}
    for value in results.values():
        for record in value["candidates"]:
            for reason in record["reasons"]:
                key = reason.split("(")[0]
                tally[key] = tally.get(key, 0) + 1
    print(f"  rejection reasons    : {tally or 'none'}")

    if not args.apply:
        print("\n  (audit only; pass --apply to write)")
        return 0

    document = {
        "version": 2,
        "model": "Stable Audio 3 Small SFX",
        "comment": [
            "Technical QA for the Stable Audio 3 pass. This records what measurement can establish:",
            "duration, onset, level, clipping, silence, channel policy, loop seam, spectral shape.",
            "",
            "It does NOT record whether a sound is convincing. Every entry carries",
            "human_auditioned: false until the owner listens, and provisional_selection marks the",
            "candidate an automated rule chose, which the owner may replace.",
        ],
        "counts": {"ids": len(entries), "candidates": total_candidates,
                   "passing": total_survivors, "ids_with_survivor":
                       len(entries) - len(no_survivor)},
        "sounds": results,
    }
    os.makedirs(os.path.dirname(QA_OUT), exist_ok=True)
    with io.open(QA_OUT, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")
    print(f"\n  delivered {DELIVERED}")
    print(f"  qa        {QA_OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
