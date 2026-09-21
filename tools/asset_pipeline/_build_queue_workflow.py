"""Build a prompt-file-driven concept workflow (LoadPromptsFromFile + easy seed).

This is the unattended batch pattern: point LoadPromptsFromFile at a prompt file,
let easy seed drive start_index so each queued run advances to the next prompt, and
let ComfyUI walk the whole file. Node envelopes are reused from a known-good
project workflow so the frontend scaffolding is real.

Usage:
    python _build_queue_workflow.py
"""
import copy
import json
import os
import sys
import uuid

SOURCE = r"C:\Users\jluca\ComfyUI\user\default\workflows\ZARA_WIDE_REFS_ZImage_ONLY.json"
OUT_DIR = r"C:\Users\jluca\ComfyUI\user\default\workflows"
PROMPT_FILE = "UNNAMED\\UNNAMED_weapons.txt"

MODEL = "z_image_turbo_bf16.safetensors"
CLIP_NAME = "thmUNCZImageTE_v10.safetensors"
CLIP_TYPE = "lumina2"
VAE_NAME = "ultrafluxVAEImproved_v10.safetensors"
LLM_ENDPOINT = "http://localhost:11434/v1"
LLM_MODEL = "orcarouter/Qwen3.8-27B-Uncensored:latest"

WANTED = ("DiffusionModelLoaderKJ", "CLIPLoader", "VAELoader", "Z_ImageAPIConfig",
          "Z_ImageIntegratedKSampler", "SaveImage",
          "LoadPromptsFromFile //Inspire", "UnzipPrompt //Inspire", "easy seed")


def load_source_nodes():
    with open(SOURCE, encoding="utf-8") as handle:
        workflow = json.load(handle)
    found = {}
    for node in workflow["nodes"]:
        if node["type"] in WANTED and node["type"] not in found:
            found[node["type"]] = copy.deepcopy(node)
    missing = [name for name in WANTED if name not in found]
    if missing:
        raise RuntimeError(f"source workflow is missing node types: {missing}")
    return found


def template(source, new_id, pos):
    node = copy.deepcopy(source)
    node["id"] = new_id
    node["pos"] = list(pos)
    node["flags"] = {}
    node["order"] = 0
    for output in node.get("outputs", []):
        output["links"] = []
    for node_input in node.get("inputs", []):
        node_input["link"] = None
    return node


def main():
    source = load_source_nodes()
    nodes = []
    links = []
    link_id = 0

    loader = template(source["DiffusionModelLoaderKJ"], 1, (0, 0))
    loader["widgets_values"] = [MODEL, "default", "default", False, "disabled", True]
    nodes.append(loader)

    clip = template(source["CLIPLoader"], 2, (0, 250))
    clip["widgets_values"] = [CLIP_NAME, CLIP_TYPE, "default"]
    nodes.append(clip)

    vae = template(source["VAELoader"], 3, (0, 390))
    vae["widgets_values"] = [VAE_NAME]
    nodes.append(vae)

    config = template(source["Z_ImageAPIConfig"], 4, (0, 490))
    config["widgets_values"] = ["local", LLM_MODEL, "", LLM_ENDPOINT, "", False, "none", "auto"]
    nodes.append(config)

    # --- prompt file walk ---
    seed = template(source["easy seed"], 5, (0, 700))
    # increment: each queued run advances start_index, which is what makes an
    # overnight queue walk the file instead of re-rendering prompt 0.
    seed["widgets_values"] = [0, "increment", None]
    nodes.append(seed)

    prompt_file = template(source["LoadPromptsFromFile //Inspire"], 6, (400, 700))
    # Schema order is [prompt_file, text_data_opt, reload, load_cap, start_index].
    # text_data_opt must stay empty: a truthy value there makes the node ignore the
    # file and treat that value as the prompt text. start_index is a placeholder
    # because it is supplied by the easy seed link.
    prompt_file["widgets_values"] = [PROMPT_FILE, "", True, 0, 0]
    nodes.append(prompt_file)

    unzip = template(source["UnzipPrompt //Inspire"], 7, (760, 700))
    unzip["widgets_values"] = []
    nodes.append(unzip)

    sampler = template(source["Z_ImageIntegratedKSampler"], 8, (1130, 0))
    sampler["widgets_values"] = [
        "", "", "text_to_image", 1024, 1024, 0, "randomize",
        8, 1.1, "euler_flow", "simple", 1,
        "custom", False, 1, 0, 0, True, True, "",
        "concepts", "",
    ]
    nodes.append(sampler)

    save = template(source["SaveImage"], 9, (1610, 0))
    save["widgets_values"] = ["concepts"]
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

    connect(1, 0, 8, "model", "MODEL")
    connect(2, 0, 8, "clip", "CLIP")
    connect(3, 0, 8, "vae", "VAE")
    connect(4, 0, 8, "config", "ZIMAGE_CONFIG")
    connect(5, 0, 6, "start_index", "INT")
    connect(6, 0, 7, "zipped_prompt", "ZIPPED_PROMPT")
    connect(7, 0, 8, "positive_prompt", "STRING")
    connect(8, 0, 9, "images", "IMAGE")

    for index, node in enumerate(nodes):
        node["order"] = index

    workflow = {
        "id": str(uuid.uuid4()),
        "revision": 0,
        "last_node_id": max(node["id"] for node in nodes),
        "last_link_id": link_id,
        "nodes": nodes,
        "links": links,
        "groups": [{
            "title": "Prompt file walk - set prompt_file, then Queue repeatedly",
            "bounding": [380, 660, 700, 190],
            "color": "#3f789e",
        }],
        "config": {},
        "extra": {"ds": {"scale": 0.8, "offset": [120, 140]}},
        "version": 0.4,
    }

    path = os.path.join(OUT_DIR, "CONCEPT_QUEUE_promptfile.json")
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(workflow, handle, indent=2)
    print(f"wrote {os.path.basename(path)}  ({len(nodes)} nodes, {len(links)} links)")
    print(f"prompt_file widget set to: {PROMPT_FILE}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
