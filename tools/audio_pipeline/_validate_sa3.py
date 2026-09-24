"""Validate the processed Stable Audio 3 set and write the V2 manifest.

Covers the brief's sections 24 and 25: validate every provisional output, and version the manifest so
V1's history survives and the active playable selection is explicit.

V1's `_audio_validate.py` is not reused directly because it reads V1's spec, QA and generation state
and writes V1's manifest - running it would overwrite the record of what V1 shipped. The measurement
helpers it uses live in `_process_audio.py` and are imported from there, so the numbers are computed by
the same code.

The Godot half writes `audio_manifest.json` (what `validate_audio.gd` reads) and stages the delivered
files, then Godot imports them and reports. Godot's own importer is the authority on whether the engine
can load these; this script only prepares and reports.

Usage:
    python _validate_sa3.py --audit
    python _validate_sa3.py --apply          # write manifest and stage for Godot
    python _validate_sa3.py --apply --godot  # also run the Godot validation
"""
import argparse
import importlib.util
import io
import json
import os
import shutil
import subprocess

import numpy as np
import soundfile as sf

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v2.json")
QA = os.path.join(ASSETS, "manifests", "audio_qa_v2.json")
MODELS = os.path.join(ASSETS, "manifests", "audio_models_v2.json")
DELIVERED = os.path.join(ASSETS, "audio", "v2_delivered")
OUT = os.path.join(ASSETS, "manifests", "playable_prototype_audio_v2.json")

GODOT_PROJECT = os.path.join(r"W:\UNNAMED\tools", "godot_validate")
GODOT_STAGED = os.path.join(GODOT_PROJECT, "assets", "audio")
# `validate_audio.gd` reads `res://assets/audio_manifest.json`. Writing anywhere else leaves it reading
# whatever manifest was there before, and it then reports a clean pass over files that are not the
# ones under test - which is exactly what happened on the first run: it validated V1's 173 staged files
# and reported ok, while the V2 set was sitting beside them unread.
GODOT_MANIFEST = os.path.join(GODOT_PROJECT, "assets", "audio_manifest.json")
GODOT = os.environ.get(
    "UNNAMED_GODOT", r"W:\UNNAMED\tools\godot\Godot_v4.7.2-stable_win64_console.exe")

TARGET_RATE = 48000
CLIP_THRESHOLD = 0.999
SILENCE_DBFS = -60.0


def load_processor():
    path = os.path.join(TOOL_DIR, "_process_audio.py")
    spec = importlib.util.spec_from_file_location("_process_audio", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--godot", action="store_true")
    args = parser.parse_args()

    review = load_processor()
    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    with io.open(QA, encoding="utf-8") as handle:
        qa = json.load(handle)["sounds"]
    models = {}
    if os.path.exists(MODELS):
        with io.open(MODELS, encoding="utf-8") as handle:
            models = json.load(handle)

    # Clear the staging directory before measuring, not after. It held V1's 173 files from the
    # previous sprint, and staging V2 alongside them leaves 401 wavs in one folder where the
    # validator cannot tell which set it is checking. V1's audio is preserved in the archive.
    # This has to run before the loop below, which is what stages the V2 files.
    if args.apply and os.path.isdir(GODOT_STAGED):
        for name in os.listdir(GODOT_STAGED):
            if name.endswith((".wav", ".import")):
                os.remove(os.path.join(GODOT_STAGED, name))

    sounds = []
    problems = []
    staged = 0
    for entry in spec["sounds"]:
        audio_id = entry["audio_id"]
        record = qa.get(audio_id, {})
        label = record.get("provisional")
        if not label:
            problems.append(f"{audio_id}: no technically valid candidate")
            continue
        path = os.path.join(DELIVERED, audio_id, f"candidate_{label}.wav")
        if not os.path.exists(path):
            problems.append(f"{audio_id}: provisional candidate {label} not delivered")
            continue

        data, rate = sf.read(path, always_2d=True, dtype="float64")
        mono = data.mean(axis=1) if data.shape[1] > 1 else data[:, 0]
        peak = float(np.max(np.abs(data))) if data.size else 0.0
        lufs = review.integrated_lufs(mono, rate)
        if lufs is None:
            lufs = review.k_weighted_rms_dbfs(mono, rate)
        centroid, rolloff = review.spectral_stats(data, rate)

        if rate != TARGET_RATE:
            problems.append(f"{audio_id}: rate {rate}")
        if data.shape[1] != entry["channels"]:
            problems.append(f"{audio_id}: {data.shape[1]} channels, spec says {entry['channels']}")
        if peak <= 0 or 20 * np.log10(max(peak, 1e-12)) < SILENCE_DBFS:
            problems.append(f"{audio_id}: silent")
        if int(np.sum(np.abs(data) >= CLIP_THRESHOLD)):
            problems.append(f"{audio_id}: clipped")

        candidate_qa = next((c for c in record.get("candidates", [])
                             if c["candidate"] == label), {})
        detail = candidate_qa.get("qa", {})
        sounds.append({
            "audio_id": audio_id,
            "category": entry["category"],
            "group": entry["group"],
            "gameplay_role": entry["gameplay_role"],
            "generation_version": "v2",
            "source": entry.get("source", "replacement"),
            "provisional_selection": True,
            "human_auditioned": False,
            "selected_candidate": label,
            "candidates": record.get("survivors", []),
            "delivered": os.path.join("audio", "v2_delivered", audio_id,
                                      f"candidate_{label}.wav").replace(os.sep, "/"),
            "prompt": entry["prompt"],
            "prompt_v1": entry.get("prompt_v1"),
            "model": "Stable Audio 3 Small SFX",
            "model_sha256": next((f["sha256"] for f in models.get("files", [])
                                  if "small_sfx" in f["path"]), None),
            "text_encoder": "t5gemma-b-b-ul2",
            "text_encoder_sha256": next((f["sha256"] for f in models.get("files", [])
                                         if "t5gemma" in f["path"]), None),
            "settings": {"steps": 8, "cfg": 1.0, "sampler": "lcm", "scheduler": "simple",
                         "negative_prompt": None},
            "seconds": entry["seconds"],
            "delivered_seconds": detail.get("delivered_seconds"),
            "channels": entry["channels"],
            "loop": entry["loop"],
            "sample_rate": rate,
            "subtype": "PCM_24",
            "measured_lufs": None if lufs is None else round(float(lufs), 2),
            "target_lufs": entry["loudness_lufs"],
            "peak_dbfs": round(20 * np.log10(max(peak, 1e-12)), 2) if peak > 0 else None,
            "spectral_centroid_hz": None if centroid is None else round(float(centroid), 1),
            "spectral_rolloff85_hz": None if rolloff is None else round(float(rolloff), 1),
            "downmix": detail.get("downmix"),
            "gain_limited_by_peak": detail.get("gain_limited_by_peak"),
            "clipped_samples": int(np.sum(np.abs(data) >= CLIP_THRESHOLD)),
            "near_silent": False,
            "godot_validated": False,
        })
        if args.apply:
            # `validate_audio.gd` resolves `res://assets/audio/<audio_id>.wav` from the raw id, dots
            # included. Sanitising the name produces files the validator cannot find, and it then
            # reports every sound as "file not staged" rather than as a naming mistake.
            shutil.copy2(path, os.path.join(GODOT_STAGED, audio_id + ".wav"))
            staged += 1

    passing = len(sounds)
    print(f"  ids in spec      : {len(spec['sounds'])}")
    print(f"  provisional set  : {passing}")
    print(f"  problems         : {len(problems)}  {problems[:4]}")
    print(f"  candidates total : {sum(len(v.get('candidates', [])) for v in qa.values())}")
    print(f"  peak-limited     : {sum(1 for s in sounds if s['gain_limited_by_peak'])}")
    print(f"  stereo           : {sum(1 for s in sounds if s['channels'] == 2)}")
    print(f"  loops            : {sum(1 for s in sounds if s['loop'])}")

    if not args.apply:
        print("\n  (audit only; pass --apply to write and stage)")
        return 0


    document = {
        "version": 2,
        "active_selection": "v2",
        "comment": [
            "V2 of the Phase-1 audio set, generated with Stable Audio 3 Small SFX.",
            "",
            "V1 (Stable Audio Open 1.0) is preserved in full at assets/audio/v1_stable_audio_open/",
            "with its own manifest, and its provenance record is untouched - nothing here replaces or",
            "mutates V1's history. The game-facing ids are identical between the two, so the event",
            "contract does not move and gameplay resolves either version.",
            "",
            "Every entry is a PROVISIONAL selection: an automated non-aesthetic rule chose between",
            "technically valid candidates. human_auditioned is false for all of them. The owner may",
            "replace any pick after listening to assets/review/audio_v2/index.html.",
        ],
        "authority": spec.get("authority"),
        "models": {
            "generation": {
                "name": "Stable Audio 3 Small SFX",
                "checkpoint": "stable_audio_3_small_sfx.safetensors",
                "repo": "Comfy-Org/stable-audio-3 (repackaging of stabilityai/stable-audio-3-small-sfx)",
                "license": "Stability AI Community License",
                "redistributed_components": ["Gemma components under the Gemma Terms of Use"],
            },
            "text_encoder": {"name": "t5gemma-b-b-ul2", "repo": "Comfy-Org/stable-audio-3"},
        },
        "settings": {"steps": 8, "cfg": 1.0, "sampler": "lcm", "scheduler": "simple",
                     "negative_prompt": None,
                     "note": "Established from ComfyUI's official SA3 blueprint, not carried over "
                             "from V1. The negative prompt is absent because the probe showed it is "
                             "inert at cfg 1.0."},
        "production_format": {"container": "WAV", "subtype": "PCM_24", "sample_rate": 48000,
                              "note": "Masters are retained as the raw 44.1 kHz FLAC the generator "
                                      "produced. Nothing lossy is re-encoded."},
        "counts": {
            "total": len(sounds),
            "replacement": sum(1 for s in sounds if s["source"] == "replacement"),
            "new": sum(1 for s in sounds if s["source"] == "new"),
        },
        "validation": {
            "ids_with_provisional": passing,
            "problems": problems,
            "human_auditioned": 0,
        },
        "sounds": sounds,
    }
    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")
    print(f"\n  manifest : {OUT}")
    print(f"  staged   : {staged} files -> {GODOT_STAGED}")

    godot_manifest = {
        "sample_rate": TARGET_RATE,
        # Only the fields `validate_audio.gd` reads. It derives the file path from `audio_id`, so a
        # `file` field here would be dead weight that looks authoritative.
        "sounds": [{"audio_id": s["audio_id"],
                    "channels": s["channels"],
                    "seconds": s["delivered_seconds"] or s["seconds"],
                    "loop": s["loop"]} for s in sounds],
    }
    with io.open(GODOT_MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(godot_manifest, handle, indent=2)
        handle.write("\n")
    print(f"  godot manifest: {GODOT_MANIFEST}")

    if args.godot:
        run_godot()
    return 0


def run_godot():
    if not os.path.exists(GODOT):
        print(f"  Godot not found at {GODOT}; skipping")
        return 1
    # Import first. `ResourceLoader.exists()` resolves an imported resource, not a loose .wav, so a
    # freshly staged file reports "file not staged" until Godot has scanned it and written .import
    # beside it. Without this the validator reports a total failure that looks like a staging bug.
    subprocess.run([GODOT, "--headless", "--path", GODOT_PROJECT, "--import"],
                   capture_output=True, text=True, timeout=900)
    result = subprocess.run(
        [GODOT, "--headless", "--path", GODOT_PROJECT, "--script", "validate_audio.gd"],
        capture_output=True, text=True, timeout=900)
    for line in result.stdout.splitlines():
        if line.startswith("AUDIO_RESULT "):
            report = json.loads(line[len("AUDIO_RESULT "):])
            print(f"\n  GODOT: ok={report.get('ok')} sounds={report.get('sounds')} "
                  f"imported={report.get('imported')} mono={report.get('mono')} "
                  f"stereo={report.get('stereo')}")
            for problem in report.get("problems", [])[:10]:
                print(f"     {problem}")
            return 0
    print(f"  no AUDIO_RESULT. stdout tail: {result.stdout[-400:]!r}")
    print(f"  stderr tail: {result.stderr[-400:]!r}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
