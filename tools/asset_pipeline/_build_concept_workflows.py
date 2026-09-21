"""Build reusable concept-generation workflows for the UNNAMED asset pipeline.

Node envelopes are copied from a known-good project workflow and rewired, so the
frontend scaffolding (widget inputs, flags, properties) is real rather than guessed.
Each output workflow is a minimal, self-contained text-to-image graph with a single
prompt box, ready to open in ComfyUI.

Usage:
    python _build_concept_workflows.py
"""
import copy
import json
import os
import sys
import uuid

SOURCE = r"C:\Users\jluca\ComfyUI\user\default\workflows\ZARA_WIDE_REFS_ZImage_ONLY.json"
OUT_DIR = r"C:\Users\jluca\ComfyUI\user\default\workflows"

MODEL = "z_image_turbo_bf16.safetensors"
CLIP_NAME = "thmUNCZImageTE_v10.safetensors"
CLIP_TYPE = "lumina2"
VAE_NAME = "ultrafluxVAEImproved_v10.safetensors"
LLM_ENDPOINT = "http://localhost:11434/v1"
LLM_MODEL = "orcarouter/Qwen3.8-27B-Uncensored:latest"

# Shared negative prompt: concepts feed a 3D generator, so anything that muddies the
# silhouette or invents a second object makes the mesh worse.
NEGATIVE = ("multiple objects, two objects, cropped, cut off, partial object, "
            "text, watermark, signature, logo, border, frame, blurry, low detail, "
            "cluttered background, busy background, scene, hands, people")

# Appended to every prompt so the 3D pass gets one clean isolated subject.
SUFFIX = (", single object centred in frame, whole object visible with margin, "
          "isolated on a plain flat light grey background, even neutral studio "
          "lighting, sharp focus, high detail, game asset concept art, no text, "
          "no watermark, no border, no extra objects")

RECIPES = [
    {
        "file": "CONCEPT_weapons_props_3D.json",
        "title": "Concept - Weapons and Props (feeds 3D)",
        "width": 1024,
        "height": 1024,
        "prompt": ("game asset concept art of a single-handed iron war axe, bearded "
                   "axe head with a rune-etched chiselled edge, dark stained oak haft "
                   "with leather cord grip, northern frontier smithing, battle-worn "
                   "but intact, three-quarter view"),
        "prefix": "concepts/weapon",
    },
    {
        "file": "CONCEPT_creature.json",
        "title": "Concept - Creature (feeds 3D)",
        "width": 1024,
        "height": 1024,
        "prompt": ("game asset concept art of a lean northern frost wolf, thick pale "
                   "grey and white winter coat, long muzzle, erect ears, yellow eyes, "
                   "standing alert in a neutral pose with all four legs visible and "
                   "clearly separated, naturalistic creature design, three-quarter view"),
        "prefix": "concepts/creature",
    },
    {
        "file": "CONCEPT_icon_ui.json",
        "title": "Concept - Icon and UI",
        "width": 768,
        "height": 768,
        "prompt": ("game UI icon of a healing spell, a stylised glowing green cross "
                   "over a small clay flask, bold simple shape, strong readable "
                   "silhouette, thick dark outline, flat painted shading, high contrast, "
                   "centred, fills most of the frame"),
        "prefix": "concepts/icon",
    },
    {
        "file": "CONCEPT_pbr_material.json",
        "title": "Concept - PBR Material Source",
        "width": 1024,
        "height": 1024,
        "prompt": ("seamless tileable PBR texture of weathered grey granite stone "
                   "blocks, flat even diffuse lighting, no shadows, no highlights, "
                   "uniform mortar lines, top-down orthographic view, high frequency "
                   "surface detail, perfectly repeating pattern"),
        "prefix": "concepts/material",
    },
]


def load_source_nodes():
    with open(SOURCE, encoding="utf-8") as handle:
        workflow = json.load(handle)
    wanted = ("DiffusionModelLoaderKJ", "CLIPLoader", "VAELoader",
              "Z_ImageAPIConfig", "Z_ImageIntegratedKSampler", "SaveImage")
    found = {}
    for node in workflow["nodes"]:
        if node["type"] in wanted and node["type"] not in found:
            found[node["type"]] = copy.deepcopy(node)
    missing = [name for name in wanted if name not in found]
    if missing:
        raise RuntimeError(f"source workflow is missing node types: {missing}")
    return found


def template(source, new_id, pos):
    node = copy.deepcopy(source)
    node["id"] = new_id
    node["pos"] = list(pos)
    node["flags"] = {}
    node["order"] = 0
    if "outputs" in node:
        for output in node["outputs"]:
            output["links"] = []
    if "inputs" in node:
        for node_input in node["inputs"]:
            node_input["link"] = None
    return node


def build(recipe, source, prompt_widget):
    nodes = []
    links = []
    link_id = 0

    loader = template(source["DiffusionModelLoaderKJ"], 1, (0, 0))
    loader["widgets_values"] = [MODEL, "default", "default", False, "disabled", True]
    nodes.append(loader)

    clip = template(source["CLIPLoader"], 2, (0, 240))
    clip["widgets_values"] = [CLIP_NAME, CLIP_TYPE, "default"]
    nodes.append(clip)

    vae = template(source["VAELoader"], 3, (0, 380))
    vae["widgets_values"] = [VAE_NAME]
    nodes.append(vae)

    config = template(source["Z_ImageAPIConfig"], 4, (0, 480))
    # Prompt enhancement stays off: it would call an LLM per render and make results
    # unreproducible from the workflow alone.
    config["widgets_values"] = ["local", LLM_MODEL, "", LLM_ENDPOINT, "", False, "none", "auto"]
    nodes.append(config)

    sampler = template(source["Z_ImageIntegratedKSampler"], 5, (460, 0))
    # ComfyUI inserts a hidden control_after_generate widget immediately after seed,
    # so it occupies a slot in widgets_values. Omitting it shifts every later value
    # by one and silently mis-assigns steps, cfg, sampler and scheduler.
    sampler["widgets_values"] = [
        recipe["prompt"] + SUFFIX, NEGATIVE, "text_to_image",
        recipe["width"], recipe["height"], 12345, "randomize",
        8, 1.1, "euler_flow", "simple", 1,
        "custom", False, 1, 0, 0, True, True, "",
        recipe["prefix"], "",
    ]
    nodes.append(sampler)

    save = template(source["SaveImage"], 6, (940, 0))
    save["widgets_values"] = [recipe["prefix"]]
    nodes.append(save)

    def connect(from_id, from_slot, to_id, to_name, slot_type):
        nonlocal link_id
        link_id += 1
        target = next(node for node in nodes if node["id"] == to_id)
        source_node = next(node for node in nodes if node["id"] == from_id)
        target_slot = None
        for index, node_input in enumerate(target["inputs"]):
            if node_input["name"] == to_name:
                node_input["link"] = link_id
                target_slot = index
        for output in source_node["outputs"]:
            if output.get("slot_index") == from_slot:
                output["links"].append(link_id)
        links.append([link_id, from_id, from_slot, to_id, target_slot, slot_type])

    connect(1, 0, 5, "model", "MODEL")
    connect(2, 0, 5, "clip", "CLIP")
    connect(3, 0, 5, "vae", "VAE")
    connect(4, 0, 5, "config", "ZIMAGE_CONFIG")
    connect(5, 0, 6, "images", "IMAGE")

    for index, node in enumerate(nodes):
        node["order"] = index

    return {
        "id": str(uuid.uuid4()),
        "revision": 0,
        "last_node_id": max(node["id"] for node in nodes),
        "last_link_id": link_id,
        "nodes": nodes,
        "links": links,
        "groups": [],
        "config": {},
        "extra": {"ds": {"scale": 0.85, "offset": [120, 160]}},
        "version": 0.4,
    }


def main():
    source = load_source_nodes()
    for recipe in RECIPES:
        workflow = build(recipe, source, recipe["prompt"])
        path = os.path.join(OUT_DIR, recipe["file"])
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(workflow, handle, indent=2)
        print(f"wrote {recipe['file']}  ({len(workflow['nodes'])} nodes, "
              f"{len(workflow['links'])} links)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
