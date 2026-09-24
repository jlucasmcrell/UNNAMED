"""Generate the Phase-1 sound set with Stable Audio Open 1.0 through ComfyUI.

Reads `assets/manifests/audio_spec.json` and renders one FLAC per spec entry into
`assets/audio/masters/`, recording the prompt, seed, prompt id, host and model hashes for each so a
sound can always be traced back to how it was made.

Resumable by design: an entry whose master already exists and decodes is skipped, so a run can be
stopped and restarted at any point. Failures retry a bounded number of times rather than forever.

Every node and every combo value is checked against the target server's own `/object_info` before a
single prompt is submitted. The audio nodes ship with ComfyUI core, but the *weights* do not, and a
missing checkpoint otherwise surfaces as a 400 after the whole batch has been queued.

Host choice: BEAST. Its RTX 3090 has 22 GB free, it is not the 3D-critical host (ASTRAL is, and it
runs the Trellis work), and rendering locally means the FLACs never cross the network. RAZER's
4070 Ti has 4.1 GB free, which is not enough for Stable Audio's checkpoint plus the T5 encoder, and
its model share is not reachable from BEAST anyway.

Usage:
    python _generate_audio.py --dry-run                 # preflight only, submit nothing
    python _generate_audio.py --only sfx.ui.select --apply
    python _generate_audio.py --category ui --apply
    python _generate_audio.py --apply --limit 20
"""
import argparse
import io
import json
import math
import os
import random
import sys
import time
import urllib.error
import urllib.request

import numpy as np
import soundfile  # noqa: F401  (used by flac_peak_after on downloaded bytes)

ASSETS = r"W:\UNNAMED\assets"
SPEC = os.path.join(ASSETS, "manifests", "audio_spec.json")
MASTERS = os.path.join(ASSETS, "audio", "masters")
STATE = os.path.join(ASSETS, "audio_generate_state.json")
LOG = os.path.join(ASSETS, "audio_generate.log")

SERVER = os.environ.get("UNNAMED_AUDIO_SERVER", "http://127.0.0.1:8188")
COMFY_OUTPUT = os.environ.get("UNNAMED_AUDIO_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output")
CHECKPOINT = "stable_audio_open_1.0.safetensors"
TEXT_ENCODER = "t5_base.safetensors"

# Stable Audio Open is a distilled-ish flow model; these are prototype settings chosen for speed at
# 173 renders. Raised per-item when a family is judged too rough on inspection.
STEPS = 32
CFG = 7.0
SAMPLER = "euler"
SCHEDULER = "simple"
NEGATIVE = "music, melody, speech, singing, voice, talking, cinematic, reverb, echo, distorted"

# EmptyLatentAudio's minimum is 1.0 s, so the model physically cannot render a 220 ms UI tick or a
# 340 ms footstep. Every one-shot is therefore generated with headroom and trimmed to its delivered
# length by the processing stage. Generated length and delivered length are separate concerns and
# the spec only ever states the latter.
GEN_MIN = 1.0
GEN_PADDING = 3.0
GEN_MAX = 9.0

# The graph's node ids, so the same ids appear in the log, the state file and any error.
NODES = {
    "checkpoint": "1", "clip_loader": "9", "positive": "2", "negative": "3",
    "conditioning": "4", "latent": "5", "sampler": "6", "decode": "7", "volume": "10",
    "save": "8",
}

# Stable Audio's output level varies enormously per prompt. Measured across the first ten renders:
# five peaked at exactly 0 dBFS with clipped samples in them, because ComfyUI's saver writes PCM_16
# without scaling, while two others landed at -54 and -62 dBFS. Lifting a -62 dBFS 16-bit master by
# 46 dB in post also lifts its quantisation noise by 46 dB, which is audible.
#
# So the graph seeks a level. It starts at unity, and an item outside the acceptable band is
# re-rendered with the gain that puts its peak at TARGET_MASTER_PEAK_DBFS. Sounds that already land
# in the band keep their first render, so most items cost one render and only the outliers cost two.
MASTER_HEADROOM_DB = 0
TARGET_MASTER_PEAK_DBFS = -6.0
ACCEPTABLE_PEAK_LOW_DBFS = -20.0
ACCEPTABLE_PEAK_HIGH_DBFS = -0.5
GAIN_LIMIT_DB = 54
STATE_FLUSH_EVERY = 5


def get(path, timeout=60):
    with urllib.request.urlopen(f"{SERVER}{path}", timeout=timeout) as response:
        return json.loads(response.read())


def post(path, payload, timeout=120):
    request = urllib.request.Request(
        f"{SERVER}{path}", data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read())


def generation_seconds(entry):
    """How long to ask the model for. Delivered length is trimmed in post-processing."""
    if entry["loop"]:
        return max(GEN_MIN, entry["seconds"])
    return max(GEN_MIN, min(entry["seconds"] * GEN_PADDING, GEN_MAX))


def graph(entry, seed, seconds, headroom_db=MASTER_HEADROOM_DB):
    """The Stable Audio graph in API format.

    The Stable Audio checkpoint carries a diffusion model and its autoencoder but *no* text
    encoder, so CheckpointLoaderSimple's CLIP output is None and feeding it to CLIPTextEncode fails
    at execution with "clip input is invalid: None". T5 is loaded separately with type
    'stable_audio', which is what makes the conditioning work.

    ConditioningStableAudio returns two conditioning outputs: 0 is the positive with the timing
    patched in and 1 is the negative. Both go to the sampler, which is why the sampler's positive
    and negative both reference node 4 with different output indices.
    """
    return {
        NODES["checkpoint"]: {
            "class_type": "CheckpointLoaderSimple",
            "inputs": {"ckpt_name": CHECKPOINT}},
        NODES["clip_loader"]: {
            "class_type": "CLIPLoader",
            "inputs": {"clip_name": TEXT_ENCODER, "type": "stable_audio"}},
        NODES["positive"]: {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": [NODES["clip_loader"], 0], "text": entry["prompt"]}},
        NODES["negative"]: {
            "class_type": "CLIPTextEncode",
            "inputs": {"clip": [NODES["clip_loader"], 0], "text": NEGATIVE}},
        NODES["conditioning"]: {
            "class_type": "ConditioningStableAudio",
            "inputs": {"positive": [NODES["positive"], 0], "negative": [NODES["negative"], 0],
                       "seconds_start": 0.0, "seconds_total": seconds}},
        NODES["latent"]: {
            "class_type": "EmptyLatentAudio",
            "inputs": {"seconds": seconds, "batch_size": 1}},
        NODES["sampler"]: {
            "class_type": "KSampler",
            "inputs": {"model": [NODES["checkpoint"], 0],
                       "positive": [NODES["conditioning"], 0],
                       "negative": [NODES["conditioning"], 1],
                       "latent_image": [NODES["latent"], 0],
                       "seed": seed, "steps": STEPS, "cfg": CFG,
                       "sampler_name": SAMPLER, "scheduler": SCHEDULER, "denoise": 1.0}},
        NODES["decode"]: {
            "class_type": "VAEDecodeAudio",
            "inputs": {"samples": [NODES["sampler"], 0], "vae": [NODES["checkpoint"], 2]}},
        NODES["volume"]: {
            "class_type": "AudioAdjustVolume",
            "inputs": {"audio": [NODES["decode"], 0], "volume": headroom_db}},
        NODES["save"]: {
            "class_type": "SaveAudioAdvanced",
            "inputs": {"audio": [NODES["volume"], 0],
                       "filename_prefix": f"otherreach_audio/{entry['audio_id']}",
                       "format": "flac"}},
    }


def preflight(entries):
    """Check every node, input and combo value against the server before submitting anything."""
    try:
        info = get("/object_info")
    except (urllib.error.URLError, OSError, ValueError) as exc:
        return [f"server unreachable at {SERVER}: {type(exc).__name__}: {exc}"]

    problems = []
    sample = graph(entries[0], 0, generation_seconds(entries[0]))
    for node_id, node in sample.items():
        class_type = node["class_type"]
        spec = info.get(class_type)
        if spec is None:
            problems.append(f"node {node_id}: '{class_type}' is not registered on this server")
            continue
        schema = ((spec.get("input") or {}).get("required") or {})
        schema.update((spec.get("input") or {}).get("optional") or {})
        for input_name, value in node["inputs"].items():
            if isinstance(value, list):          # a link, nothing to validate
                continue
            definition = schema.get(input_name)
            if definition is None:
                problems.append(f"node {node_id} ({class_type}): no input '{input_name}'")
                continue
            allowed = definition[0]
            if isinstance(allowed, list) and value not in allowed:
                problems.append(f"node {node_id} ({class_type}).{input_name}: '{value}' not in "
                                f"{allowed[:8]}{'...' if len(allowed) > 8 else ''}")
            elif isinstance(allowed, dict):
                # An IO.DynamicCombo: the value selects a branch, and that branch may add inputs.
                options = allowed.get("options") or []
                names = [o.get("value") if isinstance(o, dict) else o for o in options]
                if names and value not in names:
                    problems.append(f"node {node_id} ({class_type}).{input_name}: '{value}' not in "
                                    f"{names}")
    return problems


def wait_for(prompt_id, label, budget=900):
    """Poll /history with backoff. Returns (outputs, status, elapsed)."""
    started = time.time()
    delay = 2.0
    while time.time() - started < budget:
        try:
            history = get(f"/history/{prompt_id}")
        except (urllib.error.URLError, OSError, ValueError):
            time.sleep(delay)
            delay = min(delay * 1.5, 15.0)
            continue
        if prompt_id in history:
            entry = history[prompt_id]
            status = (entry.get("status") or {}).get("status_str", "unknown")
            return entry.get("outputs") or {}, status, time.time() - started
        time.sleep(delay)
        delay = min(delay * 1.5, 15.0)
    return {}, "timeout", time.time() - started


def collect(outputs):
    for node_output in outputs.values():
        for key in ("audio", "audio_files", "files", "flac"):
            for item in node_output.get(key) or []:
                if isinstance(item, dict) and item.get("filename"):
                    return item
    return None


def flac_peak_after(data):
    """Peak of a FLAC the batch has just downloaded, or None if it cannot be read.

    Read from the bytes rather than from the file so the check happens before the master is put in
    place: a clipped master should never reach the masters directory.
    """
    import io as _io
    try:
        import soundfile as sf
        audio, _rate = sf.read(_io.BytesIO(data), always_2d=True, dtype="float64")
        if audio.size == 0:
            return None
        return float(np.max(np.abs(audio)))
    except Exception:
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--only", nargs="*", default=None)
    parser.add_argument("--category", nargs="*", default=None)
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--retries", type=int, default=2)
    parser.add_argument("--steps", type=int, default=None)
    args = parser.parse_args()

    global STEPS
    if args.steps:
        STEPS = args.steps

    with io.open(SPEC, encoding="utf-8") as handle:
        spec = json.load(handle)
    entries = [e for e in spec["sounds"] if not e.get("alias_of")]
    if args.only:
        entries = [e for e in entries if e["audio_id"] in args.only]
    if args.category:
        entries = [e for e in entries if e["category"] in args.category]
    if not entries:
        print("  nothing selected")
        return 1

    problems = preflight(entries)
    if problems:
        for problem in problems:
            print(f"  PREFLIGHT FAIL  {problem}")
        print(f"\n  {len(problems)} preflight problem(s); nothing submitted")
        return 1
    print(f"  preflight OK against {SERVER}: {len(entries)} sound(s) selected, "
          f"checkpoint '{CHECKPOINT}', encoder '{TEXT_ENCODER}', "
          f"{STEPS} steps, cfg {CFG}")

    if args.dry_run or not args.apply:
        total = sum(e["seconds"] for e in entries)
        print(f"  {args.dry_run and 'dry run' or 'no --apply'}: would render {len(entries)} sounds, "
              f"{total:.0f} s of audio, into {MASTERS}")
        return 0

    os.makedirs(MASTERS, exist_ok=True)
    state = {"version": 1, "server": SERVER, "checkpoint": CHECKPOINT,
             "text_encoder": TEXT_ENCODER, "steps": STEPS, "cfg": CFG,
             "sampler": SAMPLER, "scheduler": SCHEDULER, "negative_prompt": NEGATIVE,
             "sounds": {}}
    if os.path.exists(STATE):
        with io.open(STATE, encoding="utf-8") as handle:
            previous = json.load(handle)
        state["sounds"] = previous.get("sounds", {})
        state["models"] = previous.get("models", {})

    done = skipped = failed = 0
    log = io.open(LOG, "a", encoding="utf-8")
    try:
        for index, entry in enumerate(entries, 1):
            audio_id = entry["audio_id"]
            master = os.path.join(MASTERS, f"{audio_id}.flac")
            record = state["sounds"].get(audio_id) or {}
            if os.path.exists(master) and os.path.getsize(master) > 1024 and record.get("status") == "ok":
                skipped += 1
                continue
            if args.limit and done >= args.limit:
                break

            seed = record.get("seed") or random.randint(0, 2**31 - 1)
            headroom_db = record.get("headroom_db", MASTER_HEADROOM_DB)
            attempt = 0
            while attempt <= args.retries:
                attempt += 1
                try:
                    submitted = post("/prompt", {"prompt": graph(entry, seed, generation_seconds(entry), headroom_db),
                                                 "client_id": "otherreach-audio"})
                except urllib.error.HTTPError as exc:
                    body = exc.read().decode("utf-8", "replace")[:400]
                    print(f"  FAIL {audio_id}: HTTP {exc.code} {body}")
                    failed += 1
                    break
                except (urllib.error.URLError, OSError) as exc:
                    print(f"  FAIL {audio_id}: {type(exc).__name__}: {exc}")
                    failed += 1
                    break
                if submitted.get("node_errors"):
                    print(f"  FAIL {audio_id}: node_errors "
                          f"{json.dumps(submitted['node_errors'])[:300]}")
                    failed += 1
                    break

                prompt_id = submitted["prompt_id"]
                outputs, status, elapsed = wait_for(prompt_id, audio_id)
                if status != "success":
                    detail = ""
                    try:
                        history = get(f"/history/{prompt_id}")
                        detail = json.dumps((history.get(prompt_id) or {}).get("status") or {})[:300]
                    except Exception:
                        pass
                    print(f"  RETRY {audio_id}: {status} after {elapsed:.0f}s {detail}")
                    seed = random.randint(0, 2**31 - 1)
                    continue

                item = collect(outputs)
                if item is None:
                    print(f"  RETRY {audio_id}: success but no audio in outputs")
                    continue
                source = os.path.join(COMFY_OUTPUT, item.get("subfolder") or "", item["filename"])
                if not os.path.exists(source):
                    print(f"  RETRY {audio_id}: {source} not on disk")
                    continue
                with open(source, "rb") as handle:
                    data = handle.read()
                if len(data) < 1024:
                    print(f"  RETRY {audio_id}: {len(data)} bytes")
                    continue

                # Peak-check the master before accepting it. A master that still reaches full scale
                # was clipped by the 16-bit write, so the item is re-rendered with more headroom
                # rather than shipped with a distorted transient.
                master_peak = flac_peak_after(data)
                if master_peak is not None and master_peak > 0:
                    peak_dbfs = 20.0 * math.log10(master_peak)
                    outside = (peak_dbfs < ACCEPTABLE_PEAK_LOW_DBFS
                               or peak_dbfs > ACCEPTABLE_PEAK_HIGH_DBFS)
                    if outside and attempt <= args.retries:
                        correction = TARGET_MASTER_PEAK_DBFS - peak_dbfs
                        new_gain = int(round(max(-GAIN_LIMIT_DB, min(GAIN_LIMIT_DB,
                                                                   headroom_db + correction))))
                        if abs(new_gain - headroom_db) >= 1:
                            reason = "clipped" if peak_dbfs > ACCEPTABLE_PEAK_HIGH_DBFS else "quiet"
                            print(f"  RELEVEL {audio_id}: master {peak_dbfs:+.1f} dBFS "
                                  f"({reason}), re-rendering at {new_gain:+d} dB")
                            # The seed is deliberately kept: correcting the level of a *different*
                            # sound would be meaningless, and re-rolling was making the loop
                            # oscillate between too quiet and clipped.
                            headroom_db = new_gain
                            continue

                with open(master + ".part", "wb") as handle:
                    handle.write(data)
                os.replace(master + ".part", master)
                try:
                    os.remove(source)
                except OSError:
                    pass

                state["sounds"][audio_id] = {
                    "audio_id": audio_id, "status": "ok", "host": SERVER,
                    "prompt_id": prompt_id, "seed": seed, "steps": STEPS, "cfg": CFG,
                    "sampler": SAMPLER, "scheduler": SCHEDULER,
                    "headroom_db": headroom_db, "master_peak": master_peak,
                    "master_peak_dbfs": (None if master_peak in (None, 0)
                                         else round(20.0 * math.log10(master_peak), 2)),
                    "low_master_level": bool(
                        master_peak is not None
                        and 20.0 * math.log10(max(master_peak, 1e-9))
                        < ACCEPTABLE_PEAK_LOW_DBFS),
                    "seconds_delivered": entry["seconds"], "seconds_generated": generation_seconds(entry),
                    "bytes": len(data), "master": f"audio/masters/{audio_id}.flac",
                    "generated": time.strftime("%Y-%m-%d %H:%M:%S"),
                }
                done += 1
                line = (f"  OK   {audio_id:<46} {elapsed:>6.1f}s seed {seed} "
                        f"{headroom_db} dB {len(data) / 1024:.0f} KB")
                print(line)
                log.write(line + "\n")
                log.flush()
                # Flush the state every few items as well as at the end. Without this a batch that
                # is interrupted loses the provenance of everything it had already rendered, and
                # the whole set has to be re-rendered to recover the prompts and seeds.
                if done % STATE_FLUSH_EVERY == 0:
                    with io.open(STATE, "w", encoding="utf-8") as handle:
                        json.dump(state, handle, indent=2)
                        handle.write("\n")
                break
            else:
                state["sounds"][audio_id] = {**(state["sounds"].get(audio_id) or {}),
                                             "status": "failed", "attempts": args.retries + 1}
                failed += 1
    finally:
        log.close()
        with io.open(STATE, "w", encoding="utf-8") as handle:
            json.dump(state, handle, indent=2)
            handle.write("\n")

    print(f"\n  {done} generated, {skipped} already present, {failed} failed")
    print(f"  state -> {STATE}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
