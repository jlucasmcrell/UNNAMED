"""Turn request JSON files into LoadPromptsFromFile prompt files.

The Inspire loader splits on lines of dashes and expects each block to carry
`positive:` / `negative:` / `name:` keys, so it can drive a workflow that walks a
whole file unattended. This writes those files from the pipeline's request format.

Usage:
    python _requests_to_promptfiles.py
"""
import json
import os
import sys

ASSETS = r"W:\UNNAMED\assets"
REQUESTS = os.path.join(ASSETS, "requests")
PROMPT_ROOT = r"C:\Users\jluca\ComfyUI\custom_nodes\comfyui-inspire-pack\prompts"
OUT_SUBDIR = "UNNAMED"

# Appended by the concept pipeline for 3D-source renders. Kept in sync with
# _make_concepts.CONCEPT_SUFFIX so a prompt file produces the same image.
CONCEPT_SUFFIX = (
    ", single object centred in frame, whole object visible with margin, "
    "isolated on a plain flat light grey background, even neutral studio lighting, "
    "sharp focus, high detail, game asset concept art, no text, no watermark, "
    "no border, no extra objects"
)

ICON_SUFFIX = (
    ", bold simple shape filling most of the frame, strong readable silhouette, "
    "thick dark outline, flat painted shading, high contrast, game UI icon art, "
    "no text, no watermark, no border"
)

NEGATIVE = ("multiple objects, two objects, cropped, cut off, partial object, "
            "text, watermark, signature, logo, border, frame, blurry, low detail, "
            "cluttered background, busy background, scene, hands, people")

FILES = [
    ("overnight_weapons.json", "UNNAMED_weapons.txt", CONCEPT_SUFFIX),
    ("overnight_props.json", "UNNAMED_props.txt", CONCEPT_SUFFIX),
    ("overnight_creatures.json", "UNNAMED_creatures.txt", CONCEPT_SUFFIX),
    ("overnight_icons.json", "UNNAMED_icons.txt", ICON_SUFFIX),
    ("overnight_materials.json", "UNNAMED_materials.txt", CONCEPT_SUFFIX),
]


def write_prompt_file(source, destination, suffix):
    with open(source, encoding="utf-8") as handle:
        requests = json.load(handle)

    blocks = []
    for request in requests:
        blocks.append(
            f"positive: {request['prompt']}{suffix}\n"
            f"negative: {NEGATIVE}\n"
            f"name: {request['id']}"
        )

    os.makedirs(os.path.dirname(destination), exist_ok=True)
    with open(destination, "w", encoding="utf-8") as handle:
        handle.write("\n---\n".join(blocks) + "\n")
    return len(requests)


def main():
    out_dir = os.path.join(PROMPT_ROOT, OUT_SUBDIR)
    total = 0
    for source_name, dest_name, suffix in FILES:
        source = os.path.join(REQUESTS, source_name)
        if not os.path.exists(source):
            print(f"skip  {source_name} (not written yet)")
            continue
        count = write_prompt_file(source, os.path.join(out_dir, dest_name), suffix)
        total += count
        print(f"wrote {OUT_SUBDIR}\\{dest_name}  ({count} prompts)")
    print(f"\n{total} prompts in {out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
