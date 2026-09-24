"""Search every host's model lists and every reachable model tree for audio generation weights.

The audio nodes shipped with ComfyUI core are present on all three hosts, which proves nothing: a
node with no weights cannot generate. This looks for the weights themselves, both through each
server's own reported option lists and on the filesystem.

Usage:
    python _audio_weights.py
"""
import json
import os
import urllib.request

HOSTS = {
    "beast": "http://127.0.0.1:8188",
    "astral": "http://127.0.0.1:18190",
    "razer": "http://RAZER:8188",
}
# Combo inputs that name a loadable weight file, across loaders and audio nodes.
LOADER_INPUTS = {
    "CheckpointLoaderSimple": "ckpt_name",
    "CheckpointLoader": "ckpt_name",
    "UNETLoader": "unet_name",
    "CLIPLoader": "clip_name",
    "VAELoader": "vae_name",
    "DualCLIPLoader": "clip_name1",
    "LoraLoader": "lora_name",
    "StableAudioSampler": "model",
}
HINTS = ("audio", "audiox", "ace", "stable", "sfx", "sound", "music", "vocal", "speech",
         "voice", "mmaudio", "audioldm", "bark", "tango")

MODEL_ROOTS = {
    "beast": [r"C:\Users\jluca\ComfyUI\models"],
    "astral": [r"W:\ComfyUI_LTX25\ComfyUI\models", r"W:\ComfyUI_H3\ComfyUI_windows_portable\ComfyUI\models"],
    "razer": [r"\\RAZER\D\ComfyUI\models"],
    "shared": [r"X:\\"],
}
WEIGHT_EXT = (".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".gguf")


def get(url, path, timeout=60):
    with urllib.request.urlopen(f"{url}{path}", timeout=timeout) as response:
        return json.loads(response.read())


def from_server(name, url):
    print(f"\n--- {name} reported option lists ---")
    try:
        info = get(url, "/object_info")
    except Exception as exc:
        print(f"  unreachable: {exc}")
        return 0
    hits = 0
    for class_name, input_name in LOADER_INPUTS.items():
        spec = info.get(class_name)
        if not spec:
            continue
        definition = (((spec.get("input") or {}).get("required") or {}).get(input_name)
                      or ((spec.get("input") or {}).get("optional") or {}).get(input_name))
        if not definition or not isinstance(definition[0], list):
            continue
        matches = [o for o in definition[0]
                   if isinstance(o, str) and any(h in o.lower() for h in HINTS)]
        if matches:
            hits += len(matches)
            print(f"  {class_name}.{input_name}: {matches}")
    if not hits:
        print("  no audio-related weights offered by any loader")
    return hits


def from_disk(name, roots):
    print(f"\n--- {name} on disk ---")
    found = 0
    for root in roots:
        if not os.path.isdir(root):
            print(f"  {root}: not reachable")
            continue
        for base, _dirs, files in os.walk(root):
            depth = base[len(root):].count(os.sep)
            if depth > 3:
                _dirs[:] = []
                continue
            for filename in files:
                if not filename.lower().endswith(WEIGHT_EXT):
                    continue
                if any(h in filename.lower() for h in HINTS):
                    size = os.path.getsize(os.path.join(base, filename))
                    print(f"  {os.path.join(base, filename)[len(root):].lstrip(os.sep)}"
                          f"  {size / 2**20:.0f} MB")
                    found += 1
    if not found:
        print("  nothing audio-related found")
    return found


total = 0
for host, url in HOSTS.items():
    total += from_server(host, url)
print(f"\n=== server-reported audio weights: {total} ===")

total_disk = 0
for group, roots in MODEL_ROOTS.items():
    total_disk += from_disk(group, roots)
print(f"\n=== on-disk audio weights: {total_disk} ===")
