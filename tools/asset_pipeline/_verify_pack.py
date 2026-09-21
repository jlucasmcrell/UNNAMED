"""Verify every finished asset in ready/, independently of the build pipeline.

The per-asset check during a run confirms each asset as it is made. This sweeps the
whole pack afterwards, so the morning report can say the collection is sound rather
than only the latest batch, and can catch an asset damaged by an interrupted run.

Usage:
    python _verify_pack.py
    python _verify_pack.py --fast      # attributes and maps only, skip texture dims
"""
import argparse
import json
import os
import struct
import subprocess
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
VERIFIER = os.path.join(TOOL_DIR, "_verify_glb.py")
ASSETS = r"W:\UNNAMED\assets"

REQUIRED_SUFFIXES = ("", "_lod1", "_lod2", "_lod3", "_collision_hull", "_collision_box")


def glb_summary(path):
    """Read a GLB's own metadata without shelling out."""
    try:
        with open(path, "rb") as handle:
            data = handle.read()
        _, version, length = struct.unpack_from("<4sII", data, 0)
        offset = 12
        gltf = None
        while offset < length:
            chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
            offset += 8
            if chunk_type == 0x4E4F534A:
                gltf = json.loads(data[offset:offset + chunk_len].decode("utf-8"))
                break
            offset += chunk_len
        if gltf is None:
            return None
        triangles = 0
        attributes = set()
        for mesh in gltf.get("meshes", []):
            for primitive in mesh.get("primitives", []):
                attributes |= set(primitive["attributes"])
                if "indices" in primitive:
                    triangles += gltf["accessors"][primitive["indices"]]["count"] // 3
        return {
            "bytes": length,
            "triangles": triangles,
            "attributes": attributes,
            "materials": len(gltf.get("materials", [])),
            "images": len(gltf.get("images", [])),
        }
    except (OSError, ValueError, struct.error):
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default=ASSETS)
    parser.add_argument("--fast", action="store_true")
    parser.add_argument("--json-out", default=None)
    args = parser.parse_args()

    ready = os.path.join(args.assets, "ready")
    if not os.path.isdir(ready):
        print(f"no ready folder at {ready}")
        return 1

    stems = sorted(name for name in os.listdir(ready)
                   if os.path.isdir(os.path.join(ready, name)))
    print(f"verifying {len(stems)} asset(s) in {ready}\n")

    problems = []
    results = []
    for stem in stems:
        asset_dir = os.path.join(ready, stem)
        missing = []
        for suffix in REQUIRED_SUFFIXES:
            candidate = os.path.join(asset_dir, f"{stem}{suffix}.glb")
            if not os.path.exists(candidate):
                missing.append(os.path.basename(candidate))

        base = os.path.join(asset_dir, f"{stem}.glb")
        summary = glb_summary(base) if os.path.exists(base) else None
        entry = {"stem": stem, "missing_files": missing, "base": summary}

        if missing:
            problems.append(f"{stem}: missing {', '.join(missing)}")
        if summary is None:
            problems.append(f"{stem}: base GLB unreadable")
        else:
            for required in ("POSITION", "NORMAL", "TEXCOORD_0"):
                if required not in summary["attributes"]:
                    problems.append(f"{stem}: base GLB lacks {required}")
            if summary["images"] == 0:
                problems.append(f"{stem}: base GLB has no embedded textures")

        if not args.fast:
            check = subprocess.run([sys.executable, VERIFIER, base],
                                   capture_output=True, text=True)
            entry["verify"] = "RESULT: complete" in (check.stdout or "")
            if not entry["verify"]:
                problems.append(f"{stem}: verifier reported incomplete")

        # Normalisation: the game depends on these being right, and a silently wrong
        # scale or a mesh sunk below the origin is invisible until something floats
        # or clips in engine. The metadata records what Blender actually applied.
        meta_path = os.path.join(asset_dir, f"{stem}_meta.json")
        if os.path.exists(meta_path):
            with open(meta_path, encoding="utf-8") as meta_handle:
                meta = json.load(meta_handle)
            transform = meta.get("transform") or {}
            dims = transform.get("dimensions") or []
            target = meta.get("target_size_m")
            entry["target_size_m"] = target
            entry["dimensions"] = dims
            if dims and target:
                longest = max(dims)
                if abs(longest - target) > 0.02 * max(target, 1e-6):
                    problems.append(
                        f"{stem}: longest axis {longest:.4f} m does not match its "
                        f"{target} m target")
                base_z = (transform.get("min") or [None, None, None])[2]
                if base_z is not None and abs(base_z) > 0.002:
                    problems.append(
                        f"{stem}: base sits at Z={base_z:.4f}, not on the ground plane")
            else:
                problems.append(f"{stem}: metadata has no dimensions or target size")
        else:
            problems.append(f"{stem}: no metadata file, cannot confirm normalisation")

        results.append(entry)

    ok = len(stems) - len({p.split(":")[0] for p in problems})
    print(f"{ok}/{len(stems)} assets clean")
    if problems:
        print(f"\nPROBLEMS ({len(problems)}):")
        for problem in problems[:40]:
            print(f"  - {problem}")
        if len(problems) > 40:
            print(f"  ...and {len(problems) - 40} more")
    else:
        print("RESULT: PACK COMPLETE")

    if args.json_out:
        with open(args.json_out, "w", encoding="utf-8") as handle:
            json.dump({"assets": results, "problems": problems}, handle, indent=2)
        print(f"\nreport -> {args.json_out}")

    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
