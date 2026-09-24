"""Which audio checkpoints and node inputs actually exist, per host.

Stable Audio and ACE-Step both load their weights as ordinary checkpoints, so the node-combo probe
in _audio_probe.py finds nothing useful. This reads the loader nodes' own option lists and prints
the input signatures of the audio nodes, which is what a graph has to satisfy.

Usage:
    python _audio_models.py
"""
import json
import urllib.request

HOSTS = {
    "beast": "http://127.0.0.1:8188",
    "astral": "http://127.0.0.1:18190",
    "razer": "http://RAZER:8188",
}
INTERESTING = (
    "CheckpointLoaderSimple", "UNETLoader", "CLIPLoader", "VAELoader", "DualCLIPLoader",
    "ConditioningStableAudio", "EmptyLatentAudio", "VAEDecodeAudio", "KSampler", "SaveAudio",
    "TextEncodeAceStepAudio", "EmptyAceStepLatentAudio", "TextEncodeAceStepAudio1.5",
    "EmptyAceStep1.5LatentAudio", "ACEStepSampler", "LoadAudio", "SaveAudioMP3", "SaveAudioOpus",
    "EmptyAudio",
)


def get(url, path, timeout=60):
    with urllib.request.urlopen(f"{url}{path}", timeout=timeout) as response:
        return json.loads(response.read())


for name, url in HOSTS.items():
    print(f"\n=== {name} ({url}) ===")
    try:
        info = get(url, "/object_info")
    except Exception as exc:
        print(f"  unreachable: {exc}")
        continue

    for class_name in INTERESTING:
        spec = info.get(class_name)
        if spec is None:
            continue
        required = ((spec.get("input") or {}).get("required") or {})
        optional = ((spec.get("input") or {}).get("optional") or {})
        parts = []
        for group, label in ((required, ""), (optional, "?")):
            for input_name, definition in group.items():
                if not isinstance(definition, list) or not definition:
                    continue
                first = definition[0]
                if isinstance(first, list):
                    shown = [v for v in first if isinstance(v, str)]
                    parts.append(f"{label}{input_name}=[{len(shown)}] {shown[:3]}")
                else:
                    parts.append(f"{label}{input_name}:{first}")
        print(f"  {class_name}")
        for part in parts:
            print(f"      {part}")

    # Which checkpoint lists mention audio at all.
    for loader in ("CheckpointLoaderSimple", "UNETLoader"):
        spec = info.get(loader)
        if not spec:
            continue
        options = (((spec.get("input") or {}).get("required") or {})
                   .get("ckpt_name") or (None,))[0]
        if isinstance(options, list):
            audio = [o for o in options if "audio" in o.lower()]
            print(f"  {loader} audio-looking checkpoints: {audio}")
