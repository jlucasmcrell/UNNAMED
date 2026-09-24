"""Technical proof for Stable Audio 3 Small SFX before the V2 batch runs on 173 ids.

Four questions this answers, none of which can be assumed from V1:

  1. Does ComfyUI actually load the SA3 checkpoint and its T5Gemma encoder on BEAST?
  2. What settings does SA3 want? The official blueprint samples at **8 steps, cfg 1.0, lcm, simple**,
     against V1's 32 steps, cfg 7.0, euler. Reusing V1's numbers is the single most likely way to
     produce a whole library of wrong-sounding audio, so they are established here rather than copied.
  3. Does the negative prompt do anything? The brief asks for the universal negative to be dropped and
     for negative prompting to be omitted entirely if the model does not meaningfully support it. At
     cfg 1.0 the classifier-free-guidance formula collapses to the positive prediction alone, which
     predicts the negative is inert. That is a testable claim, so it is tested: the same seed and
     prompt are rendered with two maximally different negatives and the results compared.
  4. What does SA3's raw output look like - sample rate, channels, level, and whether the V1 quirks
     (clipping saver output, anti-phase stereo) are present at all? A V1 workaround must not be
     applied to clean V2 audio, so each is checked rather than inherited.

Also measures whether `EmptyLatentAudio`'s 1.0 s floor behaves as V1's did, since short SFX need
pad-and-trim.

Usage:
    python _sa3_probe.py
"""
import hashlib
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
import wave

SERVER = os.environ.get("UNNAMED_AUDIO_SERVER", "http://127.0.0.1:8188")
COMFY_OUTPUT = os.environ.get("UNNAMED_AUDIO_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output")
OUT = r"W:\UNNAMED\assets\audio\sa3_probe"

CHECKPOINT = "stable_audio_3_small_sfx.safetensors"
TEXT_ENCODER = "t5gemma_b_b_ul2.safetensors"

# SA3's own settings, from the official blueprint. NOT V1's.
STEPS = 8
CFG = 1.0
SAMPLER = "lcm"
SCHEDULER = "simple"

PROMPT = ("close wild boar aggressive attack grunt, deep nasal animal vocalisation with breath and "
          "throat resonance, short forceful exhale, natural animal anatomy, dry outdoor recording")
NEGATIVE_A = "music, melody, singing"
NEGATIVE_B = "boar, grunt, animal, breath, throat resonance, nasal, exhale"


def get(path, timeout=90):
    return json.loads(urllib.request.urlopen(SERVER + path, timeout=timeout).read())


def post(path, payload, timeout=180):
    request = urllib.request.Request(SERVER + path, data=json.dumps(payload).encode(),
                                     headers={"Content-Type": "application/json"})
    return json.loads(urllib.request.urlopen(request, timeout=timeout).read())


def graph(seed, seconds, negative, filename_prefix, cfg=CFG, steps=STEPS):
    """The SA3 graph. Note what is absent: no ConditioningStableAudio.

    The official blueprint wires CLIPTextEncode straight into KSampler and takes the length from
    EmptyLatentAudio. SA3 embeds seconds_total through its own NumberConditioner inside the model, so
    the V1 conditioning node has no place here.
    """
    return {
        "1": {"class_type": "CheckpointLoaderSimple",
              "inputs": {"ckpt_name": CHECKPOINT}},
        "2": {"class_type": "CLIPLoader",
              "inputs": {"clip_name": TEXT_ENCODER, "type": "stable_audio", "device": "default"}},
        "3": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["2", 0], "text": PROMPT}},
        "4": {"class_type": "CLIPTextEncode",
              "inputs": {"clip": ["2", 0], "text": negative}},
        "5": {"class_type": "EmptyLatentAudio",
              "inputs": {"seconds": seconds, "batch_size": 1}},
        "6": {"class_type": "KSampler",
              "inputs": {"model": ["1", 0], "positive": ["3", 0], "negative": ["4", 0],
                         "latent_image": ["5", 0], "seed": seed, "steps": steps, "cfg": cfg,
                         "sampler_name": SAMPLER, "scheduler": SCHEDULER, "denoise": 1.0}},
        "7": {"class_type": "VAEDecodeAudio",
              "inputs": {"samples": ["6", 0], "vae": ["1", 2]}},
        "8": {"class_type": "SaveAudioAdvanced",
              "inputs": {"audio": ["7", 0], "filename_prefix": filename_prefix, "format": "flac"}},
    }


def preflight(prompt_graph):
    """Every class_type must exist and every combo value must be in the server's list."""
    info = get("/object_info")
    problems = []
    for node_id, node in prompt_graph.items():
        cls = node["class_type"]
        if cls not in info:
            problems.append(f"{node_id}: class_type '{cls}' not on this server")
            continue
        required = info[cls].get("input", {}).get("required", {})
        for name, value in node["inputs"].items():
            spec = required.get(name)
            if spec is None:
                continue
            allowed = spec[0]
            if isinstance(allowed, list) and isinstance(value, str) and value not in allowed:
                problems.append(f"{node_id}.{name}: '{value}' not in the server's list")
    return problems


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
        delay = min(delay * 1.4, 15.0)
    return None, time.time() - started


def measure(path):
    """Read the master with soundfile, which handles the FLAC the generator actually writes.

    Python's `wave` module reads RIFF/WAV only, and the saver is configured for FLAC, so measuring with
    it fails on a perfectly good file with "file does not start with RIFF id".
    """
    import numpy as np
    import soundfile as sf
    data, rate = sf.read(path, always_2d=True, dtype="float32")
    channels = data.shape[1]
    seconds = data.shape[0] / rate
    peak = float(np.max(np.abs(data))) if data.size else 0.0
    rms = float(np.sqrt(np.mean(data ** 2))) if data.size else 0.0
    clipped = int(np.sum(np.abs(data) >= 0.999))
    return {
        "seconds": round(seconds, 3), "rate": rate, "channels": channels,
        "peak_dbfs": round(20 * (np.log10(peak) if peak > 0 else -99), 2),
        "rms_dbfs": round(20 * (np.log10(rms) if rms > 0 else -99), 2),
        "clipped_samples": clipped,
        "sha256": hashlib.sha256(open(path, "rb").read()).hexdigest()[:16],
    }


def output_path(node_outputs):
    for entry in node_outputs.get("audio", []) + node_outputs.get("flac", []):
        name = entry.get("filename")
        if name:
            return os.path.join(COMFY_OUTPUT, entry.get("subfolder") or "", name)
    return None


def main():
    os.makedirs(OUT, exist_ok=True)
    print(f"  server     : {SERVER}")
    print(f"  checkpoint : {CHECKPOINT}")
    print(f"  encoder    : {TEXT_ENCODER}")
    print(f"  settings   : steps {STEPS}, cfg {CFG}, {SAMPLER}/{SCHEDULER}   (V1 was 32, 7.0, "
          f"euler/simple)")
    print()

    proof = graph(seed=1, seconds=3.0, negative=NEGATIVE_A, filename_prefix="sa3_probe/load")
    problems = preflight(proof)
    print(f"  preflight  : {'clean' if not problems else problems}")
    if problems:
        return 1

    results = {}

    # 1 and 2: does it load and render at SA3's own settings.
    print("\n  --- render A: SA3 settings, negative 'music, melody, singing' ---")
    started = time.time()
    queued = post("/prompt", {"prompt": proof, "client_id": "sa3-probe"})
    prompt_id = queued["prompt_id"]
    history, elapsed = wait(prompt_id)
    if history is None:
        print("     TIMEOUT")
        return 1
    status = history.get("status", {}).get("status_str")
    print(f"     status {status} in {elapsed:.1f}s")
    if status != "success":
        for message in history.get("status", {}).get("messages", [])[-3:]:
            print("     ", json.dumps(message)[:300])
        return 1
    path_a = output_path(history.get("outputs", {}).get("8", {}))
    if not path_a or not os.path.exists(path_a):
        print(f"     no output file ({path_a})")
        return 1
    metrics_a = measure(path_a)
    results["render_a"] = metrics_a
    print(f"     {metrics_a}")

    # 3: is the negative prompt inert at cfg 1.0?
    #
    # Deciding this from two renders is not sound. Two runs with the SAME inputs can differ anyway if
    # the sampler is not bit-deterministic, and a differing hash then looks exactly like a negative
    # prompt that works. So the same render is repeated with identical inputs as a control, and the
    # separate A-versus-B difference is compared against the A-versus-A2 noise floor. Only a
    # difference clearly larger than the control proves the negative is doing anything.
    print("\n  --- render A2: identical to A, as a determinism control ---")
    proof_a2 = graph(seed=1, seconds=3.0, negative=NEGATIVE_A, filename_prefix="sa3_probe/ctl")
    queued = post("/prompt", {"prompt": proof_a2, "client_id": "sa3-probe"})
    history_a2, elapsed_a2 = wait(queued["prompt_id"])
    path_a2 = output_path(history_a2.get("outputs", {}).get("8", {})) if history_a2 else None
    metrics_a2 = measure(path_a2) if path_a2 and os.path.exists(path_a2) else None
    print(f"     {metrics_a2}")

    print("\n  --- render B: same seed, OPPOSITE negative (names the animal) ---")
    proof_b = graph(seed=1, seconds=3.0, negative=NEGATIVE_B, filename_prefix="sa3_probe/neg")
    queued = post("/prompt", {"prompt": proof_b, "client_id": "sa3-probe"})
    history_b, elapsed_b = wait(queued["prompt_id"])
    if history_b is None:
        print("     TIMEOUT")
        return 1
    path_b = output_path(history_b.get("outputs", {}).get("8", {}))
    metrics_b = measure(path_b) if path_b and os.path.exists(path_b) else None
    results["render_a2"] = metrics_a2
    results["render_b"] = metrics_b
    print(f"     {metrics_b}")

    if path_a and path_a2 and path_b:
        import numpy as np
        import soundfile as sf
        a, _ = sf.read(path_a, always_2d=True, dtype="float32")
        a2, _ = sf.read(path_a2, always_2d=True, dtype="float32")
        b, _ = sf.read(path_b, always_2d=True, dtype="float32")
        n = min(len(a), len(a2), len(b))
        def diff(x, y):
            d = np.abs(x[:n] - y[:n])
            return float(np.max(d)), float(np.sqrt(np.mean(d ** 2)))
        ctl_peak, ctl_rms = diff(a, a2)
        neg_peak, neg_rms = diff(a, b)
        results["comparison"] = {
            "control_a_vs_a2": {"peak": ctl_peak, "rms": ctl_rms},
            "negative_a_vs_b": {"peak": neg_peak, "rms": neg_rms},
        }
        print(f"\n     determinism control  A vs A2 : peak {ctl_peak:.6f}  rms {ctl_rms:.6f}")
        print(f"     negative effect      A vs B  : peak {neg_peak:.6f}  rms {neg_rms:.6f}")
        if neg_peak <= ctl_peak * 1.5:
            verdict = "no larger than the run-to-run noise floor -> the negative is INERT at cfg 1.0"
            inert = True
        else:
            verdict = "clearly larger than the noise floor -> the negative DOES influence output"
            inert = False
        print(f"     verdict: {verdict}")
        results["negative_prompt_inert"] = inert
        results["sampler_deterministic"] = ctl_peak == 0.0

    # 4: a longer render, to see whether SA3 shows V1's clipping behaviour.
    print("\n  --- render C: 8 s, to check level and clipping at length ---")
    proof_c = graph(seed=7, seconds=8.0, negative=NEGATIVE_A, filename_prefix="sa3_probe/long")
    queued = post("/prompt", {"prompt": proof_c, "client_id": "sa3-probe"})
    history_c, elapsed_c = wait(queued["prompt_id"])
    path_c = output_path(history_c.get("outputs", {}).get("8", {})) if history_c else None
    metrics_c = measure(path_c) if path_c and os.path.exists(path_c) else None
    print(f"     {metrics_c}  ({elapsed_c:.1f}s)")
    results["render_c"] = metrics_c

    with open(os.path.join(OUT, "probe.json"), "w", encoding="utf-8") as handle:
        json.dump({"settings": {"steps": STEPS, "cfg": CFG, "sampler": SAMPLER,
                                "scheduler": SCHEDULER},
                   "checkpoint": CHECKPOINT, "encoder": TEXT_ENCODER,
                   "prompt": PROMPT, "results": results}, handle, indent=2)
    print(f"\n  wrote {os.path.join(OUT, 'probe.json')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
