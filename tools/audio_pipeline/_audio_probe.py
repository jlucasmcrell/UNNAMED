"""Probe every ComfyUI host for audio generation capability, before assuming any of it exists.

The audio sprint's whole plan depends on what these servers can actually do, and the rig's
documented facts go stale. This asks each host directly: its version and launch flags, its GPU, and
which audio nodes it exposes. Nodes are matched on the class name rather than hard-coded, because
custom-node packs differ per install and a missing pack is the normal case, not the exception.

Read-only: it submits nothing and changes nothing.

Usage:
    python _audio_probe.py
    python _audio_probe.py --host razer
"""
import argparse
import json
import os
import urllib.error
import urllib.request

HOSTS = {
    "beast": {"url": "http://127.0.0.1:8188", "output": r"C:\Users\jluca\ComfyUI\output"},
    "astral": {"url": "http://127.0.0.1:18190",
               "output": r"W:\ComfyUI_LTX25\ComfyUI\output"},
    "razer": {"url": "http://RAZER:8188", "output": r"\\RAZER\D\ComfyUI\output"},
}

# Node-name fragments that mean "this install can generate or handle audio". Matched
# case-insensitively against every registered class name.
AUDIO_HINTS = (
    "audio", "stableaudio", "ace_step", "acestep", "mmaudio", "audioldm", "vibevoice",
    "chatterbox", "f5tts", "tts", "voicecraft", "bark", "musicgen", "audiogen", "safx",
    "sound", "speech", "saveaudio", "loadaudio",
)
# Node packs that mean a specific model family is installed. Ordered most-capable first.
FAMILIES = {
    "stable_audio": ("EmptyLatentAudio", "StableAudio", "VAEDecodeAudio"),
    "ace_step": ("ACEStep", "TextEncodeAceStep"),
    "mmaudio": ("MMAudio",),
    "audiox": ("AudioX",),
    "vibevoice": ("VibeVoice",),
}


def get(url, path, timeout=20):
    with urllib.request.urlopen(f"{url}{path}", timeout=timeout) as response:
        return json.loads(response.read())


def probe(name, info):
    result = {"host": name, "url": info["url"], "reachable": False}
    try:
        stats = get(info["url"], "/system_stats")
    except (urllib.error.URLError, OSError, ValueError) as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        return result
    result["reachable"] = True
    system = (stats.get("system") or {})
    result["comfyui_version"] = system.get("comfyui_version")
    result["python"] = system.get("python_version", "")[:12]
    result["argv"] = system.get("argv") or []
    devices = stats.get("devices") or []
    if devices:
        device = devices[0]
        result["gpu"] = device.get("name")
        result["vram_total_gb"] = round((device.get("vram_total") or 0) / 2**30, 1)
        result["vram_free_gb"] = round((device.get("vram_free") or 0) / 2**30, 1)
    result["queue"] = _queue_depth(info["url"])

    try:
        objects = get(info["url"], "/object_info", timeout=60)
    except (urllib.error.URLError, OSError, ValueError) as exc:
        result["object_info_error"] = f"{type(exc).__name__}: {exc}"
        return result

    names = list(objects)
    result["node_count"] = len(names)
    audio = sorted(n for n in names if any(h in n.lower() for h in AUDIO_HINTS))
    result["audio_nodes"] = audio
    result["families"] = {family: [n for n in names if any(p.lower() in n.lower() for p in probes)]
                          for family, probes in FAMILIES.items()}
    result["families"] = {k: v for k, v in result["families"].items() if v}
    # Combo values are where the real model filenames live.
    result["audio_models"] = _model_options(objects)
    return result


def _queue_depth(url):
    try:
        queue = get(url, "/queue", timeout=10)
    except (urllib.error.URLError, OSError, ValueError):
        return None
    return {"running": len(queue.get("queue_running") or []),
            "pending": len(queue.get("queue_pending") or [])}


def _model_options(objects):
    """Every filename-ish combo value offered by an audio node, which is what actually loads."""
    out = {}
    for class_name, spec in objects.items():
        if not any(h in class_name.lower() for h in AUDIO_HINTS):
            continue
        required = ((spec.get("input") or {}).get("required") or {})
        for input_name, definition in required.items():
            if not isinstance(definition, list) or not definition:
                continue
            first = definition[0]
            if isinstance(first, list) and first and isinstance(first[0], str):
                if any(first[0].lower().endswith(ext)
                       for ext in (".safetensors", ".ckpt", ".pt", ".pth", ".bin", ".gguf")):
                    out[f"{class_name}.{input_name}"] = first
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", nargs="*", default=None, choices=sorted(HOSTS))
    parser.add_argument("--json-out", default=None)
    args = parser.parse_args()

    wanted = args.host or sorted(HOSTS)
    results = []
    for name in wanted:
        result = probe(name, HOSTS[name])
        results.append(result)
        print(f"\n=== {name} — {HOSTS[name]['url']} ===")
        if not result["reachable"]:
            print(f"  UNREACHABLE  {result.get('error')}")
            continue
        print(f"  ComfyUI {result.get('comfyui_version')}  python {result.get('python')}")
        print(f"  GPU {result.get('gpu')}  VRAM free {result.get('vram_free_gb')} / "
              f"{result.get('vram_total_gb')} GB")
        print(f"  nodes {result.get('node_count')}  queue {result.get('queue')}")
        families = result.get("families") or {}
        print(f"  audio families: {', '.join(f'{k} ({len(v)})' for k, v in families.items()) or 'NONE'}")
        audio = result.get("audio_nodes") or []
        print(f"  audio-ish nodes ({len(audio)}): {', '.join(audio[:24])}")
        models = result.get("audio_models") or {}
        if models:
            print("  loadable audio models:")
            for key, options in sorted(models.items()):
                print(f"    {key}: {len(options)} option(s), e.g. {options[:4]}")

    if args.json_out:
        os.makedirs(os.path.dirname(args.json_out), exist_ok=True)
        with open(args.json_out, "w", encoding="utf-8") as handle:
            json.dump(results, handle, indent=2)
        print(f"\n  wrote {args.json_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
