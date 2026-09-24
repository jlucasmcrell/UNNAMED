"""Render one candidate per id at the correct length, then verify the length and re-render failures.

Three changes from the V2 generator, all of them consequences of what the owner's verdict and the
model's own documentation established.

**One candidate, not three.** The seed chooses which realisation of a sound you get, not which sound,
so three seeds of one prompt are three takes of the same thing. That was measured
(`_seed_vs_prompt_probe.py`) and the owner heard it independently. Three candidates multiplied the
listening work by three without multiplying the range of outcomes.

**Rendered close to the target length.** The Stability prompting guide says results are better when
the duration fits what is being described, and to set a short duration for sound effects. V2 rendered
at 2.5x the target and trimmed down, which asked the model for a sound three times too long and then
threw most of it away.

**Length is verified and enforced.** In V2 the single-transient trim clamped sounds to 18-55% of their
spec, so 101 of 228 arrived under 75% of the intended length and every UI sound arrived at 18%. V3
takes exactly the spec's length after cutting leading silence, then measures the delivered file
against the spec and re-renders with a new seed if it is wrong, rather than shipping a mismatch and
hoping.

Usage:
    python _generate_sa3_v3.py --audit
    python _generate_sa3_v3.py --apply
    python _generate_sa3_v3.py --apply --only sfx.ui.select
"""
import argparse
import io
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

import numpy as np
import soundfile as sf

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
SERVER = os.environ.get("UNNAMED_AUDIO_SERVER", "http://127.0.0.1:8188")
COMFY_OUTPUT = os.environ.get("UNNAMED_AUDIO_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output")
ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec_v3.json")
MASTERS = os.path.join(ASSETS, "audio", "v3_masters")
DELIVERED = os.path.join(ASSETS, "audio", "v3_delivered")
STATE = os.path.join(ASSETS, "sa3_v3_state.json")
LOG = os.path.join(ASSETS, "sa3_v3.log")

CHECKPOINT = "stable_audio_3_small_sfx.safetensors"
TEXT_ENCODER = "t5gemma_b_b_ul2.safetensors"

STEPS = 8
CFG = 1.0
SAMPLER = "lcm"
SCHEDULER = "simple"
NEGATIVE = ""

# EmptyLatentAudio's floor is 1.0 s, so a 0.22 s tick cannot be requested directly. Ask for the target
# plus enough margin to find the onset and still have the whole event, but not a multiple of it: the
# guide is explicit that a duration fitting the description produces better results.
MIN_RENDER_S = 1.0
HEADROOM_S = 0.35
HEADROOM_FRACTION = 0.15

LENGTH_TOLERANCE_S = 0.02
MAX_ATTEMPTS = 3

# Ten of the 228 raw renders arrive already at full scale: Stable Audio 3 clips on those prompts before
# this pipeline touches them. V3 applies no gain anywhere, which is why they shipped through - the
# earlier probe measured one prompt peaking at -8 dBFS and nothing generalised from it. -1 dBFS is the
# same ceiling _process_audio.py used for V2.
#
# Gain is reduced only where a file would otherwise clip. Normalising every file would be a mix
# decision, and the set's relative loudness has not been auditioned - least of all by this pipeline,
# which cannot hear. Removing an objective defect is a different act from choosing a balance.
PEAK_CEILING_DBFS = -1.0


def apply_peak_ceiling(segment):
    """Attenuate only if the segment would otherwise clip. Returns (segment, gain_db)."""
    peak = float(np.max(np.abs(segment)))
    if peak <= 0:
        return segment, 0.0
    peak_dbfs = 20.0 * float(np.log10(peak))
    if peak_dbfs <= PEAK_CEILING_DBFS:
        return segment, 0.0
    gain_db = PEAK_CEILING_DBFS - peak_dbfs
    return segment * (10.0 ** (gain_db / 20.0)), gain_db


def load_processor():
    """_process_audio's resampler, so the 44.1 -> 48 kHz step uses the project's own code."""
    import importlib.util
    path = os.path.join(TOOL_DIR, "_process_audio.py")
    spec = importlib.util.spec_from_file_location("_process_audio", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def get(path, timeout=90):
    return json.loads(urllib.request.urlopen(SERVER + path, timeout=timeout).read())


def post(path, payload, timeout=180):
    request = urllib.request.Request(SERVER + path, data=json.dumps(payload).encode(),
                                     headers={"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(request, timeout=timeout).read())


def render_seconds(entry):
    return max(MIN_RENDER_S,
               entry["seconds"] + max(HEADROOM_S, entry["seconds"] * HEADROOM_FRACTION))


def seed_for(audio_id, attempt):
    digest = 0
    for char in audio_id:
        digest = (digest * 131 + ord(char)) & 0xFFFFFFFF
    return (digest ^ (1013904223 + attempt * 2654435761)) & 0x7FFFFFFF


def graph(entry, seed, seconds, filename_prefix):
    return {
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": CHECKPOINT}},
        "2": {"class_type": "CLIPLoader",
              "inputs": {"clip_name": TEXT_ENCODER, "type": "stable_audio", "device": "default"}},
        "3": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["2", 0], "text": entry["prompt"]}},
        "4": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["2", 0], "text": NEGATIVE}},
        "5": {"class_type": "EmptyLatentAudio",
              "inputs": {"seconds": round(seconds, 2), "batch_size": 1}},
        "6": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["3", 0], "negative": ["4", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": STEPS, "cfg": CFG,
                         "sampler_name": SAMPLER, "scheduler": SCHEDULER, "denoise": 1.0}},
        "7": {"class_type": "VAEDecodeAudio", "inputs": {"samples": ["6", 0], "vae": ["1", 2]}},
        "8": {"class_type": "SaveAudioAdvanced",
              "inputs": {"audio": ["7", 0], "filename_prefix": filename_prefix, "format": "flac"}},
    }


def wait(prompt_id, budget=900):
    started = time.time()
    delay = 2.0
    while time.time() - started < budget:
        try:
            history = get(f"/history/{prompt_id}")
        except urllib.error.HTTPError:
            history = {}
        if prompt_id in history:
            return history[prompt_id], time.time() - started
        time.sleep(delay)
        delay = min(delay * 1.35, 8.0)
    return None, time.time() - started


def output_path(outputs):
    node = outputs.get("8", {})
    for entry in node.get("audio", []) + node.get("flac", []):
        if entry.get("filename"):
            return os.path.join(COMFY_OUTPUT, entry.get("subfolder") or "", entry["filename"])
    return None


def trim_to_length(master, target_seconds):
    """Cut leading silence, then take exactly the target length.

    This is the whole point of the pass. `_process_audio.py` did the same thing up to the point where
    the single-transient rule took over and clamped the sound to its first energy lobe; the V3 spec
    sets single_transient false throughout, so the requested length is honoured.
    """
    data, rate = sf.read(master, always_2d=True, dtype="float64")
    mono = data.mean(axis=1)
    if mono.size == 0:
        return None, rate, "empty render"

    envelope = np.abs(mono)
    window = max(1, int(0.005 * rate))
    if envelope.size > window:
        envelope = np.convolve(envelope, np.ones(window) / window, mode="same")
    peak = float(envelope.max())
    if peak <= 0:
        return None, rate, "silent render"
    onset = int(np.argmax(envelope >= 0.02 * peak))

    wanted = int(round(target_seconds * rate))
    end = onset + wanted
    segment = data[onset:end]
    if segment.shape[0] < wanted:
        pad = wanted - segment.shape[0]
        segment = np.concatenate([segment, np.zeros((pad, segment.shape[1]))], axis=0)

    # A short fade at the end, so a trimmed tail does not click.
    fade = min(int(0.015 * rate), segment.shape[0] // 4)
    if fade > 4:
        segment = segment.copy()
        segment[-fade:] *= np.linspace(1.0, 0.0, fade)[:, None]
    return segment, rate, None


def deliver(entry, master):
    """Trim to the spec length and write the delivered WAV at 48 kHz.

    SA3 renders at 44.1 kHz and the game expects 48 kHz, so the resample uses `_process_audio`'s own
    FFT resampler rather than adding a dependency for it.
    """
    target = entry["seconds"]
    segment, rate, problem = trim_to_length(master, target)
    if problem:
        return None, problem, 0.0
    if rate != 48000:
        processor = load_processor()
        segment = processor.resample_fft(segment, int(rate), 48000)
        rate = 48000
    # Channel policy. Stable Audio 3 emits stereo and 220 of the 228 ids are positional one-shots the
    # event contract expects in mono. V3 shipped without this step and all 220 failed Godot's channel
    # check - the same class of omission as the missing gain ceiling, caught by validating the output
    # rather than by assuming it was right.
    if entry["channels"] == 1:
        segment, _downmix = processor.downmix_to_mono(segment)
    elif segment.shape[1] == 1:
        segment = np.repeat(segment, 2, axis=1)
    # After the resample, because an FFT resample overshoots slightly on a signal already at full
    # scale: one file went from 1.0000 to 1.0136.
    segment, gain_db = apply_peak_ceiling(segment)
    path = os.path.join(DELIVERED, entry["audio_id"] + ".wav")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sf.write(path, segment, rate, subtype="PCM_24")
    return path, None, gain_db


def measure(path, expected_seconds):
    data, rate = sf.read(path, always_2d=True, dtype="float64")
    delivered = data.shape[0] / rate
    return {
        "delivered_seconds": round(delivered, 4),
        "duration_error_s": round(abs(delivered - expected_seconds), 4),
        "seconds_ok": abs(delivered - expected_seconds) <= LENGTH_TOLERANCE_S,
        "peak_dbfs": round(20 * float(np.log10(max(np.max(np.abs(data)), 1e-12))), 2),
        "clipped_samples": int(np.sum(np.abs(data) >= 0.999)),
    }


def load_state():
    if os.path.exists(STATE):
        with io.open(STATE, encoding="utf-8") as handle:
            return json.load(handle)
    return {"sounds": {}}


def save_state(state):
    with io.open(STATE, "w", encoding="utf-8") as handle:
        json.dump(state, handle, indent=2)
        handle.write("\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--only", nargs="*", default=None)
    parser.add_argument("--limit", type=int, default=None)
    args = parser.parse_args()

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    entries = spec["sounds"]
    if args.only:
        wanted = set(args.only)
        entries = [e for e in entries if e["audio_id"] in wanted]
        missing = wanted - {e["audio_id"] for e in entries}
        if missing:
            print(f"  unknown ids: {sorted(missing)}")
            return 1
    if args.limit:
        entries = entries[:args.limit]

    state = load_state()
    outstanding = [e for e in entries
                   if not state["sounds"].get(e["audio_id"], {}).get("seconds_ok")]
    print(f"  ids          : {len(entries)}")
    print(f"  candidates   : 1 per id")
    print(f"  outstanding  : {len(outstanding)}")
    print(f"  settings     : {STEPS} steps, cfg {CFG}, {SAMPLER}/{SCHEDULER}, no negative")
    print(f"  render length: target + max({HEADROOM_S}s, {int(HEADROOM_FRACTION*100)}%), "
          f"floor {MIN_RENDER_S}s")
    print(f"  length check : delivered within {LENGTH_TOLERANCE_S}s of spec, else re-render "
          f"(max {MAX_ATTEMPTS} attempts)")
    print(f"  output       : {DELIVERED}")

    if not args.apply:
        print("\n  (audit only; pass --apply to render)")
        return 0

    print()
    started = time.time()
    ok = failed = 0
    for index, entry in enumerate(entries, 1):
        audio_id = entry["audio_id"]
        existing = state["sounds"].get(audio_id, {})
        if existing.get("seconds_ok"):
            ok += 1
            continue

        result = None
        for attempt in range(MAX_ATTEMPTS):
            seed = seed_for(audio_id, attempt)
            seconds = render_seconds(entry)
            prefix = f"sa3v3/{audio_id.replace('.', '_')}_t{attempt}"
            try:
                queued = post("/prompt", {"prompt": graph(entry, seed, seconds, prefix),
                                          "client_id": "sa3-v3"})
            except urllib.error.HTTPError as error:
                detail = error.read().decode("utf-8", "replace")[:200]
                print(f"  {audio_id} REJECTED: {detail}")
                break
            history, elapsed = wait(queued["prompt_id"])
            if history is None or history.get("status", {}).get("status_str") != "success":
                continue
            source = output_path(history.get("outputs", {}))
            if not source or not os.path.exists(source):
                continue

            os.makedirs(MASTERS, exist_ok=True)
            master = os.path.join(MASTERS, f"{audio_id}.flac")
            shutil.copy2(source, master)

            path, problem, gain_db = deliver(entry, master)
            if problem:
                result = {"error": problem, "seconds_ok": False}
                continue
            metrics = measure(path, entry["seconds"])
            result = dict(metrics, gain_reduction_db=round(gain_db, 2), seed=seed, attempt=attempt + 1, prompt_id=queued["prompt_id"],
                          render_seconds=round(seconds, 2),
                          seconds_to_render=round(elapsed, 1))
            if metrics["seconds_ok"]:
                break
            print(f"  {audio_id} attempt {attempt+1}: delivered "
                  f"{metrics['delivered_seconds']}s vs {entry['seconds']}s - re-rendering")

        state["sounds"][audio_id] = result or {"seconds_ok": False, "error": "no attempt succeeded"}
        save_state(state)
        if result and result.get("seconds_ok"):
            ok += 1
        else:
            failed += 1
            print(f"  {audio_id} FAILED length check after {MAX_ATTEMPTS} attempts")

        if index % 10 == 0 or index == len(entries):
            print(f"  {index}/{len(entries)}  ok {ok}  failed {failed}  "
                  f"{(time.time()-started)/60:.1f} min")

    save_state(state)
    print()
    print(f"  length-verified: {ok} / {len(entries)}")
    print(f"  still wrong    : {failed}")
    print(f"  wall clock     : {(time.time()-started)/60:.1f} min")
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
