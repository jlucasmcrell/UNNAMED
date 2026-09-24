"""Verify what each render host can actually do right now, rather than trusting old notes.

Section 26 of the sprint prompt is explicit: the owner reports three-machine capacity, but the
capability must be verified before jobs are assigned, because an old limitation may have been
fixed and an old capability may have regressed. This probes each host and records the facts that
decide what can be sent where:

  - ComfyUI version and whether it answers at all
  - whether the Trellis2/Pixal3D 3D pipeline is actually present (it is core ComfyUI, so it needs
    a minimum version AND the model files)
  - VRAM and free VRAM
  - which output prefixes that host may safely write

Usage:
    python _verify_hosts.py
"""
import json
import os
import sys
import urllib.error
import urllib.request

# The 3D pipeline is core ComfyUI from 0.34.0, so a host below that has no 3D nodes at all no
# matter what models are on disk.
MIN_3D_VERSION = (0, 34, 0)

# Models a host must hold to run the 3D pipeline. Paths are relative to the ComfyUI models dir.
REQUIRED_3D_MODELS = [
    "diffusion_models/pixal3d_int8_convrot.safetensors",
    "vae/trellis_2_shape_vae_bf16.safetensors",
    "vae/trellis_2_texture_vae_bf16.safetensors",
    "clip_vision/dino_v3_L_naf_fp32.safetensors",
    "background_removal/birefnet.safetensors",
]

HOSTS = [
    {"name": "BEAST", "api": "http://127.0.0.1:8188", "models": r"C:\Users\jluca\ComfyUI\models",
     "prefixes": ["weaponcomp_", "magiccomp_", "armour_", "item_", "prop_", "resource_"],
     "note": "local; RTX 3090 24 GB"},
    {"name": "ASTRAL", "api": "http://127.0.0.1:18190", "models": r"W:\ComfyUI_H3\ComfyUI_windows_portable\ComfyUI\models",
     "prefixes": ["race2_", "creature_", "vehicle_", "animal_", "magic_", "travel_"],
     "note": "via SSH tunnel 18190; RTX 5090 32.6 GB"},
    {"name": "RAZER", "api": "http://RAZER:8188", "models": r"\\RAZER\D\ComfyUI\models",
     "prefixes": [],
     "note": "RTX 4070 Ti 12.9 GB"},
]


def probe(host):
    record = {"host": host["name"], "note": host["note"], "prefixes": host["prefixes"]}
    record["api_reachable"] = False
    try:
        with urllib.request.urlopen(host["api"] + "/system_stats", timeout=25) as response:
            stats = json.loads(response.read())
        record["api_reachable"] = True
        system = stats.get("system", {})
        record["comfyui_version"] = system.get("comfyui_version")
        record["pytorch_version"] = system.get("pytorch_version")
        devices = stats.get("devices", [])
        if devices:
            d = devices[0]
            record["gpu"] = d.get("name")
            record["vram_total_gb"] = round(d.get("vram_total", 0) / 1e9, 1)
            record["vram_free_gb"] = round(d.get("vram_free", 0) / 1e9, 1)
    except (urllib.error.URLError, OSError, ValueError) as exc:
        record["api_error"] = f"{type(exc).__name__}: {exc}"

    # 3D node availability, which is what actually gates a 3D job.
    record["trellis_nodes"] = 0
    if record["api_reachable"]:
        try:
            with urllib.request.urlopen(host["api"] + "/object_info", timeout=120) as response:
                info = json.loads(response.read())
            record["node_count"] = len(info)
            record["trellis_nodes"] = sum(
                1 for k in info if k.startswith(("Trellis2", "Pixal3D", "LoadTrellis")))
        except (urllib.error.URLError, OSError, ValueError) as exc:
            record["object_info_error"] = f"{type(exc).__name__}: {exc}"

    version = record.get("comfyui_version") or "0.0.0"
    try:
        parsed = tuple(int(p) for p in version.split(".")[:3])
    except ValueError:
        parsed = (0, 0, 0)
    record["version_supports_core_3d"] = parsed >= MIN_3D_VERSION

    missing = []
    for relative in REQUIRED_3D_MODELS:
        path = os.path.join(host["models"], relative.replace("/", os.sep))
        if not os.path.exists(path):
            missing.append(relative)
    record["missing_3d_models"] = missing

    record["can_build_3d"] = bool(
        record["api_reachable"] and record["trellis_nodes"] > 0 and not missing)
    return record


def main():
    records = [probe(h) for h in HOSTS]

    print(f"  {'host':<8} {'api':<6} {'ver':<8} {'gpu':<26} {'vram free':>10} "
          f"{'3D nodes':>9} {'3D models':>10} {'can 3D':>7}")
    print("  " + "-" * 92)
    for r in records:
        ver = r.get("comfyui_version") or "n/a"
        gpu = (r.get("gpu") or r.get("api_error", "unreachable"))[:25]
        free = f"{r.get('vram_free_gb', 0)}/{r.get('vram_total_gb', 0)}"
        models = "all" if not r["missing_3d_models"] else f"{len(r['missing_3d_models'])} missing"
        print(f"  {r['host']:<8} {'up' if r['api_reachable'] else 'DOWN':<6} {ver:<8} "
              f"{gpu:<26} {free:>10} {r['trellis_nodes']:>9} {models:>10} "
              f"{'YES' if r['can_build_3d'] else 'no':>7}")

    print()
    for r in records:
        if r["can_build_3d"]:
            print(f"  {r['host']}: 3D-capable. Safe prefixes: {', '.join(r['prefixes']) or 'none assigned'}")
        else:
            reasons = []
            if not r["api_reachable"]:
                reasons.append("API unreachable")
            if not r["version_supports_core_3d"]:
                reasons.append(f"ComfyUI {r.get('comfyui_version')} is below the 0.34.0 that "
                               "first ships the core Trellis2 nodes")
            if r["trellis_nodes"] == 0 and r["api_reachable"]:
                reasons.append("no Trellis2/Pixal3D nodes exposed")
            if r["missing_3d_models"]:
                reasons.append(f"missing {len(r['missing_3d_models'])} model file(s)")
            print(f"  {r['host']}: NOT 3D-capable - {'; '.join(reasons) or 'unknown'}")

    out = os.path.join(r"W:\UNNAMED\assets\manifests", "host_capability.json")
    with open(out, "w", encoding="utf-8") as handle:
        json.dump({"hosts": records}, handle, indent=2)
    print(f"\n  wrote {out}")

    # Collision check: a prefix claimed by two 3D-capable hosts would race on one directory.
    owners = {}
    for r in records:
        if not r["can_build_3d"]:
            continue
        for prefix in r["prefixes"]:
            owners.setdefault(prefix, []).append(r["host"])
    clashes = {p: h for p, h in owners.items() if len(h) > 1}
    print(f"  write collisions: {clashes if clashes else 'none'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
