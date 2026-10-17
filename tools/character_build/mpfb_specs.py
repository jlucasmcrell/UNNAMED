"""WP17: the production (MPFB) players' build specs, for the characters lane's build_char.py (asset_work/characters/_tools).

python mpfb_specs.py --specs <asset_work/characters/specs> --out <specs dir>

Each build is the player's own spec with MakeHuman's weight and muscle macros stepped (builds.json "mpfb"): MPFB's own CC0 morph
targets, and every garment refitted to the new body by MPFB's proxy fitting (.mhclo) inside build_char.py - the body's shape
carried into the cloth at build time. Average keeps the player's spec id. The female player also gets an opaque base layer under
her linen top (toigo_camisole_top, CC0 by MRT, from its own .mhclo header): the top alone let the body show through at the bust
(WP2-UI open question 2).
Out: <out>/<id>[_<build>].json; ids pc_veth_m_body_c[_<build>] and pc_veth_f[_<build>].
"""
import argparse
import copy
import json
import os

ap = argparse.ArgumentParser()
ap.add_argument("--specs", required=True)
ap.add_argument("--out", required=True)
a = ap.parse_args()
builds = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "builds.json")))["builds"]
os.makedirs(a.out, exist_ok=True)
CAMISOLE = {"name": "toigo_camisole_top", "label": "linen_base", "fabric": "rough_linen", "tint": [0.62, 0.6, 0.55], "kind": "cloth",
            "tile_m": 0.3, "role": "top", "layer": 2}
for base_id in ("pc_veth_m_body_c", "pc_veth_f"):
    base = json.load(open(os.path.join(a.specs, f"{base_id}.json"), encoding="utf-8"))
    for build in builds:
        spec = copy.deepcopy(base)
        if build["id"] != "average":
            spec["id"] = f"{base_id}_{build['id']}"
        spec["role"] = f"{base['role']}; build {build['label']} (WP17)"
        for k, step in build["mpfb"].items():
            spec["macro"][k] = round(min(1.0, max(0.0, base["macro"][k] + step)), 3)
        if base_id == "pc_veth_f":
            # inside out: trousers 1, the base layer 2, boots 3, the linen top 4 (build_char.py's explicit "layer")
            for c in spec["clothes"]:
                c["layer"] = {"bottom": 1, "footwear": 3, "top": 4}[c["role"]]
            spec["clothes"].insert(0, dict(CAMISOLE))
        json.dump(spec, open(os.path.join(a.out, f"{spec['id']}.json"), "w", encoding="utf-8"), indent=1)
        print("SPEC", spec["id"], spec["macro"])
