"""Does the seed change a sound's character, or only its realisation?

The owner rejected three ambience sounds, they were re-rendered with new seeds, and the result sounded
the same. That is consistent with two very different explanations, and the answer decides how the
pipeline should respond to a rejection:

  the seed is not reaching the sampler, or the sampler is ignoring it; or
  the seed changes the noise but the prompt fixes the character, so six takes are six realisations of
  the same sound rather than six different sounds.

The evidence so far points at the second - six distinct seeds and six distinct prompt ids, but
spectral centroids within 240 Hz of each other - but that is an inference from one prompt. This
measures it directly by rendering the same id three ways: the same prompt at the same seed, the same
prompt at a new seed, and a rewritten prompt at a new seed, then comparing how far each moves the
sound's measured character.

Usage:
    python _seed_vs_prompt_probe.py
"""
import io
import json
import os
import shutil
import sys
import time
import urllib.error
import urllib.request

import numpy as np
import soundfile as sf

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib.util

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location(
    "_generate_sa3", os.path.join(TOOL_DIR, "_generate_sa3.py"))
gen = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gen)

OUT = r"W:\UNNAMED\assets\audio\seed_probe"

AUDIO_ID = "sfx.amb.charwood.stream_detail.01"
BASE_PROMPT = ("close water running over small stones in a woodland stream, fine water detail, "
               "outdoors, continuous small movement")
# Deliberately a different physical scene, not a reworded version of the same one. The question is
# whether the prompt is the lever that moves character; paraphrasing would not answer it.
ALT_PROMPT = ("a thin trickle of water threading between wet rocks in a dark forest, sparse droplets "
              "and small hollow splashes, much quieter and more intermittent, close perspective")


def character(path):
    data, rate = sf.read(path, always_2d=True, dtype="float64")
    mono = data.mean(axis=1)
    spectrum = np.abs(np.fft.rfft(mono * np.hanning(len(mono)))) ** 2
    spectrum = spectrum[1:]
    freqs = np.fft.rfftfreq(len(mono), 1 / rate)[1:]
    centroid = float((spectrum * freqs).sum() / spectrum.sum())
    flatness = float(np.exp(np.mean(np.log(spectrum + 1e-20))) / (spectrum.mean() + 1e-20))
    return {"seconds": round(len(mono) / rate, 2), "centroid": round(centroid),
            "flatness": round(flatness, 4),
            "rms_dbfs": round(20 * np.log10(max(float(np.sqrt(np.mean(mono ** 2))), 1e-12)), 1)}


def render(prompt, seed, label):
    entry = {"prompt": prompt, "seconds": 6.0, "loop": True}
    seconds = gen.generation_seconds(entry)
    prefix = "sa3probe/seedprobe_" + label
    graph = gen.graph({"prompt": prompt}, seed, seconds, prefix)
    queued = gen.post("/prompt", {"prompt": graph, "client_id": "seed-probe"})
    history, elapsed = gen.wait(queued["prompt_id"])
    if history is None or history.get("status", {}).get("status_str") != "success":
        raise SystemExit(f"render {label} failed")
    source = gen.output_path(history.get("outputs", {}))
    os.makedirs(OUT, exist_ok=True)
    target = os.path.join(OUT, f"{label}.flac")
    shutil.copy2(source, target)
    return target, elapsed


def main():
    print("  Rendering the same id three ways. Only one variable changes at a time.")
    print()
    runs = [
        ("A  same prompt, same seed", BASE_PROMPT, gen.seed_for(AUDIO_ID, "a")),
        ("B  same prompt, NEW seed", BASE_PROMPT, gen.seed_for(AUDIO_ID, "d")),
        ("C  REWRITTEN prompt, new seed", ALT_PROMPT, gen.seed_for(AUDIO_ID, "e")),
    ]
    results = {}
    for label, prompt, seed in runs:
        path, elapsed = render(prompt, seed, label.split()[0])
        metrics = character(path)
        results[label] = {"seed": seed, "prompt": prompt, "metrics": metrics,
                          "seconds_to_render": round(elapsed, 1)}
        print(f"  {label}")
        print(f"     seed {seed}   {metrics}   ({elapsed:.1f}s)")

    a = results["A  same prompt, same seed"]["metrics"]
    b = results["B  same prompt, NEW seed"]["metrics"]
    c = results["C  REWRITTEN prompt, new seed"]["metrics"]

    seed_shift = abs(a["centroid"] - b["centroid"])
    prompt_shift = abs(b["centroid"] - c["centroid"])
    print()
    print(f"  centroid moved by  new seed      : {seed_shift} Hz")
    print(f"  centroid moved by  new prompt    : {prompt_shift} Hz")
    print()
    if prompt_shift > seed_shift * 2:
        print("  -> the PROMPT is the lever that moves character. A rejected sound needs new wording,")
        print("     not new seeds: six seeds give six realisations of the same sound.")
    else:
        print("  -> the seed moves character as much as the prompt does, so re-seeding is a valid")
        print("     response to a rejection and something else is wrong.")
    verdict = {"seed_shift_hz": seed_shift, "prompt_shift_hz": prompt_shift,
               "prompt_is_the_lever": prompt_shift > seed_shift * 2}
    with io.open(os.path.join(OUT, "probe.json"), "w", encoding="utf-8") as handle:
        json.dump({"runs": results, "verdict": verdict}, handle, indent=2)
    print(f"\n  wrote {os.path.join(OUT, 'probe.json')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
