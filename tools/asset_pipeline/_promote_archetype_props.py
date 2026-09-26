"""Promote the prop_archetype_kit staging outputs (B8 proof set) to assets/ready/<id>/: copy LOD0, build LOD1/LOD2
by a plain Decimate (COLLAPSE) off LOD0 (each level independently, not chained, matching the production LOD
convention of never compounding a decimate on top of another decimate), verify every GLB with _verify_glb.py and
write a small <id>_meta.json (role "phase_b_archetype_prop", the archetype/parameters used, and the full
run_template provenance).

This is a lightweight promotion for a proof set on clean, watertight, already-well-UV'd procedural output -- not
the full gated multi-route LOD rebuild in _rebuild_lods.py (built for cracked, non-manifold Pixal3D reconstructions
and not needed here).

    blender --background --factory-startup --python tools/asset_pipeline/_promote_archetype_props.py
"""
import json
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from procgen_lib import export  # noqa: E402

REPO = export.REPO
STAGING = os.path.join(REPO, "assets", "_staging", "procgen")
READY = os.path.join(REPO, "assets", "ready")
VERIFY = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_verify_glb.py")

IDS = [
    "prop_arch_crate_a", "prop_arch_crate_b",
    "prop_arch_chest_a", "prop_arch_chest_b",
    "prop_arch_bench_a", "prop_arch_bench_b",
    "prop_arch_wheel_a", "prop_arch_wheel_b",
    "prop_arch_fence_a", "prop_arch_fence_b",
    "prop_arch_pipe_a", "prop_arch_pipe_b",
    "prop_arch_techhousing_a", "prop_arch_techhousing_b",
]
LOD_RATIOS = {"lod1": 0.5, "lod2": 0.18}


def tri_count(obj):
    # the base GLB is already fully triangulated (meshbuild.finish_topology ran before export), and glTF import
    # never returns n-gons, so a polygon count is a triangle count
    return len(obj.data.polygons)


def decimate_export(src_glb, dst_glb, ratio):
    import bpy
    from procgen_lib.meshbuild import select_only
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src_glb)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    base_tris = sum(tri_count(o) for o in meshes)
    for o in meshes:
        select_only(o)
        mod = o.modifiers.new("lod", "DECIMATE")
        mod.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=mod.name)
    lod_tris = sum(tri_count(o) for o in meshes)
    export.export_glb(dst_glb)
    return base_tris, lod_tris


def verify(path):
    r = subprocess.run([sys.executable, VERIFY, path], capture_output=True, text=True)
    lines = r.stdout.strip().splitlines()
    return {"ok": r.returncode == 0, "result": lines[-1].strip() if lines else r.stderr.strip()[-200:]}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ids = argv or IDS
    report = {}
    for asset_id in ids:
        stage_dir = os.path.join(STAGING, asset_id)
        src_glb = os.path.join(stage_dir, asset_id + ".glb")
        prov_path = os.path.join(stage_dir, asset_id + "_provenance.json")
        if not os.path.exists(src_glb):
            report[asset_id] = {"error": "no staging GLB (base build did not run or failed)"}
            print(f"[promote] SKIP {asset_id}: no staging GLB")
            continue
        with open(prov_path, encoding="utf-8") as h:
            prov = json.load(h)
        out_dir = os.path.join(READY, asset_id)
        os.makedirs(out_dir, exist_ok=True)
        dst_glb = os.path.join(out_dir, asset_id + ".glb")
        shutil.copy2(src_glb, dst_glb)
        lods = {}
        for lod, ratio in LOD_RATIOS.items():
            dst = os.path.join(out_dir, f"{asset_id}_{lod}.glb")
            base_tris, lod_tris = decimate_export(src_glb, dst, ratio)
            v = verify(dst)
            lods[lod] = {"ratio": ratio, "base_triangles": base_tris, "triangles": lod_tris, "verify": v}
            print(f"[promote] {asset_id} {lod}: {base_tris} -> {lod_tris} tris, verify {v['result']}")
        v0 = verify(dst_glb)
        print(f"[promote] {asset_id} base verify {v0['result']}")
        meta = {
            "asset_id": asset_id,
            "role": {"role": "phase_b_archetype_prop", "why": "Phase B B8: prop archetype layer proof set"},
            "archetype": prov.get("archetype"),
            "family": prov["parameters"].get("family"),
            "archetype_fn": prov["parameters"].get("fn"),
            "parameters": prov["parameters"].get("params"),
            "materials": prov["parameters"].get("materials"),
            "seed": prov.get("seed"),
            "dimensions_m": prov.get("dimensions_m"),
            "triangles": {"base": prov.get("triangles"), **{k: v["triangles"] for k, v in lods.items()}},
            "outputs": [asset_id + ".glb"] + [f"{asset_id}_{lod}.glb" for lod in LOD_RATIOS] + [asset_id + "_meta.json"],
            "verify_glb": {"base": v0, **{k: v["verify"] for k, v in lods.items()}},
            "cost": prov.get("cost"),
            "build_seconds": prov.get("build_seconds"),
            "provenance": prov,
        }
        meta_path = os.path.join(out_dir, asset_id + "_meta.json")
        with open(meta_path, "w", encoding="utf-8") as h:
            json.dump(meta, h, indent=2, default=str)
        report[asset_id] = {"base_ok": v0["ok"], "lods_ok": all(v["verify"]["ok"] for v in lods.values()),
                            "triangles": meta["triangles"]}
        print(f"[promote] wrote {meta_path}")
    print("PROMOTE_RESULT", json.dumps(report))


main()
