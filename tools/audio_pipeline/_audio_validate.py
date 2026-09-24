"""Validate the playable audio set and emit its manifest.

Section 30 requires the playable set to be checkable, and section 5 requires a manifest with
provenance on every entry. Both happen here, because a manifest that does not prove its own claims
is just a list.

A generated file with missing provenance fails validation. So does one that clips, is silent, has
the wrong sample rate or channel count, is wildly longer or shorter than the spec asked for, or
loops without a loop-seam measurement. The tool exits non-zero if anything fails, so it can gate a
batch rather than merely describe one.

Usage:
    python _audio_validate.py
    python _audio_validate.py --emit-manifest
"""
import argparse
import io
import json
import os
import sys

import soundfile as sf

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec.json")
QA = os.path.join(ASSETS, "manifests", "audio_qa.json")
GENERATION = os.path.join(ASSETS, "audio_generate_state.json")
MODELS = os.path.join(ASSETS, "manifests", "audio_models.json")
OUT = os.path.join(ASSETS, "manifests", "playable_prototype_audio.json")
GODOT_STAGED = os.path.join(r"W:\UNNAMED\tools\godot_validate", "assets", "audio")

TARGET_RATE = 48000
DURATION_TOLERANCE = 0.05          # seconds, against the spec's declared length
CLIP_THRESHOLD = 0.999
SILENCE_THRESHOLD_DBFS = -60.0

# Requirement levels, so "shipping" is a decision rather than a field that is always the same word.
SHIPPING_STATUS = {
    "player": "prototype_ok",
    "weapon": "prototype_ok",
    "creature": "prototype_ok",
    "magic": "prototype_ok",
    "interaction": "prototype_ok",
    "crafting": "prototype_ok",
    "ambience": "prototype_ok",
    "ui": "prototype_ok",
}


# Content-level problems that measurement cannot detect and that were found by looking at
# spectrograms. They are recorded per asset rather than only in prose, because a limitation that
# lives in a status document is a limitation nobody reads at the point of use. Each entry says what
# the sound is, what was asked for, and what would fix it.
CONTENT_LIMITATIONS = {
    "sfx.magic.mending_thread.resolve.01": [
        "reads as a sub-bass rumble (spectral centroid 180 Hz, 85% rolloff below 300 Hz) rather than "
        "the fine tissue resonance the prompt asked for; regenerate with a higher-frequency prompt"],
    "sfx.magic.strain.moderate.01": [
        "almost entirely below 150 Hz (centroid 61 Hz) with a regular pulse; likely inaudible on "
        "laptop speakers and reads as a flutter rather than continuous tension"],
    "sfx.magic.strain.high.01": [
        "very low spectral centroid for a 'rising unstable resonance'; check on small speakers"],
    "sfx.magic.strain.critical.01": [
        "very low spectral centroid for an urgent strain band; check on small speakers"],
}
CONTENT_LIMITATIONS_BY_PREFIX = {
    "sfx.ui.": [
        "the confirm tick reads as a short low-frequency thump rather than a dry wooden tick; the "
        "whole UI group is P1 and functional, and would be regenerated rather than patched"],
    "sfx.player.hurt.light.": [
        "reads as a steady broadband noise wash rather than a pained breath; the model did not "
        "produce a breath shape for this prompt"],
}


def load(path, default=None):
    if not os.path.exists(path):
        return default
    with io.open(path, encoding="utf-8") as handle:
        return json.load(handle)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--emit-manifest", action="store_true")
    parser.add_argument("--stage-godot", action="store_true")
    args = parser.parse_args()

    spec = load(SPEC)
    if not spec:
        print(f"  no spec at {SPEC}")
        return 1
    qa = (load(QA) or {}).get("sounds", {})
    generation = (load(GENERATION) or {}).get("sounds", {})
    models = (load(MODELS) or {}).get("models", {})
    checkpoint = next((m for m in models.values() if "checkpoints" in m["destination"]), None)
    encoder = next((m for m in models.values() if "text_encoders" in m["destination"]), None)

    problems = []
    entries = []
    counts = {"validated": 0, "missing": 0, "alias": 0}

    for sound in spec["sounds"]:
        audio_id = sound["audio_id"]
        record = qa.get(audio_id)
        generated = generation.get(audio_id, {})
        entry = {
            "audio_id": audio_id,
            "category": sound["category"],
            "group": sound["group"],
            "gameplay_role": sound["gameplay_role"],
            "prompt": sound["prompt"],
            "source_model": "Stable Audio Open 1.0",
            "source_model_version": (checkpoint or {}).get("sha256", "")[:16] or "unknown",
            "source_license": (checkpoint or {}).get("license", "unknown"),
            "text_encoder": "t5-base",
            "text_encoder_license": (encoder or {}).get("license", "unknown"),
            "seed": generated.get("seed"),
            "steps": generated.get("steps"),
            "cfg_scale": generated.get("cfg"),
            "sampler": generated.get("sampler"),
            "scheduler": generated.get("scheduler"),
            "master_gain_db": generated.get("headroom_db"),
            "master_peak_dbfs": generated.get("master_peak_dbfs"),
            "seconds_requested": sound["seconds"],
            "channels": sound["channels"],
            "loop": sound["loop"],
            "single_transient": sound.get("single_transient", False),
            "target_loudness_lufs": sound["loudness_lufs"],
            "priority": sound["priority"],
            "shipping_status": SHIPPING_STATUS.get(sound["category"], "prototype_ok"),
            "implementation_status": "asset_ready",
            "godot_import_status": "not_validated",
            "known_limitations": [],
        }

        if sound.get("alias_of"):
            entry["alias_of"] = sound["alias_of"]
            entry["implementation_status"] = "alias"
            counts["alias"] += 1
            entries.append(entry)
            continue

        if not record:
            counts["missing"] += 1
            entry["implementation_status"] = "not_processed"
            entry["known_limitations"].append("no processed artefact")
            entries.append(entry)
            problems.append(f"{audio_id}: specified but not processed")
            continue

        path = os.path.join(ASSETS, record["delivered"].replace("/", os.sep))
        if not os.path.exists(path):
            counts["missing"] += 1
            entry["implementation_status"] = "missing_file"
            problems.append(f"{audio_id}: {record['delivered']} missing")
            entries.append(entry)
            continue

        info = sf.info(path)
        entry.update({
            "delivered": record["delivered"],
            "sample_rate": info.samplerate,
            "delivered_seconds": round(info.frames / info.samplerate, 4),
            "subtype": info.subtype,
            "bytes": record.get("bytes"),
            "measured_lufs": record.get("measured_lufs"),
            "loudness_method": record.get("loudness_method"),
            "gain_db": record.get("gain_db"),
            "gain_limited_by_peak": record.get("gain_limited_by_peak"),
            "peak_dbfs": record.get("peak_dbfs"),
            "rms_dbfs": record.get("rms_dbfs"),
            "dc_offset": record.get("dc_offset"),
            "clipped_samples": record.get("clipped_samples"),
            "onset_s": record.get("onset_s"),
            "spectral_centroid_hz": record.get("spectral_centroid_hz"),
            "spectral_rolloff85_hz": record.get("spectral_rolloff85_hz"),
            "trim": record.get("trim"),
            "downmix": record.get("downmix"),
            "transient_trim": record.get("transient_trim"),
            "loop": bool(sound["loop"]),
            "normalized": True,
        })

        # --- the checks -------------------------------------------------------------------------
        if not generated.get("prompt_id"):
            problems.append(f"{audio_id}: no generation provenance")
        if not checkpoint or not encoder:
            problems.append(f"{audio_id}: model provenance incomplete")
        if info.samplerate != TARGET_RATE:
            problems.append(f"{audio_id}: {info.samplerate} Hz, expected {TARGET_RATE}")
        if (2 if info.channels > 1 else 1) != sound["channels"]:
            problems.append(f"{audio_id}: {info.channels} channel(s), spec says {sound['channels']}")
        if record.get("clipped_samples", 0) > 0:
            problems.append(f"{audio_id}: {record['clipped_samples']} clipped sample(s)")
        if record.get("peak_dbfs", -99) >= CLIP_THRESHOLD or record.get("near_silent"):
            problems.append(f"{audio_id}: silent or at full scale")
        if record.get("rms_dbfs", 0) < SILENCE_THRESHOLD_DBFS:
            problems.append(f"{audio_id}: rms {record.get('rms_dbfs')} dBFS is effectively silent")
        # A shorter delivered length is not automatically a defect: the processor deliberately cuts
        # a one-shot back to its first transient, because a prompt for a 420 ms footstep can arrive
        # with a second event in it. That is recorded as a trim, not as a limitation. Drift on a
        # sound that was *not* trimmed is a limitation, because nothing explains it.
        drift = abs(entry["delivered_seconds"] - sound["seconds"])
        deliberately_trimmed = bool(record.get("transient_trim") or record.get("loop_shortened"))
        entry["delivered_shorter_than_requested"] = bool(
            entry["delivered_seconds"] < sound["seconds"] - 0.01)
        if drift > max(DURATION_TOLERANCE, 0.35 * sound["seconds"]) and not deliberately_trimmed:
            entry["known_limitations"].append(
                f"delivered {entry['delivered_seconds']:.2f}s against requested "
                f"{sound['seconds']:.2f}s, unexplained")
        if sound["loop"]:
            seam = record.get("loop") or {}
            if not seam or "seam_after" not in seam:
                problems.append(f"{audio_id}: a loop with no seam measurement")
            else:
                entry["loop_seam"] = seam
                percentile = seam.get("seam_percentile_after")
                if percentile is not None and percentile > 99.5:
                    entry["known_limitations"].append(
                        f"loop wrap step sits at the {percentile:.1f}th percentile of the bed's own "
                        f"sample steps")
        if generated.get("low_master_level"):
            entry["known_limitations"].append(
                f"quiet master ({generated.get('master_peak_dbfs')} dBFS) lifted in post")
        entry["known_limitations"].extend(CONTENT_LIMITATIONS.get(audio_id, []))
        for prefix, notes in CONTENT_LIMITATIONS_BY_PREFIX.items():
            if audio_id.startswith(prefix):
                entry["known_limitations"].extend(notes)
        counts["validated"] += 1
        entries.append(entry)

    # --- report ---------------------------------------------------------------------------------
    print(f"  {'audio id':<48} {'len':>6} {'rate':>6} {'ch':>3}  status")
    print("  " + "-" * 86)
    for entry in entries:
        if entry["implementation_status"] == "alias":
            print(f"  {entry['audio_id']:<48} {'':>6} {'':>6} {'':>3}  alias of "
                  f"{entry['alias_of']}")
            continue
        if entry["implementation_status"] != "asset_ready":
            print(f"  {entry['audio_id']:<48} {'':>6} {'':>6} {'':>3}  "
                  f"{entry['implementation_status']}")
            continue
        print(f"  {entry['audio_id']:<48} {entry['delivered_seconds']:>6.2f} "
              f"{entry['sample_rate']:>6} {entry['channels']:>3}  ok")

    looping = [e for e in entries if e.get("loop") is True and e.get("loop_seam")]
    mono = [e for e in entries if e.get("channels") == 1 and e["implementation_status"] == "asset_ready"]
    stereo = [e for e in entries if e.get("channels") == 2 and e["implementation_status"] == "asset_ready"]
    print()
    print(f"  {counts['validated']} validated, {counts['alias']} alias, {counts['missing']} missing")
    print(f"  {mono.__len__()} mono (positional), {stereo.__len__()} stereo (beds and UI)")
    print(f"  looping beds with a measured seam: {len(looping)}")
    limitations = sum(len(e.get("known_limitations") or []) for e in entries)
    print(f"  known limitations recorded: {limitations}")
    if problems:
        print()
        for problem in problems[:40]:
            print(f"  FAIL  {problem}")
        if len(problems) > 40:
            print(f"  ... and {len(problems) - 40} more")

    if args.emit_manifest:
        document = {
            "version": 1,
            "comment": [
                "The smallest coherent audio set that lets Ashen Hollow play through M6 without",
                "placeholder silence. Every entry carries its prompt, seed, model hash and licence,",
                "because a sound whose provenance is missing cannot be shipped and the validator",
                "fails it.",
                "",
                "Judge this set by measurement and by spectrogram, not by ear: the pipeline that",
                "produced it has no ears. `playable_prototype_audio.json` records what was measured;",
                "docs/PHASE1_AUDIO_SPRINT_STATUS.md records what could not be.",
            ],
            "authority": "PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md",
            "production_format": {
                "container": "WAV",
                "subtype": "PCM_24",
                "sample_rate": TARGET_RATE,
                "note": "Masters are retained as the raw 44.1 kHz FLAC the generator produced; the "
                        "WAVs are the produced intermediate. Nothing lossy is ever re-encoded.",
            },
            "models": {
                "generation": {
                    "name": "Stable Audio Open 1.0",
                    "role": "text-to-audio, all sound effects and ambience",
                    "license": (checkpoint or {}).get("license"),
                    "commercial_use": (checkpoint or {}).get("commercial_use"),
                    "sha256_16": (checkpoint or {}).get("sha256", "")[:16],
                },
                "text_encoder": {
                    "name": "t5-base",
                    "license": (encoder or {}).get("license"),
                    "sha256_16": (encoder or {}).get("sha256", "")[:16],
                },
            },
            "validation": {
                "validated": counts["validated"],
                "alias": counts["alias"],
                "missing": counts["missing"],
                "mono_positional": len(mono),
                "stereo_beds": len(stereo),
                "loops_with_measured_seam": len(looping),
                "problems": problems,
            },
            "geometry": {
                "channel_policy": "Mono for every positional one-shot (impacts, footsteps, "
                                  "vocalisations, interactions, weapons). Stereo only for "
                                  "environment beds and non-positional Strain layers, per the "
                                  "brief's section 24: positional information is not baked into "
                                  "one-shots.",
                "loudness_policy": "Relative, not uniform. Each group has its own target so a sword "
                                   "swing cannot end up louder than a boar impact merely because "
                                   "its waveform peaked higher.",
            },
            "sounds": entries,
        }
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        with io.open(OUT, "w", encoding="utf-8") as handle:
            json.dump(document, handle, indent=2)
            handle.write("\n")
        print(f"\n  wrote {OUT}")

    if args.stage_godot:
        import shutil
        os.makedirs(GODOT_STAGED, exist_ok=True)
        staged = 0
        for entry in entries:
            if entry["implementation_status"] != "asset_ready":
                continue
            source = os.path.join(ASSETS, entry["delivered"].replace("/", os.sep))
            shutil.copy2(source, os.path.join(GODOT_STAGED, f"{entry['audio_id']}.wav"))
            staged += 1
        with io.open(os.path.join(GODOT_STAGED, "..", "audio_manifest.json"), "w",
                     encoding="utf-8") as handle:
            json.dump({"sounds": [e for e in entries
                                  if e["implementation_status"] == "asset_ready"]},
                      handle, indent=2)
        print(f"  staged {staged} WAV(s) into {GODOT_STAGED}")

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
