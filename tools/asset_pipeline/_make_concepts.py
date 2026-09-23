"""Generate concept images from an asset request list, ready to feed the 3D pass.

Reads a JSON request file, renders each entry with Z-Image Turbo, and writes PNGs
into an output folder named after the asset id so the 3D pipeline picks them up
without renaming.

Requests file format:
[
  {"id": "weapon_iron_war_axe", "prompt": "game asset concept art, ..."},
  {"id": "icon_heal", "prompt": "...", "width": 768, "height": 768, "seed": 42}
]

Usage:
    python _make_concepts.py requests.json
    python _make_concepts.py requests.json --limit 3 --batch-size 4
"""
import argparse
import json
import os
import time
import urllib.request
import uuid

SERVER = os.environ.get("UNNAMED_COMFY_SERVER", "http://127.0.0.1:8188")
CONCEPT_MODEL = os.environ.get("UNNAMED_CONCEPT_MODEL", "z_image_turbo_bf16.safetensors")
CONCEPT_CLIP = os.environ.get("UNNAMED_CONCEPT_CLIP", "thmUNCZImageTE_v10.safetensors")
CONCEPT_CLIP_TYPE = os.environ.get("UNNAMED_CONCEPT_CLIP_TYPE", "lumina2")
CONCEPT_VAE = os.environ.get("UNNAMED_CONCEPT_VAE", "ultrafluxVAEImproved_v10.safetensors")
# Sampler availability differs between ComfyUI installs: euler_flow only exists on
# BEAST's build of the z-image node, while RAZER offers 44 core samplers. euler is
# common to both and is what the original Glyph reference used.
CONCEPT_SAMPLER = os.environ.get("UNNAMED_CONCEPT_SAMPLER", "euler_flow")
LLM_ENDPOINT = os.environ.get("UNNAMED_LLM_ENDPOINT", "http://localhost:11434/v1")
LLM_MODEL = os.environ.get("UNNAMED_LLM_MODEL", "orcarouter/Qwen3.8-27B-Uncensored:latest")
DEFAULT_OUT = r"W:\UNNAMED\assets\concepts"

# Z-Image Turbo is a distilled model: few steps, low cfg.
# Quality defaults. This pipeline is not time-constrained, and concept quality caps
# everything downstream: the 3D step reconstructs from these images, so more steps and
# a taller canvas buy real surface definition. Raise further if it still looks soft.
STEPS = 24
CFG = 1.1
DEFAULT_WIDTH = 1536
DEFAULT_HEIGHT = 1536

# Every concept goes to the 3D pass, which needs one clean isolated subject on a
# plain background. These are appended so individual prompts stay about the asset.
# Z-Image Turbo does not honour negation in a positive prompt, so "no text" here is
# close to decorative: a labelled object still came back with gibberish lettering. The
# prohibition is therefore written positively as well, and any prompt that mentions a
# label, ledger, sign or inscription describes it as blank or unmarked instead.
CONCEPT_SUFFIX = (
    ", single object centred in frame, whole object visible with margin, "
    "isolated on a plain flat light grey background, even neutral studio lighting, "
    "sharp focus, high detail, game asset concept art, the surface entirely plain and "
    "unmarked, a completely blank bare finish with no lettering of any kind, "
    "no text, no watermark, no border, no extra objects"
)

# Icons are not 3D sources: they have to fill the frame and read at small sizes, so
# the isolated-on-grey framing above would actively hurt them.
ICON_SUFFIX = (
    ", bold simple shape filling most of the frame, strong readable silhouette, "
    "thick dark outline, flat painted shading, high contrast, game UI icon art, "
    "no text, no watermark, no border"
)


def get(path):
    with urllib.request.urlopen(f"{SERVER}{path}", timeout=180) as response:
        return json.loads(response.read())


def post(path, payload):
    request = urllib.request.Request(
        f"{SERVER}{path}",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(request, timeout=180) as response:
        return json.loads(response.read())


def build_prompt(request, prefix):
    width = request.get("width", DEFAULT_WIDTH)
    height = request.get("height", DEFAULT_HEIGHT)
    # A request may override the shared suffix when the target is not a 3D source
    # (icons need to fill the frame rather than sit on a plain backdrop).
    suffix = request.get("suffix", CONCEPT_SUFFIX)
    return {
        "1": {"class_type": "DiffusionModelLoaderKJ",
              "inputs": {"model_name": CONCEPT_MODEL, "weight_dtype": "default",
                         "compute_dtype": "default", "patch_cublaslinear": False,
                         "sage_attention": "disabled", "enable_fp16_accumulation": False}},
        "2": {"class_type": "CLIPLoader",
              "inputs": {"clip_name": CONCEPT_CLIP, "type": CONCEPT_CLIP_TYPE}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": CONCEPT_VAE}},
        "4": {"class_type": "Z_ImageAPIConfig",
              "inputs": {"provider": "local", "model": LLM_MODEL,
                         "local_endpoint": LLM_ENDPOINT}},
        "5": {"class_type": "Z_ImageIntegratedKSampler",
              "inputs": {"model": ["1", 0], "clip": ["2", 0], "vae": ["3", 0],
                         "config": ["4", 0],
                         "positive_prompt": request["prompt"] + suffix,
                         "negative_prompt": request.get("negative", ""),
                         "generation_mode": "text_to_image",
                         "width": width, "height": height,
                         "seed": request.get("seed", 0),
                         "steps": request.get("steps", STEPS),
                         "cfg": request.get("cfg", CFG),
                         "sampler_name": CONCEPT_SAMPLER, "scheduler": "simple",
                         "denoise": 1.0,
                         # Default is True, which calls an external LLM per image.
                         # Concepts must be reproducible from the request file.
                         "enable_prompt_enhance": False,
                         "batch_size": request.get("batch_size", 1),
                         "output_prefix": prefix}},
        "6": {"class_type": "SaveImage",
              "inputs": {"images": ["5", 0], "filename_prefix": prefix}},
    }


def wait_for(prompt_id, label):
    started = time.time()
    while True:
        history = get(f"/history/{prompt_id}")
        if prompt_id in history:
            entry = history[prompt_id]
            status = entry.get("status", {})
            elapsed = time.time() - started
            images = []
            for output in (entry.get("outputs") or {}).values():
                images.extend(output.get("images", []))
            if status.get("status_str") != "success":
                for message in status.get("messages", []):
                    if message[0] in ("execution_error", "execution_interrupted"):
                        print(json.dumps(message[1], indent=2)[:1500])
            return images, status.get("status_str"), elapsed
        if time.time() - started > 900:
            return [], "timeout", time.time() - started
        time.sleep(5)


def write_atomically(target, data, attempts=4):
    """Write bytes to target, replacing it only once the write has fully succeeded.

    Writing straight to the destination has failed twice on this project with
    `OSError: [Errno 22] Invalid argument` while writing to a network share, and both
    times it left a truncated file behind that looked like a normal render. Writing to a
    temporary file and renaming means a failure leaves the previous version intact
    instead of a corrupt one, and the retry covers the transient case.
    """
    directory = os.path.dirname(target)
    temporary = os.path.join(directory, f".{os.path.basename(target)}.part")
    for attempt in range(1, attempts + 1):
        try:
            with open(temporary, "wb") as handle:
                handle.write(data)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, target)
            return True
        except OSError as exc:
            print(f"    write attempt {attempt}/{attempts} failed on "
                  f"{os.path.basename(target)}: {exc}")
            time.sleep(2 * attempt)
    try:
        os.remove(temporary)
    except OSError:
        pass
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("requests", help="JSON file listing assets to concept")
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--force", action="store_true",
                        help="Re-render concepts that already exist, e.g. at higher settings")
    parser.add_argument("--skip-existing", action="store_true",
                        help="Do not regenerate concepts already on disk")
    args = parser.parse_args()

    with open(args.requests, encoding="utf-8") as handle:
        requests = json.load(handle)
    if args.limit:
        requests = requests[:args.limit]

    os.makedirs(args.out, exist_ok=True)
    print(f"{len(requests)} concept(s) -> {args.out}")
    print()

    written = []
    started = time.time()
    for index, request in enumerate(requests, 1):
        asset_id = request["id"]
        target = os.path.join(args.out, f"{asset_id}.png")
        if request.get("approved") and os.path.exists(target):
            print(f"[{index}/{len(requests)}] {asset_id}: APPROVED, never regenerated")
            written.append(target)
            continue
        if args.skip_existing and not args.force and os.path.exists(target):
            print(f"[{index}/{len(requests)}] {asset_id}: exists, skipped")
            written.append(target)
            continue

        # ComfyUI writes to its own output tree; the prefix namespaces the run.
        prefix = f"concepts/{asset_id}"
        submitted = post("/prompt", {"prompt": build_prompt(request, prefix),
                                     "client_id": uuid.uuid4().hex})
        if submitted.get("node_errors"):
            print(f"[{index}/{len(requests)}] {asset_id}: node_errors")
            print(json.dumps(submitted["node_errors"], indent=2)[:1200])
            continue

        images, status, elapsed = wait_for(submitted["prompt_id"], asset_id)
        if not images:
            print(f"[{index}/{len(requests)}] {asset_id}: {status} ({elapsed:.0f}s)")
            continue

        # Take the first render as the concept for this asset. The source may live on
        # another machine's share when rendering is offloaded, so a failed cleanup
        # must not lose the render that was already written.
        source = os.path.join(
            os.environ.get("UNNAMED_COMFY_OUTPUT", r"C:\Users\jluca\ComfyUI\output"),
            images[0].get("subfolder") or "", images[0]["filename"])
        with open(source, "rb") as handle:
            data = handle.read()
        if not write_atomically(target, data):
            print(f"[{index}/{len(requests)}] {asset_id}: RENDER LOST, could not write "
                  f"{os.path.basename(target)}")
            continue
        try:
            os.remove(source)
        except OSError:
            # A locked or read-only share leaves the render in place; harmless, but
            # say so rather than failing silently.
            print(f"    (kept source {os.path.basename(source)}; could not remove it)")
        print(f"[{index}/{len(requests)}] {asset_id}: {status} ({elapsed:.0f}s) "
              f"-> {os.path.basename(target)}")
        written.append(target)

    print()
    print(f"{len(written)}/{len(requests)} concepts in {time.time() - started:.0f}s")
    return 0 if len(written) == len(requests) else 1


if __name__ == "__main__":
    raise SystemExit(main())
