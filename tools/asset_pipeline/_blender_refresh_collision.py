"""Rebuild an asset's collision proxies and measured transform from its current base GLB.

Used when a remediated base replaces the one the proxies were made from. The base is read,
never written. Run headless:
  blender --background --factory-startup --python _blender_refresh_collision.py -- \
      --input ready/<id>/<id>.glb --outdir ready/<id> --name <id>
Prints REFRESH_RESULT {transform, base, collision} (Blender Z-up, as the meta records it).
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _blender_cleanup as cleanup  # noqa: E402  (needs bpy: run inside Blender)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--hull-faces", type=int, default=64)
    args = parser.parse_args(argv)

    cleanup.reset_scene()
    meshes = cleanup.import_glb(args.input)
    if not meshes:
        raise RuntimeError(f"no mesh objects found in {args.input}")
    merged = cleanup.merge_meshes(meshes)
    cleanup.apply_transforms(merged)
    lows, highs = cleanup.world_bounds(merged)
    transform = {
        "dimensions": [round(v, 4) for v in (highs - lows)],
        "scale_applied": 1.0,
        "min": [round(v, 4) for v in lows],
        "max": [round(v, 4) for v in highs],
    }
    base = cleanup.mesh_stats(merged)
    collision = cleanup.make_collision(merged, args.hull_faces, args.name, args.outdir)
    print("REFRESH_RESULT " + json.dumps({"transform": transform, "base": base, "collision": collision}))


if __name__ == "__main__":
    main()
