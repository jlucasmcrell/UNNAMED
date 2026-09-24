"""Submit a ComfyUI prompt and print the server's full rejection reason.

An HTTP 400 from /prompt means validation failed, and ComfyUI puts the specific offending
node and field in the response body. Getting that body is far faster than bisecting a
workflow by guesswork, which is what this exists to avoid.

Usage:
    python _diag_submit.py --server http://127.0.0.1:18190 --request path.json
"""
import argparse
import json
import sys
import urllib.error
import urllib.request


def build_prompt(request_path, model, clip, clip_type, vae, width, height, steps, seed):
    """The same graph `_make_concepts.py` builds, so a failure here reproduces a failure there."""
    with open(request_path, encoding="utf-8") as handle:
        requests = json.load(handle)
    first = requests[0]
    return {
        "1": {"class_type": "DiffusionModelLoaderKJ",
              "inputs": {"model_name": model, "weight_dtype": "default",
                         "compute_dtype": "default", "patch_cublaslinear": False,
                         "sage_attention": "disabled",
                         "enable_fp16_accumulation": False}},
        "2": {"class_type": "CLIPLoader",
              "inputs": {"clip_name": clip, "type": clip_type}},
        "3": {"class_type": "VAELoader", "inputs": {"vae_name": vae}},
        "4": {"class_type": "Z_ImageAPIConfig",
              "inputs": {"width": width, "height": height, "steps": steps,
                         "seed": seed, "prompt": first["prompt"]}},
        "5": {"class_type": "Z_ImageIntegratedKSampler",
              "inputs": {"model": ["1", 0], "clip": ["2", 0], "vae": ["3", 0],
                         "config": ["4", 0]}},
        "6": {"class_type": "SaveImage",
              "inputs": {"images": ["5", 0], "filename_prefix": "diag"}},
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", required=True)
    parser.add_argument("--request", required=True)
    parser.add_argument("--model", default="z_image_turbo_bf16.safetensors")
    parser.add_argument("--clip", default="thmUNCZImageTE_v10.safetensors")
    parser.add_argument("--clip-type", default="lumina2")
    parser.add_argument("--vae", default="ultrafluxVAEImproved_v10.safetensors")
    parser.add_argument("--width", type=int, default=1536)
    parser.add_argument("--height", type=int, default=1536)
    parser.add_argument("--steps", type=int, default=24)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    prompt = build_prompt(args.request, args.model, args.clip, args.clip_type,
                          args.vae, args.width, args.height, args.steps, args.seed)
    payload = json.dumps({"prompt": prompt}).encode()
    if args.dry_run:
        print(json.dumps(prompt, indent=2))
        return 0

    request = urllib.request.Request(
        args.server.rstrip("/") + "/prompt", data=payload,
        headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            print("ACCEPTED " + response.read().decode()[:400])
        return 0
    except urllib.error.HTTPError as exc:
        body = exc.read().decode(errors="replace")
        print(f"REJECTED {exc.code}")
        # ComfyUI returns {"error": {...}, "node_errors": {...}} - print both in full.
        try:
            parsed = json.loads(body)
            print("error      :", json.dumps(parsed.get("error"), indent=2)[:1200])
            nodes = parsed.get("node_errors") or {}
            if nodes:
                print("node_errors:")
                for node_id, detail in nodes.items():
                    print(f"  node {node_id}: {json.dumps(detail)[:600]}")
        except ValueError:
            print(body[:1200])
        return 1


if __name__ == "__main__":
    sys.exit(main())
