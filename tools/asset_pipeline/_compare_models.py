r"""Render one prompt across several diffusion models so the results can be compared.

Prompt wording and model interact strongly, and the only reliable way to pick a model
for a specific subject is to render the same text on each and look.

Usage:
    python _compare_models.py --prompt-file prompt.txt --out review\compare --name glyph
"""
import argparse
import importlib.util
import os
import sys
import time

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("mc", os.path.join(TOOL_DIR, "_make_concepts.py"))
mc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mc)

MODELS = [
    "z_image_turbo_bf16.safetensors",
    "z_image_turbo_bf16-DF11.safetensors",
    "zitPrism_v17.safetensors",
    "zImageTurboNSFW_92BF16FP8.safetensors",
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--out", required=True, help="Folder for the comparison renders")
    parser.add_argument("--name", default="compare")
    parser.add_argument("--width", type=int, default=832)
    parser.add_argument("--height", type=int, default=1216)
    parser.add_argument("--steps", type=int, default=10)
    parser.add_argument("--seed", type=int, default=12345,
                        help="Same seed on every model, so differences are the model's")
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt_text = handle.read().strip()

    os.makedirs(args.out, exist_ok=True)
    written = []
    for model in MODELS:
        label = model.replace(".safetensors", "")
        stem = f"{args.name}__{label}"
        target = os.path.join(args.out, f"{stem}.png")
        if os.path.exists(target):
            print(f"  {label}: exists, skipped")
            written.append(target)
            continue

        # Build the graph directly so the model can be swapped per render.
        request = {
            "id": stem,
            "prompt": prompt_text,
            "width": args.width,
            "height": args.height,
            "steps": args.steps,
            "seed": args.seed,
        }
        original = mc.CONCEPT_MODEL
        original_suffix = mc.CONCEPT_SUFFIX
        mc.CONCEPT_MODEL = model
        # The supplied prompt already carries its own framing and lighting language.
        mc.CONCEPT_SUFFIX = ""
        try:
            graph = mc.build_prompt(request, f"modelcmp/{stem}")
        finally:
            mc.CONCEPT_MODEL = original
            mc.CONCEPT_SUFFIX = original_suffix

        started = time.time()
        submitted = mc.post("/prompt", {"prompt": graph, "client_id": mc.uuid.uuid4().hex})
        if submitted.get("node_errors"):
            print(f"  {label}: node_errors {str(submitted['node_errors'])[:200]}")
            continue
        images, status, _elapsed = mc.wait_for(submitted["prompt_id"], label)
        if not images:
            print(f"  {label}: {status}")
            continue

        source = os.path.join(mc.os.environ.get("UNNAMED_COMFY_OUTPUT",
                                                r"C:\Users\jluca\ComfyUI\output"),
                              images[0].get("subfolder") or "", images[0]["filename"])
        with open(source, "rb") as handle:
            payload = handle.read()
        with open(target, "wb") as handle:
            handle.write(payload)
        os.remove(source)
        print(f"  {label}: {status} ({time.time() - started:.0f}s) -> {os.path.basename(target)}")
        written.append(target)

    print(f"\n{len(written)} render(s) in {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
