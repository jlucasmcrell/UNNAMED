"""Generate music or ambience with ACE-Step 1.5 through ComfyUI.

ACE-Step is MIT-licensed with compliant training data, so its output is safe to ship.
This drives it the same way the rest of the pipeline drives ComfyUI: build the graph and
POST it, no UI involved.

The graph follows the official template `audio_ace_step1_5_xl_turbo` exactly. Three
details are easy to get wrong and were:

  * DualCLIPLoader with type "ace" and BOTH encoders, not CLIPLoader. The 0.6B encoder
    alone is enough for the CLIP slot but the loader expects a second name.
  * ModelSamplingAuraFlow with shift 3 between the UNET and the sampler. Omitting it
    makes KSampler fail with "'NoneType' object has no attribute 'shape'".
  * Negative conditioning is ConditioningZeroOut of the positive prompt, not a second
    text encode.

Usage:
    python _make_music.py --tags "dark ambient, low struck stone" --seconds 30 ^
        --out W:\\UNNAMED\\assets\\audio\\ambience --name amb_kal_hold
"""
import argparse
import json
import os
import shutil
import sys
import time
import urllib.error
import urllib.request
import uuid

SERVER = os.environ.get("UNNAMED_COMFY_SERVER", "http://127.0.0.1:8188")
COMFY_OUTPUT = os.environ.get("UNNAMED_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output")

MODEL = os.environ.get("UNNAMED_MUSIC_MODEL", "acestep_v1.5_xl_turbo_bf16.safetensors")
CLIP_SMALL = os.environ.get("UNNAMED_MUSIC_CLIP1", "qwen_0.6b_ace15.safetensors")
CLIP_LARGE = os.environ.get("UNNAMED_MUSIC_CLIP2", "qwen_4b_ace15.safetensors")
VAE = os.environ.get("UNNAMED_MUSIC_VAE", "ace_1.5_vae.safetensors")
SHIFT = 3.0
HTTP_TIMEOUT = int(os.environ.get("UNNAMED_HTTP_TIMEOUT", 900))


def get(path):
    with urllib.request.urlopen(f"{SERVER}{path}", timeout=HTTP_TIMEOUT) as response:
        return json.loads(response.read())


def post(path, payload):
    request = urllib.request.Request(
        f"{SERVER}{path}",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=HTTP_TIMEOUT) as response:
        return json.loads(response.read())


def build(args):
    return {
        "1": {"class_type": "UNETLoader",
              "inputs": {"unet_name": MODEL, "weight_dtype": "default"}},
        "2": {"class_type": "DualCLIPLoader",
              "inputs": {"clip_name1": CLIP_SMALL, "clip_name2": CLIP_LARGE,
                         "type": "ace", "device": "default"}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": VAE}},
        "4": {"class_type": "ModelSamplingAuraFlow",
              "inputs": {"model": ["1", 0], "shift": args.shift}},
        "5": {"class_type": "TextEncodeAceStepAudio1.5",
              "inputs": {"clip": ["2", 0], "tags": args.tags, "lyrics": args.lyrics,
                         "seed": args.seed, "bpm": args.bpm,
                         "duration": float(args.seconds),
                         "timesignature": args.timesignature,
                         "language": args.language, "keyscale": args.keyscale,
                         "generate_audio_codes": True,
                         "cfg_scale": 2.0, "temperature": 0.85,
                         "top_p": 0.9, "top_k": 0, "min_p": 0.0}},
        "6": {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["5", 0]}},
        "7": {"class_type": "EmptyAceStep1.5LatentAudio",
              "inputs": {"seconds": float(args.seconds), "batch_size": 1}},
        "8": {"class_type": "KSampler",
              "inputs": {"model": ["4", 0], "seed": args.seed, "steps": args.steps,
                         "cfg": args.cfg, "sampler_name": args.sampler,
                         "scheduler": args.scheduler, "positive": ["5", 0],
                         "negative": ["6", 0], "latent_image": ["7", 0],
                         "denoise": 1.0}},
        "9": {"class_type": "VAEDecodeAudio", "inputs": {"samples": ["8", 0], "vae": ["3", 0]}},
        "10": {"class_type": "SaveAudioAdvanced",
               "inputs": {"audio": ["9", 0], "filename_prefix": args.prefix,
                          "format": args.format, "format.quality": args.quality}},
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tags", required=True, help="Style and instrumentation prompt")
    parser.add_argument("--lyrics", default="", help="Empty for instrumental")
    parser.add_argument("--seconds", type=float, default=30.0)
    parser.add_argument("--bpm", type=int, default=70)
    parser.add_argument("--steps", type=int, default=8, help="XL-turbo is trained for 8")
    parser.add_argument("--cfg", type=float, default=1.0)
    parser.add_argument("--shift", type=float, default=SHIFT)
    parser.add_argument("--sampler", default="euler")
    parser.add_argument("--scheduler", default="simple")
    parser.add_argument("--timesignature", default="4")
    parser.add_argument("--language", default="en")
    parser.add_argument("--keyscale", default="A minor")
    parser.add_argument("--seed", type=int, default=1234)
    parser.add_argument("--format", default="flac", choices=["flac", "mp3", "opus"])
    parser.add_argument("--quality", default="V0")
    parser.add_argument("--out", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--prefix", default=None)
    args = parser.parse_args()

    if args.prefix is None:
        args.prefix = f"music/{args.name}"

    submitted = None
    for attempt in range(2):
        try:
            submitted = post("/prompt", {"prompt": build(args),
                                         "client_id": uuid.uuid4().hex})
            break
        except urllib.error.URLError as exc:
            if attempt:
                print(f"submit failed: {exc}")
                return 1
            print(f"  server unreachable ({exc}); retrying in 15s", flush=True)
            time.sleep(15)
    if submitted is None:
        return 1

    if submitted.get("node_errors"):
        print("validation failed:")
        print(json.dumps(submitted["node_errors"], indent=2)[:3000])
        return 1

    prompt_id = submitted["prompt_id"]
    print(f"prompt_id: {prompt_id}", flush=True)

    started = time.time()
    while time.time() - started < 3600:
        try:
            history = get(f"/history/{prompt_id}")
        except urllib.error.URLError:
            time.sleep(10)
            continue
        if prompt_id in history:
            entry = history[prompt_id]
            status = entry.get("status", {})
            if status.get("status_str") == "error":
                print("execution error:")
                print(json.dumps(status, indent=2)[:3000])
                return 1
            for node in entry.get("outputs", {}).values():
                for item in (node.get("audio") or []):
                    source = os.path.join(COMFY_OUTPUT, item.get("subfolder") or "",
                                          item["filename"])
                    os.makedirs(args.out, exist_ok=True)
                    ext = os.path.splitext(item["filename"])[1]
                    target = os.path.join(args.out, f"{args.name}{ext}")
                    shutil.copy2(source, target)
                    os.remove(source)
                    print(f"wrote {target}  ({os.path.getsize(target)/1e6:.2f} MB)  "
                          f"in {time.time()-started:.0f}s")
                    return 0
            print("no audio in outputs")
            return 1
        time.sleep(5)

    print("timed out waiting for the prompt")
    return 1


if __name__ == "__main__":
    sys.exit(main())
